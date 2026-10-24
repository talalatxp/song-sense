using System.Net;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class PreparationTests
{
    private static readonly PreparationSnapshot Ready = new()
    {
        HasTrack = true, HasLyrics = true, Authorized = true, ModelValid = true, HasProvider = true,
        HasQuota = true, EditAllowed = true, Mode = AiConnectionMode.ApiKey, Date = new(2026, 10, 21), Now = DateTimeOffset.Parse("2026-10-21T12:00:00Z"), Limit = 3
    };
    public static IEnumerable<object[]> Matrix()
    {
        yield return Row(Ready with { StorageError = true }, PreparationCause.Storage, PreparationAction.RetryStorage);
        yield return Row(Ready with { Initializing = true }, PreparationCause.Initializing, PreparationAction.None);
        yield return Row(Ready with { HasTrack = false, Detection = ApplicationState.NoSpotify }, PreparationCause.NoSpotify, PreparationAction.None);
        yield return Row(Ready with { HasTrack = false, Detection = ApplicationState.NoTrack }, PreparationCause.NoTrack, PreparationAction.Diagnostic);
        yield return Row(Ready with { HasTrack = false, Detection = ApplicationState.Error }, PreparationCause.Detection, PreparationAction.Diagnostic);
        yield return Row(Ready with { Searching = true }, PreparationCause.Searching, PreparationAction.CancelSearch);
        yield return Row(Ready with { HasLyrics = false, Candidates = 2 }, PreparationCause.Versions, PreparationAction.ChooseVersion);
        yield return Row(Ready with { HasLyrics = false, PendingCache = true }, PreparationCause.CachedLyrics, PreparationAction.ConfirmCachedLyrics);
        yield return Row(Ready with { HasLyrics = false, NotFound = true }, PreparationCause.NotFound, PreparationAction.EditLyrics);
        yield return Row(Ready with { HasLyrics = false, LyricsFailure = LyricsFailure.Offline }, PreparationCause.LyricsOffline, PreparationAction.Search);
        yield return Row(Ready with { HasLyrics = false, LyricsFailure = LyricsFailure.Timeout }, PreparationCause.LyricsTimeout, PreparationAction.Search);
        yield return Row(Ready with { HasLyrics = false, LyricsFailure = LyricsFailure.RateLimited, LyricsRetryAt = Ready.Now.AddMinutes(1) }, PreparationCause.LyricsRateLimited, PreparationAction.EditLyrics);
        yield return Row(Ready with { HasLyrics = false, LyricsFailure = LyricsFailure.InvalidResponse }, PreparationCause.LyricsInvalid, PreparationAction.Search);
        yield return Row(Ready with { Instrumental = true }, PreparationCause.Instrumental, PreparationAction.ChooseVersion);
        yield return Row(Ready with { Mode = AiConnectionMode.ChatGptIncluded, Authorized = false, Session = AnalysisSessionState.Disconnected }, PreparationCause.SessionMissing, PreparationAction.OpenConnection);
        yield return Row(Ready with { Mode = AiConnectionMode.ChatGptIncluded, Authorized = false, Session = AnalysisSessionState.PendingRegistration }, PreparationCause.SessionPending, PreparationAction.OpenConnection);
        yield return Row(Ready with { ModelValid = false }, PreparationCause.ModelMissing, PreparationAction.OpenApi);
        yield return Row(Ready with { Mode = AiConnectionMode.ChatGptIncluded, Authorized = false, Session = AnalysisSessionState.ConfirmationRequired }, PreparationCause.PlanPaused, PreparationAction.ReviewPlan);
        yield return Row(Ready with { Authorized = false }, PreparationCause.ApiDisabled, PreparationAction.OpenApi);
        yield return Row(Ready with { HasQuota = false, Used = 3 }, PreparationCause.DailyLimit, PreparationAction.Diagnostic);
        yield return Row(Ready with { LanguageSaving = true }, PreparationCause.LanguageSaving, PreparationAction.None);
        yield return Row(Ready with { LanguageFailed = true }, PreparationCause.LanguageFailed, PreparationAction.RetryLanguage);
        yield return Row(Ready with { CounterFailed = true }, PreparationCause.CounterUnavailable, PreparationAction.RefreshCounter);
        yield return Row(Ready, PreparationCause.Ready, PreparationAction.Generate);
        yield return Row(Ready with { Analysing = true }, PreparationCause.Analysing, PreparationAction.CancelAnalysis);
        yield return Row(Ready with { HasResult = true }, PreparationCause.Result, PreparationAction.None);
        yield return Row(Ready with { AiFailure = AiFailure.Offline }, PreparationCause.AiFailed, PreparationAction.Generate);
        yield return Row(Ready with { AiFailure = AiFailure.Authentication }, PreparationCause.AiFailed, PreparationAction.OpenApi);
        yield return Row(Ready with { LyricsCancelled = true, HasLyrics = false }, PreparationCause.LyricsCancelled, PreparationAction.Search);
        yield return Row(Ready with { AiCancelled = true }, PreparationCause.AiCancelled, PreparationAction.Generate);
    }
    private static object[] Row(PreparationSnapshot snapshot, PreparationCause cause, PreparationAction action) => [snapshot, cause, action];
    [Fact]
    public async Task DecisionTracksModeModelAndReadinessWithoutInference()
    {
        var session = new Session { Mode = AiConnectionMode.ChatGptIncluded, Readiness = AnalysisSessionState.Disconnected };
        var provider = new Provider(); var vm = await Create(provider, session: session);
        var changes = new List<string?>(); vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.Equal("ChatGPT · fixture-model", vm.AiDecisionLabel);
        Assert.Equal(PreparationAction.OpenConnection, vm.AiDecisionAction);
        session.Model = null; session.Readiness = AnalysisSessionState.ModelRequired; session.Change();
        Assert.Equal("ChatGPT · modelo pendiente", vm.AiDecisionLabel);
        Assert.Equal(PreparationAction.ChooseModel, vm.AiDecisionAction);
        session.Model = "another-model"; session.Readiness = AnalysisSessionState.ConfirmationRequired; session.Change();
        Assert.Equal("ChatGPT · another-model", vm.AiDecisionLabel);
        Assert.Equal(PreparationAction.ReviewPlan, vm.AiDecisionAction);
        session.CanGenerate = true; session.Readiness = AnalysisSessionState.Ready; session.Change();
        Assert.Equal(PreparationAction.ChooseModel, vm.AiDecisionAction);
        session.Mode = AiConnectionMode.ApiKey; session.Change();
        Assert.Equal("API de pago · fixture-model", vm.AiDecisionLabel);
        Assert.Equal(PreparationAction.OpenApi, vm.AiDecisionAction);
        vm.ApplyAiSettings(new(false, null, 3));
        Assert.Equal("API de pago · modelo pendiente", vm.AiDecisionLabel);
        Assert.False(vm.GenerateInsightCommand.CanExecute(null));
        vm.ApplyAiSettings(new(false, "api-model", 3));
        Assert.Equal("API de pago · api-model", vm.AiDecisionLabel);
        session.CacheScope = "different-account"; session.Change();
        Assert.Equal("API de pago · api-model", vm.AiDecisionLabel);
        Assert.Contains(nameof(vm.AiDecisionLabel), changes);
        Assert.Contains(nameof(vm.AiDecisionAction), changes);
        Assert.Equal(0, provider.Calls); await vm.StopAsync();
    }
    [Fact]
    public async Task UnconfiguredAndFailedStatesKeepDecisionAndTruthfulHints()
    {
        var provider = new Provider { Error = AiFailure.Offline };
        var vm = await Create(provider);
        vm.ApplyAiSettings(new()); Assert.Equal("IA sin configurar", vm.AiDecisionLabel);
        vm.ApplyAiSettings(new(true, "fixture-model", 3));
        await vm.GenerateInsightAsync();
        Assert.Equal(PreparationCause.AiFailed, vm.Preparation.Cause);
        Assert.Equal("API de pago · fixture-model", vm.AiDecisionLabel);
        Assert.Equal(vm.Preparation.Message, vm.TranslationMessage);
        Assert.DoesNotContain("acción principal", vm.ReadingHint);
        await vm.StopAsync();
    }
    [Fact]
    public void EmptyStatesGivePossibleNextStepsAndQuotaKeepsReading()
    {
        var empty = PreparationRules.Evaluate(Ready with { HasTrack = false, HasLyrics = false, Detection = ApplicationState.NoSpotify });
        Assert.Contains("Abre Spotify", empty.Message); Assert.False(empty.HasAction);
        var noLyrics = PreparationRules.Evaluate(Ready with { HasLyrics = false });
        Assert.Equal(PreparationAction.Search, noLyrics.Action); Assert.True(noLyrics.CanSearch);
        var manual = PreparationRules.Evaluate(Ready with { HasLyrics = false, NotFound = true });
        Assert.Equal(PreparationAction.EditLyrics, manual.Action); Assert.True(manual.CanEdit);
        var instrumental = PreparationRules.Evaluate(Ready with { Instrumental = true });
        Assert.DoesNotContain("traduc", instrumental.Message, StringComparison.OrdinalIgnoreCase);
        var quota = PreparationRules.Evaluate(Ready with { HasQuota = false, Used = 3 });
        Assert.Contains("seguir leyendo", quota.Message); Assert.False(quota.CanGenerate);
        Assert.False(PreparationRules.Evaluate(Ready with { HasQuota = false, HasResult = true }).CanRegenerate);
    }
    [Theory, MemberData(nameof(Matrix))]
    public void MatrixHasOneCoherentStateAndAction(PreparationSnapshot snapshot, PreparationCause cause, PreparationAction action)
    {
        var state = PreparationRules.Evaluate(snapshot);
        Assert.Equal(cause, state.Cause); Assert.Equal(action, state.Action);
        Assert.NotEmpty(state.Message);
        if (action == PreparationAction.Generate) Assert.True(state.CanGenerate);
        if (action == PreparationAction.Search) Assert.True(state.CanSearch);
        if (action is PreparationAction.EditLyrics or PreparationAction.ChooseVersion) Assert.True(state.CanEdit);
        if (action == PreparationAction.None) Assert.False(state.HasAction);
    }
    [Fact]
    public void ConflictPrecedenceKeepsContentAndOtherRequirementsInDetail()
    {
        var paused = Ready with { Authorized = false, ModelValid = false, HasQuota = false, Used = 3 };
        var result = PreparationRules.Evaluate(paused with { HasResult = true });
        Assert.Equal(PreparationCause.Result, result.Cause); Assert.False(result.CanRegenerate);
        Assert.Equal(3, result.Blockers.Count);
        Assert.Equal(PreparationCause.Versions, PreparationRules.Evaluate(paused with { HasLyrics = false, Candidates = 2 }).Cause);
        Assert.Equal(PreparationCause.Storage, PreparationRules.Evaluate(paused with { Analysing = true, StorageError = true }).Cause);
        Assert.Equal(PreparationCause.Searching, PreparationRules.Evaluate(paused with { Searching = true, HasTrack = false }).Cause);
        Assert.Equal(PreparationCause.ModelMissing, PreparationRules.Evaluate(paused).Cause);
        Assert.Equal(PreparationCause.LanguageFailed, PreparationRules.Evaluate(paused with { LanguageFailed = true }).Cause);
        Assert.Equal(PreparationCause.Ready, PreparationRules.Evaluate(Ready with { Detection = ApplicationState.Paused }).Cause);
    }
    [Fact]
    public void CooldownReleasesOnlyTheManualActionAndNeverClaimsNoMatch()
    {
        var limited = Ready with { HasLyrics = false, LyricsFailure = LyricsFailure.RateLimited, LyricsRetryAt = Ready.Now.AddMinutes(1) };
        var wait = PreparationRules.Evaluate(limited);
        Assert.False(wait.CanSearch); Assert.Contains("esperar", wait.Message);
        var expired = PreparationRules.Evaluate(limited with { Now = Ready.Now.AddMinutes(2) });
        Assert.True(expired.CanSearch); Assert.Equal(PreparationAction.Search, expired.Action);
        var ai = PreparationRules.Evaluate(Ready with { AiRetryAt = Ready.Now.AddMinutes(1) });
        Assert.Equal(PreparationCause.AiWait, ai.Cause); Assert.False(ai.CanGenerate);
    }
    [Fact]
    public async Task SessionEventsRefreshPresentationWithoutInference()
    {
        var session = new Session { Mode = AiConnectionMode.ChatGptIncluded, Readiness = AnalysisSessionState.PendingRegistration, CanGenerate = false };
        var provider = new Provider(); var vm = await Create(provider, session: session);
        var changes = new List<string?>(); vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.Equal(PreparationCause.SessionPending, vm.Preparation.Cause);
        session.Readiness = AnalysisSessionState.ConfirmationRequired; session.Change();
        Assert.Equal(PreparationAction.ReviewPlan, vm.Preparation.Action);
        Assert.Contains(nameof(vm.Preparation), changes); Assert.Contains(nameof(vm.CanPrimaryAction), changes);
        Assert.Equal(vm.DisplayNotice, vm.ReadingHint);
        Assert.Equal(0, provider.Calls); await vm.StopAsync();
    }
    [Fact]
    public async Task InitialQuotaAndMidnightShowDateWithoutMakingInference()
    {
        var clock = new Clock(); var store = new Store { Count = 3 }; var provider = new Provider();
        var vm = await Create(provider, store, clock: clock);
        Assert.Equal(PreparationCause.DailyLimit, vm.Preparation.Cause);
        Assert.Contains("3/3", vm.DisplayNotice); Assert.Contains("22/10/2026", vm.DisplayNotice);
        clock.Now += TimeSpan.FromDays(1); store.Count = 0; await vm.RefreshPreparationTimeAsync();
        Assert.True(vm.GenerateInsightCommand.CanExecute(null)); Assert.Equal(0, provider.Calls);
        await vm.StopAsync();
    }
    [Fact]
    public async Task LanguageSavingFailureAndRetryNeverGenerateAndKeepBothTabsConsistent()
    {
        var provider = new Provider(); var store = new Store { Write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var vm = await Create(provider, store);
        vm.TargetLanguage = "en";
        Assert.Equal(PreparationCause.LanguageSaving, vm.Preparation.Cause); Assert.False(vm.CanPrimaryAction);
        store.Write.SetException(new IOException("synthetic")); await vm.WaitForCacheAsync();
        Assert.Equal(PreparationCause.LanguageFailed, vm.Preparation.Cause);
        Assert.Equal(PreparationAction.RetryLanguage, vm.Preparation.Action);
        Assert.Equal(vm.DisplayNotice, vm.ReadingHint);
        store.Write = null; await vm.RetryLanguageAsync();
        Assert.Equal(PreparationAction.Generate, vm.Preparation.Action); Assert.Equal(0, provider.Calls);
        await vm.StopAsync();
    }
    [Theory]
    [InlineData(LyricsFailure.Offline, PreparationCause.LyricsOffline)] [InlineData(LyricsFailure.Timeout, PreparationCause.LyricsTimeout)]
    [InlineData(LyricsFailure.InvalidResponse, PreparationCause.LyricsInvalid)] [InlineData(LyricsFailure.RateLimited, PreparationCause.LyricsRateLimited)]
    public async Task LyricsFailuresRemainTypedAndCooldownCannotBeBypassed(LyricsFailure failure, PreparationCause cause)
    {
        var clock = new Clock(); var provider = new Provider { LyricsError = failure }; var vm = await Create(provider, clock: clock);
        await vm.SearchLyricsAsync();
        Assert.Equal(cause, vm.Preparation.Cause); Assert.DoesNotContain("No encontramos", vm.DisplayNotice);
        if (failure == LyricsFailure.RateLimited)
        {
            Assert.False(vm.SearchLyricsCommand.CanExecute(null));
            await vm.SearchLyricsAsync(); Assert.Equal(1, provider.Searches);
            clock.Now += TimeSpan.FromSeconds(61); await vm.RefreshPreparationTimeAsync();
            Assert.True(vm.SearchLyricsCommand.CanExecute(null)); Assert.Equal(1, provider.Searches);
        }
        await vm.StopAsync();
    }
    [Fact]
    public async Task AiFailureKeepsLyricsAndCooldownDoesNotRetry()
    {
        var clock = new Clock(); var provider = new Provider { Error = AiFailure.RateLimited }; var vm = await Create(provider, clock: clock);
        await vm.GenerateInsightAsync();
        Assert.True(vm.HasPreparedLyrics); Assert.Equal("Letra propia", vm.OriginalLyrics);
        Assert.False(vm.GenerateInsightCommand.CanExecute(null));
        clock.Now += TimeSpan.FromSeconds(61); await vm.RefreshPreparationTimeAsync();
        Assert.True(vm.GenerateInsightCommand.CanExecute(null)); Assert.Equal(1, provider.Calls);
        await vm.StopAsync();
    }
    [Fact]
    public async Task CompatibleResultSurvivesPausingQueriesButNotAccountOrModelChanges()
    {
        var provider = new Provider(); var vm = await Create(provider);
        await vm.GenerateInsightAsync();
        vm.ApplyAiSettings(new(false, "fixture-model", 3));
        Assert.True(vm.HasInsight); Assert.Equal(PreparationCause.Result, vm.Preparation.Cause);
        Assert.False(vm.RegenerateCommand.CanExecute(null)); Assert.Equal(1, provider.Calls);
        await vm.StopAsync();
        var session = new Session { Mode = AiConnectionMode.ChatGptIncluded, CanGenerate = true, Readiness = AnalysisSessionState.Ready };
        vm = await Create(provider, session: session); await vm.GenerateInsightAsync();
        session.CanGenerate = false; session.Readiness = AnalysisSessionState.ConfirmationRequired; session.Change();
        Assert.True(vm.HasInsight); Assert.False(vm.RegenerateCommand.CanExecute(null));
        session.CacheScope = "different-account"; session.Change();
        Assert.False(vm.HasInsight); await vm.StopAsync();
    }
    [Fact]
    public async Task Http429PreservesRetryAfterWithoutRetrying()
    {
        using var http = new HttpClient(new Handler());
        var before = DateTimeOffset.UtcNow;
        var error = await Assert.ThrowsAsync<AiException>(() => new OpenAiResponsesClient(http).SendAsync("synthetic", new { }, CancellationToken.None));
        Assert.Equal(AiFailure.RateLimited, error.Failure);
        Assert.InRange(error.RetryAt!.Value, before.AddSeconds(120), DateTimeOffset.UtcNow.AddSeconds(120));
    }
    private static async Task<MainViewModel> Create(Provider provider, Store? store = null, Session? session = null, Clock? clock = null)
    {
        var vm = new MainViewModel(store ?? new Store(), provider, insightProvider: provider, connection: session, clock: clock);
        await vm.InitializeAsync();
        vm.ApplyDetection(new(ApplicationState.Playing, new(1, "Canción propia", "Artista", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null));
        vm.SaveManual(vm.BeginLyricsEdit()!, "Letra propia"); return vm;
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-21T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
    private sealed class Session : IAnalysisSession
    {
        public AiConnectionMode Mode { get; set; }
        public string? Model { get; set; } = "fixture-model";
        public string CacheScope { get; set; } = "synthetic";
        public bool CanGenerate { get; set; }
        public AnalysisSessionState Readiness { get; set; }
        public string Status => "Sesión sintética";
        public event EventHandler? Changed;
        public void Change() => Changed?.Invoke(this, EventArgs.Empty);
    }
    private sealed class Store : ISettingsStore, IOutputLanguageSettings
    {
        public int Count;
        public TaskCompletionSource? Write;
        public Task InitializeAsync(CancellationToken t = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => Task.FromResult(new AppSettings(true, "fixture-model", 3));
        public Task SaveSettingsAsync(AppSettings s, CancellationToken t = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly d, CancellationToken t = default) => Task.FromResult(Count);
        public Task<string> ReadOutputLanguageAsync(CancellationToken t = default) => Task.FromResult("es");
        public Task SaveOutputLanguageAsync(string language, CancellationToken t = default) => Write?.Task ?? Task.CompletedTask;
    }
    private sealed class Provider : ILyricsProvider, IInsightProvider
    {
        public int Calls, Searches;
        public AiFailure? Error;
        public LyricsFailure? LyricsError;
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token)
        {
            Calls++; if (Error is { } error) throw new AiException(error);
            return Task.FromResult(new SongInsight(track.Revision, "es", [], "Resumen propio", [], [], [], []));
        }
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token)
        {
            Searches++; if (LyricsError is { } error) throw new LyricsProviderException(error);
            return Task.FromResult(new LyricsSearchResult(track.Revision, LyricsSearchKind.NotFound, null, []));
        }
    }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120)) } });
    }
}
