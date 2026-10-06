using System.IO;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Text.Json;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class AiConfigurationTests : IDisposable
{
    private const string TestKey = "songsense-test-only-not-a-real-credential";
    private const string ValidResponse = """
        {"status":"completed","output":[{"type":"message","status":"completed","content":[{"type":"output_text","text":"{\"ok\":true}"}]}]}
        """;
    private readonly string directory = Path.Combine(Path.GetTempPath(), "SongSense.AiTests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(directory, "settings.db");
    private string KeyPath => Path.Combine(directory, "credentials.dpapi");
    private string LockPath => Path.Combine(directory, "ai-operation.lock");
    private readonly TestClock clock = new();
    private readonly List<HttpClient> clients = [];

    private static SecureString Secure(string value)
    {
        var result = new SecureString();
        foreach (var character in value) result.AppendChar(character);
        result.MakeReadOnly();
        return result;
    }
    private async Task<(AiConfigurationService Service, SqliteSettingsStore Store, Handler Handler)> SetupAsync(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null)
    {
        var store = new SqliteSettingsStore(DatabasePath);
        await store.InitializeAsync();
        var handler = new Handler(send ?? ((_, _) => Task.FromResult(Response())));
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        clients.Add(http);
        var service = new AiConfigurationService(store, new DpapiSecretStore(KeyPath), store, new OpenAiConnectionProbe(http), clock, LockPath);
        return (service, store, handler);
    }
    private static HttpResponseMessage Response(HttpStatusCode status = HttpStatusCode.OK, string body = ValidResponse) => new(status) { Content = new StringContent(body) };
    private static async Task EnableAsync(AiConfigurationService service, int limit = 20)
    {
        using var key = Secure(TestKey);
        await service.SaveAsync(new(true, "chosen-model", limit), key, true, CancellationToken.None);
    }

    [Fact]
    public async Task DpapiRoundTripReplaceDeleteAndNoPlaintextOnDisk()
    {
        Directory.CreateDirectory(directory);
        var secrets = new DpapiSecretStore(KeyPath);
        using (var key = Secure(TestKey)) await secrets.SaveKeyAsync(key);
        var bytes = await File.ReadAllBytesAsync(KeyPath);
        Assert.False(Encoding.UTF8.GetString(bytes).Contains(TestKey, StringComparison.Ordinal));
        Assert.Equal(TestKey, await new DpapiSecretStore(KeyPath).ReadKeyAsync());
        using (var key = Secure("replacement-test-only")) await secrets.SaveKeyAsync(key);
        Assert.Equal("replacement-test-only", await secrets.ReadKeyAsync());
        Assert.Single(Directory.GetFiles(directory));
        await secrets.DeleteKeyAsync();
        Assert.False(await secrets.HasKeyAsync());
        Assert.Null(await secrets.ReadKeyAsync());
    }

    [Fact]
    public async Task InvalidReplacementPreservesExistingCipherAndLeavesNoTemporaryFile()
    {
        Directory.CreateDirectory(directory);
        var secrets = new DpapiSecretStore(KeyPath);
        using (var key = Secure(TestKey)) await secrets.SaveKeyAsync(key);
        var previous = await File.ReadAllBytesAsync(KeyPath);
        using var invalid = Secure("test-only\ninvalid");
        Assert.Equal(AiFailure.InvalidSettings, (await Assert.ThrowsAsync<AiException>(() => secrets.SaveKeyAsync(invalid))).Failure);
        Assert.Equal(previous, await File.ReadAllBytesAsync(KeyPath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData(512, true)]
    [InlineData(513, false)]
    public async Task OversizedKeyIsRejectedWithoutTruncation(int length, bool allowed)
    {
        Directory.CreateDirectory(directory);
        var secrets = new DpapiSecretStore(KeyPath);
        using var key = Secure(new string('a', length));
        if (allowed)
        {
            await secrets.SaveKeyAsync(key);
            Assert.Equal(length, (await secrets.ReadKeyAsync())!.Length);
        }
        else
        {
            Assert.Equal(AiFailure.InvalidSettings, (await Assert.ThrowsAsync<AiException>(() => secrets.SaveKeyAsync(key))).Failure);
            Assert.False(File.Exists(KeyPath));
        }
        Assert.Equal(length, key.Length);
    }

    [Theory]
    [InlineData("1000")]
    [InlineData("1.5")]
    [InlineData("+2")]
    [InlineData("-1")]
    [InlineData(" 20 ")]
    public async Task InvalidLimitTextDoesNotSaveOrTruncateAnEnteredKey(string limit)
    {
        var (service, store, handler) = await SetupAsync();
        using var vm = new AiSettingsViewModel(service, CancellationToken.None);
        await vm.InitializeAsync();
        vm.Limit = limit;
        using var key = Secure(TestKey);
        await vm.SaveAsync(key);
        Assert.Contains("1 a 100", vm.Message);
        Assert.Equal(limit, vm.Limit);
        Assert.False(File.Exists(KeyPath));
        Assert.Equal(new AppSettings(), await store.ReadSettingsAsync());
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task OpeningSavingDeletingSettingsNeverCallsNetworkOrPersistsKeyInSqlite()
    {
        var (service, store, handler) = await SetupAsync();
        using var vm = new AiSettingsViewModel(service, CancellationToken.None);
        await vm.InitializeAsync();
        Assert.False(vm.AiEnabled);
        Assert.Equal("20", vm.Limit);
        using var key = Secure(TestKey);
        await vm.SaveAsync(key);
        Assert.Equal(0, handler.Calls);
        Assert.False(Encoding.UTF8.GetString(await File.ReadAllBytesAsync(DatabasePath)).Contains(TestKey, StringComparison.Ordinal));
        vm.AiEnabled = true; vm.Model = "chosen-model"; vm.NoticeAccepted = true;
        using var empty = new SecureString();
        await vm.SaveAsync(empty);
        Assert.True((await store.ReadSettingsAsync()).AiEnabled);
        await vm.DeleteKeyAsync();
        Assert.False((await store.ReadSettingsAsync()).AiEnabled);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("", 20)]
    [InlineData("has spaces", 20)]
    [InlineData("sk-not-a-model", 20)]
    [InlineData("chosen-model", 0)]
    [InlineData("chosen-model", 101)]
    public async Task InvalidSettingsNeverSaveAKeyOrConsumeQuota(string model, int limit)
    {
        var (service, store, handler) = await SetupAsync();
        using var key = Secure(TestKey);
        var error = await Assert.ThrowsAsync<AiException>(() => service.SaveAsync(new(true, model, limit), key, true, CancellationToken.None));
        Assert.Equal(AiFailure.InvalidSettings, error.Failure);
        Assert.False(File.Exists(KeyPath));
        Assert.Equal(new AppSettings(), await store.ReadSettingsAsync());
        Assert.Equal(0, await store.ReadRequestCountAsync(service.LocalDate));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task EnableRequiresNoticeAndKeyAndEmptyReplacementKeepsExistingKey()
    {
        var (service, _, handler) = await SetupAsync();
        using var key = Secure(TestKey);
        var notice = await Assert.ThrowsAsync<AiException>(() => service.SaveAsync(new(true, "chosen-model"), key, false, CancellationToken.None));
        Assert.Equal(AiFailure.InvalidSettings, notice.Failure);
        var missing = await Assert.ThrowsAsync<AiException>(() => service.SaveAsync(new(true, "chosen-model"), null, true, CancellationToken.None));
        Assert.Equal(AiFailure.MissingKey, missing.Failure);
        await EnableAsync(service);
        using var empty = new SecureString();
        await service.SaveAsync(new(true, "another-model"), empty, false, CancellationToken.None);
        Assert.Equal(TestKey, await new DpapiSecretStore(KeyPath).ReadKeyAsync());
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task RequestUsesFixedHttpsStructuredOutputStoreFalseAndNoPrivateContent()
    {
        var (service, store, handler) = await SetupAsync(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(TestKey, request.Headers.Authorization.Parameter);
            var text = await request.Content!.ReadAsStringAsync(token);
            Assert.DoesNotContain(TestKey, text);
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            Assert.Equal("chosen-model", root.GetProperty("model").GetString());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.Empty(root.GetProperty("tools").EnumerateArray());
            Assert.Equal(256, root.GetProperty("max_output_tokens").GetInt32());
            Assert.Equal("json_schema", root.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
            Assert.True(root.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
            Assert.Equal(1, await new SqliteSettingsStore(DatabasePath).ReadRequestCountAsync(DateOnly.FromDateTime(clock.GetLocalNow().DateTime)));
            return Response();
        });
        await EnableAsync(service);
        await service.TestConnectionAsync(CancellationToken.None);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, await store.ReadRequestCountAsync(service.LocalDate));
    }

    [Theory]
    [InlineData(401, AiFailure.Authentication)]
    [InlineData(403, AiFailure.Authentication)]
    [InlineData(400, AiFailure.IncompatibleModel)]
    [InlineData(404, AiFailure.IncompatibleModel)]
    [InlineData(422, AiFailure.IncompatibleModel)]
    [InlineData(429, AiFailure.RateLimited)]
    [InlineData(500, AiFailure.HttpError)]
    [InlineData(307, AiFailure.HttpError)]
    public async Task FailedAttemptsCountAndNeverRetryOrEchoProviderBody(int status, AiFailure expected)
    {
        var (service, store, handler) = await SetupAsync((_, _) => Task.FromResult(Response((HttpStatusCode)status, TestKey)));
        await EnableAsync(service);
        var error = await Assert.ThrowsAsync<AiException>(() => service.TestConnectionAsync(CancellationToken.None));
        Assert.Equal(expected, error.Failure);
        Assert.DoesNotContain(TestKey, error.ToString());
        Assert.Null(error.InnerException);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, await store.ReadRequestCountAsync(service.LocalDate));
        Assert.Equal("chosen-model", (await store.ReadSettingsAsync()).Model);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"status\":\"incomplete\",\"output\":[]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"status\":\"completed\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"no\"}]}]}")]
    public async Task InvalidIncompleteOrRefusedResponseNeverReportsSuccess(string body)
    {
        var (service, _, handler) = await SetupAsync((_, _) => Task.FromResult(Response(body: body)));
        await EnableAsync(service);
        var error = await Assert.ThrowsAsync<AiException>(() => service.TestConnectionAsync(CancellationToken.None));
        Assert.Equal(AiFailure.InvalidResponse, error.Failure);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{\"ok\":false}")]
    [InlineData("{\"ok\":true,\"extra\":1}")]
    [InlineData("{\"ok\":\"true\"}")]
    [InlineData("[true]")]
    public async Task OutputMustMatchTheStrictProbeSchema(string result)
    {
        var body = JsonSerializer.Serialize(new { status = "completed", output = new[] { new { type = "message", status = "completed", content = new[] { new { type = "output_text", text = result } } } } });
        var (service, _, handler) = await SetupAsync((_, _) => Task.FromResult(Response(body: body)));
        await EnableAsync(service);
        Assert.Equal(AiFailure.InvalidResponse, (await Assert.ThrowsAsync<AiException>(() => service.TestConnectionAsync(CancellationToken.None))).Failure);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task OfflineFailureIsSanitizedAndCountedWithoutRetry()
    {
        var (service, store, handler) = await SetupAsync((_, _) => throw new HttpRequestException(TestKey));
        await EnableAsync(service);
        var error = await Assert.ThrowsAsync<AiException>(() => service.TestConnectionAsync(CancellationToken.None));
        Assert.Equal(AiFailure.Offline, error.Failure);
        Assert.DoesNotContain(TestKey, error.ToString());
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, await store.ReadRequestCountAsync(service.LocalDate));
    }

    [Fact]
    public async Task LimitSurvivesRestartModelChangesAndDeletionThenAllowsExplicitIncreaseOrNextLocalDay()
    {
        var (service, store, handler) = await SetupAsync();
        await EnableAsync(service, 1);
        Assert.Equal(new DateOnly(2026, 10, 6), service.LocalDate);
        await service.TestConnectionAsync(CancellationToken.None);
        var (reopened, _, nextHandler) = await SetupAsync();
        await reopened.SaveAsync(new(true, "different-model", 1), null, false, CancellationToken.None);
        var limited = await Assert.ThrowsAsync<AiException>(() => reopened.TestConnectionAsync(CancellationToken.None));
        Assert.Equal(AiFailure.DailyLimit, limited.Failure);
        Assert.Equal(0, nextHandler.Calls);
        await reopened.SaveAsync(new(true, "different-model", 2), null, false, CancellationToken.None);
        await reopened.TestConnectionAsync(CancellationToken.None);
        Assert.Equal(2, await store.ReadRequestCountAsync(service.LocalDate));
        await reopened.DeleteKeyAsync(CancellationToken.None);
        Assert.Equal(2, await store.ReadRequestCountAsync(service.LocalDate));
        await EnableAsync(reopened, 1);
        clock.AdvanceDay();
        await reopened.TestConnectionAsync(CancellationToken.None);
        Assert.Equal(1, await store.ReadRequestCountAsync(reopened.LocalDate));
        Assert.Equal(2, await store.ReadRequestCountAsync(new(2026, 10, 6)));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CancelledInFlightAttemptCountsAndConcurrentInstancesAndSavesAreBlocked()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, store, handler) = await SetupAsync(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response();
        });
        await EnableAsync(service);
        var (second, _, secondHandler) = await SetupAsync();
        using var cancellation = new CancellationTokenSource();
        var first = service.TestConnectionAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(AiFailure.Busy, (await Assert.ThrowsAsync<AiException>(() => service.TestConnectionAsync(CancellationToken.None))).Failure);
        Assert.Equal(AiFailure.Busy, (await Assert.ThrowsAsync<AiException>(() => second.TestConnectionAsync(CancellationToken.None))).Failure);
        Assert.Equal(AiFailure.Busy, (await Assert.ThrowsAsync<AiException>(() => second.DeleteKeyAsync(CancellationToken.None))).Failure);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(1, await store.ReadRequestCountAsync(service.LocalDate));
        Assert.Equal(1, handler.Calls); Assert.Equal(0, secondHandler.Calls);
        await second.TestConnectionAsync(CancellationToken.None);
        Assert.Equal(2, await store.ReadRequestCountAsync(service.LocalDate));
    }

    [Fact]
    public async Task TimeoutIsExactly60SecondsAndDoesNotRefundOrRetry()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, store, handler) = await SetupAsync(async (_, token) =>
        {
            started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return Response();
        });
        await EnableAsync(service);
        var pending = service.TestConnectionAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(60), clock.LastDueTime);
        clock.FireTimers();
        Assert.Equal(AiFailure.Timeout, (await Assert.ThrowsAsync<AiException>(() => pending)).Failure);
        Assert.Equal(1, await store.ReadRequestCountAsync(service.LocalDate));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DisabledMissingOrCorruptKeyAndCancellationBeforeSendingConsumeNothing()
    {
        var (service, store, handler) = await SetupAsync();
        Assert.Equal(AiFailure.Disabled, (await Assert.ThrowsAsync<AiException>(() => service.TestConnectionAsync(CancellationToken.None))).Failure);
        await store.SaveSettingsAsync(new(true, "chosen-model"));
        Assert.Equal(AiFailure.MissingKey, (await Assert.ThrowsAsync<AiException>(() => service.TestConnectionAsync(CancellationToken.None))).Failure);
        await File.WriteAllBytesAsync(KeyPath, [1, 2, 3]);
        Assert.Equal(AiFailure.LocalStorage, (await Assert.ThrowsAsync<AiException>(() => service.TestConnectionAsync(CancellationToken.None))).Failure);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.TestConnectionAsync(cancellation.Token));
        Assert.Equal(0, await store.ReadRequestCountAsync(service.LocalDate));
        Assert.Equal(0, handler.Calls);
        Assert.True(File.Exists(KeyPath));
    }

    [Fact]
    public async Task SqliteReservationsAreAtomicAcrossConcurrentStoreInstances()
    {
        var (_, store, _) = await SetupAsync();
        await store.SaveSettingsAsync(new(true, "chosen-model", 3));
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => new SqliteSettingsStore(DatabasePath).TryReserveRequestAsync(new(2026, 10, 6)))));
        Assert.Equal(3, results.Count(success => success));
        Assert.Equal(3, await store.ReadRequestCountAsync(new(2026, 10, 6)));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return send(request, token); }
    }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 5, 22, 30, 0, TimeSpan.Zero);
        private readonly List<TestTimer> timers = [];
        public TimeSpan LastDueTime { get; private set; }
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("test-local", TimeSpan.FromHours(2), "test-local", "test-local");
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            LastDueTime = dueTime;
            var timer = new TestTimer(callback, state); timers.Add(timer); return timer;
        }
        public void FireTimers() { foreach (var timer in timers.ToArray()) timer.Fire(); }
        public void AdvanceDay() => now += TimeSpan.FromDays(1);
    }
    private sealed class TestTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool disposed;
        public void Fire() { if (!disposed) callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;
        public void Dispose() => disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
    public void Dispose()
    {
        foreach (var client in clients) client.Dispose();
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
    }
}
