using System.IO;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class AutomaticWorkflowTests
{
    private static CurrentTrack Track(long id) => new(id, "Own song " + id, "Artist", "Album", TimeSpan.FromSeconds(180), PlaybackState.Playing);
    private static void Observe(MainViewModel vm, long id) => vm.ApplyDetection(new(ApplicationState.Playing, Track(id), "Spotify.exe"));
    private static async Task<MainViewModel> Start(Provider provider, Store? store = null, IAnalysisSession? connection = null)
    {
        var vm = new MainViewModel(store ?? new Store(), provider, insightProvider: provider, connection: connection);
        vm.StartAutomaticUpdates(); await vm.InitializeAsync(); return vm;
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    [Fact]
    public async Task FiveRapidChangesOnlyProcessLatestAndPlaybackEventsDoNotRepeat()
    {
        var provider = new Provider(); var vm = await Start(provider);
        for (var id = 1; id <= 5; id++) { Observe(vm, id); await Task.Delay(100); }
        Assert.Empty(provider.LyricsCalls);
        await vm.WaitForAutomaticAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(new long[] { 5 }, provider.LyricsCalls);
        Assert.Equal(new long[] { 5 }, provider.InsightCalls);
        Assert.Equal(5, vm.Insight!.TrackRevision);
        vm.ApplyDetection(new(ApplicationState.Paused, Track(5) with { Playback = PlaybackState.Paused }, "Spotify.exe"));
        Observe(vm, 5); await vm.WaitForAutomaticAsync();
        Assert.Single(provider.InsightCalls); await vm.StopAsync();
    }

    [Fact]
    public async Task UncancellableGenerationDrainsBeforeLatestAndStaleResultIsDiscarded()
    {
        var pending = new TaskCompletionSource<SongInsight>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider { Generate = (track, _) => track.Revision == 1 ? pending.Task : Task.FromResult(Result(track)) };
        var vm = await Start(provider); Observe(vm, 1);
        await Until(() => provider.InsightCalls.Count == 1);
        for (var id = 2; id <= 5; id++) Observe(vm, id);
        await Task.Delay(1700);
        Assert.Single(provider.InsightCalls); Assert.Null(vm.Insight);
        pending.SetResult(Result(Track(1)));
        await vm.WaitForAutomaticAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(new long[] { 1, 5 }, provider.InsightCalls);
        Assert.Equal(new long[] { 1, 5 }, provider.LyricsCalls);
        Assert.Equal(5, vm.Insight!.TrackRevision); await vm.StopAsync();
    }

    [Fact]
    public async Task PausingFreezesContentAndResumingUsesLatestObservation()
    {
        var provider = new Provider(); var store = new Store(); var vm = await Start(provider, store);
        Observe(vm, 1); await vm.WaitForAutomaticAsync();
        vm.AutomaticUpdatesEnabled = false; Observe(vm, 2); Observe(vm, 3);
        Assert.Equal(1, vm.CurrentTrack!.Revision); Assert.Equal(1, vm.Insight!.TrackRevision);
        Assert.Contains("pausada", vm.AutomaticMessage); await vm.WaitForCacheAsync(); Assert.False(store.Automatic);
        vm.AutomaticUpdatesEnabled = true; await vm.WaitForAutomaticAsync();
        Assert.Equal(3, vm.Insight!.TrackRevision);
        Assert.Equal(new long[] { 1, 3 }, provider.InsightCalls); await vm.StopAsync();
    }

    [Fact]
    public async Task FailureWaitsForExplicitRetryAndDoesNotRepeatOnPlayback()
    {
        var provider = new Provider { Generate = (_, _) => Task.FromException<SongInsight>(new AiException(AiFailure.Timeout)) };
        var vm = await Start(provider); Observe(vm, 1); await vm.WaitForAutomaticAsync();
        Assert.Single(provider.InsightCalls); Assert.Null(vm.Insight);
        Observe(vm, 1); await vm.WaitForAutomaticAsync(); Assert.Single(provider.InsightCalls);
        provider.Generate = (track, _) => Task.FromResult(Result(track));
        vm.RetryAutomaticCommand.Execute(null); await vm.WaitForAutomaticAsync();
        Assert.Equal(2, provider.InsightCalls.Count); Assert.NotNull(vm.Insight); await vm.StopAsync();
    }

    [Theory]
    [InlineData(LyricsSearchKind.Instrumental)]
    [InlineData(LyricsSearchKind.Candidates)]
    [InlineData(LyricsSearchKind.NotFound)]
    public async Task UnconfirmedAbsentAndInstrumentalLyricsNeverGenerate(LyricsSearchKind kind)
    {
        var provider = new Provider { Kind = kind }; var vm = await Start(provider);
        Observe(vm, 1); await vm.WaitForAutomaticAsync();
        Assert.Single(provider.LyricsCalls); Assert.Empty(provider.InsightCalls);
        Observe(vm, 1); await vm.WaitForAutomaticAsync(); Assert.Single(provider.LyricsCalls);
        await vm.StopAsync();
    }

    [Fact]
    public async Task ChatGptBlockAndDailyQuotaNeverFallBackToPaidGeneration()
    {
        var provider = new Provider(); var vm = await Start(provider, connection: new BlockedSession());
        Observe(vm, 1); await vm.WaitForAutomaticAsync();
        Assert.NotNull(vm.PreparedLyrics); Assert.Empty(provider.InsightCalls); Assert.Contains("bloqueado", vm.AutomaticMessage);
        await vm.StopAsync();
        vm = await Start(provider, new Store { Count = 20 }); Observe(vm, 2); await vm.WaitForAutomaticAsync();
        Assert.Empty(provider.InsightCalls); Assert.Contains("Límite diario", vm.AutomaticMessage); await vm.StopAsync();
    }

    [Fact]
    public async Task AutomaticPreferenceSurvivesReopenAndIndependentAiSettingsSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SongSense.AutoTests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "settings.db"); var store = new SqliteSettingsStore(path);
            await store.InitializeAsync(); Assert.True(await store.ReadAutomaticUpdatesAsync());
            await store.SaveAutomaticUpdatesAsync(false); await store.SaveSettingsAsync(new(true, "chosen-model", 42));
            var reopened = new SqliteSettingsStore(path); await reopened.InitializeAsync();
            Assert.False(await reopened.ReadAutomaticUpdatesAsync()); Assert.Equal(42, (await reopened.ReadSettingsAsync()).DailyRequestLimit);
            var provider = new Provider(); var vm = new MainViewModel(reopened, provider, insightProvider: provider);
            vm.StartAutomaticUpdates(); await vm.InitializeAsync(); Observe(vm, 1);
            Assert.False(vm.AutomaticUpdatesEnabled); Assert.Null(vm.CurrentTrack); Assert.Empty(provider.LyricsCalls);
            await vm.StopAsync();
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ReturningToCachedTrackRestoresWithoutNetworkOrNewTokens()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SongSense.AutoTests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "settings.db"); var store = new SqliteSettingsStore(path);
            await store.InitializeAsync(); await store.SaveSettingsAsync(new(true, "chosen-model"));
            var provider = new Provider(); var vm = new MainViewModel(store, provider, insightProvider: provider, cache: new SqliteCacheStore(path));
            vm.StartAutomaticUpdates(); await vm.InitializeAsync(); Observe(vm, 1); await vm.WaitForAutomaticAsync();
            Assert.NotNull(vm.Insight); await vm.WaitForCacheAsync();
            Observe(vm, 2); Observe(vm, 3);
            vm.ApplyDetection(new(ApplicationState.Playing, Track(1) with { Revision = 4 }, "Spotify.exe"));
            await vm.WaitForAutomaticAsync();
            Assert.Equal(4, vm.Insight!.TrackRevision); Assert.Single(provider.LyricsCalls); Assert.Single(provider.InsightCalls);
            Assert.Contains("0 tokens nuevos", vm.UsageMessage); await vm.StopAsync();
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static SongInsight Result(CurrentTrack track) => new(track.Revision, "es", [],
        string.Join(' ', Enumerable.Repeat("La ventana podría representar una oportunidad de cambio mientras la repetición sugiere una decisión todavía incierta.", 5)),
        ["Cambio"], [], ["Una acción cotidiana es otra lectura posible."], ["La lectura es tentativa."]) { Usage = new(10, 20, 30) };
    private sealed class Provider : ILyricsProvider, IInsightProvider
    {
        public List<long> LyricsCalls { get; } = []; public List<long> InsightCalls { get; } = [];
        public LyricsSearchKind Kind { get; init; } = LyricsSearchKind.Matched;
        public Func<CurrentTrack, CancellationToken, Task<SongInsight>> Generate { get; set; } = (track, _) => Task.FromResult(Result(track));
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token)
        {
            LyricsCalls.Add(track.Revision);
            ResolvedLyrics? lyrics = Kind is LyricsSearchKind.Matched or LyricsSearchKind.Instrumental ? new(track.Revision, "Abro una ventana", LyricsOrigin.Lrclib, 1, Kind == LyricsSearchKind.Instrumental) : null;
            IReadOnlyList<LyricsCandidate> candidates = Kind == LyricsSearchKind.Candidates ? [new(1, track.Title, track.Artist, track.Album, track.Duration, false, "Abro una ventana", null)] : [];
            return Task.FromResult(new LyricsSearchResult(track.Revision, Kind, lyrics, candidates));
        }
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token) { InsightCalls.Add(track.Revision); return Generate(track, token); }
    }
    private sealed class Store : ISettingsStore, IAutomaticUpdateSettings
    {
        public bool Automatic = true; public int Count;
        public Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken token = default) => Task.FromResult(new AppSettings(true, "chosen-model"));
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly date, CancellationToken token = default) => Task.FromResult(Count);
        public Task<bool> ReadAutomaticUpdatesAsync(CancellationToken token = default) => Task.FromResult(Automatic);
        public Task SaveAutomaticUpdatesAsync(bool enabled, CancellationToken token = default) { Automatic = enabled; return Task.CompletedTask; }
    }
    private sealed class BlockedSession : IAnalysisSession
    {
        public AiConnectionMode Mode => AiConnectionMode.ChatGptIncluded;
        public string Model => "chosen-model"; public string CacheScope => "chatgpt:test";
        public bool CanGenerate => false; public string Status => "Uso incluido bloqueado por el proveedor";
        public event EventHandler? Changed { add { } remove { } }
    }
}
