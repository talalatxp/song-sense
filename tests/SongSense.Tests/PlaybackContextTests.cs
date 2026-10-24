using SongSense.App;
using SongSense.Core;
using Xunit;

namespace SongSense.Tests;

public sealed class PlaybackContextTests
{
    private static TrackDetection Observation(long revision, PlaybackState playback = PlaybackState.Playing) =>
        new(playback == PlaybackState.Paused ? ApplicationState.Paused : ApplicationState.Playing,
            new(revision, "Canción propia " + revision, "Artista", "Álbum", TimeSpan.FromMinutes(3), playback), "Spotify.exe");
    private static async Task<MainViewModel> Create(Store store, Provider provider, Session? session = null)
    {
        var vm = new MainViewModel(store, provider, insightProvider: provider, connection: session);
        await vm.InitializeAsync(); return vm;
    }
    private static async Task Prepare(MainViewModel vm)
    {
        vm.ApplyDetection(Observation(1));
        vm.SaveManual(vm.BeginLyricsEdit()!, "Abro una ventana");
        await vm.GenerateInsightAsync();
    }

    [Fact]
    public async Task PlaybackPauseAndResumeKeepReadyContentAndNeverInfer()
    {
        var provider = new Provider(); var vm = await Create(new(), provider);
        await Prepare(vm); var result = vm.Insight; var lyrics = vm.PreparedLyrics;
        vm.ApplyDetection(Observation(1, PlaybackState.Paused));
        Assert.Equal("Spotify en pausa", vm.PlaybackStatus);
        Assert.Equal("CANCIÓN MOSTRADA", vm.TrackContextLabel);
        Assert.Equal("Seguimiento activo", vm.TrackingStatus);
        Assert.Equal("Listo", vm.DisplayStatus); Assert.Same(result, vm.Insight); Assert.Same(lyrics, vm.PreparedLyrics);
        vm.ApplyDetection(Observation(1));
        Assert.Equal("AHORA SUENA", vm.TrackContextLabel); Assert.Equal("Spotify reproduciendo", vm.PlaybackStatus);
        Assert.False(vm.ResumeTrackingCommand.CanExecute(null)); Assert.Equal(1, provider.Inferences);
        await vm.StopAsync();
    }

    [Fact]
    public async Task FrozenContentAndPlaybackStaySeparateAndResumeUsesLatestWithoutSwitchingProvider()
    {
        var provider = new Provider(); var store = new Store(); var session = new Session();
        var vm = await Create(store, provider, session); await Prepare(vm);
        var result = vm.Insight; vm.StartAutomaticUpdates(); vm.AutomaticUpdatesEnabled = false;
        var notifications = new HashSet<string?>(); vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        vm.ApplyDetection(Observation(2)); vm.ApplyDetection(Observation(3, PlaybackState.Paused));
        Assert.Equal(1, vm.CurrentTrack!.Revision); Assert.Same(result, vm.Insight);
        Assert.Equal("CANCIÓN MOSTRADA", vm.TrackContextLabel);
        Assert.Equal("Spotify en pausa · otra canción", vm.PlaybackStatus);
        Assert.Equal("Seguimiento pausado · mostrando esta canción", vm.TrackingStatus);
        Assert.Equal("Listo", vm.DisplayStatus); Assert.True(vm.ResumeTrackingCommand.CanExecute(null));
        Assert.Contains(nameof(vm.PlaybackStatus), notifications); Assert.Contains(nameof(vm.TrackingStatus), notifications);
        vm.ResumeTrackingCommand.Execute(null); await vm.WaitForAutomaticAsync(); await vm.WaitForCacheAsync();
        Assert.Equal(3, vm.CurrentTrack!.Revision); Assert.Equal("Spotify en pausa", vm.PlaybackStatus);
        Assert.Equal("Seguimiento activo", vm.TrackingStatus); Assert.False(vm.CanResumeTracking);
        Assert.True(store.Enabled); Assert.Equal(new long[] { 3 }, provider.Searches);
        Assert.Equal(1, provider.Inferences); Assert.Null(vm.Insight);
        Assert.Equal(AiConnectionMode.ChatGptIncluded, session.Mode); Assert.Equal("fixture", session.Model);
        await vm.StopAsync();
    }

