using SongSense.Core;
using SongSense.Infrastructure;
namespace SongSense.App;
public sealed partial class ConnectionViewModel
{
    private int? reviewStep;
    private string? observedAccount;
    private string? observedModel;
    private AiException? operationFailure;
    public int Step => IsApiMode ? 5 : reviewStep ?? (!connection.Connected || !connection.PlanGranted || connection.HasPendingRegistration ? 1 : !AiSettingsRules.IsValidModel(connection.Model) ? 2 : connection.PlanUseEnabled ? 4 : 3);
    public bool IsConnectStep => Step == 1;
    public bool IsModelStep => Step == 2;
    public bool IsConfirmStep => Step == 3;
    public bool IsReadyStep => Step == 4;
    public bool IsChatGptMode => !IsApiMode;
    public bool ShowAccountSelector => Accounts.Count > 1;
    public bool CanSelectAccount => CanManage && Accounts.Any(a => a.ClientId == SelectedClientId);
    public string StepTitle => Step switch { 1 => "1 · Conectar cuenta", 2 => "2 · Elegir modelo", 3 => "3 · Habilitar esta sesión", 4 => "Sesión preparada", _ => "API de pago seleccionada" };
    public string SessionSummary => IsApiMode ? "La clave y el modelo de API se configuran en Ajustes de API. No se utiliza el modelo de ChatGPT." : $"Cuenta: {Account}\nModelo guardado: {connection.Model ?? "sin elegir"}";
    public string ModeDescription => IsApiMode ? "Modo activo: API de pago. Usa facturación de API independiente de la suscripción. No se envía una prueba al elegir este modo." : "Modo activo: ChatGPT. Requiere permiso del plan y confirmación de créditos desactivados en cada sesión. Elegir el modo no genera análisis.";
    public string PrimaryLabel => Step switch { 1 => connection.HasPendingRegistration ? "Continuar registro pendiente" : "Continuar con ChatGPT", 2 => Models.Count == 0 ? CatalogState == ModelCatalogState.NotRequested ? "Consultar modelos" : "Consultar de nuevo" : "Guardar modelo seleccionado", 3 => "Habilitar consultas con mi plan", 4 => "Volver a lectura", _ => "Abrir ajustes de API de pago" };
    public bool CanPrimary => CanManage && (Step != 2 || Models.Count == 0 || CanSaveModel) && (Step != 3 || CanEnablePlan);
    public string PrimaryReason => busy ? Operation switch
    {
        ConnectionOperation.Access => "Completa el acceso y los permisos en el navegador. Puedes cancelar desde aquí.",
        ConnectionOperation.Catalog => "Consultando el catálogo. Puedes cancelar; no se genera un análisis.",
        _ => "Espera a que termine la operación o pulsa Cancelar. No se genera un análisis."
    } : Step switch
    {
        1 => connection.HasPendingRegistration ? "Continúa el registro pendiente con la cuenta y los permisos elegidos en el navegador." : "Conecta la cuenta elegida y concede el permiso de uso del plan en el navegador.",
        2 => !HasModels ? "Consulta el catálogo para elegir un modelo. No se genera un análisis." : !CanSaveModel ? "Selecciona un modelo disponible antes de guardarlo." : "Guarda el modelo seleccionado y revisa la habilitación de esta sesión.",
        3 => !CreditsDisabledConfirmed ? "Confirma el ajuste externo de créditos para habilitar esta sesión." : "Habilita las consultas para esta sesión. No se genera un análisis al habilitar.",
        4 => "La sesión está habilitada. Vuelve a lectura para generar cuando tú lo decidas.",
        _ => "Configura la clave y el modelo en Ajustes de API de pago. No se envía una prueba al abrirlos."
    };
    private AiException? DiagnosticFailure => operationFailure ?? connection.CatalogFailure;
    public string DiagnosticDetails => $"Modo: {connection.Mode}\nEstado: {connection.Readiness}\nOperación: {Operation}\nEn curso: {busy}\nÚltimo acceso: {connection.LoginOutcome}\nFallo: {DiagnosticFailure?.Failure ?? connection.LoginFailure}\nEtapa: {DiagnosticFailure?.AuthenticationStep ?? connection.LoginStep}\nCódigo: {DiagnosticFailure?.OAuthCode ?? connection.LoginCode}\nHTTP: {(DiagnosticFailure?.HttpStatus ?? connection.LoginHttpStatus)?.ToString() ?? "no comunicado"}\nCatálogo: {CatalogState} · {Models.Count} modelos";
    public void Review(int step) { if (busy || step is < 1 or > 3 || step > 1 && !connection.PlanGranted || step == 3 && !AiSettingsRules.IsValidModel(connection.Model)) return; ClearOperationResult(); reviewStep = step; NotifyWorkflow(); }
    public Task PrimaryAsync() => Step switch { 1 => ConnectAsync(false), 2 => Models.Count == 0 ? ModelsAsync() : SelectModelAsync(), 3 => EnablePlanAsync(), _ => Task.CompletedTask };
    private void NotifyWorkflow()
    {
        foreach (var name in new[] { nameof(Step), nameof(StepTitle), nameof(IsConnectStep), nameof(IsModelStep), nameof(IsConfirmStep), nameof(IsReadyStep), nameof(IsChatGptMode), nameof(ShowAccountSelector), nameof(CanSelectAccount), nameof(SessionSummary), nameof(ModeDescription), nameof(PrimaryLabel), nameof(CanPrimary), nameof(PrimaryReason), nameof(DiagnosticDetails) }) Changed(name);
    }
    private void ConnectionChanged(object? sender, EventArgs args)
    {
        if (observedAccount != connection.CacheScope || observedModel != connection.Model || !connection.Connected || !connection.PlanGranted)
        { creditsDisabledConfirmed = false; reviewStep = null; Changed(nameof(CreditsDisabledConfirmed)); }
        observedAccount = connection.CacheScope; observedModel = connection.Model;
        Synchronize();
    }
}
