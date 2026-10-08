using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SongSense.Core;
using SongSense.Infrastructure;

namespace SongSense.App;

public sealed class ConnectionViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ChatGptConnection connection;
    private readonly CancellationTokenSource session;
    private CancellationTokenSource? request;
    private Task active = Task.CompletedTask;
    private bool busy;
    private string message = "Elige en el navegador la misma cuenta que utilizas en Codex. Song Sense no puede leer la sesión de Codex ni comprobar esa coincidencia automáticamente.";
    private string? selectedClientId;
    private string? selectedModel;
    public string Status => connection.Status;
    public string Account => connection.AccountLabel;
    public string Message => message;
    public bool CanManage => !busy && !session.IsCancellationRequested;
    public bool CanDisconnect => CanManage && connection.Connected;
    public bool CanLoadModels => CanManage && connection.PlanGranted;
    public bool CanCancel => busy;
    public bool CanAddAccount => CanManage && Accounts.Count > 0 && !connection.HasPendingRegistration;
    public bool IsApiMode => connection.Mode == AiConnectionMode.ApiKey;
    public IReadOnlyList<ChatGptAccount> Accounts => connection.Accounts;
    public IReadOnlyList<ChatGptModel> Models => connection.Models;
    public string? SelectedClientId { get => selectedClientId; set { selectedClientId = value; Changed(); } }
    public string? SelectedModel { get => selectedModel; set { selectedModel = value; Changed(); } }
    public ConnectionViewModel(ChatGptConnection connection, CancellationToken lifetime)
    {
        this.connection = connection; session = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
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
        foreach (var name in new[] { nameof(Status), nameof(Account), nameof(Message), nameof(CanManage), nameof(CanAddAccount), nameof(CanDisconnect), nameof(CanLoadModels), nameof(CanCancel), nameof(IsApiMode), nameof(Accounts), nameof(Models), nameof(SelectedClientId), nameof(SelectedModel) }) Changed(name);
    }
    public Task ConnectAsync(bool newAccount) => RunAsync(async token =>
    {
        message = "Completa el acceso y los permisos en el navegador. Verifica allí que es tu cuenta actual de Codex."; Changed(nameof(Message));
        await connection.ConnectAsync(newAccount, uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }), token);
        message = connection.PlanGranted ? "Identidad verificada por OpenAI y sesión protegida con DPAPI. Comprueba el correo mostrado. Las consultas siguen bloqueadas: no existe una verificación documentada de créditos desactivados." :
            "Identidad verificada y protegida con DPAPI, sin permiso de uso del plan. Vuelve a Continuar con ChatGPT para revisar los permisos. No se consulta IA ni se usa una API key como alternativa.";
    });
    public Task SelectAccountAsync() => RunAsync(async token =>
    {
        if (selectedClientId is null) throw new AiException(AiFailure.InvalidSettings);
        await connection.SelectAccountAsync(selectedClientId, token); message = "Cuenta seleccionada. Los modelos se actualizan únicamente al pulsar Consultar modelos.";
    });
    public Task SelectModelAsync() => RunAsync(async token =>
    {
        if (selectedModel is null) throw new AiException(AiFailure.IncompatibleModel);
        await connection.SetModelAsync(selectedModel, token); message = "Modelo guardado para esta cuenta. Seleccionar un modelo no habilita consultas mientras no se pueda verificar la política de cero cargos.";
    });
    public Task ModelsAsync() => RunAsync(async token =>
    {
        await connection.LoadModelsAsync(token);
        message = "Modelos comunicados por OpenAI. Esta consulta no genera una respuesta de IA. No acredita cuota disponible ni créditos desactivados.";
    });
    public Task ModeAsync(AiConnectionMode mode) => RunAsync(async token =>
    {
        await connection.SetModeAsync(mode, token);
        message = mode == AiConnectionMode.ApiKey ? "Has elegido API key · de pago. Configura la clave, el modelo y el aviso de costes en Ajustes de API. Usa una cuenta de facturación de API independiente de la suscripción." : "Modo ChatGPT · solo uso incluido. Las consultas están bloqueadas hasta disponer de verificación oficial de cero cargos extra.";
    });
    public Task DisconnectAsync() => RunAsync(async token =>
    {
        bool revoked = await connection.DisconnectAsync(token);
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
