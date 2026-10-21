using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SongSense.Core;
using SongSense.Infrastructure;

namespace SongSense.App;

public sealed class ConnectionViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ChatGptConnection connection;
    private readonly Action<Uri> openBrowser;
    private readonly CancellationTokenSource session;
    private CancellationTokenSource? request;
    private Task active = Task.CompletedTask;
    private bool busy;
    private string message = "Elige en el navegador la misma cuenta que utilizas en Codex. Song Sense no puede leer la sesión de Codex ni comprobar esa coincidencia automáticamente.";
    private string? selectedClientId;
    private string? selectedModel;
    private string modelMessage = "";
    private bool creditsDisabledConfirmed;
    public bool CreditsDisabledConfirmed { get => creditsDisabledConfirmed; set { creditsDisabledConfirmed = value; Changed(); Changed(nameof(CanEnablePlan)); } }
    public bool CanEnablePlan => CanManage && connection.PlanGranted && AiSettingsRules.IsValidModel(connection.Model) && CreditsDisabledConfirmed;
    public bool CanPausePlan => CanManage && connection.PlanUseEnabled;
    public Task EnablePlanAsync() => RunAsync(async token =>
    {
        if (!creditsDisabledConfirmed) throw new AiException(AiFailure.PlanSafetyUnverified);
        await connection.SetPlanUseAsync(true, token);
        message = "Consultas con tu plan habilitadas para esta sesión según tu confirmación de créditos desactivados. Al reiniciar tendrás que confirmarlo de nuevo. Si cambias ese ajuste en ChatGPT, pausa aquí las consultas.";
    });
    public Task PausePlanAsync() => RunAsync(async token => { await connection.SetPlanUseAsync(false, token); message = "Consultas con ChatGPT pausadas."; });
    public string Status => connection.Status;
    public string Account => connection.AccountLabel;
    public string Message => message;
    public bool CanManage => !busy && !session.IsCancellationRequested;
    public bool CanDisconnect => CanManage && connection.Connected;
    public bool CanLoadModels => CanManage && connection.PlanGranted;
    public bool CanSelectModel => CanManage && Models.Count > 0;
    public bool CanSaveModel => CanSelectModel && Models.Any(x => x.Slug == selectedModel);
    public string ModelMessage => modelMessage.Length > 0 ? modelMessage : connection.PlanGranted
        ? "Pulsa Consultar modelos para cargar la lista de tu cuenta."
        : "Para consultar modelos, conecta la cuenta y concede el permiso de uso de tu plan.";
    public bool CanCancel => busy;
    public bool CanAddAccount => CanManage && Accounts.Count > 0 && !connection.HasPendingRegistration;
    public bool CanChangePendingAccount => CanManage && connection.CanReplaceUnverifiedRegistration;
    public string PendingAccountMessage => connection.HasPendingRegistration
        ? "Hay un registro pendiente ligado a la cuenta y al espacio elegidos originalmente. Continuar lo reutiliza. Si elegiste una cuenta equivocada, usa Conectar una cuenta distinta; se conservará el registro anterior cifrado y se iniciará otro separado."
        : "";
    public bool IsApiMode => connection.Mode == AiConnectionMode.ApiKey;
    public IReadOnlyList<ChatGptAccount> Accounts => connection.Accounts;
    public IReadOnlyList<ChatGptModel> Models => connection.Models;
    public string? SelectedClientId { get => selectedClientId; set { if (selectedClientId != value) CreditsDisabledConfirmed = false; selectedClientId = value; Changed(); } }
    public string? SelectedModel { get => selectedModel; set { selectedModel = value; Changed(); Changed(nameof(CanSaveModel)); } }
    public ConnectionViewModel(ChatGptConnection connection, CancellationToken lifetime, Action<Uri>? openBrowser = null)
    {
        this.connection = connection; session = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        this.openBrowser = openBrowser ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        message = connection.LoginOutcome switch
        {
            ChatGptLoginOutcome.Failed => DescribeFailure(connection.LoginFailure, connection.LoginStep, connection.LoginCode, connection.LoginHttpStatus),
            ChatGptLoginOutcome.Cancelled => "El último acceso se canceló o agotó su plazo. No se ha enviado ninguna consulta de IA.",
            ChatGptLoginOutcome.Waiting => "El último acceso quedó incompleto. Comprueba los registros de Song Sense en ChatGPT → Uso antes de crear otro.",
            _ => message
        };
        Synchronize();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void Synchronize()
    {
        selectedClientId = connection.ActiveClientId; selectedModel = connection.Model;
        Changed(nameof(CanEnablePlan)); Changed(nameof(CanPausePlan));
        foreach (var name in new[] { nameof(Status), nameof(Account), nameof(Message), nameof(CanManage), nameof(CanAddAccount), nameof(CanChangePendingAccount), nameof(PendingAccountMessage), nameof(CanDisconnect), nameof(CanLoadModels), nameof(CanSelectModel), nameof(CanSaveModel), nameof(ModelMessage), nameof(CanCancel), nameof(IsApiMode), nameof(Accounts), nameof(Models), nameof(SelectedClientId), nameof(SelectedModel) }) Changed(name);
    }
    public Task ConnectAsync(bool newAccount) => RunAsync(async token =>
    {
        message = "Completa el acceso y los permisos en el navegador. Verifica allí que es tu cuenta actual de Codex."; Changed(nameof(Message));
        await connection.ConnectAsync(newAccount, openBrowser, token);
        CreditsDisabledConfirmed = false;
        modelMessage = "";
        message = connection.PlanGranted ? "Cuenta conectada y protegida con DPAPI. Guarda un modelo y confirma que has desactivado el uso de créditos para habilitar consultas con tu plan." :
            "Identidad verificada y protegida con DPAPI, sin permiso de uso del plan. Vuelve a Continuar con ChatGPT para revisar los permisos. No se consulta IA ni se usa una API key como alternativa.";
    });
    public async Task ChangePendingAccountAsync()
    {
        if (!CanChangePendingAccount) return;
        bool prepared = false;
        await RunAsync(async token => { await connection.PrepareDifferentAccountAsync(token); prepared = true; });
        if (prepared && CanManage) await ConnectAsync(false);
    }
    public Task SelectAccountAsync()
    {
        var accountId = selectedClientId;
        return RunAsync(async token =>
        {
            if (accountId is null) throw new AiException(AiFailure.InvalidSettings);
            await connection.SelectAccountAsync(accountId, token); CreditsDisabledConfirmed = false;
            modelMessage = ""; message = "Cuenta seleccionada. Los modelos se actualizan únicamente al pulsar Consultar modelos.";
        });
    }
    public Task SelectModelAsync()
    {
        // Synchronize restores persisted selections when an operation starts.
        // Capture the user's choice before that refresh so Save uses that choice.
        var model = selectedModel;
        return RunAsync(async token =>
        {
            if (model is null) throw new AiException(AiFailure.IncompatibleModel);
            await connection.SetModelAsync(model, token); message = "Modelo guardado. Para usarlo, revisa el ajuste de créditos y pulsa Habilitar consultas con mi plan.";
        });
    }
    public Task ModelsAsync() => RunAsync(async token =>
    {
        modelMessage = "Consultando modelos en OpenAI… Puede tardar hasta 30 segundos por petición.";
        Changed(nameof(ModelMessage));
        try
        {
            await connection.LoadModelsAsync(token);
            modelMessage = Models.Count == 0
                ? "OpenAI respondió, pero no devolvió modelos disponibles para mostrar en esta cuenta. No se ha elegido ningún modelo."
                : $"{Models.Count} modelos disponibles. Abre la lista, elige uno y pulsa Guardar modelo seleccionado.";
            message = modelMessage + " Consultar el catálogo no genera una respuesta de IA.";
        }
        catch (AiException error)
        {
            modelMessage = error.Failure switch
            {
                AiFailure.InvalidResponse => "OpenAI devolvió un catálogo de modelos incompatible o mayor que el tamaño permitido. No se ha elegido ningún modelo.",
                AiFailure.Authentication when error.AuthenticationStep == AuthenticationStep.IdentityMetadata =>
                    $"OpenAI rechazó el acceso al catálogo de modelos (HTTP {error.HttpStatus}). Revisa los permisos de la cuenta; no se ha elegido ningún modelo.",
                _ => "No se pudo cargar la lista. " + DescribeFailure(error.Failure, error.AuthenticationStep, error.OAuthCode, error.HttpStatus)
            };
            throw;
        }
        catch (OperationCanceledException)
        {
            modelMessage = "Consulta de modelos cancelada. Puedes volver a intentarlo.";
            throw;
        }
        catch (Exception)
        {
            modelMessage = "No se pudo cargar la lista de modelos. La respuesta no es compatible con esta integración.";
            throw;
        }
    });
    public Task ModeAsync(AiConnectionMode mode) => RunAsync(async token =>
    {
        await connection.SetModeAsync(mode, token);
        message = mode == AiConnectionMode.ApiKey ? "Has elegido API key · de pago. Configura la clave, el modelo y el aviso de costes en Ajustes de API. Usa una cuenta de facturación de API independiente de la suscripción." : "Modo ChatGPT. Revisa el uso de créditos y habilita las consultas con tu plan.";
    });
    public Task DisconnectAsync() => RunAsync(async token =>
    {
        bool revoked = await connection.DisconnectAsync(token);
        modelMessage = "";
        message = revoked ? "Sesión revocada en OpenAI y tokens eliminados localmente. Se conserva la identidad del registro para volver a conectar." :
            "Tokens eliminados localmente. No se confirmó la revocación remota: desconecta Song Sense en los ajustes de ChatGPT.";
    });
    public static void ManageUsage() => Process.Start(new ProcessStartInfo("https://chatgpt.com/settings/usage") { UseShellExecute = true });
    public void Cancel() => request?.Cancel();
    private Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (!CanManage) return Task.CompletedTask;
        busy = true; request = CancellationTokenSource.CreateLinkedTokenSource(session.Token); Synchronize();
        return active = RunCoreAsync(operation, request);
    }
    private async Task RunCoreAsync(Func<CancellationToken, Task> operation, CancellationTokenSource current)
    {
        try { await operation(current.Token); }
        catch (OperationCanceledException) { message = "Operación cancelada o plazo de login agotado. No se ha enviado ninguna consulta de IA."; }
        catch (AiException error) { message = DescribeFailure(error.Failure, error.AuthenticationStep, error.OAuthCode, error.HttpStatus); }
        catch (Exception) { message = "No se pudo completar la conexión. No se muestran credenciales ni respuestas privadas en el error."; }
        finally { current.Dispose(); request = null; busy = false; Synchronize(); }
    }
    private static string DescribeFailure(AiFailure failure, AuthenticationStep step, OAuthFailureCode code, int? status)
    {
        string text = failure == AiFailure.Authentication ? DescribeAuthentication(step) : AiSettingsViewModel.Describe(failure);
        if (step == AuthenticationStep.TokenExchange)
        {
            text = code switch
            {
                OAuthFailureCode.InvalidGrant => "OpenAI rechazó el código de acceso (invalid_grant). No se reutiliza ese código. Continuar con ChatGPT obtiene otro usando el mismo registro protegido, sin crear un alta nueva.",
                OAuthFailureCode.InvalidClient => "OpenAI rechazó la configuración del registro (invalid_client). Hace falta corregir la integración antes de repetir el acceso.",
                OAuthFailureCode.InvalidRequest or OAuthFailureCode.InvalidScope or OAuthFailureCode.UnsupportedGrantType => "OpenAI rechazó los parámetros del acceso. Hace falta revisar la integración antes de repetir el acceso.",
                _ => text
            };
        }
        return status is null ? text : text + $" Diagnóstico: HTTP {status}; etapa {step}; código {code}.";
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
    public void Dispose() => session.Dispose();
}
