using SongSense.App;
using SongSense.Core;
using Xunit;

namespace SongSense.Tests;

public sealed class LyricsViewModelTests
{
    private static CurrentTrack Track(long revision = 1) => new(revision, $"Song {revision}", "Artist", "Album", TimeSpan.FromSeconds(180), PlaybackState.Playing);
    private static TrackDetection Detection(CurrentTrack? track) => new(track is null ? ApplicationState.NoSpotify : ApplicationState.Playing, track, "Spotify.exe");
    private static LyricsSearchResult Matched(long revision) => new(revision, LyricsSearchKind.Matched,
        new ResolvedLyrics(revision, "Texto propio", LyricsOrigin.Lrclib, 123, false), []);

    [Fact]
    public async Task LateResultOfPreviousSongCannotChangeNewSongOrNewSearch()
    {
        var old = new TaskCompletionSource<LyricsSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = new TaskCompletionSource<LyricsSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider((track, _) => track.Revision == 1 ? old.Task : next.Task);
        var vm = new MainViewModel(new Store(), provider);
        vm.ApplyDetection(Detection(Track()));
        var first = vm.SearchLyricsAsync();
        vm.ApplyDetection(Detection(Track(2)));
        Assert.Empty(vm.OriginalLyrics);
        var second = vm.SearchLyricsAsync();
        old.SetResult(Matched(1));
        await first;
        Assert.Empty(vm.OriginalLyrics);
        Assert.True(vm.CancelLyricsCommand.CanExecute(null));
        next.SetResult(Matched(2));
        await second;
        Assert.Equal(2, vm.PreparedLyrics!.TrackRevision);
        Assert.Equal("Texto propio", vm.OriginalLyrics);
        Assert.False(vm.CancelLyricsCommand.CanExecute(null));
    }

    [Fact]
    public async Task CancellationRejectsResponseEvenWhenProviderIgnoresCancellation()
    {
        var response = new TaskCompletionSource<LyricsSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new MainViewModel(new Store(), new Provider((_, _) => response.Task));
        vm.ApplyDetection(Detection(Track()));
        var pending = vm.SearchLyricsAsync();
        vm.CancelLyrics();
        response.SetResult(Matched(1));
        await pending;
        Assert.Null(vm.PreparedLyrics);
        Assert.Empty(vm.OriginalLyrics);
        Assert.Contains("cancelada", vm.LyricsMessage);
        Assert.True(vm.SearchLyricsCommand.CanExecute(null));
    }

    [Fact]
    public async Task PauseResumeKeepsLyricsWithoutAdditionalRequestsAndClosingClearsThem()
    {
        var provider = new Provider((_, _) => Task.FromResult(Matched(1)));
        var vm = new MainViewModel(new Store(), provider);
        vm.ApplyDetection(Detection(Track()));
        await vm.SearchLyricsAsync();
        vm.ApplyDetection(new TrackDetection(ApplicationState.Paused, Track() with { Playback = PlaybackState.Paused }, "Spotify.exe"));
        Assert.Equal("En pausa", vm.Status);
        Assert.Equal("Texto propio", vm.OriginalLyrics);
        vm.ApplyDetection(Detection(Track()));
        Assert.Equal("Texto propio", vm.OriginalLyrics);
        Assert.Equal(1, provider.Calls);
        vm.ApplyDetection(Detection(null));
        Assert.Empty(vm.OriginalLyrics);
        Assert.Null(vm.PreparedLyrics);
        Assert.False(vm.SearchLyricsCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(LyricsSearchKind.NotFound)]
    [InlineData(LyricsSearchKind.Candidates)]
    public async Task AbsenceOrAmbiguityCannotPrepareLyrics(LyricsSearchKind kind)
    {
        var vm = new MainViewModel(new Store(), new Provider((_, _) => Task.FromResult(new LyricsSearchResult(1, kind, null, []))));
        vm.ApplyDetection(Detection(Track()));
        await vm.SearchLyricsAsync();
        Assert.Null(vm.PreparedLyrics);
        Assert.Empty(vm.OriginalLyrics);
        Assert.True(vm.SearchLyricsCommand.CanExecute(null));
    }

    private sealed class Provider(Func<CurrentTrack, CancellationToken, Task<LyricsSearchResult>> find) : ILyricsProvider
    {
        public int Calls { get; private set; }
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken cancellationToken)
        { Calls++; return find(track, cancellationToken); }
    }

    private sealed class Store : ISettingsStore
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppSettings());
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly localDate, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
