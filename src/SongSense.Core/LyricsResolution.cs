using System.Text;
using System.Text.RegularExpressions;

namespace SongSense.Core;

public enum LyricsSearchKind { Matched, Instrumental, Candidates, NotFound }
public sealed record LyricsSearchResult(long TrackRevision, LyricsSearchKind Kind,
    ResolvedLyrics? Lyrics, IReadOnlyList<LyricsCandidate> Candidates);
public enum LyricsFailure { Offline, Timeout, HttpError, InvalidResponse, RateLimited }

public sealed class LyricsProviderException(LyricsFailure failure, DateTimeOffset? retryAt = null) : Exception("No se pudo completar la consulta de letras.")
{
    public LyricsFailure Failure { get; } = failure;
    public DateTimeOffset? RetryAt { get; } = retryAt;
}

public static partial class LyricsResolution
{
    public static string Normalize(string text) => Spaces().Replace(text.Normalize(NormalizationForm.FormKC).Trim(), " ").ToUpperInvariant();

    public static string TextOf(LyricsCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.PlainLyrics)) return NormalizeNewlines(candidate.PlainLyrics);
        if (string.IsNullOrWhiteSpace(candidate.SyncedLyrics)) return "";
        var lines = NormalizeNewlines(candidate.SyncedLyrics).Split('\n')
            .Where(line => !LrcMetadata().IsMatch(line))
            .Select(line => EnhancedTimestamp().Replace(TimestampPrefix().Replace(line, ""), ""));
        var text = string.Join('\n', lines);
        return string.IsNullOrWhiteSpace(text) ? "" : text;
    }

    public static LyricsSearchResult Resolve(CurrentTrack track, IEnumerable<LyricsCandidate> candidates)
    {
        var usable = candidates.Where(c => c.IsInstrumental || !string.IsNullOrWhiteSpace(TextOf(c))).ToArray();
        if (usable.Length == 0) return new(track.Revision, LyricsSearchKind.NotFound, null, usable);
        // Missing metadata or alternative versions require an explicit choice in F04.
        if (usable.Length != 1 || !CompleteMatch(track, usable[0]))
            return new(track.Revision, LyricsSearchKind.Candidates, null, usable);
        var match = usable[0];
        return new(track.Revision, match.IsInstrumental ? LyricsSearchKind.Instrumental : LyricsSearchKind.Matched,
            new ResolvedLyrics(track.Revision, match.IsInstrumental ? "" : TextOf(match), LyricsOrigin.Lrclib, match.Id, match.IsInstrumental), usable);
    }

    private static bool CompleteMatch(CurrentTrack track, LyricsCandidate candidate) =>
        !string.IsNullOrWhiteSpace(track.Album) && !string.IsNullOrWhiteSpace(candidate.Album) &&
        track.Duration is { } trackDuration && trackDuration > TimeSpan.Zero &&
        candidate.Duration is { } duration && duration > TimeSpan.Zero &&
        Normalize(track.Title) == Normalize(candidate.Title) && Normalize(track.Artist) == Normalize(candidate.Artist) &&
        Normalize(track.Album) == Normalize(candidate.Album) && Math.Abs((trackDuration - duration).TotalSeconds) <= 2;

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    [GeneratedRegex(@"\s+", RegexOptions.NonBacktracking)]
    private static partial Regex Spaces();
    [GeneratedRegex(@"^\s*(?:\[\d{1,3}:\d{2}(?:[.:]\d{1,3})?\])+[ \t]?", RegexOptions.NonBacktracking)]
    private static partial Regex TimestampPrefix();
    [GeneratedRegex(@"<\d{1,3}:\d{2}(?:[.:]\d{1,3})?>", RegexOptions.NonBacktracking)]
    private static partial Regex EnhancedTimestamp();
    [GeneratedRegex(@"^\s*\[(?:ar|ti|al|by|offset|re|ve|length):[^\]]*\]\s*$", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex LrcMetadata();
}
