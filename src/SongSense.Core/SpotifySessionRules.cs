namespace SongSense.Core;

public enum MediaKind { Unknown, Music, Other }

public sealed record MediaSessionSnapshot(string SessionKey, string SourceAppId,
    string Title, string Artist, string? Album, TimeSpan? Duration,
    PlaybackState Playback, MediaKind Kind = MediaKind.Unknown);

public static class SpotifySessionRules
{
    public const string StoreAppId = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify";

    public static bool IsSpotify(string appId) =>
        string.Equals(appId, "Spotify.exe", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(appId, StoreAppId, StringComparison.OrdinalIgnoreCase);

    public static MediaSessionSnapshot? Select(IEnumerable<MediaSessionSnapshot> sessions, string? previousKey)
    {
        var spotify = sessions.Where(s => IsSpotify(s.SourceAppId)).ToArray();
        var playing = spotify.Where(s => s.Playback == PlaybackState.Playing).ToArray();
        var preferred = playing.Length > 0 ? playing : spotify;
        return preferred.FirstOrDefault(s => s.SessionKey == previousKey)
            ?? preferred.OrderBy(s => s.SourceAppId, StringComparer.Ordinal)
                .ThenBy(s => s.SessionKey, StringComparer.Ordinal).FirstOrDefault();
    }
}

// Used only by the serialized detector worker. Playback changes do not create track revisions.
public sealed class TrackObservationReducer
{
    private long revision;
    private string? selectedKey;
    private CurrentTrack? active;
    public string? SelectedKey => selectedKey;

    public TrackDetection Apply(IEnumerable<MediaSessionSnapshot> sessions)
    {
        var session = SpotifySessionRules.Select(sessions, selectedKey);
        var oldKey = selectedKey;
        selectedKey = session?.SessionKey;
        if (session is null) return Clear(ApplicationState.NoSpotify, null);
        if (session.Kind == MediaKind.Other || string.IsNullOrWhiteSpace(session.Title) || string.IsNullOrWhiteSpace(session.Artist))
            return Clear(ApplicationState.NoTrack, session.SourceAppId);

        var title = session.Title.Trim();
        var artist = session.Artist.Trim();
        var album = string.IsNullOrWhiteSpace(session.Album) ? null : session.Album.Trim();
        var duration = session.Duration > TimeSpan.Zero ? session.Duration : null;
        if (active is null || oldKey != selectedKey || active.Title != title || active.Artist != artist || active.Album != album)
            revision++;
        active = new CurrentTrack(revision, title, artist, album, duration, session.Playback);
        var state = session.Playback switch
        {
            PlaybackState.Playing => ApplicationState.Playing,
            PlaybackState.Paused => ApplicationState.Paused,
            PlaybackState.Stopped => ApplicationState.Stopped,
            _ => ApplicationState.PlaybackUnknown
        };
        return new TrackDetection(state, active, session.SourceAppId);
    }

    public TrackDetection Error()
    {
        selectedKey = null;
        return Clear(ApplicationState.Error, null);
    }

    private TrackDetection Clear(ApplicationState state, string? source)
    {
        if (active is not null) revision++;
        active = null;
        return new TrackDetection(state, null, source);
    }
}
