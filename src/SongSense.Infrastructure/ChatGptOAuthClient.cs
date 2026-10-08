using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed record ChatGptModel(string Slug, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class ChatGptOAuthClient(HttpClient http)
{
    public const string Issuer = "https://auth.openai.com";
    public const string Resource = "https://api.openai.com/v1";
    public const string PlanScope = "chatgpt.tokens.use.direct";
    private const string Scopes = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";
    private static AiException Auth(AuthenticationStep step) => new(AiFailure.Authentication) { AuthenticationStep = step };

    public static string NewRandom() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    public static string Challenge(string verifier) => Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    private static bool SameSecret(string left, string right) => left.Length == right.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    public static Uri AuthorizationUri(string hostId, string? clientId, string redirect, string state, string nonce, string verifier)
    {
        var fields = new Dictionary<string, string>
        {
            ["client_id"] = clientId ?? "dynamic_agent_client", ["ext_agent_host_id"] = hostId,
            ["response_type"] = "code", ["redirect_uri"] = redirect,
            ["scope"] = Scopes, ["resource"] = Resource, ["state"] = state, ["nonce"] = nonce,
            ["code_challenge_method"] = "S256", ["code_challenge"] = Challenge(verifier)
        };
        if (clientId is null) fields["agent_name_hint"] = "Song Sense";
        return new Uri(Issuer + "/api/accounts/authorize?" + string.Join('&', fields.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))));
    }

    public async Task<ChatGptRegistration> ConnectAsync(string hostId, ChatGptRegistration? selected, Action<Uri> openBrowser, CancellationToken token,
        string? pendingClientId = null, Func<string, CancellationToken, Task>? retainClient = null,
        Func<ChatGptRegistration, CancellationToken, Task>? saveSession = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        using var listener = StartListener(out var redirect);
        string state = NewRandom(), nonce = NewRandom(), verifier = NewRandom();
        string? expectedClient = selected?.ClientId ?? pendingClientId;
        openBrowser(AuthorizationUri(hostId, expectedClient, redirect, state, nonce, verifier));
        while (true)
        {
            var context = await listener.GetContextAsync().WaitAsync(deadline.Token);
            var request = context.Request;
            bool validPath = request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/auth/callback" &&
                request.Url.Host == "127.0.0.1" && request.RawUrl is { Length: <= 8192 };
            var query = request.QueryString;
            bool validState = validPath && query.GetValues("state") is { Length: 1 } states && SameSecret(state, states[0]);
            if (!validState) { await ReplyAsync(context, "Solicitud no válida."); continue; }
            try
            {
            if (query.GetValues("error") is not null) throw Auth(AuthenticationStep.Consent);
            if (query.GetValues("code") is not { Length: 1 } codes || codes[0] is not { Length: > 0 and <= 4096 })
                throw Auth(AuthenticationStep.Callback);
            string? issued = query.GetValues("client_id") is { Length: 1 } clients ? clients[0] : null;
            if (query.GetValues("client_id") is { Length: > 1 } || expectedClient is not null && issued is not null && issued != expectedClient)
                throw Auth(AuthenticationStep.Callback);
            issued ??= expectedClient;
            if (!ChatGptCredentialStore.IsClientId(issued)) throw Auth(AuthenticationStep.Callback);
            if (selected is null && retainClient is not null) await retainClient(issued!, deadline.Token);
            var fields = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = issued!, ["code"] = codes[0],
                ["code_verifier"] = verifier, ["redirect_uri"] = redirect, ["resource"] = Resource
            };
            var result = await ExchangeAsync(fields, deadline.Token);
            var identity = await ValidateIdentityAsync(result.IdToken!, issued!, nonce, deadline.Token);
            if (selected is not null && identity.Subject != selected.Subject) throw Auth(AuthenticationStep.SelectedAccount);
            result.ClientId = issued!; result.Subject = identity.Subject; result.Email = identity.Email;
            result.Model = selected?.Model;
            if (saveSession is not null) await saveSession(result, deadline.Token);
            await ReplyAsync(context, saveSession is not null ? "Cuenta conectada y sesión guardada en Song Sense. Puedes volver a la aplicación." :
                "Identidad validada. Vuelve a Song Sense para comprobar si se ha guardado la sesión.");
            return result;
            }
            catch (Exception)
            {
                await ReplyAsync(context, "No se ha completado la conexión. Vuelve a Cuenta y consumo en Song Sense para ver el diagnóstico. No repitas el registro desde esta página.");
                throw;
            }
        }
    }

    private static HttpListener StartListener(out string redirect)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int port;
            using (var reserve = new TcpListener(IPAddress.Loopback, 0))
            { reserve.Start(); port = ((IPEndPoint)reserve.LocalEndpoint).Port; }
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try { listener.Start(); redirect = $"http://127.0.0.1:{port}/auth/callback"; return listener; }
            catch (HttpListenerException) { listener.Close(); }
        }
        throw new AiException(AiFailure.LocalStorage);
    }

    private static async Task ReplyAsync(HttpListenerContext context, string message)
    {
        using var response = context.Response;
        response.StatusCode = 200;
        response.ContentType = "text/plain; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        byte[] body = Encoding.UTF8.GetBytes(message);
        response.ContentLength64 = body.Length;
        try { await response.OutputStream.WriteAsync(body); }
        catch (Exception error) when (error is HttpListenerException or IOException or ObjectDisposedException) { }
    }

    public async Task<ChatGptRegistration> RefreshAsync(ChatGptRegistration saved, CancellationToken token)
    {
        if (!saved.HasSession || saved.EarliestRefreshAt > DateTimeOffset.UtcNow) throw new AiException(AiFailure.Authentication);
        var result = await ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["client_id"] = saved.ClientId,
            ["refresh_token"] = saved.RefreshToken!, ["resource"] = Resource
        }, token, saved);
        if (result.IdToken is not null)
        {
            var identity = await ValidateIdentityAsync(result.IdToken, saved.ClientId, null, token);
            if (identity.Subject != saved.Subject) throw new AiException(AiFailure.Authentication);
            result.Email = identity.Email;
        }
        else { result.IdToken = saved.IdToken; result.Email = saved.Email; }
        result.Subject = saved.Subject; result.ClientId = saved.ClientId; result.Model = saved.Model;
        return result;
    }

    private async Task<ChatGptRegistration> ExchangeAsync(Dictionary<string, string> fields, CancellationToken token, ChatGptRegistration? saved = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Issuer + "/api/accounts/oauth/token") { Content = new FormUrlEncodedContent(fields) };
        using var document = await SendJsonAsync(request, token, authentication: true, renewal: saved is not null);
        var root = document.RootElement;
        if (!root.TryGetProperty("token_type", out var kind) || kind.ValueKind != JsonValueKind.String || !string.Equals(kind.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase) ||
            !root.TryGetProperty("expires_in", out var duration) || !duration.TryGetInt32(out int seconds) || seconds is < 1 or > 86_400)
            throw Auth(AuthenticationStep.TokenShape);
        var result = new ChatGptRegistration
        {
            AccessToken = RequiredString(root, "access_token", 32_768), RefreshToken = RequiredString(root, "refresh_token", 32_768),
            IdToken = root.TryGetProperty("id_token", out var id) ? id.GetString() : null,
            Scopes = root.TryGetProperty("scope", out var scope) ? (scope.GetString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries) : saved?.Scopes ?? [],
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds)
        };
        if (saved is null && string.IsNullOrWhiteSpace(result.IdToken) || result.IdToken?.Length > 32_768) throw Auth(AuthenticationStep.TokenShape);
        if (root.TryGetProperty("earliest_refresh_at", out var earliest) && earliest.ValueKind != JsonValueKind.Null)
        {
            if (earliest.ValueKind == JsonValueKind.Number && earliest.TryGetInt64(out long unix) && unix is >= 0 and <= 253402300799) result.EarliestRefreshAt = DateTimeOffset.FromUnixTimeSeconds(unix);
            else throw Auth(AuthenticationStep.TokenShape);
        }
        return result;
    }

    public async Task<(string Subject, string Email)> ValidateIdentityAsync(string jwt, string clientId, string? nonce, CancellationToken token)
    {
        using var metadata = await MetadataAsync(token);
        string jwksUri = RequiredString(metadata.RootElement, "jwks_uri", 256);
        if (jwksUri != Issuer + "/.well-known/jwks.json") throw Auth(AuthenticationStep.IdentityMetadata);
        using var request = new HttpRequestMessage(HttpMethod.Get, jwksUri);
        using var jwks = await SendJsonAsync(request, token, true);
        var validation = await new JsonWebTokenHandler { MaximumTokenSizeInBytes = 32_768 }.ValidateTokenAsync(jwt, new TokenValidationParameters
        {
            ValidIssuer = Issuer, ValidAudience = clientId, ValidateIssuer = true, ValidateAudience = true,
            ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true, ValidateIssuerSigningKey = true,
            IssuerSigningKeys = new JsonWebKeySet(jwks.RootElement.GetRawText()).GetSigningKeys(),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ClockSkew = TimeSpan.FromSeconds(30)
        });
        if (!validation.IsValid || validation.SecurityToken is not JsonWebToken verified) throw Auth(AuthenticationStep.IdentitySignature);
        if (!verified.TryGetClaim("sub", out var subject) || subject.Value is not { Length: > 0 and <= 512 } ||
            nonce is not null && (!verified.TryGetClaim("nonce", out var claim) || !SameSecret(nonce, claim.Value)))
            throw Auth(AuthenticationStep.IdentityClaims);
        string email = verified.TryGetClaim("email", out var address) ? address.Value : "Cuenta sin correo comunicado";
        if (email.Length > 254 || email.Any(char.IsControl)) throw new AiException(AiFailure.Authentication);
        return (subject.Value, email);
    }

    private async Task<JsonDocument> MetadataAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Issuer + "/.well-known/openid-configuration");
        var document = await SendJsonAsync(request, token, true);
        if (RequiredString(document.RootElement, "issuer", 256) != Issuer) { document.Dispose(); throw new AiException(AiFailure.Authentication); }
        return document;
    }

    public async Task<bool> RevokeAsync(ChatGptRegistration registration, CancellationToken token)
    {
        if (registration.RefreshToken is null) return true;
        using var metadata = await MetadataAsync(token);
        string endpoint = RequiredString(metadata.RootElement, "revocation_endpoint", 256);
        if (endpoint != Issuer + "/api/accounts/oauth/revoke") throw new AiException(AiFailure.Authentication);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            { ["token"] = registration.RefreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = registration.ClientId })
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        return response.StatusCode == HttpStatusCode.OK;
    }

    public async Task<IReadOnlyList<ChatGptModel>> ModelsAsync(ChatGptRegistration registration, CancellationToken token)
    {
        if (!registration.HasSession || !registration.Scopes.Contains(PlanScope, StringComparer.Ordinal)) throw new AiException(AiFailure.Authentication);
        using var request = new HttpRequestMessage(HttpMethod.Get, Resource + "/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);
        try
        {
            using var document = await SendJsonAsync(request, token, true);
            var models = document.RootElement.GetProperty("models");
            if (models.ValueKind != JsonValueKind.Array || models.GetArrayLength() > 300) throw new AiException(AiFailure.InvalidResponse);
            return models.EnumerateArray().Where(x => x.GetProperty("visibility").GetString() == "list")
                .Select(x => new ChatGptModel(RequiredString(x, "slug", 120), RequiredString(x, "display_name", 200)))
                .Where(x => AiSettingsRules.IsValidModel(x.Slug) && !x.DisplayName.Any(char.IsControl)).DistinctBy(x => x.Slug).ToArray();
        }
        finally { request.Headers.Authorization = null; }
    }

    private static string RequiredString(JsonElement root, string name, int max)
    {
        if (!root.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String || field.GetString() is not { Length: > 0 } value || value.Length > max)
            throw new AiException(AiFailure.InvalidResponse);
        return value;
    }

    private async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken token, bool authentication, bool renewal = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.Content.Headers.ContentLength > 524_288) throw new AiException(AiFailure.InvalidResponse);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var memory = new MemoryStream(); byte[] chunk = new byte[4096]; int count;
            while ((count = await stream.ReadAsync(chunk, deadline.Token)) != 0)
            { if (memory.Length + count > 524_288) throw new AiException(AiFailure.InvalidResponse); memory.Write(chunk, 0, count); }
            byte[] bytes = memory.ToArray();
            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    if (renewal && response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                    {
                        try
                        {
                            using var error = JsonDocument.Parse(bytes);
                            if (error.RootElement.TryGetProperty("error", out var code) && code.ValueKind == JsonValueKind.String &&
                                code.GetString() is "invalid_grant" or "invalid_refresh_token" or "token_expired" or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused")
                                throw new AiException(AiFailure.SessionExpired);
                        }
                        catch (JsonException) { }
                    }
                    bool rejected = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || authentication && response.StatusCode == HttpStatusCode.BadRequest;
                    throw new AiException(rejected ? AiFailure.Authentication : AiFailure.HttpError)
                    {
                        AuthenticationStep = request.RequestUri?.AbsolutePath == "/api/accounts/oauth/token" ? AuthenticationStep.TokenExchange : AuthenticationStep.IdentityMetadata,
                        HttpStatus = (int)response.StatusCode, OAuthCode = ReadFailureCode(bytes)
                    };
                }
                using var source = new MemoryStream(bytes, writable: false);
                return JsonDocument.Parse(source);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); CryptographicOperations.ZeroMemory(memory.GetBuffer()); CryptographicOperations.ZeroMemory(chunk); }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new AiException(AiFailure.Timeout); }
        catch (Exception error) when (error is HttpRequestException or IOException) { throw new AiException(AiFailure.Offline); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { throw new AiException(AiFailure.InvalidResponse); }
    }

    private static OAuthFailureCode ReadFailureCode(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("error", out var error)) return OAuthFailureCode.Other;
            if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var nested)) error = nested;
            if (error.ValueKind != JsonValueKind.String) return OAuthFailureCode.Other;
            return error.GetString() switch
            {
                "invalid_grant" => OAuthFailureCode.InvalidGrant,
                "invalid_client" => OAuthFailureCode.InvalidClient,
                "invalid_request" => OAuthFailureCode.InvalidRequest,
                "invalid_scope" => OAuthFailureCode.InvalidScope,
                "access_denied" => OAuthFailureCode.AccessDenied,
                "unsupported_grant_type" => OAuthFailureCode.UnsupportedGrantType,
                _ => OAuthFailureCode.Other
            };
        }
        catch (JsonException) { return OAuthFailureCode.Other; }
    }
}
