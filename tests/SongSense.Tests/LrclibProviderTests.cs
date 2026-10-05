using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class LrclibProviderTests
{
    private static CurrentTrack Track => new(9, "Song & title", "Artist", "Album", TimeSpan.FromSeconds(180), PlaybackState.Playing);
    private static object Record(string? text = "Texto propio", bool instrumental = false) => new
    {
        id = 123, trackName = Track.Title, artistName = Track.Artist, albumName = Track.Album,
        duration = 180, instrumental, plainLyrics = text, syncedLyrics = (string?)null
    };
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ExactRequestUsesGetWithEncodedMetadataAndApplicationUserAgent()
    {
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("lrclib.net", request.RequestUri.Host);
            Assert.Equal("/api/get", request.RequestUri.AbsolutePath);
            Assert.Contains("track_name=Song%20%26%20title", request.RequestUri.Query);
            Assert.Contains("album_name=Album", request.RequestUri.Query);
            Assert.Contains("duration=180", request.RequestUri.Query);
            Assert.Contains("SongSense/0.3", request.Headers.UserAgent.ToString());
            return Task.FromResult(Json(Record()));
        });
        using var http = new HttpClient(handler);
        using var provider = new LrclibLyricsProvider(http);
        Assert.Equal(LyricsSearchKind.Matched, (await provider.FindAsync(Track, CancellationToken.None)).Kind);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(204)]
    [InlineData(200)]
    public async Task MissingExactResultFallsBackToSearchAfterAtLeast300Milliseconds(int status)
    {
        var completed = 0L;
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/get")
            {
                completed = Stopwatch.GetTimestamp();
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("") });
            }
            Assert.True(Stopwatch.GetElapsedTime(completed).TotalMilliseconds >= 295);
            Assert.Equal("/api/search", request.RequestUri.AbsolutePath);
            Assert.DoesNotContain("album_name=", request.RequestUri.Query);
            return Task.FromResult(Json(new[] { Record() }));
        });
        using var http = new HttpClient(handler);
        using var provider = new LrclibLyricsProvider(http);
        Assert.Equal(LyricsSearchKind.Matched, (await provider.FindAsync(Track, CancellationToken.None)).Kind);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task UnknownMetadataUsesSearchAndRequiresSelection()
    {
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal("/api/search", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(new[] { Record() }));
        });
        using var http = new HttpClient(handler);
        using var provider = new LrclibLyricsProvider(http);
        var result = await provider.FindAsync(Track with { Album = null, Duration = null }, CancellationToken.None);
        Assert.Equal(LyricsSearchKind.Candidates, result.Kind);
        Assert.Null(result.Lyrics);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    public async Task EmptySearchMeansNoLyrics(string payload)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) }));
        using var http = new HttpClient(handler);
        using var provider = new LrclibLyricsProvider(http);
        Assert.Equal(LyricsSearchKind.NotFound, (await provider.FindAsync(Track with { Album = null }, CancellationToken.None)).Kind);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[{\"id\":123}]")]
    public async Task InvalidPayloadIsRecoverable(string payload)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) }));
        using var http = new HttpClient(handler);
        using var provider = new LrclibLyricsProvider(http);
        var error = await Assert.ThrowsAsync<LyricsProviderException>(() => provider.FindAsync(Track with { Album = null }, CancellationToken.None));
        Assert.Equal(LyricsFailure.InvalidResponse, error.Failure);
    }

    [Fact]
    public async Task InstrumentalDoesNotNeedLyrics()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(Record(null, true))));
        using var http = new HttpClient(handler);
        using var provider = new LrclibLyricsProvider(http);
        Assert.Equal(LyricsSearchKind.Instrumental, (await provider.FindAsync(Track, CancellationToken.None)).Kind);
    }

    [Theory]
    [InlineData("seconds", 10)]
    [InlineData("date", 10)]
    [InlineData("missing", 60)]
    [InlineData("invalid", 60)]
    public async Task RateLimitBlocksAllNetworkUntilRetryAfterAndNeverAutomaticallyRetries(string kind, int seconds)
    {
        var clock = new MutableClock();
        using var handler = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            if (kind == "seconds") response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            if (kind == "date") response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(seconds));
            if (kind == "invalid") response.Headers.TryAddWithoutValidation("Retry-After", "later");
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        using var provider = new LrclibLyricsProvider(http, clock);
        var first = await Assert.ThrowsAsync<LyricsProviderException>(() => provider.FindAsync(Track, CancellationToken.None));
        Assert.Equal(LyricsFailure.RateLimited, first.Failure);
        Assert.Equal(clock.GetUtcNow().AddSeconds(seconds), first.RetryAt);
        await Assert.ThrowsAsync<LyricsProviderException>(() => provider.FindAsync(Track with { Title = "Other" }, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
        clock.Advance(TimeSpan.FromSeconds(seconds + 1));
        await Assert.ThrowsAsync<LyricsProviderException>(() => provider.FindAsync(Track, CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("http", LyricsFailure.HttpError)]
    [InlineData("offline", LyricsFailure.Offline)]
    [InlineData("timeout", LyricsFailure.Timeout)]
    public async Task ErrorsHaveSeparateRecoveryStates(string kind, LyricsFailure expected)
    {
        using var handler = new Handler((_, _) => kind switch
        {
            "offline" => throw new HttpRequestException("controlled disconnect"),
            "timeout" => throw new TaskCanceledException("controlled timeout"),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
        });
        using var http = new HttpClient(handler);
        using var provider = new LrclibLyricsProvider(http);
        Assert.Equal(expected, (await Assert.ThrowsAsync<LyricsProviderException>(() => provider.FindAsync(Track, CancellationToken.None))).Failure);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ConcurrentSearchesAreSerialAndCancellationReleasesTheQueue()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maxActive = 0;
        var calls = 0;
        using var handler = new Handler(async (_, token) =>
        {
            maxActive = Math.Max(maxActive, Interlocked.Increment(ref active));
            try
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    started.SetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }
                return Json(new[] { Record() });
            }
            finally { Interlocked.Decrement(ref active); }
        });
        using var http = new HttpClient(handler);
        using var provider = new LrclibLyricsProvider(http);
        using var cancel = new CancellationTokenSource();
        var first = provider.FindAsync(Track with { Album = null }, cancel.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = provider.FindAsync(Track with { Album = null }, CancellationToken.None);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(LyricsSearchKind.Candidates, (await second.WaitAsync(TimeSpan.FromSeconds(2))).Kind);
        Assert.Equal(1, maxActive);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return send(request, cancellationToken); }
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