    [Fact]
    public async Task PausedPreferenceOnReopenKeepsObservingAndResumesLatestEvenBeforeWorkerStarts()
    {
        var store = new Store(); var provider = new Provider(); var first = await Create(store, provider);
        first.AutomaticUpdatesEnabled = false; await first.WaitForCacheAsync(); await first.StopAsync();
        var vm = await Create(store, provider);
        Assert.False(vm.AutomaticUpdatesEnabled); Assert.Equal("Seguimiento pausado · sin canción mostrada", vm.TrackingStatus);
        vm.ApplyDetection(Observation(2)); vm.ApplyDetection(Observation(4, PlaybackState.Paused));
        Assert.Null(vm.CurrentTrack); Assert.Equal("Spotify en pausa", vm.PlaybackStatus);
        Assert.Equal("Sin canción mostrada", vm.DisplayStatus);
        Assert.Contains("Reanuda el seguimiento", vm.DisplayNotice); Assert.False(vm.ShowPrimaryAction);
        vm.StartAutomaticUpdates(); vm.ResumeTrackingCommand.Execute(null); await vm.WaitForAutomaticAsync();
        Assert.Equal(4, vm.CurrentTrack!.Revision); Assert.Equal(new long[] { 4 }, provider.Searches);
        Assert.Equal(0, provider.Inferences); await vm.StopAsync();
    }

    [Fact]
    public async Task ResumeSameSongPreservesResultAndDoesNotRepeatInference()
    {
        var provider = new Provider(); var vm = await Create(new(), provider); await Prepare(vm);
        var result = vm.Insight; vm.AutomaticUpdatesEnabled = false;
        vm.ApplyDetection(Observation(1, PlaybackState.Paused)); vm.ResumeTrackingCommand.Execute(null);
        Assert.Same(result, vm.Insight); Assert.Equal("Spotify en pausa", vm.PlaybackStatus);
        Assert.Equal(1, provider.Inferences); Assert.Empty(provider.Searches); await vm.StopAsync();
    }

    [Theory]
    [InlineData(ApplicationState.NoSpotify, "Spotify no disponible")]
    [InlineData(ApplicationState.NoTrack, "Spotify sin canción identificable")]
    [InlineData(ApplicationState.Error, "Reproducción no disponible")]
    public async Task LossOfPlaybackWhileFrozenDoesNotRelabelOldContentAsNowPlaying(ApplicationState state, string status)
    {
        var provider = new Provider(); var vm = await Create(new(), provider); await Prepare(vm);
        vm.AutomaticUpdatesEnabled = false; vm.ApplyDetection(new(state, null, null));
        Assert.Equal(status, vm.PlaybackStatus); Assert.Equal(1, vm.CurrentTrack!.Revision);
        Assert.Equal("CANCIÓN MOSTRADA", vm.TrackContextLabel); Assert.NotNull(vm.Insight);
        vm.ResumeTrackingCommand.Execute(null);
        Assert.Null(vm.CurrentTrack); Assert.Null(vm.Insight); Assert.Equal(1, provider.Inferences);
        await vm.StopAsync(); Assert.False(vm.ResumeTrackingCommand.CanExecute(null));
    }

    private sealed class Store : ISettingsStore, IAutomaticUpdateSettings
    {
        public bool Enabled = true;
        public Task InitializeAsync(CancellationToken t = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => Task.FromResult(new AppSettings(true, "fixture"));
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken t = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly date, CancellationToken t = default) => Task.FromResult(0);
        public Task<bool> ReadAutomaticUpdatesAsync(CancellationToken t = default) => Task.FromResult(Enabled);
        public Task SaveAutomaticUpdatesAsync(bool enabled, CancellationToken t = default) { Enabled = enabled; return Task.CompletedTask; }
    }
    private sealed class Provider : ILyricsProvider, IInsightProvider
    {
        public int Inferences; public List<long> Searches { get; } = [];
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken t)
        {
            Searches.Add(track.Revision);
            return Task.FromResult(new LyricsSearchResult(track.Revision, LyricsSearchKind.Matched,
                new(track.Revision, "Abro una ventana", LyricsOrigin.Lrclib, 1, false), []));
        }
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken t)
        {
            Inferences++;
            return Task.FromResult(new SongInsight(track.Revision, "es", [], "Una oportunidad de cambio.", ["Cambio"], [], [], []));
        }
    }
    private sealed class Session : IAnalysisSession
    {
        public AiConnectionMode Mode => AiConnectionMode.ChatGptIncluded;
        public string Model => "fixture"; public string CacheScope => "synthetic";
        public bool CanGenerate => true; public string Status => "Lista";
        public event EventHandler? Changed { add { } remove { } }
    }
}
