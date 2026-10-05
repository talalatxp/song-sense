using SongSense.Core;
using Xunit;

namespace SongSense.Tests;

public sealed class LyricsResolutionTests
{
    private static CurrentTrack Track => new(4, "Mi canción", "Artista", "Álbum", TimeSpan.FromSeconds(180), PlaybackState.Playing);
    private static LyricsCandidate Candidate => new(123, Track.Title, Track.Artist, Track.Album, Track.Duration, false, "Texto propio\n\nOtra línea", null);

    [Fact]
    public void CompleteUniqueMatchNormalizesUnicodeCaseAndSpaces()
    {
        var result = LyricsResolution.Resolve(Track, [Candidate with { Title = "  MI  CANCIO\u0301N ", Album = "A\u0301LBUM", Duration = TimeSpan.FromSeconds(182) }]);
        Assert.Equal(LyricsSearchKind.Matched, result.Kind);
        Assert.Equal(4, result.Lyrics!.TrackRevision);
        Assert.Equal(Candidate.PlainLyrics, result.Lyrics.Text);
        Assert.Equal(123, result.Lyrics.LrclibId);
    }

    [Theory]
    [InlineData("Live")]
    [InlineData("Remix")]
    [InlineData("Remaster")]
    public void VersionLabelsAreNeverRemoved(string variant)
    {
        var result = LyricsResolution.Resolve(Track, [Candidate with { Title = $"{Track.Title} ({variant})" }]);
        Assert.Equal(LyricsSearchKind.Candidates, result.Kind);
        Assert.Null(result.Lyrics);
    }

    [Fact]
    public void MissingMetadataOrMultipleVersionsRequireSelection()
    {
        foreach (var track in new[] { Track with { Album = null }, Track with { Duration = null } })
            Assert.Equal(LyricsSearchKind.Candidates, LyricsResolution.Resolve(track, [Candidate]).Kind);
        foreach (var candidate in new[] { Candidate with { Album = null }, Candidate with { Duration = null }, Candidate with { Duration = TimeSpan.FromSeconds(183) }, Candidate with { Artist = "Otra persona" } })
            Assert.Equal(LyricsSearchKind.Candidates, LyricsResolution.Resolve(Track, [candidate]).Kind);
        var multiple = LyricsResolution.Resolve(Track, [Candidate, Candidate with { Id = 124, Title = Track.Title + " (Live)" }]);
        Assert.Equal(LyricsSearchKind.Candidates, multiple.Kind);
        Assert.Null(multiple.Lyrics);
        Assert.Equal(2, multiple.Candidates.Count);
    }

    [Fact]
    public void SyncedLyricsFallbackPreservesLinesAndRemovesOnlyLrcMarkup()
    {
        var candidate = Candidate with { PlainLyrics = " ", SyncedLyrics = "[ar:Artista]\r\n[00:01.12][01:01.12]Línea uno\r\n[00:03.00]\r\n[00:04.00]<00:04.00>Línea <00:04.50>dos\r\n[Chorus]\r\n[00:05]Línea uno" };
        Assert.Equal("Línea uno\n\nLínea dos\n[Chorus]\nLínea uno", LyricsResolution.TextOf(candidate));
        Assert.Equal(LyricsSearchKind.Matched, LyricsResolution.Resolve(Track, [candidate]).Kind);
        Assert.Equal(Candidate.PlainLyrics, LyricsResolution.TextOf(Candidate with { SyncedLyrics = "[00:01]Ignorada" }));
    }

    [Fact]
    public void AbsenceAndInstrumentalHaveDifferentResults()
    {
        Assert.Equal(LyricsSearchKind.NotFound, LyricsResolution.Resolve(Track, []).Kind);
        Assert.Equal(LyricsSearchKind.NotFound, LyricsResolution.Resolve(Track, [Candidate with { PlainLyrics = null, SyncedLyrics = "[00:01]\n[00:02]" }]).Kind);
        var instrumental = LyricsResolution.Resolve(Track, [Candidate with { IsInstrumental = true, PlainLyrics = null }]);
        Assert.Equal(LyricsSearchKind.Instrumental, instrumental.Kind);
        Assert.True(instrumental.Lyrics!.IsInstrumental);
        Assert.Empty(instrumental.Lyrics.Text);
    }
}
