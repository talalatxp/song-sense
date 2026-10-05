using SongSense.Core;
using Xunit;

namespace SongSense.Tests;

public sealed class SpotifyRulesTests
{
    private static MediaSessionSnapshot Track(string key = "1", PlaybackState playback = PlaybackState.Playing,
        string app = "Spotify.exe") => new(key, app, "Song", "Artist", "Album", TimeSpan.FromSeconds(180), playback, MediaKind.Music);

    [Theory]
    [InlineData("Spotify.exe", true)]
    [InlineData("spotify.EXE", true)]
    [InlineData(SpotifySessionRules.StoreAppId, true)]
    [InlineData("Chrome.exe", false)]
    [InlineData("FakeSpotify.exe", false)]
    [InlineData("https://open.spotify.com", false)]
    public void OnlyExplicitSpotifyAppIdsAreRecognized(string id, bool expected) => Assert.Equal(expected, SpotifySessionRules.IsSpotify(id));

    [Fact]
    public void PlayingSpotifyWinsAndBrowserNeverReplacesIt()
    {
        var paused = Track("1", PlaybackState.Paused);
        var playing = Track("2");
        var browser = Track("3", app: "Chrome.exe");
        Assert.Equal(playing, SpotifySessionRules.Select([browser, paused, playing], "1"));
        Assert.Equal(paused, SpotifySessionRules.Select([browser, paused], null));
        Assert.Null(SpotifySessionRules.Select([browser], null));
    }

    [Fact]
    public void TiesPreservePreviousSessionOrChooseOrdinalIdentifier()
    {
        var first = Track("1");
        var second = Track("2");
        Assert.Equal(second, SpotifySessionRules.Select([first, second], "2"));
        Assert.Equal(first, SpotifySessionRules.Select([second, first], null));
        var store = Track("3", app: SpotifySessionRules.StoreAppId);
        Assert.Equal(first, SpotifySessionRules.Select([store, first], null));
    }

    [Fact]
    public void PauseResumeAndRepeatedEventsKeepRevisionButNewSongsDoNot()
    {
        var reducer = new TrackObservationReducer();
        var original = Track();
        var first = reducer.Apply([original]);
        var paused = reducer.Apply([original with { Playback = PlaybackState.Paused }]);
        var resumed = reducer.Apply([original]);
        Assert.Equal(first.Track!.Revision, paused.Track!.Revision);
        Assert.Equal(first, resumed);
        Assert.Equal(ApplicationState.Paused, paused.State);
        var changed = reducer.Apply([original with { Title = "Another song" }]);
        Assert.True(changed.Track!.Revision > resumed.Track!.Revision);
        Assert.Equal("Another song", changed.Track.Title);
    }

    [Fact]
    public void ClosingClearsTrackAndReopeningUsesNewRevision()
    {
        var reducer = new TrackObservationReducer();
        var first = reducer.Apply([Track()]);
        var closed = reducer.Apply([]);
        Assert.Equal(ApplicationState.NoSpotify, closed.State);
        Assert.Null(closed.Track);
        Assert.Null(closed.SourceAppId);
        var reopened = reducer.Apply([Track()]);
        Assert.True(reopened.Track!.Revision > first.Track!.Revision);
    }

    [Theory]
    [InlineData("", "Artist", MediaKind.Music)]
    [InlineData("Advertisement", "", MediaKind.Unknown)]
    [InlineData("Episode", "   ", MediaKind.Unknown)]
    [InlineData("Video", "Artist", MediaKind.Other)]
    public void UnidentifiableOrNonMusicContentClearsActiveTrack(string title, string artist, MediaKind kind)
    {
        var reducer = new TrackObservationReducer();
        reducer.Apply([Track()]);
        var result = reducer.Apply([Track() with { Title = title, Artist = artist, Kind = kind }]);
        Assert.Equal(ApplicationState.NoTrack, result.State);
        Assert.Null(result.Track);
        Assert.Equal("Spotify.exe", result.SourceAppId);
    }

    [Fact]
    public void MissingOrInvalidDurationStaysUnknownAndEnrichmentKeepsRevision()
    {
        var reducer = new TrackObservationReducer();
        var first = reducer.Apply([Track() with { Duration = null, Album = " " }]);
        Assert.Null(first.Track!.Duration);
        Assert.Null(first.Track.Album);
        Assert.Null(reducer.Apply([Track() with { Duration = TimeSpan.Zero, Album = null }]).Track!.Duration);
        var known = reducer.Apply([Track() with { Album = null }]);
        Assert.Equal(first.Track.Revision, known.Track!.Revision);
        Assert.Equal(TimeSpan.FromSeconds(180), known.Track.Duration);
    }

    [Fact]
    public void ErrorsInvalidateActiveTrackAndRecoveryUsesFreshRevision()
    {
        var reducer = new TrackObservationReducer();
        var first = reducer.Apply([Track()]);
        Assert.Null(reducer.Error().Track);
        Assert.True(reducer.Apply([Track()]).Track!.Revision > first.Track!.Revision);
    }
}
