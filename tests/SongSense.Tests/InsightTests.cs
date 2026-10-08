using System.IO;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class InsightTests
{
    // All excerpts and explanations here are original test material.
    private const string Lyrics = "I open a window\n\nI open a window";
    private static readonly string Summary = string.Join(' ', Enumerable.Repeat(
        "La ventana podría representar una oportunidad de cambio mientras la repetición sugiere una decisión todavía incierta.", 5));
    private static string Json(string language = "en", object[]? translation = null) => JsonSerializer.Serialize(new
    {
        language,
        translation = translation ?? [new { id = 1, text = "Abro una ventana", source_language = "en" }, new { id = 3, text = "Abro una ventana", source_language = "en" }],
        summary = Summary, themes = new[] { "Cambio", "Incertidumbre" },
        metaphors = new[] { new { text = "window", explanation = "Podría representar una oportunidad." } },
        alternatives = new[] { "También puede describir una acción cotidiana." }, warnings = new[] { "El texto admite varias lecturas." }
    });
    private static string Envelope(string json) => JsonSerializer.Serialize(new { status = "completed", output = new[] { new { type = "message", status = "completed", content = new[] { new { type = "output_text", text = json } } } } });
    private static CurrentTrack Track(long revision = 1) => new(revision, "Private title", "Private artist", "Private album", null, PlaybackState.Playing);

    [Theory]
    [InlineData("{\"input_tokens\":12,\"output_tokens\":8,\"total_tokens\":20}", true)]
    [InlineData("null", false)]
    [InlineData("{}", false)]
    [InlineData("{\"input_tokens\":\"12\",\"output_tokens\":8,\"total_tokens\":20}", false)]
    [InlineData("{\"input_tokens\":-1,\"output_tokens\":8,\"total_tokens\":7}", false)]
    [InlineData("{\"input_tokens\":12,\"output_tokens\":8,\"total_tokens\":21}", false)]
    public async Task UsageIsProviderReportedOrUnknownNeverInvented(string usage, bool valid)
    {
        var root = JsonNode.Parse(Envelope(Json()))!; root["usage"] = JsonNode.Parse(usage);
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(root.ToJsonString()) })));
        var response = await new OpenAiResponsesClient(http).SendWithUsageAsync("test-only", new { }, CancellationToken.None);
        Assert.Equal(Json(), response.Text);
        if (valid) Assert.Equal(new AiUsage(12, 8, 20), response.Usage); else Assert.Null(response.Usage);
    }

    [Fact]
    public async Task IncompleteAndInvalidInsightsStillExposeCommunicatedTokens()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"status\":\"incomplete\",\"output\":[],\"usage\":{\"input_tokens\":10,\"output_tokens\":4,\"total_tokens\":14}}") })));
        var error = await Assert.ThrowsAsync<AiException>(() => new OpenAiResponsesClient(http).SendWithUsageAsync("test-only", new { }, CancellationToken.None));
        Assert.Equal(AiFailure.Incomplete, error.Failure); Assert.Equal(new AiUsage(10, 4, 14), error.Usage);
        var vm = ReadyVm(new InsightProvider((_, _, _) => Task.FromException<SongInsight>(new AiException(AiFailure.InvalidResponse) { Usage = new AiUsage(10, 4, 14) })));
        await vm.GenerateInsightAsync(); Assert.Null(vm.Insight); Assert.Contains("14", vm.UsageMessage);
    }

    [Fact]
    public async Task EachQueryDisplaysItsOwnTokensAndUnknownIsNotZero()
    {
        var calls = 0;
        var vm = ReadyVm(new InsightProvider((_, _, _) => Task.FromResult(InsightValidation.Parse(1, Lyrics, Json()) with
        { Usage = ++calls == 1 ? new AiUsage(100, 50, 150) : null })));
        await vm.GenerateInsightAsync(); Assert.Contains("150", vm.UsageMessage);
        await vm.RegenerateInsightAsync(); Assert.Contains("no comunicado", vm.UsageMessage); Assert.DoesNotContain("150", vm.UsageMessage);
    }

    [Fact]
    public void EnglishPreservesBlankStanzasRepetitionAndRevision()
    {
        var result = InsightValidation.Parse(42, Lyrics, Json());
        Assert.Equal(42, result.TrackRevision);
        Assert.Equal("Abro una ventana\n\nAbro una ventana", InsightValidation.TranslationText(Lyrics, result));
    }

    [Fact]
    public void SpanishHasNoTranslationAndMixedCopiesSpanishLiterally()
    {
        var spanish = JsonNode.Parse(Json("es", []))!;
        spanish["metaphors"] = new JsonArray();
        var result = InsightValidation.Parse(1, "Abro una ventana", spanish.ToJsonString());
        Assert.Empty(result.Translation);
        Assert.Equal("", InsightValidation.TranslationText("Abro una ventana", result));
        var mixed = Json("mixed", [new { id = 1, text = "  Abro una ventana  ", source_language = "es" }, new { id = 3, text = "Abro una ventana", source_language = "en" }]);
        Assert.Equal("  Abro una ventana  \n\nAbro una ventana", InsightValidation.TranslationText("  Abro una ventana  \n\nI open a window", InsightValidation.Parse(1, "  Abro una ventana  \n\nI open a window", mixed)));
        Assert.Equal(AiFailure.InvalidResponse, Assert.Throws<AiException>(() => InsightValidation.Parse(1, "Abro una ventana\n\nI open a window", mixed)).Failure);
    }

    [Theory]
    [InlineData("missing")][InlineData("duplicate")][InlineData("invented")][InlineData("reordered")]
    [InlineData("extra")][InlineData("short")][InlineData("quote")][InlineData("source")]
    [InlineData("themes")][InlineData("duplicate-field")][InlineData("malformed")][InlineData("empty")][InlineData("extra-line")]
    public void InvalidResponseNeverBecomesAnInsight(string kind)
    {
        var root = JsonNode.Parse(Json())!;
        var lines = (JsonArray)root["translation"]!;
        switch (kind)
        {
            case "missing": lines.RemoveAt(1); break;
            case "duplicate": lines[1]!["id"] = 1; break;
            case "invented": lines[1]!["id"] = 99; break;
            case "reordered": lines[0]!["id"] = 3; lines[1]!["id"] = 1; break;
            case "extra": root["unexpected"] = true; break;
            case "short": root["summary"] = "Demasiado corto"; break;
            case "quote": root["metaphors"]![0]!["text"] = "invented quotation"; break;
            case "source": lines[0]!["source_language"] = "fr"; break;
            case "themes": root["themes"] = new JsonArray(); break;
            case "empty": lines[0]!["text"] = " "; break;
            case "extra-line": lines[0]!["text"] = "Abro una ventana\nVerso inventado"; break;
        }
        var json = kind == "malformed" ? "{" : root.ToJsonString();
        if (kind == "duplicate-field") json = json.Insert(1, "\"language\":\"en\",");
        Assert.Equal(AiFailure.InvalidResponse, Assert.Throws<AiException>(() => InsightValidation.Parse(1, Lyrics, json)).Failure);
    }

    [Fact]
    public void UnknownLanguageIsRecoverable()
        => Assert.Equal(AiFailure.UnknownLanguage, Assert.Throws<AiException>(() => InsightValidation.Parse(1, Lyrics, Json("unknown"))).Failure);

    [Theory]
    [InlineData("{\"status\":\"incomplete\",\"output\":[]}", AiFailure.Incomplete)]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"status\":\"completed\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"private provider text\"}]}]}", AiFailure.Refused)]
    [InlineData("{}", AiFailure.InvalidResponse)]
    public async Task EnvelopeFailuresAreSanitizedAndNotRetried(string body, AiFailure expected)
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AiException>(() => new OpenAiResponsesClient(http).SendAsync("test-only", new { }, CancellationToken.None));
        Assert.Equal(expected, error.Failure);
        Assert.DoesNotContain("private provider text", error.ToString());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task OnePaidRequestContainsOnlyLyricsAndUsesSharedBudgetWithoutPersistence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SongSense.InsightTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SqliteSettingsStore(Path.Combine(directory, "settings.db"));
            await store.InitializeAsync();
            var handler = new Handler(async request =>
            {
                Assert.Equal(1, await store.ReadRequestCountAsync(DateOnly.FromDateTime(DateTime.Now)));
                Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                var body = await request.Content!.ReadAsStringAsync();
                Assert.DoesNotContain("test-only-key", body);
                Assert.DoesNotContain("Private title", body);
                Assert.DoesNotContain("Private artist", body);
                using var payload = JsonDocument.Parse(body);
                Assert.False(payload.RootElement.GetProperty("store").GetBoolean());
                Assert.Equal(0, payload.RootElement.GetProperty("tools").GetArrayLength());
                Assert.True(payload.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
                var input = payload.RootElement.GetProperty("input")[0].GetProperty("content").GetString()!;
                Assert.Contains("I open a window", input);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Envelope(Json())) };
            });
            using var http = new HttpClient(handler);
            var service = new AiConfigurationService(store, new DpapiSecretStore(Path.Combine(directory, "credentials.dpapi")), store, new OpenAiConnectionProbe(http));
            using var key = new SecureString();
            foreach (var c in "test-only-key") key.AppendChar(c);
            await service.SaveAsync(new(true, "chosen-model", 1), key, true, CancellationToken.None);
            var provider = new OpenAiInsightProvider(service, new OpenAiResponsesClient(http));
            var lyrics = new ResolvedLyrics(1, Lyrics, LyricsOrigin.Manual, null, false);
            Assert.Equal(AiFailure.InvalidLyrics, (await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(Track(), lyrics with { IsInstrumental = true }, CancellationToken.None))).Failure);
            Assert.Equal(AiFailure.InvalidLyrics, (await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(Track(2), lyrics, CancellationToken.None))).Failure);
            Assert.Equal(0, handler.Calls);
            var result = await provider.GenerateAsync(Track(), lyrics, CancellationToken.None);
            Assert.Equal("en", result.Language);
            Assert.Equal(AiFailure.DailyLimit, (await Assert.ThrowsAsync<AiException>(() => service.TestConnectionAsync(CancellationToken.None))).Failure);
            Assert.Equal(1, handler.Calls);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, "settings.db"));
            Assert.DoesNotContain("I open a window", System.Text.Encoding.UTF8.GetString(bytes));
            Assert.DoesNotContain(Summary, System.Text.Encoding.UTF8.GetString(bytes));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("track")][InlineData("lyrics")][InlineData("model")][InlineData("cancel")][InlineData("disappear")]
    public async Task LateResponseCannotOverwriteCurrentContextEvenIfProviderIgnoresCancellation(string change)
    {
        var pending = new TaskCompletionSource<SongInsight>();
        var provider = new InsightProvider((_, _, _) => pending.Task);
        var vm = ReadyVm(provider);
        var task = vm.GenerateInsightAsync();
        await vm.GenerateInsightAsync(); // Double click sends nothing.
        Assert.Equal(1, provider.Calls);
        switch (change)
        {
            case "track": vm.ApplyDetection(new(ApplicationState.Playing, Track(2), null)); break;
            case "lyrics": Assert.Null(vm.SaveManual(vm.BeginLyricsEdit()!, "New own text")); break;
            case "model": vm.ApplyAiSettings(new(true, "different-model")); break;
            case "cancel": vm.CancelInsight(); break;
            case "disappear": vm.ApplyDetection(new(ApplicationState.NoSpotify, null, null)); break;
        }
        pending.SetResult(InsightValidation.Parse(1, Lyrics, Json()));
        await task;
        Assert.Null(vm.Insight);
        Assert.Equal("", vm.TranslatedLyrics);
    }

    [Fact]
    public async Task SuccessfulResultSurvivesPauseWithoutAdditionalPayment()
    {
        var provider = new InsightProvider((_, _, _) => Task.FromResult(InsightValidation.Parse(1, Lyrics, Json())));
        var vm = ReadyVm(provider);
        await vm.GenerateInsightAsync();
        Assert.Equal(vm.LyricsRevision, vm.Insight!.LyricsRevision);
        vm.ApplyDetection(new(ApplicationState.Paused, Track() with { Playback = PlaybackState.Paused }, null));
        Assert.True(vm.HasInsight);
        Assert.False(vm.GenerateInsightCommand.CanExecute(null));
        await vm.GenerateInsightAsync();
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData(AiFailure.Refused)][InlineData(AiFailure.Incomplete)][InlineData(AiFailure.InvalidResponse)]
    [InlineData(AiFailure.UnknownLanguage)][InlineData(AiFailure.Timeout)][InlineData(AiFailure.DailyLimit)]
    public async Task FailuresShowNoPartialResultAndNeverRetryAutomatically(AiFailure failure)
    {
        var provider = new InsightProvider((_, _, _) => Task.FromException<SongInsight>(new AiException(failure)));
        var vm = ReadyVm(provider);
        await vm.GenerateInsightAsync();
        Assert.Null(vm.Insight);
        Assert.NotEmpty(vm.InsightMessage);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task DisabledUnconfirmedInstrumentalAndExhaustedQuotaDoNotSend()
    {
        var provider = new InsightProvider((_, _, _) => throw new InvalidOperationException("Must not send"));
        var store = new Store();
        var vm = new MainViewModel(store, new LyricsProvider(), insightProvider: provider);
        await vm.GenerateInsightAsync();
        vm.ApplyDetection(new(ApplicationState.Playing, Track(), null));
        vm.ApplyAiSettings(new(true, "chosen-model", 1));
        await vm.GenerateInsightAsync();
        Assert.Null(vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics));
        vm.ApplyAiSettings(new(false, "chosen-model", 1));
        await vm.GenerateInsightAsync();
        vm.ApplyAiSettings(new(true, "chosen-model", 1));
        store.Count = 1;
        await vm.RefreshAiUsageAsync();
        await vm.GenerateInsightAsync();
        Assert.False(vm.GenerateInsightCommand.CanExecute(null));
        Assert.Equal(0, provider.Calls);
    }

    private static MainViewModel ReadyVm(IInsightProvider provider)
    {
        var vm = new MainViewModel(new Store(), new LyricsProvider(), insightProvider: provider);
        vm.ApplyDetection(new(ApplicationState.Playing, Track(), null));
        vm.ApplyAiSettings(new(true, "chosen-model"));
        Assert.Null(vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics));
        return vm;
    }
    private sealed class InsightProvider(Func<CurrentTrack, ResolvedLyrics, CancellationToken, Task<SongInsight>> generate) : IInsightProvider
    {
        public int Calls { get; private set; }
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken cancellationToken) { Calls++; return generate(track, lyrics, cancellationToken); }
    }
    private sealed class LyricsProvider : ILyricsProvider
    {
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken cancellationToken) => Task.FromResult(new LyricsSearchResult(track.Revision, LyricsSearchKind.NotFound, null, []));
    }
    private sealed class Store : ISettingsStore
    {
        public int Count { get; set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppSettings());
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult(Count);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return send(request); }
    }
}
