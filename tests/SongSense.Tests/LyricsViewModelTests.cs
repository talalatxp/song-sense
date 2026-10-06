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

    [Fact]
    public async Task SelectingTwoVersionsChangesOnlyLyricsAndInvalidatesEarlierRevision()
    {
        LyricsCandidate[] candidates = [new(1, "Song live", "Other artist", "Live", TimeSpan.FromSeconds(200), false, "Primera versión", null),
            new(2, "Song remix", "Artist", "Remix", TimeSpan.FromSeconds(180), false, "Segunda versión", null)];
        var vm = new MainViewModel(new Store(), new Provider((_, _) => Task.FromResult(new LyricsSearchResult(1, LyricsSearchKind.Candidates, null, candidates))));
        vm.ApplyDetection(Detection(Track()));
        await vm.SearchLyricsAsync();
        Assert.Null(vm.PreparedLyrics);
        var first = vm.BeginLyricsEdit()!;
        Assert.Null(vm.SelectCandidate(first, 1));
        Assert.Equal("Primera versión", vm.OriginalLyrics);
        Assert.Equal("Song 1", vm.TrackTitle);
        Assert.False(vm.IsEditCurrent(first));
        Assert.Null(vm.SelectCandidate(vm.BeginLyricsEdit()!, 2));
        Assert.Equal("Segunda versión", vm.OriginalLyrics);
        Assert.Equal(2L, vm.PreparedLyrics!.LrclibId);
        Assert.Equal(LyricsOrigin.Lrclib, vm.PreparedLyrics.Origin);
        Assert.Equal("Song 1", vm.TrackTitle);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \r\n\t")]
    public void EmptyManualLyricsCannotBeSaved(string text)
    {
        var vm = new MainViewModel(new Store(), new Provider((_, _) => Task.FromResult(Matched(1))));
        vm.ApplyDetection(Detection(Track()));
        Assert.NotNull(vm.SaveManual(vm.BeginLyricsEdit()!, text));
        Assert.Null(vm.PreparedLyrics);
    }

    [Fact]
    public async Task ManualWorksWithoutResultsRejectsExcessWithoutTruncationAndInvalidatesRevision()
    {
        var vm = new MainViewModel(new Store(), new Provider((_, _) => Task.FromResult(new LyricsSearchResult(1, LyricsSearchKind.NotFound, null, []))));
        vm.ApplyDetection(Detection(Track()));
        await vm.SearchLyricsAsync();
        using var editor = new LyricsEditorViewModel(vm, vm.BeginLyricsEdit()!);
        editor.ManualText = new string('a', 20_001);
        editor.SaveCommand.Execute(null);
        Assert.Equal(20_001, editor.ManualText.Length);
        Assert.Contains("20.000", editor.Error);
        Assert.Null(vm.PreparedLyrics);
        editor.ManualText = new string('a', 20_000);
        var revision = vm.LyricsRevision;
        editor.SaveCommand.Execute(null);
        Assert.Equal(20_000, vm.OriginalLyrics.Length);
        Assert.Equal(LyricsOrigin.Manual, vm.PreparedLyrics!.Origin);
        Assert.Null(vm.PreparedLyrics.LrclibId);
        Assert.True(vm.LyricsRevision > revision);
        Assert.False(editor.IsCurrent);
        Assert.Null(vm.SaveManual(vm.BeginLyricsEdit()!, "Texto editado\r\nSegunda línea"));
        Assert.Equal("Texto editado\r\nSegunda línea", vm.OriginalLyrics);
    }

    [Fact]
    public async Task OldSelectorAndEditorCannotSaveAfterTrackChangeEvenWithSameTitle()
    {
        var candidate = new LyricsCandidate(7, "Song 1", "Artist", "Album", TimeSpan.FromSeconds(180), false, "Letra", null);
        var vm = new MainViewModel(new Store(), new Provider((_, _) => Task.FromResult(new LyricsSearchResult(1, LyricsSearchKind.Candidates, null, [candidate]))));
        vm.ApplyDetection(Detection(Track()));
        await vm.SearchLyricsAsync();
        var context = vm.BeginLyricsEdit()!;
        using var editor = new LyricsEditorViewModel(vm, context);
        editor.Selected = editor.Options[0];
        vm.ApplyDetection(Detection(Track() with { Revision = 2 }));
        Assert.NotNull(vm.SaveManual(context, "Texto antiguo"));
        Assert.NotNull(vm.SelectCandidate(context, 7));
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.False(editor.ConfirmCommand.CanExecute(null));
        Assert.Contains("ha cambiado", editor.Error);
        Assert.Null(vm.PreparedLyrics);
    }

    [Fact]
    public async Task NewSearchInvalidatesOpenEditorButPauseDoesNot()
    {
        var pending = new TaskCompletionSource<LyricsSearchResult>();
        var vm = new MainViewModel(new Store(), new Provider((_, _) => pending.Task));
        vm.ApplyDetection(Detection(Track()));
        var context = vm.BeginLyricsEdit()!;
        vm.ApplyDetection(new(ApplicationState.Paused, Track() with { Playback = PlaybackState.Paused }, "Spotify.exe"));
        Assert.True(vm.IsEditCurrent(context));
        var search = vm.SearchLyricsAsync();
        Assert.Null(vm.BeginLyricsEdit());
        Assert.NotNull(vm.SaveManual(context, "Antigua"));
        vm.CancelLyrics();
        pending.SetResult(Matched(1));
        await search;
        Assert.False(vm.IsEditCurrent(context));
        Assert.Null(vm.PreparedLyrics);
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
