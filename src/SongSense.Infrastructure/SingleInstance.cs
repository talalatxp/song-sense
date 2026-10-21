using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace SongSense.Infrastructure;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource stop = new();
    private Task listener = Task.CompletedTask;
    public bool IsFirst { get; }
    public SingleInstance(string? isolatedName = null)
    {
        var identity = isolatedName ?? WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Usuario no disponible.");
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        pipeName = "SongSense.Open." + suffix + "." + System.Diagnostics.Process.GetCurrentProcess().SessionId;
        // Kernel object lifetime elects one process without thread-affine mutex ownership.
        mutex = new Mutex(false, @"Local\SongSense.Instance." + suffix, out var created);
        IsFirst = created;
    }
    public void Listen(Action open)
    {
        if (!IsFirst || !listener.IsCompleted) return;
        listener = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(stop.Token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    var buffer = new byte[1];
                    if (await server.ReadAsync(buffer, timeout.Token) == 1 && buffer[0] == 1) open();
                }
                catch (OperationCanceledException) { }
                catch (IOException) { if (!stop.IsCancellationRequested) await Task.Delay(100, stop.Token).ConfigureAwait(false); }
            }
        });
    }
    public async Task<bool> ActivateFirstAsync()
    {
        if (IsFirst) return false;
        try
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await client.ConnectAsync(timeout.Token); await client.WriteAsync(new byte[] { 1 }, timeout.Token); return true;
        }
        catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException) { return false; }
    }
    public void Dispose()
    {
        stop.Cancel();
        mutex.Dispose();
        _ = listener.ContinueWith(task => { _ = task.Exception; stop.Dispose(); }, TaskScheduler.Default);
    }
}
