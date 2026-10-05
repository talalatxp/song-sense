using SongSense.Core;

namespace SongSense.Infrastructure;

// A small seam around WinRT for controlled race, selection and lifecycle tests.
public interface IMediaSession : IDisposable
{
    string SourceAppId { get; }
    Task<MediaSessionSnapshot> ReadAsync(CancellationToken cancellationToken);
}

public interface IMediaSessionSource : IDisposable
{
    event EventHandler? Changed;
    Task<IReadOnlyList<IMediaSession>> GetSessionsAsync(CancellationToken cancellationToken);
}
