using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class ChatGptTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "SongSense.ChatGptTests", Guid.NewGuid().ToString("N"));
    private readonly RSA rsa = RSA.Create(2048);
    private ChatGptCredentialStore Store => new(Path.Combine(directory, "session.dpapi"));
    private const string ClientId = "oaiapp_songsense_test";
    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    private static Dictionary<string, string> Query(string query) => query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1]));
    private RsaSecurityKey Key => new(rsa) { KeyId = "test-generated" };
    private string Jwt(string audience = ClientId, string issuer = ChatGptOAuthClient.Issuer, string subject = "test-subject", string nonce = "test-nonce", bool expired = false)
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer, Audience = audience, Subject = new System.Security.Claims.ClaimsIdentity([
                new("sub", subject), new("email", "test@example.invalid"), new("nonce", nonce)]),
            IssuedAt = DateTime.UtcNow.AddMinutes(-10), NotBefore = DateTime.UtcNow.AddMinutes(-10),
            Expires = expired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(10), SigningCredentials = new(Key, SecurityAlgorithms.RsaSha256)
        });
    private HttpResponseMessage IdentityResponse(HttpRequestMessage request)
    {
        Assert.Equal("https", request.RequestUri!.Scheme);
        Assert.Equal("auth.openai.com", request.RequestUri.Host);
        if (request.RequestUri.AbsolutePath == "/.well-known/openid-configuration")
            return Json(new { issuer = ChatGptOAuthClient.Issuer, jwks_uri = ChatGptOAuthClient.Issuer + "/.well-known/jwks.json", revocation_endpoint = ChatGptOAuthClient.Issuer + "/api/accounts/oauth/revoke" });
        var parameters = rsa.ExportParameters(false);
        return Json(new { keys = new[] { new { kty = "RSA", kid = "test-generated", use = "sig", alg = "RS256", n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!) } } });
    }

    [Theory]
    [InlineData("valid")][InlineData("audience")][InlineData("issuer")][InlineData("nonce")][InlineData("expired")][InlineData("signature")]
    public async Task IdentityRequiresSignatureIssuerAudienceExpiryAndNonce(string scenario)
    {
        using var http = new HttpClient(new Handler(request => Task.FromResult(IdentityResponse(request))));
        var jwt = Jwt(audience: scenario == "audience" ? "other-client" : ClientId,
            issuer: scenario == "issuer" ? "https://example.invalid" : ChatGptOAuthClient.Issuer,
            nonce: scenario == "nonce" ? "wrong-nonce" : "test-nonce", expired: scenario == "expired");
        if (scenario == "signature") { var parts = jwt.Split('.'); parts[2] = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(256)); jwt = string.Join('.', parts); }
        var oauth = new ChatGptOAuthClient(http);
        if (scenario == "valid") Assert.Equal(("test-subject", "test@example.invalid"), await oauth.ValidateIdentityAsync(jwt, ClientId, "test-nonce", CancellationToken.None));
        else Assert.Equal(AiFailure.Authentication, (await Assert.ThrowsAsync<AiException>(() => oauth.ValidateIdentityAsync(jwt, ClientId, "test-nonce", CancellationToken.None))).Failure);
    }

    [Theory]
    [InlineData("success")][InlineData("denied")][InlineData("missing-client")][InlineData("other-account")]
    public async Task LoopbackBindsStatePkceClientAndSelectedIdentity(string scenario)
    {
        Dictionary<string, string>? authorize = null;
        int exchanges = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/api/accounts/oauth/token") return IdentityResponse(request);
            exchanges++;
            var form = Query(await request.Content!.ReadAsStringAsync());
            Assert.Equal(ClientId, form["client_id"]);
            Assert.Equal(authorize!["redirect_uri"], form["redirect_uri"]);
            Assert.Equal(authorize["code_challenge"], ChatGptOAuthClient.Challenge(form["code_verifier"]));
            Assert.False(form.ContainsKey("client_secret"));
            return Json(new { access_token = "test-access", refresh_token = "test-refresh", id_token = Jwt(nonce: authorize["nonce"]), token_type = "bearer", expires_in = 3600, scope = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct" });
        }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task callback = Task.CompletedTask;
        var selected = scenario == "other-account" ? new ChatGptRegistration { ClientId = ClientId, Subject = "different-subject" } : null;
        var login = new ChatGptOAuthClient(http).ConnectAsync("urn:uuid:" + Guid.NewGuid(), selected, uri =>
        {
            authorize = Query(uri.Query);
            Assert.Equal("https://auth.openai.com/api/accounts/authorize", uri.GetLeftPart(UriPartial.Path));
            Assert.DoesNotContain("id_token_hint", uri.Query);
            Assert.Equal("S256", authorize["code_challenge_method"]);
            Assert.Equal(selected?.ClientId ?? "dynamic_agent_client", authorize["client_id"]);
            callback = Task.Run(async () =>
            {
                using var loopback = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });
                var wrong = await loopback.GetStringAsync(authorize["redirect_uri"] + "?state=wrong&code=not-used&client_id=" + ClientId, timeout.Token);
                Assert.Equal("Solicitud no válida.", wrong);
                Assert.Equal(0, exchanges);
                string suffix = scenario == "denied" ? "&error=access_denied" : "&code=test-code" + (scenario == "missing-client" ? "" : "&client_id=" + ClientId);
                await loopback.GetStringAsync(authorize["redirect_uri"] + "?state=" + authorize["state"] + suffix, timeout.Token);
            }, timeout.Token);
        }, timeout.Token);
        if (scenario == "success")
        {
            var result = await login;
            Assert.Equal(ClientId, result.ClientId); Assert.Equal("test-subject", result.Subject); Assert.True(result.HasSession);
        }
        else Assert.Equal(AiFailure.Authentication, (await Assert.ThrowsAsync<AiException>(() => login)).Failure);
        await callback;
        Assert.Equal(scenario is "success" or "other-account" ? 1 : 0, exchanges);
    }

    [Fact]
    public async Task ProtectedSessionDefaultBlockingPersistsAndNeverFallsBackToApi()
    {
        var store = Store;
        var saved = new ChatGptCredentials { ActiveClientId = ClientId, Registrations = [new ChatGptRegistration
        { ClientId = ClientId, Subject = "test-subject", Email = "test@example.invalid", AccessToken = "test-access", RefreshToken = "test-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), Scopes = [ChatGptOAuthClient.PlanScope], Model = "model-a" }] };
        await store.SaveAsync(saved, CancellationToken.None);
        string bytes = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(directory, "session.dpapi")));
        Assert.DoesNotContain("test-access", bytes); Assert.DoesNotContain("test@example.invalid", bytes);
        using var http = new HttpClient(new Handler(_ => throw new Exception("Must not use network")));
        var connection = new ChatGptConnection(store, new ChatGptOAuthClient(http));
        await connection.InitializeAsync(CancellationToken.None);
        Assert.True(connection.Connected); Assert.False(connection.CanGenerate);
        var spy = new Spy(); var provider = new ConnectionInsightProvider(connection, spy);
        await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(Track(), Lyrics(), CancellationToken.None));
        Assert.Equal(0, spy.Calls);
        string before = connection.CacheScope;
        await connection.SetModeAsync(AiConnectionMode.ApiKey, CancellationToken.None);
        Assert.True(connection.CanGenerate); Assert.NotEqual(before, connection.CacheScope);
        await provider.GenerateAsync(Track(), Lyrics(), CancellationToken.None); Assert.Equal(1, spy.Calls);
        await connection.SetModeAsync(AiConnectionMode.ChatGptIncluded, CancellationToken.None);
        var restarted = new ChatGptConnection(store, new ChatGptOAuthClient(http));
        await restarted.InitializeAsync(CancellationToken.None); Assert.False(restarted.CanGenerate); Assert.Equal(before, restarted.CacheScope);
        var vm = new MainViewModel(new Settings(), new LyricsProvider(), insightProvider: provider, connection: restarted);
        await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing, Track(), null));
        Assert.Null(vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics().Text));
        Assert.False(vm.GenerateInsightCommand.CanExecute(null)); Assert.False(vm.RegenerateCommand.CanExecute(null));
        await vm.GenerateInsightAsync(); Assert.Equal(1, spy.Calls);
    }

    [Fact]
    public async Task RevocationFailureStillRemovesTokensAndPreservesRegistration()
    {
        var store = Store;
        await store.SaveAsync(new ChatGptCredentials { ActiveClientId = ClientId, Registrations = [new ChatGptRegistration
        { ClientId = ClientId, Subject = "test-subject", AccessToken = "test-access", RefreshToken = "test-refresh" }] }, CancellationToken.None);
        using var http = new HttpClient(new Handler(request => Task.FromResult(request.Method == HttpMethod.Get ? IdentityResponse(request) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var connection = new ChatGptConnection(store, new ChatGptOAuthClient(http)); await connection.InitializeAsync(CancellationToken.None);
        Assert.False(await connection.DisconnectAsync(CancellationToken.None)); Assert.False(connection.Connected);
        var stored = (await store.ReadAsync(CancellationToken.None)).Registrations.Single();
        Assert.Equal(ClientId, stored.ClientId); Assert.Null(stored.AccessToken); Assert.Null(stored.RefreshToken); Assert.Null(stored.IdToken);
    }

    [Theory]
    [InlineData("invalid_grant", true)][InlineData("invalid_client", false)]
    public async Task RefreshFailureClearsOnlyUnusableSessionAndDoesNotRetry(string code, bool removed)
    {
        var store = Store;
        await store.SaveAsync(new ChatGptCredentials { ActiveClientId = ClientId, Registrations = [new ChatGptRegistration
        { ClientId = ClientId, Subject = "test-subject", AccessToken = "test-access", RefreshToken = "test-refresh", Scopes = [ChatGptOAuthClient.PlanScope] }] }, CancellationToken.None);
        var handler = new Handler(_ => Task.FromResult(Json(new { error = code }, HttpStatusCode.BadRequest)));
        using var http = new HttpClient(handler); var connection = new ChatGptConnection(store, new ChatGptOAuthClient(http)); await connection.InitializeAsync(CancellationToken.None);
        await Assert.ThrowsAsync<AiException>(() => connection.LoadModelsAsync(CancellationToken.None));
        Assert.Equal(1, handler.Calls); Assert.Equal(!removed, (await store.ReadAsync(CancellationToken.None)).Registrations.Single().HasSession);
        Assert.False(connection.CanGenerate);
    }

    [Fact]
    public async Task ModelsRequirePlanScopeAndNeverEnableIncludedInference()
    {
        var store = Store;
        var saved = new ChatGptCredentials { ActiveClientId = ClientId, Registrations = [new ChatGptRegistration
        { ClientId = ClientId, Subject = "test-subject", AccessToken = "test-access", RefreshToken = "test-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }] };
        await store.SaveAsync(saved, CancellationToken.None);
        var handler = new Handler(request =>
        {
            Assert.Equal("https://api.openai.com/v1/models", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            return Task.FromResult(Json(new { models = new[] { new { visibility = "list", slug = "model-a", display_name = "Modelo A" }, new { visibility = "hidden", slug = "model-b", display_name = "Oculto" } } }));
        });
        using var http = new HttpClient(handler); var connection = new ChatGptConnection(store, new ChatGptOAuthClient(http)); await connection.InitializeAsync(CancellationToken.None);
        await Assert.ThrowsAsync<AiException>(() => connection.LoadModelsAsync(CancellationToken.None)); Assert.Equal(0, handler.Calls);
        saved.Registrations[0].Scopes = [ChatGptOAuthClient.PlanScope]; await store.SaveAsync(saved, CancellationToken.None);
        await connection.LoadModelsAsync(CancellationToken.None); Assert.Single(connection.Models); Assert.Null(connection.Model);
        await connection.SetModelAsync("model-a", CancellationToken.None); Assert.Equal("model-a", connection.Model); Assert.False(connection.CanGenerate);
    }

    [Fact]
    public async Task PaidProbeIsBlockedBeforeReadingKeyOrReservingBudget()
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("Must not send")));
        var settings = new Settings();
        var service = new AiConfigurationService(settings, new NoSecrets(), settings, new OpenAiConnectionProbe(http), allowPaidRequests: _ => Task.FromResult(false));
        Assert.Equal(AiFailure.PlanSafetyUnverified, (await Assert.ThrowsAsync<AiException>(() => service.TestConnectionAsync(CancellationToken.None))).Failure);
        Assert.Equal(0, settings.Reservations);
    }

    [Fact]
    public async Task AccountOperationCancelsPreviouslyAuthorizedPaidRequest()
    {
        var store = Store;
        using var http = new HttpClient(new Handler(_ => throw new Exception("Must not send")));
        var connection = new ChatGptConnection(store, new ChatGptOAuthClient(http));
        await connection.InitializeAsync(CancellationToken.None); await connection.SetModeAsync(AiConnectionMode.ApiKey, CancellationToken.None);
        var granted = connection.AuthorizationCancellation;
        Assert.False(granted.IsCancellationRequested);
        await connection.SetModeAsync(AiConnectionMode.ChatGptIncluded, CancellationToken.None);
        Assert.True(granted.IsCancellationRequested); Assert.False(connection.CanGenerate);
        Assert.False(connection.AuthorizationCancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task ReauthorizationOmitsRegistrationHintAndRetainsPendingClientAfterRejectedCode()
    {
        var store = Store;
        Dictionary<string, string>? authorize = null;
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest))));
        var connection = new ChatGptConnection(store, new ChatGptOAuthClient(http));
        await connection.InitializeAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task callback = Task.CompletedTask;
        string? browserReply = null;
        Action<Uri> browser = uri =>
        {
            authorize = Query(uri.Query);
            callback = Task.Run(async () =>
            {
                using var loopback = new HttpClient(new SocketsHttpHandler { UseProxy = false });
                browserReply = await loopback.GetStringAsync(authorize["redirect_uri"] + "?state=" + authorize["state"] + "&code=test-code&client_id=" + ClientId, timeout.Token);
            }, timeout.Token);
        };
        var error = await Assert.ThrowsAsync<AiException>(() => connection.ConnectAsync(false, browser, timeout.Token)); await callback;
        Assert.Equal(AuthenticationStep.TokenExchange, error.AuthenticationStep);
        Assert.Contains("No se ha completado", browserReply);
        Assert.Equal(ClientId, (await store.ReadAsync(CancellationToken.None)).PendingClientId); Assert.False(connection.Connected);
        var restarted = new ChatGptConnection(store, new ChatGptOAuthClient(http));
        await restarted.InitializeAsync(CancellationToken.None);
        using (var reopened = new ConnectionViewModel(restarted, CancellationToken.None))
            Assert.Contains("invalid_grant", reopened.Message);
        Assert.Equal(OAuthFailureCode.InvalidGrant, restarted.LoginCode);
        Assert.Equal(400, restarted.LoginHttpStatus);
        Assert.Equal(AuthenticationStep.TokenExchange, restarted.LoginStep);
        using (var pendingView = new ConnectionViewModel(restarted, CancellationToken.None)) Assert.False(pendingView.CanAddAccount);
        await Assert.ThrowsAsync<AiException>(() => restarted.ConnectAsync(true, _ => throw new Exception("Must not open another registration"), timeout.Token));
        Assert.Equal("dynamic_agent_client", authorize!["client_id"]); Assert.True(authorize.ContainsKey("agent_name_hint"));
        await Assert.ThrowsAsync<AiException>(() => connection.ConnectAsync(false, browser, timeout.Token)); await callback;
        Assert.Equal(ClientId, authorize!["client_id"]); Assert.False(authorize.ContainsKey("agent_name_hint"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task BrowserConfirmsConnectionOnlyAfterValidatedSessionIsSaved(bool failSave)
    {
        Dictionary<string, string>? authorize = null;
        bool saved = false;
        using var http = new HttpClient(new Handler(request => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/api/accounts/oauth/token" ?
                Json(new { access_token = "test-access", refresh_token = "test-refresh", id_token = Jwt(nonce: authorize!["nonce"]), token_type = "Bearer", expires_in = 3600, scope = ChatGptOAuthClient.PlanScope }) : IdentityResponse(request))));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task callback = Task.CompletedTask; string? reply = null;
        var login = new ChatGptOAuthClient(http).ConnectAsync("urn:uuid:" + Guid.NewGuid(), null, uri =>
        {
            authorize = Query(uri.Query);
            callback = Task.Run(async () =>
            {
                using var loopback = new HttpClient(new SocketsHttpHandler { UseProxy = false });
                reply = await loopback.GetStringAsync(authorize["redirect_uri"] + "?state=" + authorize["state"] + "&code=test-code&client_id=" + ClientId, timeout.Token);
                Assert.Equal(!failSave, saved);
            }, timeout.Token);
        }, timeout.Token, saveSession: async (result, token) =>
        {
            if (failSave) throw new AiException(AiFailure.LocalStorage);
            await Store.SaveAsync(new ChatGptCredentials { ActiveClientId = result.ClientId, Registrations = [result] }, token);
            saved = (await Store.ReadAsync(token)).Registrations.Single().HasSession;
        });
        if (failSave) await Assert.ThrowsAsync<AiException>(() => login); else await login;
        await callback;
        Assert.Contains(failSave ? "No se ha completado" : "Cuenta conectada y sesión guardada", reply);
        Assert.DoesNotContain("test-access", reply); Assert.DoesNotContain("test-refresh", reply);
    }

    [Theory]
    [InlineData("invalid_client", OAuthFailureCode.InvalidClient)]
    [InlineData("invalid_request", OAuthFailureCode.InvalidRequest)]
    [InlineData("invalid_scope", OAuthFailureCode.InvalidScope)]
    [InlineData("nested", OAuthFailureCode.InvalidGrant)]
    [InlineData("html", OAuthFailureCode.Other)]
    [InlineData("unknown-private-description", OAuthFailureCode.Other)]
    public async Task RejectedLoginPreservesOnlyWhitelistedErrorCodeAndStatus(string shape, OAuthFailureCode expected)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(shape == "html" ? "<html>private-response</html>" :
                shape == "nested" ? JsonSerializer.Serialize(new { error = new { code = "invalid_grant", message = "private-response" } }) :
                JsonSerializer.Serialize(new { error = shape, error_description = "private-response" }))
        })));
        var connection = new ChatGptConnection(Store, new ChatGptOAuthClient(http));
        await connection.InitializeAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task callback = Task.CompletedTask;
        await Assert.ThrowsAsync<AiException>(() => connection.ConnectAsync(false, uri =>
        {
            var authorize = Query(uri.Query);
            callback = Task.Run(async () =>
            {
                using var loopback = new HttpClient(new SocketsHttpHandler { UseProxy = false });
                await loopback.GetStringAsync(authorize["redirect_uri"] + "?state=" + authorize["state"] + "&code=test-code&client_id=" + ClientId, timeout.Token);
            }, timeout.Token);
        }, timeout.Token));
        await callback;
        var persisted = await Store.ReadAsync(CancellationToken.None);
        Assert.Equal(expected, persisted.LoginCode); Assert.Equal(400, persisted.LoginHttpStatus);
        Assert.DoesNotContain("private-response", JsonSerializer.Serialize(persisted));
        using var view = new ConnectionViewModel(connection, CancellationToken.None);
        Assert.Contains("HTTP 400", view.Message); Assert.DoesNotContain("private-response", view.Message);
        Assert.False(connection.Connected); Assert.False(connection.CanGenerate);
    }

    [Fact]
    public async Task CorruptProtectedSessionNeverEnablesPaidModeOrOverwritesOriginal()
    {
        var store = Store; store.EnsurePrivateDirectory();
        string path = Path.Combine(directory, "session.dpapi"); byte[] corrupted = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(path, corrupted);
        using var http = new HttpClient(new Handler(_ => throw new Exception("Must not send")));
        var connection = new ChatGptConnection(store, new ChatGptOAuthClient(http));
        await Assert.ThrowsAsync<AiException>(() => connection.InitializeAsync(CancellationToken.None));
        Assert.False(connection.CanGenerate); Assert.Equal(corrupted, await File.ReadAllBytesAsync(path));
        Assert.Contains("almacenamiento", connection.Status);
    }

    [Fact]
    public async Task RefreshRotationIsSavedBeforeUsingReplacementWithModels()
    {
        var store = Store;
        await store.SaveAsync(new ChatGptCredentials { ActiveClientId = ClientId, Registrations = [new ChatGptRegistration
        { ClientId = ClientId, Subject = "test-subject", AccessToken = "old-access", RefreshToken = "old-refresh", Scopes = [ChatGptOAuthClient.PlanScope] }] }, CancellationToken.None);
        var handler = new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/oauth/token", StringComparison.Ordinal))
            {
                var form = Query(await request.Content!.ReadAsStringAsync());
                Assert.Equal(ClientId, form["client_id"]); Assert.Equal("old-refresh", form["refresh_token"]); Assert.False(form.ContainsKey("scope"));
                return Json(new { access_token = "new-access", refresh_token = "new-refresh", token_type = "Bearer", expires_in = 3600, scope = ChatGptOAuthClient.PlanScope });
            }
            var persisted = (await store.ReadAsync(CancellationToken.None)).Registrations.Single();
            Assert.Equal("new-refresh", persisted.RefreshToken); Assert.Equal("new-access", request.Headers.Authorization!.Parameter);
            return Json(new { models = new[] { new { visibility = "list", slug = "model-a", display_name = "Modelo A" } } });
        });
        using var http = new HttpClient(handler); var connection = new ChatGptConnection(store, new ChatGptOAuthClient(http));
        await connection.InitializeAsync(CancellationToken.None); await connection.LoadModelsAsync(CancellationToken.None);
        Assert.Equal(2, handler.Calls); Assert.False(connection.CanGenerate);
    }

    private static CurrentTrack Track() => new(1, "Own song", "Test artist", "Test album", TimeSpan.FromMinutes(3), PlaybackState.Playing);
    private static ResolvedLyrics Lyrics() => new(1, "Abro una ventana", LyricsOrigin.Manual, null, false);
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { public int Calls { get; private set; } protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return send(request); } }
    private sealed class Spy : IInsightProvider
    { public int Calls; public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token) { Calls++; return Task.FromResult(new SongInsight(1, "es", [], "", [], [], [], [])); } }
    private sealed class Settings : ISettingsStore, IRequestBudget
    {
        public int Reservations;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppSettings(true, "model-a"));
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly localDate, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<bool> TryReserveRequestAsync(DateOnly localDate, CancellationToken cancellationToken = default) { Reservations++; return Task.FromResult(true); }
    }
    private sealed class LyricsProvider : ILyricsProvider
    { public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken cancellationToken) => throw new NotImplementedException(); }
    private sealed class NoSecrets : ISecretStore
    {
        public Task<bool> HasKeyAsync(CancellationToken cancellationToken = default) => throw new Exception("Must not read key");
        public Task<string?> ReadKeyAsync(CancellationToken cancellationToken = default) => throw new Exception("Must not read key");
        public Task SaveKeyAsync(System.Security.SecureString key, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteKeyAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
    public void Dispose()
    {
        rsa.Dispose();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
