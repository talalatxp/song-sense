using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed class ChatGptRequestBudget(SqliteSettingsStore store) : IRequestBudget
{
    public Task<bool> TryReserveRequestAsync(DateOnly date, CancellationToken cancellationToken = default)
        => store.TryReservePlanRequestAsync(date, cancellationToken);
}
