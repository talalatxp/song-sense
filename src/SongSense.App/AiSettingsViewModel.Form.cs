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
    private bool modeSubscribed;
    public bool ApiSelected => analysisSession?.Mode != AiConnectionMode.ChatGptIncluded;
    private void ModeChanged(object? sender, EventArgs args) => NotifyForm();
    public bool HasChanges => initialized && (draftKeyLength > 0 || enabled != saved.AiEnabled || model != (saved.Model ?? "") || limitText != saved.DailyRequestLimit.ToString(CultureInfo.InvariantCulture) || accepted != savedAcceptance);
    public string ChangeStatus => HasChanges ? "Cambios sin guardar" : "Sin cambios pendientes";
    public bool CanTest => CanManage && ApiSelected && !HasChanges && saved.AiEnabled && hasKey && AiSettingsRules.IsValidModel(saved.Model);
    public string TestReason => !ApiSelected ? "La API está bloqueada mientras el modo activo sea ChatGPT. Elige API de pago explícitamente en Cuenta y consumo." : HasChanges ? "Guarda los cambios antes de probar. Guardar no envía solicitudes." : !saved.AiEnabled ? "Activa IA, acepta el aviso y guarda para poder probar." : !hasKey ? "Guarda una clave para poder probar." : !AiSettingsRules.IsValidModel(saved.Model) ? "Guarda un ID de modelo válido para poder probar." : "Prueba explícita de pago: envía un texto fijo y consume una solicitud; puede generar coste.";
    public string EffectiveConfiguration => $"Modo activo: {(ApiSelected ? "API de pago" : "ChatGPT; API bloqueada")} · Modelo API guardado: {saved.Model ?? "sin elegir"} · {(saved.AiEnabled ? "activada" : "desactivada")}";
    public string ModelError { get; private set; } = "";
    public string LimitError { get; private set; } = "";
    public string KeyError { get; private set; } = "";
    public string NoticeError { get; private set; } = "";
    public string? FirstInvalidField { get; private set; }
    public event EventHandler? ValidationFailed;
    public void KeyEdited(int length) { draftKeyLength = length; KeyError = ""; NotifyForm(); }
    private void NotifyForm()
    {
        if (AiSettingsRules.IsValidModel(model) || !enabled && model.Length == 0) ModelError = "";
        if (int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out var correctedLimit) && correctedLimit is >= 1 and <= 100) LimitError = "";
        if (accepted || !enabled || saved.AiEnabled) NoticeError = "";
        if (FirstInvalidField is not null && ModelError.Length + LimitError.Length + KeyError.Length + NoticeError.Length == 0)
        {
            FirstInvalidField = null; messageKind = NoticeKind.Information;
            message = HasChanges ? "Corrección preparada. Guarda antes de probar." : "Sin cambios pendientes; se conserva la configuración guardada.";
            PropertyChanged?.Invoke(this, new(nameof(Message))); PropertyChanged?.Invoke(this, new(nameof(MessageKind)));
        }
        foreach (var name in new[] { nameof(AiEnabled), nameof(Model), nameof(Limit), nameof(NoticeAccepted), nameof(HasChanges), nameof(ChangeStatus), nameof(CanTest), nameof(TestReason), nameof(EffectiveConfiguration), nameof(ModelError), nameof(LimitError), nameof(KeyError), nameof(NoticeError), nameof(FirstInvalidField) }) PropertyChanged?.Invoke(this, new(name));
    }
    private void ClearErrors() { ModelError = LimitError = KeyError = NoticeError = ""; FirstInvalidField = null; }
    private bool ValidateForm(SecureString key)
    {
        ClearErrors();
        if ((enabled || model.Length > 0) && !AiSettingsRules.IsValidModel(model)) ModelError = "Escribe un ID de modelo sin espacios, de hasta 120 caracteres; no introduzcas la clave aquí.";
        if (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit is < 1 or > 100) LimitError = "Escribe un entero de 1 a 100, sin espacios ni decimales.";
        if (enabled && !hasKey && key.Length == 0) KeyError = "Introduce una clave o desactiva IA para guardar sin ella.";
        if (key.Length > 512) KeyError = "La clave admite como máximo 512 caracteres, sin espacios.";
        else if (key.Length > 0)
        {
            var pointer = Marshal.SecureStringToGlobalAllocUnicode(key); var chars = new char[key.Length];
            try { Marshal.Copy(pointer, chars, 0, chars.Length); if (chars.Any(c => c is < '!' or > '~')) KeyError = "La clave contiene espacios o caracteres no admitidos. No se recorta automáticamente."; }
            finally { Array.Clear(chars); Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
        }
        if (enabled && !saved.AiEnabled && !accepted) NoticeError = "Acepta el aviso de envío y coste para activar IA.";
        FirstInvalidField = KeyError.Length > 0 ? "Key" : ModelError.Length > 0 ? "Model" : LimitError.Length > 0 ? "Limit" : NoticeError.Length > 0 ? "Notice" : null;
        if (FirstInvalidField is null) return true;
        messageKind = NoticeKind.Error; message = "Revisa los campos indicados. " + string.Join(" ", new[] { KeyError, ModelError, LimitError, NoticeError }.Where(x => x.Length > 0));
        Notify(); ValidationFailed?.Invoke(this, EventArgs.Empty); return false;
    }
    private Task BlockTest() { messageKind = NoticeKind.Warning; message = TestReason; Notify(); return Task.CompletedTask; }
}
