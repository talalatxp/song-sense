using SongSense.Core;
using Windows.Media;
using Windows.Media.Control;

namespace SongSense.Infrastructure;

public sealed class WindowsMediaSessionSource : IMediaSessionSource
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private readonly Dictionary<GlobalSystemMediaTransportControlsSession, Session> subscriptions = new();
    private long nextKey;
    public event EventHandler? Changed;

    public async Task<IReadOnlyList<IMediaSession>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        if (manager is null)
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);
            manager.SessionsChanged += SessionsChanged;
        }
        var sessions = manager.GetSessions().Where(s => SpotifySessionRules.IsSpotify(s.SourceAppUserModelId)).ToArray();
        foreach (var removed in subscriptions.Keys.Except(sessions).ToArray())
        {
            subscriptions[removed].Dispose();
            subscriptions.Remove(removed);
        }
        foreach (var session in sessions)
        {
            if (!subscriptions.ContainsKey(session))
                subscriptions.Add(session, new Session(session, (++nextKey).ToString("D12"), () => Changed?.Invoke(this, EventArgs.Empty)));
        }
        return subscriptions.Values.Cast<IMediaSession>().ToArray();
    }

    private void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (manager is not null) manager.SessionsChanged -= SessionsChanged;
        foreach (var session in subscriptions.Values) session.Dispose();
        subscriptions.Clear();
        manager = null;
    }

    private sealed class Session : IMediaSession
    {
        private readonly GlobalSystemMediaTransportControlsSession session;
        private readonly string key;
        private readonly Action changed;
        public string SourceAppId => session.SourceAppUserModelId;

        public Session(GlobalSystemMediaTransportControlsSession session, string key, Action changed)
        {
            this.session = session;
            this.key = key;
            this.changed = changed;
            session.MediaPropertiesChanged += MediaChanged;
            session.PlaybackInfoChanged += PlaybackChanged;
            session.TimelinePropertiesChanged += TimelineChanged;
        }

        public async Task<MediaSessionSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            var properties = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
            var timeline = session.GetTimelineProperties();
            var length = timeline.EndTime - timeline.StartTime;
            var playback = session.GetPlaybackInfo().PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackState.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackState.Paused,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => PlaybackState.Stopped,
                _ => PlaybackState.Unknown
            };
            var kind = properties.PlaybackType switch
            {
                MediaPlaybackType.Music => MediaKind.Music,
                null => MediaKind.Unknown,
                _ => MediaKind.Other
            };
            return new MediaSessionSnapshot(key, SourceAppId, properties.Title, properties.Artist,
                properties.AlbumTitle, length > TimeSpan.Zero ? length : null, playback, kind);
        }

        private void MediaChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => changed();
        private void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => changed();
        private void TimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => changed();

        public void Dispose()
        {
            session.MediaPropertiesChanged -= MediaChanged;
            session.PlaybackInfoChanged -= PlaybackChanged;
            session.TimelinePropertiesChanged -= TimelineChanged;
        }
    }
}
