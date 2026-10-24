using SongSense.Core;

namespace SongSense.App;

public sealed partial class MainViewModel
{
    private LyricsFailure? lastLyricsFailure;
    private AiFailure? lastAiFailure;
    private DateTimeOffset? lyricsRetryAt, aiRetryAt;
    private bool lyricsCancelled, aiCancelled, counterFailed;
    private DateOnly? aiFailureDate;
    public string SongDetails => $"{TrackTitle}\n{Artist}\nÁlbum: {Album}\n{Duration}\nVersión: " + (PreparedLyrics is null ? "Sin confirmar" : PreparedLyrics.Origin == LyricsOrigin.Manual ? "Edición manual" : Candidates.FirstOrDefault(c => c.Id == PreparedLyrics.LrclibId)?.Title ?? "Letra de LRCLIB confirmada") + $"\n{ReadingSource}\n{AnalysisDetails}\n{CacheMessage}";
    public PreparationDescriptor Preparation => PreparationRules.Evaluate(new()
    {
        StorageError = StorageError, Initializing = initializing, CanRecover = CanRecoverStorage,
        Stopped = lifetime.IsCancellationRequested || automaticStopped, HasTrack = CurrentTrack is not null,
        TrackingPaused = !AutomaticUpdatesEnabled,
        Deleting = deletingCache,
        Detection = detection.State, Searching = searching, Analysing = analysing,
        HasLyrics = HasPreparedLyrics, Instrumental = PreparedLyrics?.IsInstrumental == true,
        HasResult = HasInsight, Candidates = Candidates.Count, PendingCache = HasPendingCachedLyrics,
        NotFound = lyricsState == ApplicationState.NoLyrics, LyricsFailure = lastLyricsFailure, AiFailure = lastAiFailure,
        LyricsCancelled = lyricsCancelled, AiCancelled = aiCancelled, Now = clock.GetUtcNow(),
        LyricsRetryAt = lyricsRetryAt, AiRetryAt = aiRetryAt,
        LanguageSaving = languageSaving, LanguageFailed = languageSaveFailed,
        Language = OutputLanguages.Get(targetLanguage).Name, Mode = connection?.Mode ?? AiConnectionMode.ApiKey,
        Session = connection?.Readiness ?? AnalysisSessionState.Ready,
        Authorized = CanUseAi, ModelValid = AiSettingsRules.IsValidModel(EffectiveModel),
        HasProvider = insightProvider is not null, CounterFailed = counterFailed,
        HasQuota = HasQuota, Used = TodayRequests, Limit = aiSettings.DailyRequestLimit, Date = LocalDate,
        EditAllowed = CurrentTrack is not null && !lifetime.IsCancellationRequested && (cache is null || storageReady && !StorageError)
    });
    public NoticeKind DisplayNoticeKind => Preparation.Kind;
    public string DisplayStatus => Preparation.Status;
    public string DisplayNotice => Preparation.Message;
    public string ReadingHint => Preparation.Message;
    // Current decision, independent of the provenance and usage of an earlier result.
    public string AiDecisionLabel => connection is null && string.IsNullOrEmpty(EffectiveModel)
        ? "IA sin configurar"
        : $"{(connection?.Mode == AiConnectionMode.ChatGptIncluded ? "ChatGPT" : "API de pago")} · {(AiSettingsRules.IsValidModel(EffectiveModel) ? EffectiveModel : "modelo pendiente")}";
    public PreparationAction AiDecisionAction => connection?.Mode != AiConnectionMode.ChatGptIncluded
        ? PreparationAction.OpenApi
        : connection.Readiness is AnalysisSessionState.Disconnected or AnalysisSessionState.PendingRegistration or AnalysisSessionState.PermissionRequired or AnalysisSessionState.Unavailable
            ? PreparationAction.OpenConnection
            : !AiSettingsRules.IsValidModel(EffectiveModel) ? PreparationAction.ChooseModel
            : connection.CanGenerate ? PreparationAction.ChooseModel : PreparationAction.ReviewPlan;
    public string CompactUsage => Usage.Compact;
    public bool ShowGenerate => Preparation.Action == PreparationAction.Generate;
    public bool ShowSearch => Preparation.Action == PreparationAction.Search;
    public bool ShowPrimaryAction => Preparation.HasAction;
    public bool CanPrimaryAction => Preparation.Action switch
    {
        PreparationAction.Search => SearchLyricsCommand.CanExecute(null),
        PreparationAction.Generate => GenerateInsightCommand.CanExecute(null),
        PreparationAction.Regenerate => RegenerateCommand.CanExecute(null),
        PreparationAction.CancelSearch => CancelLyricsCommand.CanExecute(null),
        PreparationAction.CancelAnalysis => CancelInsightCommand.CanExecute(null),
        PreparationAction.ChooseVersion or PreparationAction.EditLyrics => CanEditLyrics,
        PreparationAction.ConfirmCachedLyrics => ConfirmCachedLyricsCommand.CanExecute(null),
        PreparationAction.RetryLanguage => RetryLanguageCommand.CanExecute(null),
        PreparationAction.RetryStorage => RetryStorageCommand.CanExecute(null),
        PreparationAction.RecoverStorage => RecoverStorageCommand.CanExecute(null),
        PreparationAction.None => false,
        _ => !lifetime.IsCancellationRequested && !automaticStopped
    };
    public string PrimaryActionLabel => Preparation.ActionLabel;
    public bool ShowDiagnostic => Preparation.Action != PreparationAction.Diagnostic &&
        (Preparation.Blockers.Count > 0 || Preparation.Cause is not (PreparationCause.Ready or PreparationCause.Result));
    public string DiagnosticDetails => string.Join("\n\n", new[]
    {
        Preparation.Message, string.Join("\n", Preparation.Blockers.Where(b => b.Message != Preparation.Message).Select(b => "• " + b.Message)),
        StorageError ? StorageMessage : "", lastLyricsFailure is null ? "" : "Servicio de letras: " + lyricsMessage,
        lastAiFailure is null ? "" : "Última solicitud de IA: " + insightMessage, cacheMessage, Activity.Counter, UsageMessage,
        PreparedLyrics?.LrclibId is { } id ? $"Identificador de LRCLIB: {id}" : ""
    }.Where(text => !string.IsNullOrWhiteSpace(text)).Distinct());
    // A clock tick only refreshes local presentation/counters, never lyrics or inference.
    public async Task RefreshPreparationTimeAsync()
    {
        if (usageDate is { } date && date != LocalDate && !initializing) await RefreshAiUsageAsync();
        else NotifyInsight();
    }
}
