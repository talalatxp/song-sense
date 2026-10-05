using System.Globalization;
using System.Net;
using System.Text.Json;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed class LrclibLyricsProvider : ILyricsProvider, IDisposable
{
    private readonly HttpClient http;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim requests = new(1, 1);
    private DateTimeOffset nextRequestAt;
    private DateTimeOffset? retryAt;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public LrclibLyricsProvider(HttpClient http, TimeProvider? clock = null)
    {
        this.http = http;
        this.clock = clock ?? TimeProvider.System;
    }

    public async Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken cancellationToken)
    {
        await requests.WaitAsync(cancellationToken);
        try
        {
            var query = $"track_name={Uri.EscapeDataString(track.Title)}&artist_name={Uri.EscapeDataString(track.Artist)}";
            if (!string.IsNullOrWhiteSpace(track.Album) && track.Duration is { } duration && duration > TimeSpan.Zero)
            {
                var exactQuery = query + $"&album_name={Uri.EscapeDataString(track.Album)}&duration={duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}";
                var exact = await GetAsync("get?" + exactQuery, false, cancellationToken);
                if (exact.Count > 0)
                {
                    var result = LyricsResolution.Resolve(track, exact);
                    if (result.Kind != LyricsSearchKind.NotFound) return result;
                }
            }
            return LyricsResolution.Resolve(track, await GetAsync("search?" + query, true, cancellationToken));
        }
        finally { requests.Release(); }
    }

    private async Task<IReadOnlyList<LyricsCandidate>> GetAsync(string path, bool isSearch, CancellationToken cancellationToken)
    {
        if (retryAt is { } cooldown && clock.GetUtcNow() < cooldown)
            throw new LyricsProviderException(LyricsFailure.RateLimited, cooldown);
        var delay = nextRequestAt - clock.GetUtcNow();
        if (delay > TimeSpan.Zero) await Task.Delay(delay, clock, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://lrclib.net/api/" + path));
            request.Headers.UserAgent.ParseAdd("SongSense/0.3 (Personal Windows application)");
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var now = clock.GetUtcNow();
                DateTimeOffset? until = null;
                try
                {
                    var header = response.Headers.RetryAfter;
                    if (header?.Delta is { } seconds && seconds >= TimeSpan.Zero) until = now + seconds;
                    else if (header?.Date is { } date && date > now) until = date;
                }
                catch (FormatException) { }
                retryAt = until ?? now.AddSeconds(60);
                throw new LyricsProviderException(LyricsFailure.RateLimited, retryAt);
            }
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.NoContent) return [];
            if (!response.IsSuccessStatusCode) throw new LyricsProviderException(LyricsFailure.HttpError);
            var payload = await response.Content.ReadAsStringAsync(timeout.Token);
            if (string.IsNullOrWhiteSpace(payload)) return [];
            if (isSearch)
            {
                var values = JsonSerializer.Deserialize<Record[]>(payload, JsonOptions);
                return values?.Select(ToCandidate).ToArray() ?? [];
            }
            var value = JsonSerializer.Deserialize<Record>(payload, JsonOptions);
            return value is null ? [] : [ToCandidate(value)];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new LyricsProviderException(LyricsFailure.Timeout); }
        catch (HttpRequestException)
        { throw new LyricsProviderException(LyricsFailure.Offline); }
        catch (JsonException)
        { throw new LyricsProviderException(LyricsFailure.InvalidResponse); }
        finally { nextRequestAt = clock.GetUtcNow().AddMilliseconds(300); }
    }

    private static LyricsCandidate ToCandidate(Record value)
    {
        if (value is null || value.Id is null or < 0 || string.IsNullOrWhiteSpace(value.TrackName) ||
            string.IsNullOrWhiteSpace(value.ArtistName) || value.Instrumental is null ||
            (value.Duration is { } invalid && (!double.IsFinite(invalid) || invalid < 0 || invalid > TimeSpan.MaxValue.TotalSeconds)))
            throw new JsonException("Invalid LRCLIB record.");
        return new LyricsCandidate(value.Id.Value, value.TrackName, value.ArtistName, value.AlbumName,
            value.Duration is > 0 ? TimeSpan.FromSeconds(value.Duration.Value) : null,
            value.Instrumental.Value, value.PlainLyrics, value.SyncedLyrics);
    }

    private sealed record Record(long? Id, string? TrackName, string? ArtistName, string? AlbumName,
        double? Duration, bool? Instrumental, string? PlainLyrics, string? SyncedLyrics);

    public void Dispose() => requests.Dispose();
}
