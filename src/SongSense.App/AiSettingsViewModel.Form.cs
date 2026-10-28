using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using SongSense.Core;

namespace SongSense.App;
public sealed partial class AiSettingsViewModel
{
    private AppSettings saved = new();
    private bool savedAcceptance;
    private int draftKeyLength;
    private string draftKeyError = "";
    private bool validationAttempted;
    private bool modeSubscribed;
    public bool ApiSelected => analysisSession?.Mode != AiConnectionMode.ChatGptIncluded;
    public string NoticeSummary => FormInstruction;
    public bool IsDraftValid => CurrentModelError.Length + CurrentLimitError.Length + CurrentKeyError.Length + CurrentNoticeError.Length == 0;
    public string FormInstruction => busy ? OperationMessage : !initialized ? message
        : !IsDraftValid ? "Borrador inválido. Revisa los campos antes de guardar o probar."
        : HasChanges ? "Cambios sin guardar. Guarda antes de probar; guardar no envía solicitudes."
        : "Sin cambios pendientes; se conserva la configuración guardada.";
    public NoticeKind FormNoticeKind => busy ? NoticeKind.Progress : !initialized ? messageKind : !IsDraftValid ? validationAttempted ? NoticeKind.Error : NoticeKind.Warning : NoticeKind.Information;
    private void ModeChanged(object? sender, EventArgs args) => NotifyForm();
    public bool HasChanges => initialized && (draftKeyLength > 0 || enabled != saved.AiEnabled || model != (saved.Model ?? "") || limitText != saved.DailyRequestLimit.ToString(CultureInfo.InvariantCulture) || accepted != savedAcceptance);
    public string ChangeStatus => HasChanges ? "Cambios sin guardar" : "Sin cambios pendientes";
    public bool HasDraftKey => draftKeyLength > 0;
    public string DiscardChangesMessage
    {
        get
        {
            var fields = new List<string>();
            if (model != (saved.Model ?? "")) fields.Add("modelo");
            if (limitText != saved.DailyRequestLimit.ToString(CultureInfo.InvariantCulture)) fields.Add("límite diario");
            if (enabled != saved.AiEnabled) fields.Add("activación de IA");
            if (accepted != savedAcceptance) fields.Add("aceptación del aviso");
            var text = fields.Count > 0 ? "Se descartarán los cambios sin guardar del formulario: " + string.Join(", ", fields) + ". " : "";
            if (HasDraftKey) text += "Se descartará la clave nueva escrita en este formulario; aún no se ha guardado. " + (hasKey ? "La clave guardada en este dispositivo se conserva. " : "");
            return text + "No se guardará ningún borrador. ¿Descartar y cerrar?";
        }
    }
    public bool CanTest => CanManage && ApiSelected && !HasChanges && IsDraftValid && saved.AiEnabled && hasKey && AiSettingsRules.IsValidModel(saved.Model);
    public string TestReason => !ApiSelected ? "La API está bloqueada mientras el modo activo sea ChatGPT. Elige API de pago explícitamente en Cuenta y consumo." : !IsDraftValid ? "Corrige el borrador antes de guardar o probar. Editar no envía solicitudes." : HasChanges ? "Guarda los cambios antes de probar. Guardar no envía solicitudes." : !saved.AiEnabled ? "Activa IA, acepta el aviso y guarda para poder probar." : !hasKey ? "Guarda una clave para poder probar." : !AiSettingsRules.IsValidModel(saved.Model) ? "Guarda un ID de modelo válido para poder probar." : "Prueba explícita de pago: envía un texto fijo y consume una solicitud; puede generar coste.";
    public string EffectiveConfiguration => $"Modo activo: {(ApiSelected ? "API de pago" : "ChatGPT; API bloqueada")} · Modelo API guardado: {saved.Model ?? "sin elegir"} · {(saved.AiEnabled ? "activada" : "desactivada")}";
    public string ModelError { get; private set; } = "";
    public string LimitError { get; private set; } = "";
    public string KeyError { get; private set; } = "";
    public string NoticeError { get; private set; } = "";
    public string? FirstInvalidField { get; private set; }
    public event EventHandler? ValidationFailed;
    public void KeyEdited(int length) { draftKeyLength = length; draftKeyError = length > 512 ? "La clave admite como máximo 512 caracteres, sin espacios." : ""; NotifyForm(); }
    public void KeyEdited(SecureString key) { draftKeyLength = key.Length; draftKeyError = KeyFormatError(key); NotifyForm(); }
    private string CurrentModelError => (enabled || model.Length > 0) && !AiSettingsRules.IsValidModel(model) ? "Escribe un ID de modelo sin espacios, de hasta 120 caracteres; no introduzcas la clave aquí." : "";
    private string CurrentLimitError => !int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit is < 1 or > 100 ? "Escribe un entero de 1 a 100, sin espacios ni decimales." : "";
    private string CurrentKeyError => draftKeyError.Length > 0 ? draftKeyError : enabled && !hasKey && draftKeyLength == 0 ? "Introduce una clave o desactiva IA para guardar sin ella." : "";
    private string CurrentNoticeError => enabled && !saved.AiEnabled && !accepted ? "Acepta el aviso de envío y coste para activar IA." : "";
    private void NotifyForm()
    {
        if (validationAttempted) UpdateErrors();
        foreach (var name in new[] { nameof(AiEnabled), nameof(Model), nameof(Limit), nameof(NoticeAccepted), nameof(HasChanges), nameof(ChangeStatus), nameof(CanTest), nameof(TestReason), nameof(EffectiveConfiguration), nameof(ModelError), nameof(LimitError), nameof(KeyError), nameof(NoticeError), nameof(FirstInvalidField), nameof(IsDraftValid), nameof(FormInstruction), nameof(FormNoticeKind), nameof(Message), nameof(MessageKind), nameof(NoticeSummary), nameof(HasDraftKey), nameof(DiscardChangesMessage) }) PropertyChanged?.Invoke(this, new(name));
    }
    private void ClearErrors() { validationAttempted = false; draftKeyError = ""; ModelError = LimitError = KeyError = NoticeError = ""; FirstInvalidField = null; }
    private void UpdateErrors()
    {
        ModelError = CurrentModelError; LimitError = CurrentLimitError; KeyError = CurrentKeyError; NoticeError = CurrentNoticeError;
        FirstInvalidField = KeyError.Length > 0 ? "Key" : ModelError.Length > 0 ? "Model" : LimitError.Length > 0 ? "Limit" : NoticeError.Length > 0 ? "Notice" : null;
    }
    private bool ValidateForm(SecureString key)
    {
        draftKeyLength = key.Length; draftKeyError = KeyFormatError(key); validationAttempted = true; UpdateErrors();
        if (FirstInvalidField is null) return true;
        Notify(); ValidationFailed?.Invoke(this, EventArgs.Empty); return false;
    }
    private static string KeyFormatError(SecureString key)
    {
        if (key.Length > 512) return "La clave admite como máximo 512 caracteres, sin espacios.";
        if (key.Length > 0)
        {
            var pointer = Marshal.SecureStringToGlobalAllocUnicode(key); var chars = new char[key.Length];
            try { Marshal.Copy(pointer, chars, 0, chars.Length); if (chars.Any(c => c is < '!' or > '~')) return "La clave contiene espacios o caracteres no admitidos. No se recorta automáticamente."; }
            finally { Array.Clear(chars); Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
        }
        return "";
    }
    private Task BlockTest() { Notify(); return Task.CompletedTask; }
}
