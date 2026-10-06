using System.ComponentModel;
using SongSense.Core;

namespace SongSense.App;

public sealed class LyricsEditorViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MainViewModel owner;
    private readonly LyricsEditContext context;
    private string manualText;
    private string error = "";
    private CandidateOption? selected;
    public LyricsEditorViewModel(MainViewModel owner, LyricsEditContext context)
    {
        this.owner = owner;
        this.context = context;
        TrackLabel = $"{owner.TrackTitle} · {owner.Artist}";
        Options = owner.Candidates.Select(candidate => new CandidateOption(candidate)).ToArray();
        manualText = owner.OriginalLyrics;
        ConfirmCommand = new ActionCommand(() => Commit(owner.SelectCandidate(context, Selected!.Candidate.Id)), () => IsCurrent && Selected is not null);
        SaveCommand = new ActionCommand(() => Commit(owner.SaveManual(context, ManualText)), () => IsCurrent);
        owner.PropertyChanged += OwnerChanged;
    }
    public string TrackLabel { get; }
    public IReadOnlyList<CandidateOption> Options { get; }
    public string VersionsMessage => Options.Count == 0 ? "No hay versiones disponibles. Puedes introducir una letra manual." : "Selecciona una versión y confírmala para preparar su letra.";
    public bool IsCurrent => owner.IsEditCurrent(context);
    public string Error => IsCurrent ? error : "La canción o la letra ha cambiado. Cierra y vuelve a abrir esta ventana.";
    public string ManualText { get => manualText; set { manualText = value; Changed(nameof(ManualText)); Changed(nameof(CharacterCount)); } }
    public string CharacterCount => $"{ManualText.Length:N0} / 20.000 caracteres";
    public CandidateOption? Selected { get => selected; set { selected = value; Changed(nameof(Selected)); Changed(nameof(Preview)); ConfirmCommand.Refresh(); } }
    public string Preview => Selected is null ? "" : Selected.Candidate.IsInstrumental ? "Esta canción es instrumental" : LyricsResolution.TextOf(Selected.Candidate);
    public ActionCommand ConfirmCommand { get; }
    public ActionCommand SaveCommand { get; }
    public event EventHandler? Committed;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Commit(string? failure)
    {
        error = failure ?? "";
        Changed(nameof(Error));
        if (failure is null) Committed?.Invoke(this, EventArgs.Empty);
    }
    private void OwnerChanged(object? sender, PropertyChangedEventArgs args)
    {
        Changed(nameof(IsCurrent)); Changed(nameof(Error));
        ConfirmCommand.Refresh(); SaveCommand.Refresh();
    }
    private void Changed(string property) => PropertyChanged?.Invoke(this, new(property));
    public void Dispose() => owner.PropertyChanged -= OwnerChanged;
}

public sealed record CandidateOption(LyricsCandidate Candidate)
{
    public override string ToString() => $"{Title} · {Details}";
    public string Title => Candidate.Title;
    public string Details => $"{Candidate.Artist} · {Candidate.Album ?? "Álbum desconocido"}\n" +
        (Candidate.Duration is { } duration ? $"{(int)duration.TotalMinutes}:{duration.Seconds:00}" : "Duración desconocida") + $" · LRCLIB #{Candidate.Id}";
}
