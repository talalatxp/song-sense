namespace SongSense.Core;

public enum PlaybackState { Unknown, Playing, Paused, Stopped }
public enum ApplicationState
{
    NoSpotify, NoTrack, Paused, SearchingLyrics, ChoosingVersion, NoLyrics,
    AiNotConfigured, Processing, Ready, Offline, DailyLimit, Error,
    Playing, Stopped, PlaybackUnknown
}

// Revision identifies the active track, independently of playback pause/resume.
public sealed record CurrentTrack(long Revision, string Title, string Artist,
    string? Album, TimeSpan? Duration, PlaybackState Playback);

public sealed record LyricsCandidate(long Id, string Title, string Artist,
    string? Album, TimeSpan? Duration, bool IsInstrumental,
    string? PlainLyrics, string? SyncedLyrics);

public enum LyricsOrigin { Lrclib, Manual }
public sealed record ResolvedLyrics(long TrackRevision, string Text,
    LyricsOrigin Origin, long? LrclibId, bool IsInstrumental);

public sealed record TranslatedLine(int Id, string Text);
public sealed record ExplainedMetaphor(string Text, string Explanation);
public sealed record SongInsight(long TrackRevision, string Language,
    IReadOnlyList<TranslatedLine> Translation, string Summary,
    IReadOnlyList<string> Themes, IReadOnlyList<ExplainedMetaphor> Metaphors,
    IReadOnlyList<string> Alternatives, IReadOnlyList<string> Warnings);

public sealed record AppSettings(bool AiEnabled = false, string? Model = null,
    int DailyRequestLimit = 20);

public interface ITrackDetector : IAsyncDisposable
{
    event EventHandler<TrackDetection>? TrackChanged;
    Task StartAsync(CancellationToken cancellationToken);
}

public sealed record TrackDetection(ApplicationState State, CurrentTrack? Track, string? SourceAppId);

public interface ILyricsProvider
{
    Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken cancellationToken);
}

public interface IInsightProvider
{
    Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken cancellationToken);
}

// Secrets have no place in this interface or its SQLite implementation.
public interface ISettingsStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<AppSettings> ReadSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<int> ReadRequestCountAsync(DateOnly localDate, CancellationToken cancellationToken = default);
}
