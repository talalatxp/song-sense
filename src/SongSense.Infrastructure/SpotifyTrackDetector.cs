using System.Threading.Channels;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed class SpotifyTrackDetector : ITrackDetector
{
    private readonly IMediaSessionSource source;
    private readonly TimeSpan recoveryInterval;
    private readonly TrackObservationReducer reducer = new();
    private readonly Channel<bool> changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest
    });
    private CancellationTokenSource? lifetime;
    private Task? worker;
    private Task? recovery;
    private TrackDetection? last;
    private long generation;
    private bool disposed;
    public event EventHandler<TrackDetection>? TrackChanged;

    public SpotifyTrackDetector(IMediaSessionSource source, TimeSpan? recoveryInterval = null)
    {
        this.source = source;
        this.recoveryInterval = recoveryInterval ?? TimeSpan.FromSeconds(5);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (worker is not null) return Task.CompletedTask;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.Changed += OnChanged;
        worker = Task.Run(() => ObserveAsync(lifetime.Token), CancellationToken.None);
        recovery = Task.Run(() => RecoverAsync(lifetime.Token), CancellationToken.None);
        Signal();
        return Task.CompletedTask;
    }

    private void OnChanged(object? sender, EventArgs args) => Signal();
    private void Signal()
    {
        Interlocked.Increment(ref generation);
        changes.Writer.TryWrite(true);
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(recoveryInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken)) changes.Writer.TryWrite(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task ObserveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in changes.Reader.ReadAllAsync(cancellationToken))
            {
                var readingGeneration = Interlocked.Read(ref generation);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    var sessions = await source.GetSessionsAsync(timeout.Token);
                    var reads = sessions.Where(s => SpotifySessionRules.IsSpotify(s.SourceAppId)).Select(s => s.ReadAsync(timeout.Token));
                    var snapshots = await Task.WhenAll(reads);
                    if (cancellationToken.IsCancellationRequested) break;
                    if (readingGeneration != Interlocked.Read(ref generation)) continue;
                    Publish(reducer.Apply(snapshots));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    if (readingGeneration == Interlocked.Read(ref generation)) Publish(reducer.Error());
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            source.Changed -= OnChanged;
            source.Dispose();
        }
    }

    private void Publish(TrackDetection observation)
    {
        if (last == observation) return;
        last = observation;
        TrackChanged?.Invoke(this, observation);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        if (lifetime is null) { source.Dispose(); return; }
        await lifetime.CancelAsync();
        changes.Writer.TryComplete();
        await Task.WhenAll(worker!, recovery!);
        lifetime.Dispose();
    }
}
