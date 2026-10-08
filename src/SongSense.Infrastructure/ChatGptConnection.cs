using System.Security.Cryptography;
using System.Text;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed record ChatGptAccount(string ClientId, string Label)
{
    public override string ToString() => Label;
}

public sealed class ChatGptConnection(ChatGptCredentialStore store, ChatGptOAuthClient oauth) : IAnalysisSession
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private ChatGptCredentials credentials = new();
    private bool ready;
    private CancellationTokenSource authorization = new();
    public CancellationToken AuthorizationCancellation => authorization.Token;
    public AiConnectionMode Mode => credentials.Mode;
    private ChatGptRegistration? Active => credentials.Registrations.FirstOrDefault(x => x.ClientId == credentials.ActiveClientId);
    public string? Model => Active?.Model;
    public bool Connected => ready && Active is { HasSession: true };
    public bool PlanGranted => Connected && Active!.Scopes.Contains(ChatGptOAuthClient.PlanScope, StringComparer.Ordinal);
    public string AccountLabel => Active is { } account ? Label(account) : "Ninguna cuenta conectada";
    public IReadOnlyList<ChatGptAccount> Accounts => credentials.Registrations.Select(x => new ChatGptAccount(x.ClientId, Label(x))).ToArray();
    public IReadOnlyList<ChatGptModel> Models { get; private set; } = [];
    public string? ActiveClientId => credentials.ActiveClientId;
    public ChatGptLoginOutcome LoginOutcome => credentials.LoginOutcome;
    public AiFailure LoginFailure => credentials.LoginFailure;
    public AuthenticationStep LoginStep => credentials.LoginStep;
    public OAuthFailureCode LoginCode => credentials.LoginCode;
    public int? LoginHttpStatus => credentials.LoginHttpStatus;
    public bool HasPendingRegistration => credentials.PendingClientId is not null;
    // No official provider assertion or included-only request flag is documented.
    // Neither local consent nor a successful model listing can change this gate.
    public bool CanGenerate => ready && Mode == AiConnectionMode.ApiKey;
    public string Status => !ready ? "Conexión no disponible: revisa el almacenamiento local." : Mode == AiConnectionMode.ApiKey ? "API key · de pago" :
        Connected ? PlanGranted ? "ChatGPT · solo uso incluido · Consultas bloqueadas: no se puede verificar que los créditos estén desactivados." :
            "ChatGPT · Cuenta conectada sin permiso de uso del plan · Consultas bloqueadas" : "ChatGPT · solo uso incluido · Sin sesión";
    public string CacheScope => Mode == AiConnectionMode.ApiKey ? "api-key" : "chatgpt:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((Active?.Subject ?? "none") + "\n" + (Active?.ClientId ?? "none"))));
    public event EventHandler? Changed;
    private static string Label(ChatGptRegistration account) => account.Email + " · " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.ClientId)))[..8] + (account.HasSession ? " · Conectada" : " · Desconectada");

    public Task InitializeAsync(CancellationToken token) => RunAsync(_ => Task.CompletedTask, token);
    public Task SetModeAsync(AiConnectionMode mode, CancellationToken token) => RunAsync(async current =>
    {
        if (!Enum.IsDefined(mode)) throw new AiException(AiFailure.InvalidSettings);
        credentials.Mode = mode; await store.SaveAsync(credentials, current);
    }, token);
    public Task SelectAccountAsync(string clientId, CancellationToken token) => RunAsync(async current =>
    {
        if (credentials.Registrations.All(x => x.ClientId != clientId)) throw new AiException(AiFailure.InvalidSettings);
        credentials.ActiveClientId = clientId; Models = []; await store.SaveAsync(credentials, current);
    }, token);
    public Task SetModelAsync(string model, CancellationToken token) => RunAsync(async current =>
    {
        if (Active is null || !Models.Any(x => x.Slug == model)) throw new AiException(AiFailure.IncompatibleModel);
        Active.Model = model; await store.SaveAsync(credentials, current);
    }, token);
    public Task ConnectAsync(bool newAccount, Action<Uri> openBrowser, CancellationToken token) => RunAsync(async current =>
    {
        if (newAccount && credentials.Registrations.Count >= 20) throw new AiException(AiFailure.InvalidSettings);
        if (newAccount && HasPendingRegistration) throw new AiException(AiFailure.Busy);
        credentials.LoginOutcome = ChatGptLoginOutcome.Waiting;
        credentials.LoginFailure = AiFailure.Disabled; credentials.LoginStep = AuthenticationStep.None;
        credentials.LoginCode = OAuthFailureCode.None; credentials.LoginHttpStatus = null;
        await store.SaveAsync(credentials, current);
        try
        {
        await oauth.ConnectAsync(credentials.HostId, newAccount ? null : Active, openBrowser, current,
            newAccount ? null : credentials.PendingClientId, async (issued, cancellation) =>
            { credentials.PendingClientId = issued; await store.SaveAsync(credentials, cancellation); }, async (result, cancellation) =>
        {
        credentials.Registrations.RemoveAll(x => x.ClientId == result.ClientId);
        credentials.Registrations.Add(result); credentials.ActiveClientId = result.ClientId;
        if (credentials.PendingClientId == result.ClientId) credentials.PendingClientId = null;
        credentials.Mode = AiConnectionMode.ChatGptIncluded; Models = [];
        credentials.LoginOutcome = ChatGptLoginOutcome.Connected;
        await store.SaveAsync(credentials, cancellation);
        });
        }
        catch (Exception error)
        {
            // A failed atomic save must not leave an unpersisted session active in memory.
            credentials = await store.ReadAsync(CancellationToken.None);
            credentials.LoginOutcome = error is OperationCanceledException ? ChatGptLoginOutcome.Cancelled : ChatGptLoginOutcome.Failed;
            credentials.LoginFailure = error is AiException ai ? ai.Failure : AiFailure.LocalStorage;
            credentials.LoginStep = error is AiException failure ? failure.AuthenticationStep : AuthenticationStep.None;
            credentials.LoginCode = error is AiException oauthError ? oauthError.OAuthCode : OAuthFailureCode.None;
            credentials.LoginHttpStatus = error is AiException httpError ? httpError.HttpStatus : null;
            await store.SaveAsync(credentials, CancellationToken.None);
            throw;
        }
    }, token);

    public Task LoadModelsAsync(CancellationToken token) => RunAsync(async current =>
    {
        Models = [];
        if (Active is not { HasSession: true } account) throw new AiException(AiFailure.Authentication);
        if (!account.Scopes.Contains(ChatGptOAuthClient.PlanScope, StringComparer.Ordinal)) throw new AiException(AiFailure.Disabled);
        if (account.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
        {
            ChatGptRegistration replacement;
            try { replacement = await oauth.RefreshAsync(account, current); }
            catch (AiException error) when (error.Failure == AiFailure.SessionExpired)
            { account.ClearTokens(); await store.SaveAsync(credentials, current); throw; }
            credentials.Registrations.Remove(account); credentials.Registrations.Add(replacement); account = replacement;
            // Rotation must be durable before a replacement access token is used.
            await store.SaveAsync(credentials, current);
        }
        Models = await oauth.ModelsAsync(account, current);
        if (account.Model is not null && Models.All(x => x.Slug != account.Model))
        { account.Model = null; await store.SaveAsync(credentials, current); }
    }, token);

    public async Task<bool> DisconnectAsync(CancellationToken token)
    {
        bool revoked = false;
        await RunAsync(async current =>
        {
            if (Active is not { } account) { revoked = true; return; }
            try { revoked = await oauth.RevokeAsync(account, current); }
            catch (Exception) { revoked = false; }
            finally
            {
                account.ClearTokens(); Models = [];
                // Local removal must still run after a canceled remote revocation.
                await store.SaveAsync(credentials, CancellationToken.None);
            }
        }, token);
        return revoked;
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken token)
    {
        if (!await gate.WaitAsync(0, token)) throw new AiException(AiFailure.Busy);
        ready = false;
        authorization.Cancel(); Changed?.Invoke(this, EventArgs.Empty);
        bool loaded = false;
        try
        {
            store.EnsurePrivateDirectory();
            await using var fileLock = await AcquireLockAsync(token);
            credentials = await store.ReadAsync(token);
            loaded = true;
            // Persist the generated host before opening any authorization URL.
            await store.SaveAsync(credentials, token);
            await operation(token);
        }
        catch (AiException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { loaded = false; throw new AiException(AiFailure.LocalStorage); }
        finally
        {
            ready = loaded;
            authorization.Dispose(); authorization = new CancellationTokenSource();
            gate.Release(); Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken token)
    {
        for (int i = 0; i < 40; i++)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(store.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < 39) { await Task.Delay(50, token); }
            catch (IOException) { throw new AiException(AiFailure.Busy); }
        }
        throw new AiException(AiFailure.Busy);
    }
}

public sealed class ConnectionInsightProvider(IAnalysisSession connection, IInsightProvider apiProvider) : IInsightProvider
{
    public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!connection.CanGenerate || connection.Mode != AiConnectionMode.ApiKey)
            throw new AiException(AiFailure.PlanSafetyUnverified);
        return apiProvider.GenerateAsync(track, lyrics, cancellationToken);
    }
}
