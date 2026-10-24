using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SongSense.Core;
using SongSense.Infrastructure;

namespace SongSense.App;

public enum ConnectionOperation { None, Access, Catalog, AccountChange, ModelChange, EnableSession, PauseSession, ModeChange, Disconnect }

public sealed partial class ConnectionViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ChatGptConnection connection;
    private readonly Action<Uri> openBrowser;
    private readonly CancellationTokenSource session;
    private CancellationTokenSource? request;
    private Task active = Task.CompletedTask;
    private bool busy;
    private NoticeKind messageKind;
    public NoticeKind MessageKind => busy ? NoticeKind.Progress : messageKind;
    private string message = "";
    public ConnectionOperation Operation { get; private set; }
    public bool IsOperating => busy;
    public bool ShowOperationResult => !busy && message.Length > 0;
    public string OperationTitle => Operation switch
    {
        ConnectionOperation.Access => "Conectando la cuenta…", ConnectionOperation.Catalog => "Consultando modelos…",
        ConnectionOperation.AccountChange => "Cambiando de cuenta…", ConnectionOperation.ModelChange => "Guardando modelo…",
        ConnectionOperation.EnableSession => "Habilitando esta sesión…", ConnectionOperation.PauseSession => "Pausando consultas…",
        ConnectionOperation.ModeChange => "Cambiando el modo de IA…", ConnectionOperation.Disconnect => "Desconectando la cuenta…", _ => ""
    };
    private string? selectedClientId;
    private string? selectedModel;
    private bool creditsDisabledConfirmed;
    public AiActivityViewModel Activity { get; }
    public bool CreditsDisabledConfirmed { get => creditsDisabledConfirmed; set { creditsDisabledConfirmed = value; Changed(); Changed(nameof(CanEnablePlan)); NotifyWorkflow(); } }
    public bool CanEnablePlan => CanManage && !IsApiMode && connection.PlanGranted && AiSettingsRules.IsValidModel(connection.Model) && CreditsDisabledConfirmed;
    public bool CanPausePlan => CanManage && connection.PlanUseEnabled;
    public Task EnablePlanAsync() => RunAsync(ConnectionOperation.EnableSession, async token =>
    {
        if (!creditsDisabledConfirmed) throw new AiException(AiFailure.PlanSafetyUnverified);
        await connection.SetPlanUseAsync(true, token);
        message = "Consultas con tu plan habilitadas para esta sesión según tu confirmación de créditos desactivados. Al reiniciar tendrás que confirmarlo de nuevo. Si cambias ese ajuste en ChatGPT, pausa aquí las consultas.";
    });
    public Task PausePlanAsync() => RunAsync(ConnectionOperation.PauseSession, async token => { await connection.SetPlanUseAsync(false, token); message = "Consultas con ChatGPT pausadas."; });
    public string Status => connection.Status;
    public string Account => connection.AccountLabel;
    public string Message => message;
    public bool CanManage => !busy && !session.IsCancellationRequested;
    public bool CanDisconnect => CanManage && connection.Connected;
    public bool CanLoadModels => CanManage && connection.PlanGranted;
    public bool HasModels => connection.PlanGranted && CatalogState == ModelCatalogState.Available && Models.Count > 0;
    public bool CanSelectModel => CanManage && HasModels;
    public bool CanSaveModel => CanSelectModel && Models.Any(x => x.Slug == selectedModel);
    public ModelCatalogState CatalogState => busy && Operation == ConnectionOperation.Catalog ? ModelCatalogState.Loading : connection.CatalogState;
    public string ModelMessage => CatalogState switch
    {
        ModelCatalogState.Loading => "Consultando modelos en OpenAI… Puede tardar hasta 30 segundos por petición.",
        ModelCatalogState.Empty => "OpenAI respondió, pero no devolvió modelos disponibles para mostrar en esta cuenta. No se ha elegido ningún modelo.",
        ModelCatalogState.Available => $"{Models.Count} modelos disponibles. Elige uno y pulsa Guardar modelo seleccionado.",
        ModelCatalogState.Cancelled => "Consulta de modelos cancelada. Puedes volver a intentarlo.",
        ModelCatalogState.Failed => DescribeCatalogFailure(connection.CatalogFailure),
        _ => connection.PlanGranted ? "Pulsa Consultar modelos para cargar la lista de tu cuenta." : "Para consultar modelos, conecta la cuenta y concede el permiso de uso del plan."
    };
    public bool CanCancel => busy;
    public bool CanAddAccount => CanManage && Accounts.Count > 0 && !connection.HasPendingRegistration;
    public bool CanChangePendingAccount => CanManage && connection.CanReplaceUnverifiedRegistration;
    public string PendingAccountMessage => connection.HasPendingRegistration
        ? "Hay un registro pendiente ligado a la cuenta y al espacio elegidos originalmente. Continuar lo reutiliza. Si elegiste una cuenta equivocada, usa Conectar una cuenta distinta; se conservará el registro anterior cifrado y se iniciará otro separado."
        : "";
    public bool IsApiMode => connection.Mode == AiConnectionMode.ApiKey;
    public IReadOnlyList<ChatGptAccount> Accounts => connection.Accounts;
    public IReadOnlyList<ChatGptModel> Models => connection.Models;
    public string? SelectedClientId { get => selectedClientId; set { if (selectedClientId != value) CreditsDisabledConfirmed = false; selectedClientId = value; Changed(); NotifyWorkflow(); } }
    public string? SelectedModel { get => selectedModel; set { selectedModel = value; ClearOperationResult(); Changed(); Changed(nameof(CanSaveModel)); NotifyWorkflow(); } }
    public ConnectionViewModel(ChatGptConnection connection, CancellationToken lifetime, Action<Uri>? openBrowser = null, AiActivityViewModel? activity = null)
    {
        Activity = activity ?? new();
        this.connection = connection; session = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        this.openBrowser = openBrowser ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        Synchronize();
        observedAccount = connection.CacheScope; observedModel = connection.Model;
        connection.Changed += ConnectionChanged;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void Synchronize()
    {
        selectedClientId = connection.ActiveClientId; selectedModel = connection.Model;
        Changed(nameof(MessageKind));
        foreach (var name in new[] { nameof(Operation), nameof(OperationTitle), nameof(IsOperating), nameof(ShowOperationResult), nameof(HasModels), nameof(CatalogState) }) Changed(name);
        Changed(nameof(CanEnablePlan)); Changed(nameof(CanPausePlan));
        foreach (var name in new[] { nameof(Status), nameof(Account), nameof(Message), nameof(CanManage), nameof(CanAddAccount), nameof(CanChangePendingAccount), nameof(PendingAccountMessage), nameof(CanDisconnect), nameof(CanLoadModels), nameof(CanSelectModel), nameof(CanSaveModel), nameof(ModelMessage), nameof(CanCancel), nameof(IsApiMode), nameof(Accounts), nameof(Models), nameof(SelectedClientId), nameof(SelectedModel) }) Changed(name);
        NotifyWorkflow();
    }
    public Task ConnectAsync(bool newAccount) => RunAsync(ConnectionOperation.Access, async token =>
    {
        message = "Completa el acceso y los permisos en el navegador. Comprueba que has elegido la cuenta que quieres utilizar."; Changed(nameof(Message));
        await connection.ConnectAsync(newAccount, openBrowser, token);
        messageKind = connection.PlanGranted ? NoticeKind.Success : NoticeKind.Warning;
        CreditsDisabledConfirmed = false;
        message = connection.PlanGranted ? "Cuenta conectada y protegida en este dispositivo. Elige un modelo y revisa la habilitación de consultas." :
            "Cuenta verificada, sin permiso de uso del plan. Continúa con ChatGPT para revisar los permisos. No se ha enviado una consulta de IA.";
    });
    public async Task ChangePendingAccountAsync()
    {
        if (!CanChangePendingAccount) return;
        bool prepared = false;
        await RunAsync(ConnectionOperation.AccountChange, async token => { await connection.PrepareDifferentAccountAsync(token); prepared = true; });
        if (prepared && CanManage) await ConnectAsync(false);
    }
    public Task SelectAccountAsync()
    {
        var accountId = selectedClientId;
        return RunAsync(ConnectionOperation.AccountChange, async token =>
        {
            if (accountId is null || !Accounts.Any(a => a.ClientId == accountId)) throw new AiException(AiFailure.InvalidSettings);
            await connection.SelectAccountAsync(accountId, token); CreditsDisabledConfirmed = false;
            message = "Cuenta seleccionada. Los modelos se actualizan únicamente al pulsar Consultar modelos.";
        });
    }
    public Task SelectModelAsync()
    {
        // Synchronize restores persisted selections when an operation starts.
        // Capture the user's choice before that refresh so Save uses that choice.
        var model = selectedModel;
        return RunAsync(ConnectionOperation.ModelChange, async token =>
        {
            if (model is null || !Models.Any(x => x.Slug == model)) throw new AiException(AiFailure.IncompatibleModel);
            await connection.SetModelAsync(model, token); message = "Modelo guardado. Para usarlo, revisa el ajuste de créditos y pulsa Habilitar consultas con mi plan.";
        });
    }
    public Task ModelsAsync() => RunAsync(ConnectionOperation.Catalog, async token =>
    {
        await connection.LoadModelsAsync(token);
        messageKind = Models.Count == 0 ? NoticeKind.Warning : NoticeKind.Success;
        message = "Consulta de modelos terminada. No se ha generado un análisis.";
    });
    public Task ModeAsync(AiConnectionMode mode) => RunAsync(ConnectionOperation.ModeChange, async token =>
    {
        await connection.SetModeAsync(mode, token);
        CreditsDisabledConfirmed = false;
        message = mode == AiConnectionMode.ApiKey ? "Has elegido API key · de pago. Configura la clave, el modelo y el aviso de costes en Ajustes de API. Usa una cuenta de facturación de API independiente de la suscripción." : "Modo ChatGPT. Revisa el uso de créditos y habilita las consultas con tu plan.";
    });
    public Task DisconnectAsync() => RunAsync(ConnectionOperation.Disconnect, async token =>
    {
        bool revoked = await connection.DisconnectAsync(token);
        messageKind = revoked ? NoticeKind.Success : NoticeKind.Warning;
        message = revoked ? "Sesión revocada en OpenAI y tokens eliminados localmente. Se conserva la identidad del registro para volver a conectar." :
            "Tokens eliminados localmente. No se confirmó la revocación remota: desconecta Song Sense en los ajustes de ChatGPT.";
    });
    public static void ManageUsage() => Process.Start(new ProcessStartInfo("https://chatgpt.com/settings/usage") { UseShellExecute = true });
    public void Cancel() => request?.Cancel();
    private void ClearOperationResult() { if (busy) return; message = ""; Changed(nameof(Message)); Changed(nameof(ShowOperationResult)); }
    private Task RunAsync(ConnectionOperation kind, Func<CancellationToken, Task> operation)
    {
        if (!CanManage) return Task.CompletedTask;
        Operation = kind; message = ""; operationFailure = null;
        if (kind == ConnectionOperation.Catalog && connection.PlanGranted) reviewStep = 2;
        busy = true; request = CancellationTokenSource.CreateLinkedTokenSource(session.Token); Synchronize();
        return active = RunCoreAsync(operation, request);
    }
    private async Task RunCoreAsync(Func<CancellationToken, Task> operation, CancellationTokenSource current)
    {
        try { operationFailure = null; messageKind = NoticeKind.Success; await operation(current.Token); }
        catch (OperationCanceledException) { messageKind = NoticeKind.Warning; message = Operation switch
        {
            ConnectionOperation.Catalog => "Consulta de modelos cancelada. Puedes volver a intentarlo.",
            ConnectionOperation.Access => "Acceso cancelado o plazo de acceso agotado. Puedes volver a conectar desde Cuenta.",
            ConnectionOperation.AccountChange => "Cambio de cuenta cancelado. Revisa la cuenta actual antes de volver a intentarlo.",
            ConnectionOperation.EnableSession => "Habilitación cancelada. Revisa el estado de esta sesión.",
            ConnectionOperation.PauseSession => "Pausa cancelada. Revisa si las consultas siguen habilitadas.",
            ConnectionOperation.ModelChange => "Guardado de modelo cancelado. Revisa el modelo guardado.",
            ConnectionOperation.ModeChange => "Cambio de modo cancelado. Revisa el modo actual.",
            ConnectionOperation.Disconnect => "Desconexión cancelada. Revisa el estado actual de la cuenta.", _ => "Operación cancelada."
        }; }
        catch (AiException error) { operationFailure = error; messageKind = NoticeKind.Error; message = Operation == ConnectionOperation.Catalog ? DescribeCatalogFailure(error) : OperationError + " " + DescribeFailure(error.Failure, error.AuthenticationStep, error.OAuthCode); }
        catch (Exception) { messageKind = NoticeKind.Error; message = OperationError + " Revisa el diagnóstico."; }
        finally { current.Dispose(); request = null; busy = false; reviewStep = Operation == ConnectionOperation.Catalog && connection.PlanGranted ? 2 : null; Synchronize(); }
    }
    private string OperationError => Operation switch
    {
        ConnectionOperation.Access => "No se pudo completar el acceso.", ConnectionOperation.Catalog => "No se pudo consultar el catálogo.",
        ConnectionOperation.AccountChange => "No se pudo cambiar de cuenta.", ConnectionOperation.EnableSession => "No se pudo habilitar esta sesión.",
        ConnectionOperation.PauseSession => "No se pudieron pausar las consultas.", ConnectionOperation.ModelChange => "No se pudo guardar el modelo.",
        ConnectionOperation.ModeChange => "No se pudo cambiar el modo de IA.", ConnectionOperation.Disconnect => "No se pudo desconectar la cuenta.", _ => "No se pudo completar la operación."
    };
    private static string DescribeCatalogFailure(AiException? error) => error?.Failure switch
    {
        AiFailure.SessionExpired => "La sesión ha caducado y no se pudo renovar. Vuelve a conectar la cuenta.",
        AiFailure.Authentication when error.HttpStatus == 401 => "OpenAI rechazó la sesión. Vuelve a conectar la cuenta.",
        AiFailure.Authentication when error.HttpStatus == 403 => "OpenAI rechazó la consulta del catálogo. La respuesta no confirma la causa. Revisa el diagnóstico antes de reintentar.",
        AiFailure.InvalidResponse => "OpenAI devolvió un catálogo incompatible o mayor que el tamaño permitido. Puedes reintentar o revisar el diagnóstico.",
        AiFailure.Disabled => "La cuenta no tiene permiso de uso del plan. Revisa la conexión y los permisos.",
        AiFailure.Timeout => "La consulta de modelos agotó el plazo. Puedes reintentar manualmente.",
        AiFailure.Offline => "No se pudo conectar con el catálogo. Comprueba la red y vuelve a consultar.",
        _ => "No se pudo consultar el catálogo. Reintenta o revisa el diagnóstico."
    };
    private static string DescribeFailure(AiFailure failure, AuthenticationStep step, OAuthFailureCode code)
    {
        string text = failure == AiFailure.Authentication ? DescribeAuthentication(step) : AiSettingsViewModel.Describe(failure);
        if (step == AuthenticationStep.TokenExchange)
        {
            text = code switch
            {
                OAuthFailureCode.InvalidGrant => "OpenAI rechazó el código de acceso. Continuar con ChatGPT obtiene otro usando el mismo registro protegido, sin crear un alta nueva.",
                OAuthFailureCode.InvalidClient => "OpenAI rechazó la configuración del registro. Hace falta corregir la integración antes de repetir el acceso.",
                OAuthFailureCode.InvalidRequest or OAuthFailureCode.InvalidScope or OAuthFailureCode.UnsupportedGrantType => "OpenAI rechazó los parámetros del acceso. Hace falta revisar la integración antes de repetir el acceso.",
                _ => text
            };
        }
        return text;
    }
    private static string DescribeAuthentication(AuthenticationStep step) => step switch
    {
        AuthenticationStep.Consent => "El acceso fue denegado en el navegador o no se concedieron los permisos. No se ha intercambiado el código ni guardado una sesión. Revisa la cuenta y el consentimiento.",
        AuthenticationStep.Callback => "El retorno del navegador no contiene el registro o código esperado. No se ha guardado una sesión. Inicia de nuevo el acceso desde Song Sense.",
        AuthenticationStep.TokenExchange => "OpenAI rechazó el intercambio del código de acceso. El registro pendiente se conserva protegido; vuelve a pulsar Continuar con ChatGPT para usar un código nuevo.",
        AuthenticationStep.TokenShape => "OpenAI devolvió un formato de credenciales incompatible con esta integración. No se ha guardado una sesión. Hace falta revisar la compatibilidad del flujo oficial.",
        AuthenticationStep.IdentitySignature => "No se pudo validar la firma, emisor, destinatario o caducidad de la identidad de OpenAI. No se ha guardado una sesión. Hace falta revisar la compatibilidad del flujo oficial.",
        AuthenticationStep.IdentityClaims => "La identidad no coincide con el intento de acceso o no contiene un identificador válido. No se ha guardado una sesión. Inicia un nuevo acceso.",
        AuthenticationStep.SelectedAccount => "Has accedido con una cuenta distinta del registro seleccionado. La sesión anterior se conserva. Selecciona la cuenta correcta o pulsa Añadir otra cuenta.",
        AuthenticationStep.IdentityMetadata => "OpenAI no permitió consultar los datos oficiales de identidad o los modelos. No se ha confirmado la conexión. Comprueba la conexión y los permisos de la cuenta.",
        _ => "OpenAI no confirmó la identidad, el permiso o la sesión. Vuelve a conectar la cuenta elegida. No se usará una API key como alternativa."
    };
    public async Task StopAsync() { session.Cancel(); await active; }
    public void Dispose() { connection.Changed -= ConnectionChanged; session.Dispose(); }
}
