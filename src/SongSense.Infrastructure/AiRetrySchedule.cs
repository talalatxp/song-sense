using System.Net.Http;

namespace SongSense.Infrastructure;

internal static class AiRetrySchedule
{
    public static DateTimeOffset? Read(HttpResponseMessage response)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var retry = response.Headers.RetryAfter;
            if (retry?.Delta is { } delay && delay >= TimeSpan.Zero && delay <= DateTimeOffset.MaxValue - now) return now + delay;
            if (retry?.Date is { } date && date > now) return date;
        }
        catch (FormatException) { }
        return null;
    }
}
