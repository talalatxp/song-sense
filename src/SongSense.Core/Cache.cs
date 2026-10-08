using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SongSense.Core;

public sealed record CachedSong(ResolvedLyrics? Lyrics, bool IsAbsence, bool RequiresConfirmation);
public interface ICacheStore
{
    Task<CachedSong?> ReadSongAsync(CurrentTrack track, CancellationToken token = default);
    Task SaveLyricsAsync(CurrentTrack track, ResolvedLyrics? lyrics, CancellationToken token = default);
    Task<SongInsight?> ReadInsightAsync(ResolvedLyrics lyrics, string model, string version, CancellationToken token = default, CurrentTrack? track = null, string scope = "api-key");
    Task SaveInsightAsync(CurrentTrack track, ResolvedLyrics lyrics, string model, string version, SongInsight insight, CancellationToken token = default, string scope = "api-key");
    Task DeleteSongAsync(CurrentTrack track, CancellationToken token = default);
    Task ClearAsync(CancellationToken token = default);
}
public interface IRecoverableSettingsStore
{
    bool CanRecover { get; }
    string RecoveryMessage { get; }
    Task RecoverAsync(CancellationToken token = default);
}
public static class CacheIdentity
{
    public const string Version = "songsense_insight_v1";
    private static string Normalize(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ").ToUpperInvariant();
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static bool IsUnambiguous(CurrentTrack track) => !string.IsNullOrWhiteSpace(track.Title) && !string.IsNullOrWhiteSpace(track.Artist) && !string.IsNullOrWhiteSpace(track.Album) && track.Duration is { TotalSeconds: > 0 };
    public static string Song(CurrentTrack track) => Hash(new { title = Normalize(track.Title), artist = Normalize(track.Artist), album = Normalize(track.Album ?? ""), duration = track.Duration is { } duration ? (long?)Math.Round(duration.TotalMilliseconds) : null });
    // Normalize line endings only: spaces, stanzas, repetitions and version labels remain significant.
    public static string Analysis(ResolvedLyrics lyrics, string model, string version, string scope = "api-key")
    {
        var text = lyrics.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        return scope == "api-key" ? Hash(new { text, target = "es", provider = "openai", model, version }) :
            Hash(new { text, target = "es", provider = "openai", model, version, scope });
    }
    public static string Serialize(SongInsight insight) => JsonSerializer.Serialize(new
    {
        language = insight.Language,
        translation = insight.Translation.Select(line => new { id = line.Id, text = line.Text, source_language = line.SourceLanguage }),
        summary = insight.Summary, themes = insight.Themes,
        metaphors = insight.Metaphors.Select(item => new { text = item.Text, explanation = item.Explanation }),
        alternatives = insight.Alternatives, warnings = insight.Warnings
    });
}
