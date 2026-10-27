using System.ComponentModel;
using System.Globalization;
using System.Security;
using SongSense.Core;
using SongSense.Infrastructure;

namespace SongSense.App;

public enum ApiSettingsOperation { None, Loading, Saving, DeletingKey, Testing }

public sealed partial class AiSettingsViewModel(AiConfigurationService service, CancellationToken lifetime, AiActivityViewModel? activity = null, IAnalysisSession? analysisSession = null) : INotifyPropertyChanged, IDisposable
{
    public AiActivityViewModel Activity { get; } = activity ?? new();
    private readonly CancellationTokenSource session = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    private CancellationTokenSource? request;
    private Task active = Task.CompletedTask;
    private bool busy;
    public ApiSettingsOperation Operation { get; private set; }
    public bool IsOperating => busy;
    public string OperationMessage => Operation switch
    {
        ApiSettingsOperation.Loading => "Cargando ajustes locales…",
        ApiSettingsOperation.Saving => "Guardando ajustes en este dispositivo… No se envían solicitudes.",
        ApiSettingsOperation.DeletingKey => "Eliminando la clave local y desactivando IA…",
        ApiSettingsOperation.Testing => "Probando conexión: solicitud de pago en curso…",
        _ => ""
    };
    private bool initialized;
    private bool hasKey;
    private long keyRevision;
    public bool HasKey => hasKey;
    public long? BeginKeyDeletion() => CanManage && hasKey ? keyRevision : null;
    public Task ConfirmKeyDeletionAsync(long revision) => revision == keyRevision && hasKey && CanManage ? DeleteKeyAsync() : Task.CompletedTask;
    private string message = "Cargando ajustes locales…";
    private NoticeKind messageKind;
    public NoticeKind MessageKind => busy ? NoticeKind.Progress : messageKind;
    private bool enabled, accepted;
    private string model = "", limitText = "20";
    public bool AiEnabled { get => enabled; set { enabled = value; NotifyForm(); } }
    public string Model { get => model; set { model = value; NotifyForm(); } }
    public string Limit { get => limitText; set { limitText = value; NotifyForm(); } }
    public bool NoticeAccepted { get => accepted; set { accepted = value; NotifyForm(); } }
    public bool CanManage => initialized && !busy && !session.IsCancellationRequested;
    public bool CanCancel => busy && request is not null;
    public string KeyStatus => hasKey ? "Hay una clave guardada y cifrada. Déjalo vacío para conservarla." : "No hay una clave guardada.";
    public string CountLabel { get; private set; } = "";
    public string Message => message;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<AppSettings>? SettingsLoaded;
    private void Notify() { foreach (var name in new[] { nameof(Operation), nameof(IsOperating), nameof(OperationMessage), nameof(HasKey), nameof(AiEnabled), nameof(Model), nameof(Limit), nameof(NoticeAccepted), nameof(CanManage), nameof(CanCancel), nameof(KeyStatus), nameof(CountLabel), nameof(Message), nameof(MessageKind) }) PropertyChanged?.Invoke(this, new(name)); NotifyForm(); }

