using System.Threading.Channels;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class SpotifyDetectorTests
{
    private static MediaSessionSnapshot Track(string title) => new("1", "Spotify.exe", title, "Artist", null, null, PlaybackState.Playing);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task ObsoleteMetadataReadCannotPublishAfterNewerEvent()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<MediaSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeSession { Read = token => { started.TrySetResult(); return release.Task.WaitAsync(token); } };
        var source = new FakeSource(session);
        await using var detector = new SpotifyTrackDetector(source);
        var observations = Observe(detector);
        await detector.StartAsync(CancellationToken.None);
        await started.Task.WaitAsync(Deadline);
        session.Read = _ => Task.FromResult(Track("New"));
        source.RaiseChanged();
        release.SetResult(Track("Old"));
        var result = await observations.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
        Assert.Equal("New", result.Track!.Title);
        Assert.False(observations.Reader.TryRead(out _));
    }

    [Fact]
    public async Task RecoveryPollingWorksWithoutNativeEventsAndDisposalUnsubscribes()
    {
        var session = new FakeSession { Read = _ => Task.FromResult(Track("First")) };
        var source = new FakeSource(session);
        var detector = new SpotifyTrackDetector(source, TimeSpan.FromMilliseconds(30));
        var observations = Observe(detector);
        try
        {
            await detector.StartAsync(CancellationToken.None);
            Assert.Equal("First", (await observations.Reader.ReadAsync().AsTask().WaitAsync(Deadline)).Track!.Title);
            session.Read = _ => Task.FromResult(Track("Second"));
            Assert.Equal("Second", (await observations.Reader.ReadAsync().AsTask().WaitAsync(Deadline)).Track!.Title);
        }
        finally { await detector.DisposeAsync(); }
        Assert.True(source.Disposed);
        Assert.True(session.Disposed);
        Assert.Equal(0, source.SubscriberCount);
        source.RaiseChanged();
        Assert.False(observations.Reader.TryRead(out _));
        await detector.DisposeAsync();
    }

    [Fact]
    public async Task ReadFailureClearsTrackAndNextEventRecovers()
    {
        var session = new FakeSession { Read = _ => Task.FromResult(Track("First")) };
        var source = new FakeSource(session);
        await using var detector = new SpotifyTrackDetector(source);
        var observations = Observe(detector);
        await detector.StartAsync(CancellationToken.None);
        var first = await observations.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
        session.Read = _ => throw new InvalidOperationException("controlled read failure");
        source.RaiseChanged();
        var error = await observations.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
        Assert.Equal(ApplicationState.Error, error.State);
        Assert.Null(error.Track);
        session.Read = _ => Task.FromResult(Track("Recovered"));
        source.RaiseChanged();
        var recovered = await observations.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
        Assert.Equal("Recovered", recovered.Track!.Title);
        Assert.True(recovered.Track.Revision > first.Track!.Revision);
    }

    [Fact]
    public async Task CancellationReleasesSourceEvenDuringPendingRead()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeSession { Read = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Track("Never published");
        } };
        var source = new FakeSource(session);
        var detector = new SpotifyTrackDetector(source);
        var observations = Observe(detector);
        using var lifetime = new CancellationTokenSource();
        await detector.StartAsync(lifetime.Token);
        await started.Task.WaitAsync(Deadline);
        lifetime.Cancel();
        await detector.DisposeAsync().AsTask().WaitAsync(Deadline);
        Assert.True(source.Disposed);
        Assert.Equal(0, source.SubscriberCount);
        Assert.False(observations.Reader.TryRead(out _));
    }

    private static Channel<TrackDetection> Observe(ITrackDetector detector)
    {
        var channel = Channel.CreateUnbounded<TrackDetection>();
        detector.TrackChanged += (_, result) => channel.Writer.TryWrite(result);
        return channel;
    }

    private sealed class FakeSession : IMediaSession
    {
        public string SourceAppId => "Spotify.exe";
        public Func<CancellationToken, Task<MediaSessionSnapshot>> Read { get; set; } = null!;
        public bool Disposed { get; private set; }
        public Task<MediaSessionSnapshot> ReadAsync(CancellationToken cancellationToken) => Read(cancellationToken);
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeSource(FakeSession session) : IMediaSessionSource
    {
        public event EventHandler? Changed;
        public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;
        public bool Disposed { get; private set; }
        public Task<IReadOnlyList<IMediaSession>> GetSessionsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<IMediaSession>>([session]);
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
        public void Dispose() { Disposed = true; session.Dispose(); }
    }
}
