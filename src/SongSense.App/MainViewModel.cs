using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SongSense.Core;

namespace SongSense.App;

public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private readonly ISettingsStore store;
    private readonly ILyricsProvider lyricsProvider;
    private readonly CancellationToken lifetime;
    private bool initializing;
    private readonly ICacheStore? cache;
    private readonly SemaphoreSlim cacheGate = new(1, 1);
    private readonly List<Task> cacheTasks = [];
    private bool storageReady;
    private long cacheEpoch;
    private ResolvedLyrics? pendingCachedLyrics;
    private string cacheMessage = "";
    public string CacheMessage => cacheMessage;
    public bool HasPendingCachedLyrics => pendingCachedLyrics is not null;
    public bool CanRecoverStorage => StorageError && store is IRecoverableSettingsStore { CanRecover: true };
    private TrackDetection detection = new(ApplicationState.NoSpotify, null, null);
    private string storageMessage = "Preparando almacenamiento local…";
    private bool storageError;
    private readonly IInsightProvider? insightProvider;
    private readonly IAnalysisSession? connection;
    private string usageMessage = "Todavía no se ha enviado ninguna consulta de IA.";
    public string UsageMessage => usageMessage;
    private string? EffectiveModel => connection?.Mode == AiConnectionMode.ChatGptIncluded ? connection.Model : aiSettings.Model;
    private string CacheScope => connection?.CacheScope ?? "api-key";
    private bool CanUseAi => connection?.Mode == AiConnectionMode.ChatGptIncluded ? connection.CanGenerate : (connection?.CanGenerate ?? true) && aiSettings.AiEnabled;
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
    public string AiConfigurationStatus => connection?.Mode == AiConnectionMode.ChatGptIncluded ? connection.Status :
        aiSettings.AiEnabled ? $"API key · de pago · {aiSettings.Model} · Hoy: {TodayRequests}/{aiSettings.DailyRequestLimit}" : "API key · de pago · IA desactivada";
    public void ApplyConnectionChange()
    {
        configurationRevision++; InvalidateInsight(); NotifyInsight();
        if (storageReady) QueueCacheRestore();
    }
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
        if (storageReady) QueueCacheRestore();
    }
    private CancellationTokenSource? lyricsRequest;
    private readonly List<Task> searches = [];
    private bool searching;
    private ApplicationState? lyricsState;
    private string lyricsMessage = "";
    public ResolvedLyrics? PreparedLyrics { get; private set; }
    public IReadOnlyList<LyricsCandidate> Candidates { get; private set; } = [];
    public long LyricsRevision { get; private set; }
    public bool CanEditLyrics => CurrentTrack is not null && !searching && !lifetime.IsCancellationRequested && (cache is null || storageReady && !StorageError);
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
        pendingCachedLyrics = null;
        InvalidateInsight();
        PreparedLyrics = lyrics;
        // Consumers of future analyses must bind results to both track and lyrics revisions.
        LyricsRevision++;
        lyricsState = lyrics.IsInstrumental ? ApplicationState.Ready : ApplicationState.AiNotConfigured;
        lyricsMessage = lyrics.IsInstrumental ? "Esta canción es instrumental" : "Letra confirmada. Usa Traducir y explicar cuando la IA esté activada.";
        NotifyLyrics();
        if (CurrentTrack is { } track) QueueCacheSave(track, lyrics);
        ScheduleAutomatic(true);
    }

    public MainViewModel(ISettingsStore store, ILyricsProvider lyricsProvider, CancellationToken lifetime = default,
        IInsightProvider? insightProvider = null, TimeProvider? clock = null, ICacheStore? cache = null, IAnalysisSession? connection = null)
    {
        this.store = store;
        this.lyricsProvider = lyricsProvider;
        this.lifetime = lifetime;
        this.insightProvider = insightProvider;
        this.clock = clock ?? TimeProvider.System;
        this.cache = cache;
        this.connection = connection;
        RetryAutomaticCommand = new AsyncCommand(RetryAutomaticAsync, () => AutomaticUpdatesEnabled && storageReady && !StorageError && CurrentTrack is not null && !automaticRunning && !automaticStopped);
        RetryStorageCommand = new AsyncCommand(InitializeAsync, () => !initializing && StorageError);
        SearchLyricsCommand = new AsyncCommand(SearchLyricsAsync, () => CurrentTrack is not null && !searching && !StorageError);
        CancelLyricsCommand = new ActionCommand(CancelLyrics, () => searching);
        GenerateInsightCommand = new AsyncCommand(GenerateInsightAsync, () => insightProvider is not null &&
            CurrentTrack is not null && PreparedLyrics is { IsInstrumental: false } && !searching && !analysing &&
            CanUseAi && AiSettingsRules.IsValidModel(EffectiveModel) && HasQuota && !StorageError &&
            !lifetime.IsCancellationRequested && Insight is null);
        CancelInsightCommand = new ActionCommand(CancelInsight, () => analysing);
        RegenerateCommand = new AsyncCommand(RegenerateInsightAsync, () => Insight is not null && CanRequestInsight());
        DeleteSongCommand = new AsyncCommand(() => DeleteCacheAsync(false), () => cache is not null && CurrentTrack is not null && !StorageError);
        ClearCacheCommand = new AsyncCommand(() => DeleteCacheAsync(true), () => cache is not null && !StorageError);
        ConfirmCachedLyricsCommand = new ActionCommand(ConfirmCachedLyrics, () => pendingCachedLyrics is not null && CanEditLyrics);
        RecoverStorageCommand = new AsyncCommand(RecoverStorageAsync, () => CanRecoverStorage && !initializing);
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
    public AsyncCommand RegenerateCommand { get; }
    public AsyncCommand DeleteSongCommand { get; }
    public AsyncCommand ClearCacheCommand { get; }
    public ActionCommand ConfirmCachedLyricsCommand { get; }
    public AsyncCommand RecoverStorageCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public void ApplyDetection(TrackDetection observation)
    {
        latestObservation = observation;
        if (automaticStarted && !AutomaticUpdatesEnabled) return;
        ApplyActiveDetection(observation);
    }
    private void ApplyActiveDetection(TrackDetection observation)
    {
        var changed = CurrentTrack?.Revision != observation.Track?.Revision;
        if (changed)
        {
            pendingCachedLyrics = null; cacheMessage = "";
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
        if (changed && storageReady && !automaticStarted) QueueCacheRestore();
        if (changed) ScheduleAutomatic();
    }

    private void NotifyLyrics()
    {
        foreach (var property in new[] { nameof(Status), nameof(LyricsMessage), nameof(OriginalLyrics), nameof(HasPreparedLyrics), nameof(PreparedLyrics), nameof(Candidates), nameof(LyricsRevision), nameof(CanEditLyrics), nameof(LyricsSource) }) OnChanged(property);
        SearchLyricsCommand.Refresh();
        CancelLyricsCommand.Refresh();
        OnChanged(nameof(CacheMessage)); OnChanged(nameof(HasPendingCachedLyrics));
        ConfirmCachedLyricsCommand.Refresh(); DeleteSongCommand.Refresh(); ClearCacheCommand.Refresh();
        NotifyInsight();
    }

    public Task SearchLyricsAsync()
    {
        if (!SearchLyricsCommand.CanExecute(null) || CurrentTrack is not { } track || lifetime.IsCancellationRequested) return Task.CompletedTask;
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        InvalidateInsight();
        pendingCachedLyrics = null;
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
            var cached = cache is null ? null : await CacheIoAsync(() => cache.ReadSongAsync(track, request.Token), request.Token);
            if (!IsCurrent(track, request)) return;
            if (cached is { RequiresConfirmation: true })
            {
                pendingCachedLyrics = cached.Lyrics; lyricsState = ApplicationState.ChoosingVersion;
                cacheMessage = "Faltan metadatos para identificar la versión. Confirma Usar letra guardada o introduce otra letra.";
                return;
            }
            var result = cached is null ? await lyricsProvider.FindAsync(track, request.Token) :
                new LyricsSearchResult(track.Revision, cached.IsAbsence ? LyricsSearchKind.NotFound : cached.Lyrics!.IsInstrumental ? LyricsSearchKind.Instrumental : LyricsSearchKind.Matched, cached.Lyrics, []);
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
            if (cached is null && result.Kind is LyricsSearchKind.Matched or LyricsSearchKind.Instrumental or LyricsSearchKind.NotFound)
                QueueCacheSave(track, result.Lyrics);
            if (cached is not null) { lyricsMessage = cached.IsAbsence ? "Ausencia recuperada de caché; caduca una hora después de la consulta original." : "Letra recuperada de la caché local."; QueueCacheRestore(); }
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
                if (cache is not null) QueueCacheRestore();
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
        automaticStopped = true; automaticPending = false; automaticRequest?.Cancel();
        CancelLyrics();
        CancelInsight();
        await automaticWorker;
        await Task.WhenAll(searches.Concat(generations));
        await WaitForCacheAsync();
    }

    private void InvalidateInsight()
    {
        insightRequest?.Cancel(); insightRequest = null; analysing = false;
        Insight = null; insightState = null; insightMessage = "";
        usageMessage = "Todavía no se ha enviado ninguna consulta de IA en este contexto.";
    }
    private void NotifyInsight()
    {
        if (insightState == ApplicationState.DailyLimit && HasQuota) insightState = null;
        foreach (var property in new[] { nameof(Status), nameof(Insight), nameof(HasInsight), nameof(InsightMessage), nameof(UsageMessage), nameof(TranslationMessage), nameof(TranslatedLyrics), nameof(InsightSummary), nameof(InsightThemes), nameof(InsightMetaphors), nameof(InsightAlternatives), nameof(InsightWarnings), nameof(AiConfigurationStatus) }) OnChanged(property);
        GenerateInsightCommand.Refresh(); CancelInsightCommand.Refresh();
        RegenerateCommand.Refresh();
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
    private bool CanRequestInsight() => insightProvider is not null && CurrentTrack is not null &&
        PreparedLyrics is { IsInstrumental: false } && !searching && !analysing && CanUseAi &&
        AiSettingsRules.IsValidModel(EffectiveModel) && HasQuota && !StorageError && !lifetime.IsCancellationRequested;
    public Task GenerateInsightAsync() => GenerateInsightAsync(false);
    public Task RegenerateInsightAsync() => GenerateInsightAsync(true);
    private Task GenerateInsightAsync(bool force)
    {
        if (!(force ? RegenerateCommand.CanExecute(null) : GenerateInsightCommand.CanExecute(null)) || CurrentTrack is not { } track || PreparedLyrics is not { } lyrics) return Task.CompletedTask;
        var revision = LyricsRevision;
        var configuration = configurationRevision;
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        insightRequest = request; analysing = true;
        insightMessage = "Traduciendo y explicando… Solicitud de pago en curso.";
        usageMessage = "Consumo pendiente de comunicar por OpenAI; no se puede calcular el porcentaje del plan a partir de tokens.";
        NotifyInsight();
        generations.RemoveAll(task => task.IsCompleted);
        var task = GenerateCoreAsync(track, lyrics, revision, configuration, EffectiveModel!, CacheScope, force, request);
        generations.Add(task);
        return task;
    }
    private bool InsightIsCurrent(CurrentTrack track, long revision, long configuration, CancellationTokenSource request) =>
        !request.IsCancellationRequested && ReferenceEquals(insightRequest, request) && CurrentTrack?.Revision == track.Revision &&
        LyricsRevision == revision && configurationRevision == configuration;
    private async Task GenerateCoreAsync(CurrentTrack track, ResolvedLyrics lyrics, long revision, long configuration, string model, string scope, bool force, CancellationTokenSource request)
    {
        try
        {
            SongInsight? result = null;
            if (!force && cache is not null)
                result = await CacheIoAsync(() => cache.ReadInsightAsync(lyrics, model, CacheIdentity.Version, request.Token, track, scope), request.Token);
            if (!InsightIsCurrent(track, revision, configuration, request)) return;
            var fromCache = result is not null;
            result ??= await insightProvider!.GenerateAsync(track, lyrics, request.Token);
            if (!InsightIsCurrent(track, revision, configuration, request)) return;
            usageMessage = fromCache ? "Caché local · 0 tokens nuevos; no se ha enviado una consulta." : DescribeUsage(result.Usage);
            if (result.TrackRevision != track.Revision) throw new AiException(AiFailure.InvalidResponse);
            if (result.Model is not null && result.Model != model || result.PromptVersion is not null && result.PromptVersion != CacheIdentity.Version)
            {
                insightState = ApplicationState.Error;
                insightMessage = "El modelo o la versión cambió durante la solicitud. No se ha guardado el resultado con una clave incorrecta. Revisa los ajustes antes de reintentar.";
                return;
            }
            Insight = result with { LyricsRevision = revision };
            insightState = ApplicationState.Ready;
            insightMessage = "Análisis recibido y formato validado. Interpretación generada por IA.";
            if (cache is not null)
            {
                try { await CacheIoAsync(async () => { await cache.SaveInsightAsync(track, lyrics, model, CacheIdentity.Version, result, request.Token, scope); return true; }, request.Token); }
                catch (OperationCanceledException) when (request.IsCancellationRequested) { }
                catch (Exception) { cacheMessage = "El análisis se muestra, pero no se pudo guardar en la caché local."; OnChanged(nameof(CacheMessage)); }
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (AiException error)
        {
            if (!InsightIsCurrent(track, revision, configuration, request)) return;
            usageMessage = DescribeUsage(error.Usage);
            insightState = error.Failure == AiFailure.DailyLimit ? ApplicationState.DailyLimit :
                error.Failure == AiFailure.Offline ? ApplicationState.Offline : ApplicationState.Error;
            insightMessage = AiSettingsViewModel.Describe(error.Failure);
        }
        catch (Exception)
        {
            if (!InsightIsCurrent(track, revision, configuration, request)) return;
            insightState = ApplicationState.Error;
            usageMessage = DescribeUsage(null);
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
        usageMessage = "Consulta cancelada · Consumo no comunicado; puede haber consumido tokens.";
        NotifyInsight();
    }
    private static string DescribeUsage(AiUsage? usage) => usage is null ? "Consumo no comunicado por OpenAI; no equivale a 0 tokens." : usage.Describe();

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
                if (store is IAutomaticUpdateSettings preferences) automaticUpdatesEnabled = await preferences.ReadAutomaticUpdatesAsync(lifetime);
                return await store.ReadSettingsAsync(lifetime);
            }, lifetime);
            ApplyAiSettings(settings);
            await RefreshAiUsageAsync();
            StorageError = false;
            storageReady = true;
            StorageMessage = "Almacenamiento local listo";
            if (!automaticStarted) QueueCacheRestore();
            NotifyAutomatic(); ScheduleAutomatic();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            StorageError = true;
            storageReady = false;
            StorageMessage = "No se pudo abrir la base local. Comprueba el acceso a %LOCALAPPDATA%\\SongSense y usa una versión compatible con la base. Después, pulsa Reintentar. Tus datos se conservan.";
            if (store is IRecoverableSettingsStore { CanRecover: true } recovery) StorageMessage = recovery.RecoveryMessage;
        }
        finally
        {
            initializing = false;
            OnChanged(nameof(Status));
            RetryStorageCommand.Refresh();
            OnChanged(nameof(CanRecoverStorage)); RecoverStorageCommand.Refresh();
            NotifyLyrics();
        }
    }

    private async Task<T> CacheIoAsync<T>(Func<Task<T>> operation, CancellationToken token)
    {
        await cacheGate.WaitAsync(token);
        try { return await Task.Run(operation, token); }
        finally { cacheGate.Release(); }
    }
    private void TrackCacheTask(Task task) { cacheTasks.RemoveAll(item => item.IsCompleted); cacheTasks.Add(task); }
    public async Task WaitForCacheAsync()
    {
        while (cacheTasks.Any(task => !task.IsCompleted)) await Task.WhenAll(cacheTasks.ToArray());
    }
    private void QueueCacheRestore()
    {
        if (cache is not null && storageReady && CurrentTrack is { } track) TrackCacheTask(RestoreCacheAsync(track, LyricsRevision, configurationRevision, cacheEpoch));
    }
    private async Task RestoreCacheAsync(CurrentTrack track, long revision, long configuration, long epoch)
    {
        try
        {
            var song = await CacheIoAsync(() => cache!.ReadSongAsync(track, lifetime), lifetime);
            if (!CacheContextCurrent(track, revision, configuration, epoch) || searching || analysing) return;
            if (PreparedLyrics is null && song is not null)
            {
                if (song.RequiresConfirmation)
                {
                    pendingCachedLyrics = song.Lyrics;
                    cacheMessage = "Hay contenido guardado, pero faltan metadatos. Confirma Usar letra guardada o selecciona otra versión.";
                    NotifyLyrics(); return;
                }
                if (song.IsAbsence) { lyricsState = ApplicationState.NoLyrics; lyricsMessage = "Ausencia guardada durante una hora desde la consulta original."; NotifyLyrics(); return; }
                PreparedLyrics = song.Lyrics; LyricsRevision++; revision = LyricsRevision;
                lyricsMessage = "Letra recuperada de la caché local."; NotifyLyrics();
            }
            if (PreparedLyrics is { IsInstrumental: false } lyrics && AiSettingsRules.IsValidModel(EffectiveModel))
            {
                var insight = await CacheIoAsync(() => cache!.ReadInsightAsync(lyrics, EffectiveModel!, CacheIdentity.Version, lifetime, track, CacheScope), lifetime);
                if (CacheContextCurrent(track, revision, configuration, epoch) && !analysing && insight is not null)
                { Insight = insight with { LyricsRevision = revision }; insightState = ApplicationState.Ready; insightMessage = "Análisis recuperado de la caché local, sin solicitud de IA."; usageMessage = "Caché local · 0 tokens nuevos; no se ha enviado una consulta."; NotifyInsight(); }
            }
            else if (PreparedLyrics is { IsInstrumental: true }) { lyricsState = ApplicationState.Ready; lyricsMessage = "Esta canción es instrumental · Caché local"; NotifyLyrics(); }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception) { if (CurrentTrack?.Revision == track.Revision && epoch == cacheEpoch) { cacheMessage = "No se pudo leer la caché local. Tus datos se conservan; no se ha hecho una llamada de IA."; OnChanged(nameof(CacheMessage)); } }
    }
    private bool CacheContextCurrent(CurrentTrack track, long revision, long configuration, long epoch) =>
        !lifetime.IsCancellationRequested && CurrentTrack?.Revision == track.Revision && LyricsRevision == revision && configurationRevision == configuration && epoch == cacheEpoch;
    private void QueueCacheSave(CurrentTrack track, ResolvedLyrics? lyrics)
    {
        if (cache is not null) TrackCacheTask(SaveCacheLyricsAsync(track, lyrics, cacheEpoch));
    }
    private async Task SaveCacheLyricsAsync(CurrentTrack track, ResolvedLyrics? lyrics, long epoch)
    {
        try
        {
            await CacheIoAsync(async () => { if (epoch != cacheEpoch) return false; await cache!.SaveLyricsAsync(track, lyrics, lifetime); return true; }, lifetime);
            if (epoch == cacheEpoch && CurrentTrack?.Revision == track.Revision) QueueCacheRestore();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception) { cacheMessage = "La letra se muestra, pero no se pudo guardar en la caché local."; OnChanged(nameof(CacheMessage)); }
    }
    private void ConfirmCachedLyrics()
    {
        if (pendingCachedLyrics is not { } lyrics || CurrentTrack is not { } track) return;
        pendingCachedLyrics = null; cacheMessage = "Letra guardada confirmada explícitamente.";
        ConfirmLyrics(lyrics with { TrackRevision = track.Revision });
    }
    public async Task DeleteCacheAsync(bool all)
    {
        if (cache is null || StorageError || (!all && CurrentTrack is null)) return;
        var track = CurrentTrack;
        cacheEpoch++; CancelLyrics(); InvalidateInsight(); pendingCachedLyrics = null;
        PreparedLyrics = null; Candidates = []; LyricsRevision++; lyricsState = null; lyricsMessage = "";
        NotifyLyrics();
        try
        {
            await CacheIoAsync(async () => { if (all) await cache.ClearAsync(lifetime); else await cache.DeleteSongAsync(track!, lifetime); return true; }, lifetime);
            cacheMessage = all ? "Caché borrada. Clave, ajustes y contador conservados." : "Contenido de esta canción borrado. Clave y contador conservados.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception) { cacheMessage = "No se pudo borrar la caché. Los datos locales se conservan."; }
        OnChanged(nameof(CacheMessage));
    }
    public async Task RecoverStorageAsync()
    {
        if (!CanRecoverStorage || store is not IRecoverableSettingsStore recovery) return;
        try { await Task.Run(() => recovery.RecoverAsync(lifetime), lifetime); await InitializeAsync(); StorageMessage = recovery.RecoveryMessage; }
        catch (Exception) { StorageMessage = "No se pudo recuperar la base. Se conservan el original y las copias locales; no se ha activado IA."; }
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
