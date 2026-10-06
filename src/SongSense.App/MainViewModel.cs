using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SongSense.Core;

namespace SongSense.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ISettingsStore store;
    private readonly ILyricsProvider lyricsProvider;
    private readonly CancellationToken lifetime;
    private bool initializing;
    private TrackDetection detection = new(ApplicationState.NoSpotify, null, null);
    private string storageMessage = "Preparando almacenamiento local…";
    private bool storageError;
    private readonly IInsightProvider? insightProvider;
    private readonly TimeProvider clock;
    private CancellationTokenSource? insightRequest;
    private readonly List<Task> generations = [];
    private bool analysing;
    private ApplicationState? insightState;
    private string insightMessage = "";
    private int usedRequests;
    private DateOnly? usageDate;
    private long configurationRevision;
    private DateOnly LocalDate => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    private int TodayRequests => usageDate == LocalDate ? usedRequests : 0;
    private bool HasQuota => TodayRequests < aiSettings.DailyRequestLimit;
    public SongInsight? Insight { get; private set; }
    public bool HasInsight => Insight is not null;
    public string InsightMessage => insightMessage;
    public string TranslationMessage => Insight?.Language == "es" ? "La letra ya está en español" :
        Insight is not null ? $"Idioma original: {Insight.Language} · Traducción al español" : "Confirma una letra y pulsa Traducir y explicar.";
    public string TranslatedLyrics => Insight is not null && PreparedLyrics is not null ? InsightValidation.TranslationText(PreparedLyrics.Text, Insight) : "";
    public string InsightSummary => Insight?.Summary ?? "";
    public string InsightThemes => Insight is null ? "" : string.Join('\n', Insight.Themes.Select(text => "• " + text));
    public string InsightMetaphors => Insight is null ? "" : string.Join("\n\n", Insight.Metaphors.Select(item => $"«{item.Text}»\n{item.Explanation}"));
    public string InsightAlternatives => Insight is null ? "" : string.Join("\n\n", Insight.Alternatives.Select(text => "• " + text));
    public string InsightWarnings => Insight is null ? "" : string.Join('\n', Insight.Warnings.Select(text => "• " + text));
    private AppSettings aiSettings = new();
    public string AiConfigurationStatus => aiSettings.AiEnabled ? $"IA activada · {aiSettings.Model} · Hoy: {TodayRequests}/{aiSettings.DailyRequestLimit}" : "IA desactivada";
    public void ApplyAiSettings(AppSettings settings)
    {
        if (aiSettings.Model != settings.Model || aiSettings.AiEnabled != settings.AiEnabled)
        {
            configurationRevision++;
            InvalidateInsight();
        }
        aiSettings = settings;
        if (insightState == ApplicationState.DailyLimit && HasQuota) insightState = null;
        OnChanged(nameof(AiConfigurationStatus));
        OnChanged(nameof(Status));
        NotifyInsight();
    }
    private CancellationTokenSource? lyricsRequest;
    private readonly List<Task> searches = [];
    private bool searching;
    private ApplicationState? lyricsState;
    private string lyricsMessage = "";
    public ResolvedLyrics? PreparedLyrics { get; private set; }
    public IReadOnlyList<LyricsCandidate> Candidates { get; private set; } = [];
    public long LyricsRevision { get; private set; }
    public bool CanEditLyrics => CurrentTrack is not null && !searching && !lifetime.IsCancellationRequested;
    public string LyricsSource => PreparedLyrics is not { } lyrics ? "" : lyrics.Origin == LyricsOrigin.Manual
        ? "Fuente: Manual" : $"Fuente: LRCLIB · ID {lyrics.LrclibId}";

    public LyricsEditContext? BeginLyricsEdit() => CanEditLyrics ? new(CurrentTrack!.Revision, LyricsRevision) : null;
    public bool IsEditCurrent(LyricsEditContext context) => CanEditLyrics &&
        CurrentTrack!.Revision == context.TrackRevision && LyricsRevision == context.LyricsRevision;

    public string? SaveManual(LyricsEditContext context, string text)
    {
        if (!IsEditCurrent(context)) return "La canción o la letra ha cambiado. Cierra y vuelve a abrir el editor.";
        if (string.IsNullOrWhiteSpace(text)) return "Introduce una letra que no esté vacía.";
        if (text.Length > 20_000) return "La letra supera el máximo de 20.000 caracteres. No se ha guardado ni recortado.";
        ConfirmLyrics(new(context.TrackRevision, text, LyricsOrigin.Manual, null, false));
        return null;
    }

    public string? SelectCandidate(LyricsEditContext context, long id)
    {
        if (!IsEditCurrent(context)) return "La canción o la letra ha cambiado. Cierra y vuelve a abrir el selector.";
        var candidate = Candidates.FirstOrDefault(item => item.Id == id);
        if (candidate is null) return "Selecciona una versión disponible.";
        var text = LyricsResolution.TextOf(candidate);
        if (!candidate.IsInstrumental && string.IsNullOrWhiteSpace(text)) return "Esta versión no contiene una letra disponible.";
        ConfirmLyrics(new(context.TrackRevision, candidate.IsInstrumental ? "" : text!, LyricsOrigin.Lrclib, id, candidate.IsInstrumental));
        return null;
    }

    private void ConfirmLyrics(ResolvedLyrics lyrics)
    {
        InvalidateInsight();
        PreparedLyrics = lyrics;
        // Consumers of future analyses must bind results to both track and lyrics revisions.
        LyricsRevision++;
        lyricsState = lyrics.IsInstrumental ? ApplicationState.Ready : ApplicationState.AiNotConfigured;
        lyricsMessage = lyrics.IsInstrumental ? "Esta canción es instrumental" : "Letra confirmada. Usa Traducir y explicar cuando la IA esté activada.";
        NotifyLyrics();
    }

    public MainViewModel(ISettingsStore store, ILyricsProvider lyricsProvider, CancellationToken lifetime = default,
        IInsightProvider? insightProvider = null, TimeProvider? clock = null)
    {
        this.store = store;
        this.lyricsProvider = lyricsProvider;
        this.lifetime = lifetime;
        this.insightProvider = insightProvider;
        this.clock = clock ?? TimeProvider.System;
        RetryStorageCommand = new AsyncCommand(InitializeAsync, () => !initializing && StorageError);
        SearchLyricsCommand = new AsyncCommand(SearchLyricsAsync, () => CurrentTrack is not null && !searching && !StorageError);
        CancelLyricsCommand = new ActionCommand(CancelLyrics, () => searching);
        GenerateInsightCommand = new AsyncCommand(GenerateInsightAsync, () => insightProvider is not null &&
            CurrentTrack is not null && PreparedLyrics is { IsInstrumental: false } && !searching && !analysing &&
            aiSettings.AiEnabled && AiSettingsRules.IsValidModel(aiSettings.Model) && HasQuota && !StorageError &&
            !lifetime.IsCancellationRequested && Insight is null);
        CancelInsightCommand = new ActionCommand(CancelInsight, () => analysing);
    }

    public CurrentTrack? CurrentTrack => detection.Track;
    public string TrackTitle => CurrentTrack?.Title ?? "Todavía no hay una canción";
    public string Artist => CurrentTrack?.Artist ?? "El título y el artista aparecerán aquí";
    public string Album => CurrentTrack?.Album ?? "Álbum no disponible";
    public string Duration => CurrentTrack?.Duration is { } duration
        ? $"Duración: {(int)duration.TotalMinutes}:{duration.Seconds:00}" : "Duración desconocida";
    public string Status => (StorageError ? ApplicationState.Error :
        analysing ? ApplicationState.Processing :
        detection.State == ApplicationState.Paused && !searching ? ApplicationState.Paused :
        insightState is not null ? insightState.Value :
        lyricsState == ApplicationState.AiNotConfigured && aiSettings.AiEnabled ? ApplicationState.Ready :
        lyricsState ?? detection.State).InSpanish();
    public string LyricsMessage => lyricsMessage;
    public string OriginalLyrics => PreparedLyrics?.Text ?? "";
    public bool HasPreparedLyrics => PreparedLyrics is not null;
    public string DetectionMessage => detection.State switch
    {
        ApplicationState.NoSpotify => "Abre Spotify y reproduce una canción para verla aquí.",
        ApplicationState.NoTrack => "Spotify está disponible, pero no ofrece una canción identificable. Reproduce una canción con título y artista.",
        ApplicationState.Error => "No se pudieron leer las sesiones de Windows. Se reintentará cada 5 segundos; si persiste, reinicia Spotify.",
        _ => "Pulsa Buscar letra para consultar LRCLIB. También puedes seleccionar otra versión o introducir el texto con Cambiar letra."
    };
    public string StorageMessage { get => storageMessage; private set { storageMessage = value; OnChanged(); } }
    public bool StorageError { get => storageError; private set { storageError = value; OnChanged(); RetryStorageCommand.Refresh(); SearchLyricsCommand.Refresh(); GenerateInsightCommand.Refresh(); } }
    public AsyncCommand RetryStorageCommand { get; }
    public AsyncCommand SearchLyricsCommand { get; }
    public ActionCommand CancelLyricsCommand { get; }
    public AsyncCommand GenerateInsightCommand { get; }
    public ActionCommand CancelInsightCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public void ApplyDetection(TrackDetection observation)
    {
        if (CurrentTrack?.Revision != observation.Track?.Revision)
        {
            InvalidateInsight();
            lyricsRequest?.Cancel();
            lyricsRequest = null;
            searching = false;
            PreparedLyrics = null;
            LyricsRevision++;
            Candidates = [];
            lyricsState = null;
            lyricsMessage = "";
        }
        detection = observation;
        foreach (var property in new[] { nameof(CurrentTrack), nameof(TrackTitle), nameof(Artist), nameof(Album), nameof(Duration), nameof(Status), nameof(DetectionMessage) })
            OnChanged(property);
        NotifyLyrics();
    }

    private void NotifyLyrics()
    {
        foreach (var property in new[] { nameof(Status), nameof(LyricsMessage), nameof(OriginalLyrics), nameof(HasPreparedLyrics), nameof(PreparedLyrics), nameof(Candidates), nameof(LyricsRevision), nameof(CanEditLyrics), nameof(LyricsSource) }) OnChanged(property);
        SearchLyricsCommand.Refresh();
        CancelLyricsCommand.Refresh();
        NotifyInsight();
    }

    public Task SearchLyricsAsync()
    {
        if (!SearchLyricsCommand.CanExecute(null) || CurrentTrack is not { } track || lifetime.IsCancellationRequested) return Task.CompletedTask;
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        InvalidateInsight();
        lyricsRequest = request;
        searching = true;
        PreparedLyrics = null;
        LyricsRevision++;
        Candidates = [];
        lyricsState = ApplicationState.SearchingLyrics;
        lyricsMessage = "Buscando letra en LRCLIB…";
        NotifyLyrics();
        searches.RemoveAll(task => task.IsCompleted);
        var task = SearchCoreAsync(track, request);
        searches.Add(task);
        return task;
    }

    private bool IsCurrent(CurrentTrack track, CancellationTokenSource request) =>
        !request.IsCancellationRequested && ReferenceEquals(lyricsRequest, request) && CurrentTrack?.Revision == track.Revision;

    private async Task SearchCoreAsync(CurrentTrack track, CancellationTokenSource request)
    {
        try
        {
            var result = await lyricsProvider.FindAsync(track, request.Token);
            if (!IsCurrent(track, request) || result.TrackRevision != track.Revision) return;
            PreparedLyrics = result.Lyrics;
            Candidates = result.Candidates;
            (lyricsState, lyricsMessage) = result.Kind switch
            {
                LyricsSearchKind.Matched => (ApplicationState.AiNotConfigured, "Letra encontrada en LRCLIB. Usa Traducir y explicar cuando la IA esté activada."),
                LyricsSearchKind.Instrumental => (ApplicationState.Ready, "Esta canción es instrumental"),
                LyricsSearchKind.Candidates => (ApplicationState.ChoosingVersion, $"Hay {Candidates.Count} candidato(s). Pulsa Cambiar letra y confirma una versión; todavía no hay letra preparada."),
                _ => (ApplicationState.NoLyrics, "No se encontró una letra disponible. Puedes volver a buscar o introducirla con Cambiar letra.")
            };
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (LyricsProviderException error)
        {
            if (!IsCurrent(track, request)) return;
            lyricsState = error.Failure == LyricsFailure.Offline ? ApplicationState.Offline : ApplicationState.Error;
            lyricsMessage = error.Failure switch
            {
                LyricsFailure.RateLimited => error.RetryAt is { } until
                    ? $"LRCLIB ha limitado las consultas. Espera hasta {until.ToLocalTime():HH:mm:ss} y pulsa Buscar letra."
                    : "LRCLIB ha limitado las consultas. Espera 60 segundos y pulsa Buscar letra.",
                LyricsFailure.Offline => "No se pudo conectar con LRCLIB. Comprueba tu conexión y pulsa Buscar letra.",
                LyricsFailure.Timeout => "LRCLIB no respondió en 15 segundos. Pulsa Buscar letra para reintentar.",
                LyricsFailure.InvalidResponse => "LRCLIB devolvió un formato inválido. No se ha preparado ninguna letra. Pulsa Buscar letra para reintentar.",
                _ => "LRCLIB devolvió un error. Pulsa Buscar letra para reintentar más tarde."
            };
        }
        catch (Exception)
        {
            if (!IsCurrent(track, request)) return;
            lyricsState = ApplicationState.Error;
            lyricsMessage = "No se pudo buscar la letra. Pulsa Buscar letra para reintentar.";
        }
        finally
        {
            if (ReferenceEquals(lyricsRequest, request))
            {
                lyricsRequest = null;
                searching = false;
                NotifyLyrics();
            }
            request.Dispose();
        }
    }

    public void CancelLyrics()
    {
        if (!searching) return;
        lyricsRequest?.Cancel();
        lyricsRequest = null;
        searching = false;
        lyricsState = null;
        lyricsMessage = "Búsqueda cancelada. Puedes volver a intentarlo.";
        NotifyLyrics();
    }

    public async Task StopAsync()
    {
        CancelLyrics();
        CancelInsight();
        await Task.WhenAll(searches.Concat(generations));
    }

    private void InvalidateInsight()
    {
        insightRequest?.Cancel(); insightRequest = null; analysing = false;
        Insight = null; insightState = null; insightMessage = "";
    }
    private void NotifyInsight()
    {
        if (insightState == ApplicationState.DailyLimit && HasQuota) insightState = null;
        foreach (var property in new[] { nameof(Status), nameof(Insight), nameof(HasInsight), nameof(InsightMessage), nameof(TranslationMessage), nameof(TranslatedLyrics), nameof(InsightSummary), nameof(InsightThemes), nameof(InsightMetaphors), nameof(InsightAlternatives), nameof(InsightWarnings), nameof(AiConfigurationStatus) }) OnChanged(property);
        GenerateInsightCommand.Refresh(); CancelInsightCommand.Refresh();
    }
    public async Task RefreshAiUsageAsync()
    {
        var date = LocalDate;
        try
        {
            var count = await Task.Run(() => store.ReadRequestCountAsync(date, lifetime), lifetime);
            usageDate = date; usedRequests = count;
            NotifyInsight();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception) { insightMessage = "No se pudo actualizar el contador local. Se comprobará antes de enviar otra solicitud."; NotifyInsight(); }
    }
    public Task GenerateInsightAsync()
    {
        if (!GenerateInsightCommand.CanExecute(null) || CurrentTrack is not { } track || PreparedLyrics is not { } lyrics) return Task.CompletedTask;
        var revision = LyricsRevision;
        var configuration = configurationRevision;
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        insightRequest = request; analysing = true;
        insightMessage = "Traduciendo y explicando… Solicitud de pago en curso.";
        NotifyInsight();
        generations.RemoveAll(task => task.IsCompleted);
        var task = GenerateCoreAsync(track, lyrics, revision, configuration, request);
        generations.Add(task);
        return task;
    }
    private bool InsightIsCurrent(CurrentTrack track, long revision, long configuration, CancellationTokenSource request) =>
        !request.IsCancellationRequested && ReferenceEquals(insightRequest, request) && CurrentTrack?.Revision == track.Revision &&
        LyricsRevision == revision && configurationRevision == configuration;
    private async Task GenerateCoreAsync(CurrentTrack track, ResolvedLyrics lyrics, long revision, long configuration, CancellationTokenSource request)
    {
        try
        {
            var result = await insightProvider!.GenerateAsync(track, lyrics, request.Token);
            if (!InsightIsCurrent(track, revision, configuration, request)) return;
            if (result.TrackRevision != track.Revision) throw new AiException(AiFailure.InvalidResponse);
            Insight = result with { LyricsRevision = revision };
            insightState = ApplicationState.Ready;
            insightMessage = "Análisis recibido y formato validado. Interpretación generada por IA.";
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (AiException error)
        {
            if (!InsightIsCurrent(track, revision, configuration, request)) return;
            insightState = error.Failure == AiFailure.DailyLimit ? ApplicationState.DailyLimit :
                error.Failure == AiFailure.Offline ? ApplicationState.Offline : ApplicationState.Error;
            insightMessage = AiSettingsViewModel.Describe(error.Failure);
        }
        catch (Exception)
        {
            if (!InsightIsCurrent(track, revision, configuration, request)) return;
            insightState = ApplicationState.Error;
            insightMessage = "No se pudo analizar la letra. No se muestran resultados parciales ni se reintentará automáticamente.";
        }
        finally
        {
            if (ReferenceEquals(insightRequest, request)) { insightRequest = null; analysing = false; NotifyInsight(); }
            request.Dispose();
            await RefreshAiUsageAsync();
        }
    }
    public void CancelInsight()
    {
        if (!analysing) return;
        InvalidateInsight();
        insightMessage = "Análisis cancelado. La solicitud reservada cuenta para el límite diario.";
        NotifyInsight();
    }

    public async Task InitializeAsync()
    {
        if (initializing) return;
        initializing = true;
        RetryStorageCommand.Refresh();
        StorageMessage = "Preparando almacenamiento local…";
        try
        {
            // Microsoft.Data.Sqlite performs local I/O synchronously; keep it off the UI thread.
            var settings = await Task.Run(async () =>
            {
                await store.InitializeAsync(lifetime);
                return await store.ReadSettingsAsync(lifetime);
            }, lifetime);
            ApplyAiSettings(settings);
            await RefreshAiUsageAsync();
            StorageError = false;
            StorageMessage = "Almacenamiento local listo";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            StorageError = true;
            StorageMessage = "No se pudo abrir la base local. Comprueba el acceso a %LOCALAPPDATA%\\SongSense y usa una versión compatible con la base. Después, pulsa Reintentar. Tus datos se conservan.";
        }
        finally
        {
            initializing = false;
            OnChanged(nameof(Status));
            RetryStorageCommand.Refresh();
        }
    }
}

public sealed record LyricsEditContext(long TrackRevision, long LyricsRevision);

public sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) { if (CanExecute(parameter)) await execute(); }
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class ActionCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
