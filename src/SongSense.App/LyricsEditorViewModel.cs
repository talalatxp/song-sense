using System.ComponentModel;
using SongSense.Core;
namespace SongSense.App;
public sealed class LyricsEditorViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MainViewModel owner;
    private readonly LyricsEditContext context;
    private string manualText, originalText, error = "";
    private CandidateOption? selected;
    private bool manualTouched, saveAttempted, validationCorrected;
    public LyricsEditorViewModel(MainViewModel owner, LyricsEditContext context)
    {
        this.owner = owner; this.context = context; TrackLabel = $"{owner.TrackTitle} · {owner.Artist}";
        Options = owner.Candidates.Select(c => new CandidateOption(c)).ToArray(); originalText = manualText = owner.OriginalLyrics;
        ConfirmCommand = new AsyncCommand(() => SaveAsync(true), () => IsCurrent && !Saving && Selected is not null);
        SaveCommand = new AsyncCommand(() => SaveAsync(), () => IsCurrent && !Saving && IsValid);
        owner.PropertyChanged += OwnerChanged;
    }
    public string TrackLabel { get; }
    public IReadOnlyList<CandidateOption> Options { get; }
    public bool PreferManual => Options.Count == 0;
    public string VersionsMessage => Options.Count == 0 ? "No hay versiones disponibles. Puedes introducir una letra manual." : Selected is null ? "Selecciona una versión para revisar su letra." : "Vista previa de la letra completa";
    public bool IsCurrent => owner.IsEditCurrent(context);
    public bool Saving { get; private set; }
    public bool CanEditDraft => !Saving;
    public bool CanCopyDraft => ManualText.Length > 0;
    public bool HasChanges => manualText != originalText || Selected is not null;
    public bool IsValid => !string.IsNullOrWhiteSpace(ManualText) && ManualText.Length <= 20_000;
    public string Error => !IsCurrent ? "La canción o la letra ha cambiado. El borrador sigue vinculado a la canción original; puedes editarlo y copiarlo, pero no aplicarlo aquí."
        : error;
    public string ErrorSummary => !IsCurrent ? "Borrador anterior: puedes editarlo y copiarlo" : "No se guardó: revisa el motivo y reintenta";
    public string ManualError => ManualText.Length > 20_000 ? "Supera 20.000 caracteres. Corrige el exceso; no se recorta el texto." : !IsValid && (manualTouched || saveAttempted) ? "Introduce una letra que no esté vacía." : "";
    public string ManualFeedback => ManualError.Length > 0 ? ManualError : validationCorrected ? "Letra corregida: no está vacía y respeta los 20.000 caracteres." : "";
    public string ManualAccessibilityHint => ManualError.Length > 0 ? ManualError : ManualHint;
    public string ManualHint => !IsValid && ManualError.Length == 0 ? "Guardar requiere una letra no vacía, de hasta 20.000 caracteres." : "";
    public string SaveMessage { get; private set; } = "Borrador sin guardar · solo en esta ventana.";
    public string ManualText { get => manualText; set { var hadError = ManualError.Length > 0; manualText = value; manualTouched = true; validationCorrected = IsValid && (hadError || validationCorrected); error = ""; Notify(); } }
    public string CharacterCount => $"{ManualText.Length:N0} / 20.000 caracteres";
    public CandidateOption? Selected { get => selected; set { selected = value; error = ""; Notify(); } }
    public string Preview => Selected is null ? "" : Selected.Candidate.IsInstrumental ? "Esta canción es instrumental" : LyricsResolution.TextOf(Selected.Candidate);
    public string PreviewStanza => string.Join("\n", Preview.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Take(1));
    public AsyncCommand ConfirmCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public event EventHandler? Committed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public async Task SaveAsync(bool version = false)
    {
        if (!version) { saveAttempted = true; Notify(); }
        if (!IsCurrent || Saving || (version ? Selected is null : !IsValid)) return;
        Saving = true; Notify();
        try
        {
            var result = await owner.SaveEditedLyricsAsync(context, version ? null : ManualText, version ? Selected?.Candidate.Id : null);
            error = result.Success ? "" : result.Message; SaveMessage = result.Message;
            if (result.Success) { originalText = manualText; selected = null; Committed?.Invoke(this, EventArgs.Empty); }
        }
        finally { Saving = false; Notify(); }
    }
    private void OwnerChanged(object? sender, PropertyChangedEventArgs args) => Notify();
    private void Notify()
    {
        foreach (var name in new[] { nameof(ErrorSummary), nameof(ManualFeedback), nameof(ManualAccessibilityHint), nameof(ManualError), nameof(ManualHint), nameof(CanCopyDraft), nameof(VersionsMessage), nameof(ManualText), nameof(CharacterCount), nameof(Selected), nameof(Preview), nameof(PreviewStanza), nameof(IsCurrent), nameof(Saving), nameof(CanEditDraft), nameof(HasChanges), nameof(IsValid), nameof(Error), nameof(SaveMessage) }) PropertyChanged?.Invoke(this, new(name));
        ConfirmCommand.Refresh(); SaveCommand.Refresh();
    }
    public void Dispose() => owner.PropertyChanged -= OwnerChanged;
}
public sealed record CandidateOption(LyricsCandidate Candidate)
{
    public override string ToString() => $"{Title} · {Details}";
    public string Title => Candidate.Title;
    public string Details => $"{Candidate.Artist} · {Candidate.Album ?? "Álbum desconocido"} · " + (Candidate.Duration is { } d ? $"{(int)d.TotalMinutes}:{d.Seconds:00}" : "Duración desconocida");
}
