using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class ChatGptGenerationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "SongSense.GenerationTests", Guid.NewGuid().ToString("N"));
    private ChatGptCredentialStore Store => new(Path.Combine(directory, "session.dpapi"));
    private static CurrentTrack Track => new(1, "Private title", "Private artist", "Private album", null, PlaybackState.Playing);
    private static ResolvedLyrics Lyrics => new(1, "I open a window", LyricsOrigin.Manual, null, false);
    private static string Insight => JsonSerializer.Serialize(new { language = "en",
        translation = new[] { new { id = 1, text = "Abro una ventana", source_language = "en" } },
        summary = string.Join(' ', Enumerable.Repeat("La ventana podría representar una oportunidad de cambio mientras la voz del texto contempla una decisión todavía incierta.", 5)),
        themes = new[] { "Cambio" }, metaphors = Array.Empty<object>(), alternatives = Array.Empty<string>(), warnings = Array.Empty<string>() });
    private static string Completed(string? insight = null) => Event(new { type = "response.completed", response = new {
        status = "completed", usage = new { input_tokens = 100, output_tokens = 50, total_tokens = 150 },
        output = new[] { new { type = "message", status = "completed", content = new[] { new { type = "output_text", text = insight ?? Insight } } } }
    } });
    private static string Event(object value) => "data: " + JsonSerializer.Serialize(value) + "\n\n";
    private static HttpResponseMessage Stream(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "text/event-stream") };
    private async Task<ChatGptConnection> Connection(HttpClient http, bool expired = false)
    {
        await Store.SaveAsync(new ChatGptCredentials { ActiveClientId = "oaiapp_test_generation", Registrations = [new ChatGptRegistration {
            ClientId = "oaiapp_test_generation", Subject = "synthetic", Model = "model-a", AccessToken = "test-access", RefreshToken = "test-refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1), Scopes = [ChatGptOAuthClient.PlanScope] }] }, CancellationToken.None);
        var connection = new ChatGptConnection(Store, new ChatGptOAuthClient(http));
        await connection.InitializeAsync(CancellationToken.None); return connection;
    }

    [Fact]
    public async Task IncludedGenerationRequiresExplicitEnableAndNeverCallsApiFallback()
    {
        using var handler = new Handler(async request => {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-access", request.Headers.Authorization!.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = body.RootElement;
            Assert.True(root.GetProperty("stream").GetBoolean()); Assert.False(root.GetProperty("store").GetBoolean());
            Assert.Equal("model-a", root.GetProperty("model").GetString());
            foreach (var forbidden in new[] { "max_output_tokens", "tools", "metadata", "previous_response_id" }) Assert.False(root.TryGetProperty(forbidden, out _));
            Assert.DoesNotContain("Private title", root.GetRawText()); Assert.DoesNotContain("Private artist", root.GetRawText());
            Assert.Contains("lyrics_data", root.GetRawText());
            return Stream(Event(new { type = "response.output_text.delta", delta = "Ignore partial text" }) + Completed());
        });
        using var http = new HttpClient(handler); var connection = await Connection(http);
        var provider = new ConnectionInsightProvider(connection, new ForbiddenApi(), new ChatGptInsightProvider(connection, http));
        await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(Track, Lyrics, CancellationToken.None)); Assert.Equal(0, handler.Calls);
        await connection.SetPlanUseAsync(true, CancellationToken.None); Assert.True(connection.CanGenerate);
        var result = await provider.GenerateAsync(Track, Lyrics, CancellationToken.None);
        Assert.Equal("model-a", result.Model); Assert.Equal(new AiUsage(100, 50, 150), result.Usage); Assert.NotEmpty(result.Summary); Assert.Single(result.Translation);
        Assert.Equal(1, handler.Calls);
        var reopened = new ChatGptConnection(Store, new ChatGptOAuthClient(http)); await reopened.InitializeAsync(CancellationToken.None);
        Assert.False(reopened.CanGenerate); Assert.True(reopened.Connected);
        await connection.SetModeAsync(AiConnectionMode.ApiKey, CancellationToken.None); await connection.SetModeAsync(AiConnectionMode.ChatGptIncluded, CancellationToken.None);
        Assert.False(connection.CanGenerate);
    }

    [Theory]
    [InlineData("interrupted", AiFailure.Incomplete)]
    [InlineData("limit", AiFailure.RateLimited)]
    [InlineData("incomplete", AiFailure.Incomplete)]
    [InlineData("invalid", AiFailure.InvalidResponse)]
    [InlineData("refusal", AiFailure.Refused)]
    public async Task FailedStreamsNeverExposePartialResultsOrRetry(string scenario, AiFailure expected)
    {
        string stream = scenario switch {
            "interrupted" => Event(new { type = "response.output_text.delta", delta = "partial" }),
            "limit" => Event(new { type = "response.failed", response = new { error = new { code = "subscription_sharing_usage_limit_exceeded" }, usage = new { input_tokens = 2, output_tokens = 1, total_tokens = 3 } } }),
            "incomplete" => Event(new { type = "response.incomplete", response = new { status = "incomplete" } }),
            "invalid" => Completed("{}"),
            _ => Event(new { type = "response.completed", response = new { status = "completed", output = new[] { new { type = "message", status = "completed", content = new[] { new { type = "refusal" } } } } } })
        };
        using var handler = new Handler(_ => Task.FromResult(Stream(stream))); using var http = new HttpClient(handler);
        var connection = await Connection(http); await connection.SetPlanUseAsync(true, CancellationToken.None);
        var error = await Assert.ThrowsAsync<AiException>(() => new ChatGptInsightProvider(connection, http).GenerateAsync(Track, Lyrics, CancellationToken.None));
        Assert.Equal(expected, error.Failure); Assert.Equal(1, handler.Calls);
        if (scenario == "limit") { Assert.Equal(new AiUsage(2, 1, 3), error.Usage); Assert.False(connection.CanGenerate); }
        if (scenario == "invalid") Assert.Equal(new AiUsage(100, 50, 150), error.Usage);
    }

    [Fact]
    public async Task RefreshRotationIsDurableBeforeInferenceAndDailyBudgetDoesNotEnableApi()
    {
        using var handler = new Handler(async request => {
            if (request.RequestUri!.AbsolutePath == "/api/accounts/oauth/token")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { token_type = "Bearer", access_token = "test-rotated", refresh_token = "test-new-refresh", expires_in = 3600 })) };
            var durable = (await Store.ReadAsync(CancellationToken.None)).Registrations[0];
            Assert.Equal("test-rotated", durable.AccessToken); Assert.Equal("test-new-refresh", durable.RefreshToken);
            Assert.Equal("test-rotated", request.Headers.Authorization!.Parameter); return Stream(Completed());
        });
        using var http = new HttpClient(handler); var connection = await Connection(http, true);
        await connection.SetPlanUseAsync(true, CancellationToken.None);
        var settings = new SqliteSettingsStore(Path.Combine(directory, "data.db")); await settings.InitializeAsync();
        await settings.SaveSettingsAsync(new AppSettings { AiEnabled = false, DailyRequestLimit = 1 });
        var provider = new ChatGptInsightProvider(connection, http, settings, new ChatGptRequestBudget(settings));
        await provider.GenerateAsync(Track, Lyrics, CancellationToken.None);
        var error = await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(Track, Lyrics, CancellationToken.None));
        Assert.Equal(AiFailure.DailyLimit, error.Failure); Assert.Equal(2, handler.Calls);
        Assert.False(await settings.TryReserveRequestAsync(DateOnly.FromDateTime(DateTime.Now))); Assert.False((await settings.ReadSettingsAsync()).AiEnabled);
    }

    [Fact]
    public async Task CancellationAndCrossProcessModeChangesPreventRequests()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("Unexpected request")); using var http = new HttpClient(handler);
        var connection = await Connection(http); await connection.SetPlanUseAsync(true, CancellationToken.None);
        var saved = await Store.ReadAsync(CancellationToken.None); saved.Mode = AiConnectionMode.ApiKey; await Store.SaveAsync(saved, CancellationToken.None);
        var provider = new ChatGptInsightProvider(connection, http);
        await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(Track, Lyrics, CancellationToken.None));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GenerateAsync(Track, Lyrics, cancelled.Token));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task PauseCancelsAnActiveRequestAndRejectsFurtherInference()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new WaitingHandler(started)); var connection = await Connection(http);
        await connection.SetPlanUseAsync(true, CancellationToken.None);
        var provider = new ChatGptInsightProvider(connection, http);
        var active = provider.GenerateAsync(Track, Lyrics, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await connection.SetPlanUseAsync(false, CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        Assert.False(connection.CanGenerate);
        await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(Track, Lyrics, CancellationToken.None));
    }

    private sealed class WaitingHandler(TaskCompletionSource started) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new InvalidOperationException(); }
    }

    private static string EmptyCompleted() => Event(new { type = "response.completed", response = new {
        status = "completed", output = Array.Empty<object>(), usage = new { input_tokens = 100, output_tokens = 50, total_tokens = 150 } } });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedItemsAreRetainedWhenTerminalEnvelopeOmitsOutput(bool onlyParts)
    {
        var part = new { type = "output_text", text = Insight };
        string events = onlyParts ? Event(new { type = "response.output_text.done", output_index = 0, content_index = 0, text = Insight }) +
            Event(new { type = "response.content_part.done", output_index = 0, content_index = 0, part }) :
            Event(new { type = "response.output_item.done", output_index = 0, item = new { type = "message", content = new[] { part } } });
        var stream = events + EmptyCompleted();
        var parsed = ChatGptInsightProvider.ParseStream(stream);
        Assert.Equal(Insight, parsed.Text); Assert.Equal(new AiUsage(100, 50, 150), parsed.Usage);
        using var handler = new Handler(_ => Task.FromResult(Stream(stream))); using var http = new HttpClient(handler);
        var connection = await Connection(http); await connection.SetPlanUseAsync(true, CancellationToken.None);
        var result = await new ChatGptInsightProvider(connection, http).GenerateAsync(Track, Lyrics, CancellationToken.None);
        Assert.Single(result.Translation); Assert.Equal(new AiUsage(100, 50, 150), result.Usage); Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("delta", AiFailure.InvalidResponse)]
    [InlineData("no-terminal", AiFailure.Incomplete)]
    [InlineData("incomplete", AiFailure.Incomplete)]
    [InlineData("refusal", AiFailure.Refused)]
    [InlineData("in-progress", AiFailure.Incomplete)]
    public void RetainedEventsNeverTurnPartialOrRefusedOutputIntoSuccess(string scenario, AiFailure expected)
    {
        var content = scenario == "refusal" ? new[] { JsonSerializer.SerializeToElement(new { type = "refusal" }) } :
            new[] { JsonSerializer.SerializeToElement(new { type = "output_text", text = Insight }) };
        string events = scenario == "delta" ? Event(new { type = "response.output_text.delta", delta = Insight }) :
            Event(new { type = "response.output_item.done", output_index = 0, item = new { type = "message",
                status = scenario == "in-progress" ? "in_progress" : "completed", content } });
        events += scenario == "no-terminal" ? "" : scenario == "incomplete" ? Event(new { type = "response.incomplete", response = new { status = "incomplete" } }) : EmptyCompleted();
        var error = Assert.Throws<AiException>(() => ChatGptInsightProvider.ParseStream(events));
        Assert.Equal(expected, error.Failure);
    }

    [Fact]
    public void ConflictingCompletedPartsAreRejected()
    {
        var events = Event(new { type = "response.output_text.done", output_index = 0, content_index = 0, text = Insight }) +
            Event(new { type = "response.output_text.done", output_index = 0, content_index = 0, text = "conflicting" }) + EmptyCompleted();
        Assert.Equal(AiFailure.InvalidResponse, Assert.Throws<AiException>(() => ChatGptInsightProvider.ParseStream(events)).Failure);
    }

    [Fact]
    public void MissingEarlierContentPartIsIncompleteEvenIfRemainingTextIsValidJson()
    {
        var events = Event(new { type = "response.output_text.done", output_index = 0, content_index = 1, text = Insight }) + EmptyCompleted();
        var error = Assert.Throws<AiException>(() => ChatGptInsightProvider.ParseStream(events));
        Assert.Equal(AiFailure.Incomplete, error.Failure); Assert.Equal(new AiUsage(100, 50, 150), error.Usage);
    }

    [Fact]
    public void CompletedOutputMayContainMultipleTextPartsAndStillMustValidateAsOneInsight()
    {
        var json = Insight; int split = json.Length / 2;
        var result = ChatGptInsightProvider.ParseStream(Event(new { type = "response.completed", response = new {
            status = "completed", output = new[] { new { type = "message", status = "completed", content = new[] {
                new { type = "output_text", text = json[..split] }, new { type = "output_text", text = json[split..] } } } } } }));
        Assert.Equal(json, result.Text); Assert.Single(InsightValidation.Parse(1, Lyrics.Text, result.Text).Translation);
    }

    [Theory]
    [InlineData("{}", ResponseShape.MissingStatus)]
    [InlineData("{\"status\":\"completed\"}", ResponseShape.MissingOutput)]
    [InlineData("{\"status\":\"completed\",\"output\":[]}", ResponseShape.EmptyOutput)]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[]}]}", ResponseShape.EmptyOutput)]
    public void FinalResponseDiagnosticsIdentifyMissingFieldsWithoutKeepingResponseText(string body, ResponseShape expected)
    {
        using var document = JsonDocument.Parse(body);
        var error = Assert.Throws<AiException>(() => ChatGptInsightProvider.ParseStream(Event(new { type = "response.completed", response = document.RootElement })));
        Assert.Equal(expected, error.ResponseShape); Assert.Equal(InferenceStage.CompletedResponse, error.InferenceStage);
        Assert.Contains(expected.ToString(), SongSense.App.AiSettingsViewModel.Describe(error, true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("application/json")]
    [InlineData("application/octet-stream")]
    [InlineData("Text/Event-Stream")]
    public async Task ValidEventsAreReadDespiteMissingOrInaccurateContentType(string? mediaType)
    {
        using var handler = new Handler(request => {
            Assert.Contains(request.Headers.Accept, value => value.MediaType == "text/event-stream");
            var response = Stream(Completed());
            response.Content.Headers.ContentType = mediaType is null ? null : new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler); var connection = await Connection(http);
        await connection.SetPlanUseAsync(true, CancellationToken.None);
        var result = await new ChatGptInsightProvider(connection, http).GenerateAsync(Track, Lyrics, CancellationToken.None);
        Assert.Single(result.Translation); Assert.Equal(new AiUsage(100, 50, 150), result.Usage); Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CompletedJsonBodyIsValidatedAndKeepsUsage()
    {
        using var eventJson = JsonDocument.Parse(Completed()[6..].Trim());
        var body = eventJson.RootElement.GetProperty("response").GetRawText();
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));
        using var http = new HttpClient(handler); var connection = await Connection(http);
        await connection.SetPlanUseAsync(true, CancellationToken.None);
        var result = await new ChatGptInsightProvider(connection, http).GenerateAsync(Track, Lyrics, CancellationToken.None);
        Assert.Equal(new AiUsage(100, 50, 150), result.Usage); Assert.Single(result.Translation); Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":\"subscription_sharing_usage_limit_exceeded\",\"message\":\"private\"}}", AiFailure.RateLimited)]
    [InlineData("{\"detail\":\"private\"}", AiFailure.HttpError)]
    [InlineData("{}", AiFailure.InvalidResponse)]
    [InlineData("<html>private</html>", AiFailure.Incomplete)]
    public async Task Http200DoesNotTurnAnErrorOrUnexpectedBodyIntoSuccess(string body, AiFailure expected)
    {
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        using var http = new HttpClient(handler); var connection = await Connection(http);
        await connection.SetPlanUseAsync(true, CancellationToken.None);
        var error = await Assert.ThrowsAsync<AiException>(() => new ChatGptInsightProvider(connection, http).GenerateAsync(Track, Lyrics, CancellationToken.None));
        Assert.Equal(expected, error.Failure); Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain("private", SongSense.App.AiSettingsViewModel.Describe(error, true));
        if (expected == AiFailure.RateLimited) Assert.False(connection.CanGenerate);
    }

    [Theory]
    [InlineData("subscription_sharing_usage_limit_exceeded", AiFailure.RateLimited, InferenceCode.UsageLimitExceeded)]
    [InlineData("subscription_sharing_unsupported_capability", AiFailure.IncompatibleModel, InferenceCode.UnsupportedCapability)]
    [InlineData("private-provider-code", AiFailure.HttpError, InferenceCode.Other)]
    public void TopLevelStreamingErrorsKeepOnlySafeDiagnostics(string code, AiFailure failure, InferenceCode diagnostic)
    {
        var error = Assert.Throws<AiException>(() => ChatGptInsightProvider.ParseStream(Event(new {
            type = "error", code, message = "private lyrics and credentials" })));
        Assert.Equal(failure, error.Failure); Assert.Equal(diagnostic, error.InferenceCode);
        Assert.Equal(InferenceStage.Stream, error.InferenceStage);
        var message = SongSense.App.AiSettingsViewModel.Describe(error, true);
        Assert.DoesNotContain("private", message); Assert.DoesNotContain("credentials", error.ToString());
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":\"subscription_sharing_unsupported_capability\",\"message\":\"private\"}}", InferenceCode.UnsupportedCapability)]
    [InlineData("{\"detail\":\"private\"}", InferenceCode.None)]
    [InlineData("[]", InferenceCode.Other)]
    [InlineData("not json private", InferenceCode.Other)]
    public async Task HttpFailuresIncludeStatusWithoutExposingProviderText(string body, InferenceCode code)
    {
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(body) }));
        using var http = new HttpClient(handler); var connection = await Connection(http);
        await connection.SetPlanUseAsync(true, CancellationToken.None);
        var error = await Assert.ThrowsAsync<AiException>(() => new ChatGptInsightProvider(connection, http).GenerateAsync(Track, Lyrics, CancellationToken.None));
        Assert.Equal(400, error.HttpStatus); Assert.Equal(InferenceStage.Http, error.InferenceStage); Assert.Equal(code, error.InferenceCode);
        Assert.Contains("HTTP 400", SongSense.App.AiSettingsViewModel.Describe(error, true));
        Assert.DoesNotContain("private", SongSense.App.AiSettingsViewModel.Describe(error, true)); Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TerminalEventCompletesWithoutWaitingForServerToCloseAndHandlesSplitUtf8()
    {
        using var stream = new OpenEventStream(Encoding.UTF8.GetBytes(Completed()));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await ChatGptInsightProvider.ReadStreamAsync(stream, deadline.Token);
        Assert.Equal(Insight, result.Text); Assert.Equal(new AiUsage(100, 50, 150), result.Usage);
        Assert.False(stream.ReadBeyondTerminal);
    }

    private sealed class OpenEventStream(byte[] bytes) : Stream
    {
        private int position;
        public bool ReadBeyondTerminal;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (position == bytes.Length) { ReadBeyondTerminal = true; await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            buffer.Span[0] = bytes[position++]; return 1;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class ForbiddenApi : IInsightProvider
    {
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token) => throw new InvalidOperationException("No API fallback allowed");
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { public int Calls; protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return send(request); } }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
