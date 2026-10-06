using System.ComponentModel;
using System.Globalization;
using System.Security;
using SongSense.Core;
using SongSense.Infrastructure;

namespace SongSense.App;

public sealed class AiSettingsViewModel(AiConfigurationService service, CancellationToken lifetime) : INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource session = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    private CancellationTokenSource? request;
    private Task active = Task.CompletedTask;
    private bool busy;
    private bool initialized;
    private bool hasKey;
    private string message = "Cargando ajustes locales…";
    public bool AiEnabled { get; set; }
    public string Model { get; set; } = "";
    public string Limit { get; set; } = "20";
    public bool NoticeAccepted { get; set; }
    public bool CanManage => initialized && !busy && !session.IsCancellationRequested;
    public bool CanCancel => busy && request is not null;
    public string KeyStatus => hasKey ? "Hay una clave guardada y cifrada. Déjalo vacío para conservarla." : "No hay una clave guardada.";
    public string CountLabel { get; private set; } = "";
    public string Message => message;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<AppSettings>? SettingsLoaded;
    private void Notify() { foreach (var name in new[] { nameof(AiEnabled), nameof(Model), nameof(Limit), nameof(NoticeAccepted), nameof(CanManage), nameof(CanCancel), nameof(KeyStatus), nameof(CountLabel), nameof(Message) }) PropertyChanged?.Invoke(this, new(name)); }

    public Task InitializeAsync() => RunAsync(async token => { await ReloadAsync(token); initialized = true; message = "Abrir y guardar ajustes no hace solicitudes de red."; });
    public Task SaveAsync(SecureString key) => RunAsync(async token =>
    {
        if (!int.TryParse(Limit, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit is < 1 or > 100)
            throw new AiException(AiFailure.InvalidSettings);
        await service.SaveAsync(new(AiEnabled, string.IsNullOrEmpty(Model) ? null : Model, limit), key, NoticeAccepted, token);
        await ReloadAsync(token);
        message = "Ajustes guardados. No se ha enviado ninguna solicitud.";
    });
    public Task DeleteKeyAsync() => RunAsync(async token => { await service.DeleteKeyAsync(token); await ReloadAsync(token); message = "Clave eliminada. IA desactivada; el contador se conserva."; });
    public Task TestAsync() => RunAsync(async token =>
    {
        message = "Probando conexión: solicitud de pago en curso…"; Notify();
        await service.TestConnectionAsync(token);
        message = "Conexión válida: respuesta estructurada comprobada. La solicitud cuenta para el límite diario.";
    });
    public void Cancel() => request?.Cancel();

    private async Task ReloadAsync(CancellationToken token)
    {
        var data = await service.ReadAsync(token);
        AiEnabled = data.Settings.AiEnabled; Model = data.Settings.Model ?? "";
        Limit = data.Settings.DailyRequestLimit.ToString(CultureInfo.InvariantCulture);
        hasKey = data.HasKey;
        SettingsLoaded?.Invoke(this, data.Settings);
        CountLabel = $"Hoy ({service.LocalDate:dd/MM/yyyy}): {data.Count} / {data.Settings.DailyRequestLimit} solicitudes. Se usa la fecha local de Windows.";
    }
    private Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy || session.IsCancellationRequested) return Task.CompletedTask;
        busy = true;
        request = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Notify();
        return active = RunCoreAsync(action, request);
    }
    private async Task RunCoreAsync(Func<CancellationToken, Task> action, CancellationTokenSource current)
    {
        try { await action(current.Token); }
        catch (OperationCanceledException) { message = "Operación cancelada. Los intentos reservados no se devuelven al contador."; }
        catch (AiException error) { message = Describe(error.Failure); }
        catch (Exception) { message = Describe(AiFailure.LocalStorage); }
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
                }
                catch (Exception) { message = Describe(AiFailure.LocalStorage); }
            }
            current.Dispose(); request = null; busy = false; Notify();
        }
    }
    public static string Describe(AiFailure failure) => failure switch
    {
        AiFailure.Disabled => "La IA está desactivada. Lee el aviso, actívala y guarda antes de probar.",
        AiFailure.MissingKey => "Falta una clave. Introdúcela en el campo enmascarado y guarda.",
        AiFailure.InvalidSettings => "Revisa los ajustes: modelo explícito sin espacios (máximo 120 caracteres; no pegues la clave aquí), clave de hasta 512 caracteres sin espacios, límite entero de 1 a 100 y aviso aceptado antes de activar IA. No se recortan valores.",
        AiFailure.DailyLimit => "Límite diario alcanzado. Espera al día siguiente o aumenta explícitamente el límite y guarda.",
        AiFailure.Busy => "Ya hay una operación de IA en curso. Espera a que termine.",
        AiFailure.Authentication => "OpenAI rechazó la autenticación o el acceso. Revisa la clave y los permisos del proyecto.",
        AiFailure.IncompatibleModel => "El modelo o la configuración fue rechazado. Revisa el ID y la compatibilidad con Responses y JSON Schema. No se ha cambiado el modelo.",
        AiFailure.RateLimited => "OpenAI ha limitado la petición (429). Revisa cuota y facturación. No se reintentará automáticamente.",
        AiFailure.Timeout => "OpenAI no completó la solicitud en 60 segundos. No se reintentará automáticamente.",
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
    public void Dispose() => session.Dispose();
}