    public Task InitializeAsync() => RunAsync(ApiSettingsOperation.Loading, async token => { if (!modeSubscribed && analysisSession is not null) { analysisSession.Changed += ModeChanged; modeSubscribed = true; } await ReloadAsync(token); initialized = true; message = "Abrir y guardar ajustes no hace solicitudes de red."; });
    public Task SaveAsync(SecureString key) => !CanManage || !ValidateForm(key) ? Task.CompletedTask : RunAsync(ApiSettingsOperation.Saving, async token =>
    {
        if (!int.TryParse(Limit, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit is < 1 or > 100)
            throw new AiException(AiFailure.InvalidSettings);
        await service.SaveAsync(new(AiEnabled, string.IsNullOrEmpty(Model) ? null : Model, limit), key, NoticeAccepted, token);
        await ReloadAsync(token);
        message = "Ajustes guardados. No se ha enviado ninguna solicitud.";
    });
    public Task DeleteKeyAsync() => RunAsync(ApiSettingsOperation.DeletingKey, async token =>
    {
        try { await service.DeleteKeyAsync(token); }
        finally { await ReloadAsync(token); }
        message = "Clave eliminada. IA desactivada; el contador se conserva.";
    });
    public Task TestAsync() => !CanTest ? BlockTest() : RunAsync(ApiSettingsOperation.Testing, async token =>
    {
        // Use persisted configuration, never the potentially unsaved form fields.
        var saved = await service.ReadAsync(token);
        var current = new AiUsagePresentation(AiUsageState.EnCurso, Context:
            new("Prueba de conexión", "Texto fijo de prueba", null, null, "—", AiConnectionMode.ApiKey, saved.Settings.Model, "api-key"), At: DateTimeOffset.Now);
        Activity.Record(current);
        AiUsage? usage;
        try
        {
            usage = await service.TestConnectionAsync(token);
            Activity.Record(current with { State = usage is null ? AiUsageState.NoComunicado : AiUsageState.Comunicado, Tokens = usage });
        }
        catch (AiException error)
        {
            Activity.Record(current with { State = error.Usage is null ? AiUsageState.NoComunicado : AiUsageState.Comunicado, Tokens = error.Usage });
            throw;
        }
        catch
        {
            Activity.Record(current with { State = AiUsageState.NoComunicado });
            throw;
        }
        message = "Conexión válida: respuesta estructurada comprobada. La solicitud cuenta para el límite diario. " +
            (usage?.Describe() ?? "Consumo no comunicado por OpenAI; no equivale a 0 tokens.");
    });
    public void Cancel() => request?.Cancel();

    private async Task ReloadAsync(CancellationToken token)
    {
        var data = await service.ReadAsync(token);
        saved = data.Settings; enabled = saved.AiEnabled; model = saved.Model ?? "";
        limitText = saved.DailyRequestLimit.ToString(CultureInfo.InvariantCulture);
        draftKeyLength = 0; savedAcceptance = accepted; ClearErrors();
        hasKey = data.HasKey;
        keyRevision++;
        SettingsLoaded?.Invoke(this, data.Settings);
        CountLabel = $"Hoy ({service.LocalDate:dd/MM/yyyy}): {data.Count} / {data.Settings.DailyRequestLimit} solicitudes. Se usa la fecha local de Windows.";
        Activity.UpdateCounter(service.LocalDate, data.Count, data.Settings.DailyRequestLimit);
    }
    private Task RunAsync(ApiSettingsOperation operation, Func<CancellationToken, Task> action)
    {
        if (busy || session.IsCancellationRequested) return Task.CompletedTask;
        busy = true;
        Operation = operation; message = OperationMessage;
        request = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Notify();
        return active = RunCoreAsync(action, request);
    }
    private async Task RunCoreAsync(Func<CancellationToken, Task> action, CancellationTokenSource current)
    {
        try { messageKind = NoticeKind.Success; await action(current.Token); }
        catch (OperationCanceledException) { messageKind = NoticeKind.Warning; message = Operation == ApiSettingsOperation.Testing
            ? "Prueba cancelada. Una petición ya enviada puede consumir; los intentos reservados no se devuelven al contador."
            : "Operación local cancelada. Revisa los ajustes guardados antes de continuar; no se ha enviado una solicitud de IA."; }
        catch (AiException error) { messageKind = NoticeKind.Error; message = Describe(error.Failure) + (error.Usage is { } usage ? " " + usage.Describe() : ""); }
        catch (Exception) { messageKind = NoticeKind.Error; message = Describe(AiFailure.LocalStorage); }
        finally
        {
            // Update usage after success, HTTP failure or cancellation without altering
            // unsaved fields. No network request is made by this refresh.
            if (!session.IsCancellationRequested && initialized)
            {
                try
                {
                    var data = await service.ReadAsync(session.Token);
                    hasKey = data.HasKey;
                    CountLabel = $"Hoy ({service.LocalDate:dd/MM/yyyy}): {data.Count} / {data.Settings.DailyRequestLimit} solicitudes. Se usa la fecha local de Windows.";
                    Activity.UpdateCounter(service.LocalDate, data.Count, data.Settings.DailyRequestLimit);
                }
                catch (Exception) { messageKind = NoticeKind.Error; Activity.UpdateCounter(service.LocalDate, null, 0); CountLabel = "Contador local no disponible."; message = Describe(AiFailure.LocalStorage); }
            }
            current.Dispose(); request = null; busy = false; Operation = ApiSettingsOperation.None; Notify();
        }
    }
    public static string Describe(AiException error, bool chatGpt)
    {
        var message = chatGpt && error.Failure == AiFailure.Authentication
            ? "OpenAI rechazó el acceso de esta sesión de ChatGPT. Revisa Cuenta y consumo."
            : chatGpt && error.Failure == AiFailure.RateLimited
            ? "OpenAI ha limitado el uso del plan. Las consultas quedan pausadas; no habrá reintento automático."
            : Describe(error.Failure);
        var stage = error.InferenceStage switch {
            InferenceStage.Http => "petición HTTP",
            InferenceStage.Stream => "recepción de eventos",
            InferenceStage.CompletedResponse => "respuesta final",
            InferenceStage.InsightValidation => "validación de traducción y significado",
            _ => "" };
        if (stage.Length > 0) message += $" Etapa: {stage}.";
        if (error.HttpStatus is { } status) message += $" HTTP {status}.";
        if (error.InferenceCode != InferenceCode.None) message += $" Código: {error.InferenceCode}.";
        if (error.ResponseShape != ResponseShape.None) message += $" Estructura: {error.ResponseShape}.";
        return message;
    }
    public static string Describe(AiFailure failure) => failure switch
    {
        AiFailure.Disabled => "La IA está desactivada. Lee el aviso, actívala y guarda antes de probar.",
        AiFailure.PlanSafetyUnverified => "Consultas con ChatGPT pausadas. Revisa Cuenta y consumo, confirma que has desactivado el uso de créditos y habilita las consultas con tu plan. No se utilizará una API key automáticamente.",
        AiFailure.SessionExpired => "La sesión de ChatGPT caducó o fue revocada. Sus tokens ya no se utilizan; vuelve a conectar la cuenta.",
        AiFailure.MissingKey => "Falta una clave. Introdúcela en el campo enmascarado y guarda.",
        AiFailure.InvalidSettings => "Revisa los ajustes: modelo explícito sin espacios (máximo 120 caracteres; no pegues la clave aquí), clave de hasta 512 caracteres sin espacios, límite entero de 1 a 100 y aviso aceptado antes de activar IA. No se recortan valores.",
        AiFailure.DailyLimit => "Límite diario alcanzado. Espera al día siguiente o aumenta explícitamente el límite y guarda.",
        AiFailure.Busy => "Ya hay una operación de IA en curso. Espera a que termine.",
        AiFailure.Authentication => "OpenAI rechazó la autenticación o el acceso. Revisa la clave y los permisos del proyecto.",
        AiFailure.IncompatibleModel => "El modelo o la configuración fue rechazado. Revisa el ID y la compatibilidad con Responses y JSON Schema. No se ha cambiado el modelo.",
        AiFailure.RateLimited => "OpenAI ha limitado la petición (429). Revisa cuota y facturación. No se reintentará automáticamente.",
        AiFailure.Timeout => "OpenAI no completó la solicitud dentro del plazo. No se reintentará automáticamente.",
        AiFailure.Offline => "No se pudo conectar con OpenAI. Comprueba la conexión. No se reintentará automáticamente.",
        AiFailure.InvalidResponse => "La respuesta de IA no cumple el formato o faltan líneas. No se muestran resultados parciales ni se reintentará automáticamente.",
        AiFailure.Refused => "OpenAI rechazó analizar este texto. No se ha generado un resultado y no habrá reintento automático.",
        AiFailure.Incomplete => "La respuesta de IA quedó incompleta. No se muestran resultados parciales; no habrá reintento automático.",
        AiFailure.UnknownLanguage => "No se pudo identificar el idioma de la letra. Revisa el texto antes de intentarlo de nuevo.",
        AiFailure.InvalidLyrics => "Hace falta una letra no instrumental de hasta 20.000 caracteres, confirmada para la canción actual.",
        AiFailure.HttpError => "OpenAI devolvió un error HTTP. No se reintentará automáticamente.",
        _ => "No se pudo acceder al almacenamiento local o descifrar la clave. Tus archivos se conservan; revisa el acceso o reemplaza la clave."
    };
    public async Task StopAsync() { session.Cancel(); await active; }
    public void Dispose() { if (modeSubscribed && analysisSession is not null) analysisSession.Changed -= ModeChanged; session.Dispose(); }
}
