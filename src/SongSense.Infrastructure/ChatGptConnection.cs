using System.Security.Cryptography;
using System.Text;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed record ChatGptAccount(string ClientId, string Label)
{
    public override string ToString() => Label;
}
public enum ModelCatalogState { NotRequested, Loading, Empty, Available, Cancelled, Failed }

public sealed class ChatGptConnection(ChatGptCredentialStore store, ChatGptOAuthClient oauth) : IAnalysisSession
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private ChatGptCredentials credentials = new();
    private bool ready;
    public bool IsBusy { get; private set; }
    private string? confirmedPlanClientId;
    private CancellationTokenSource authorization = new();
    public CancellationToken AuthorizationCancellation => authorization.Token;
    public AiConnectionMode Mode => credentials.Mode;
    private ChatGptRegistration? Active => credentials.Registrations.FirstOrDefault(x => x.ClientId == credentials.ActiveClientId);
    public string? Model => Active?.Model;
    public bool Connected => ready && Active is { HasSession: true };
    public bool PlanGranted => Connected && Active!.Scopes.Contains(ChatGptOAuthClient.PlanScope, StringComparer.Ordinal);
    public string AccountLabel => Active is { } account ? Label(account, credentials.Registrations.IndexOf(account) + 1) : "Ninguna cuenta conectada";
    public IReadOnlyList<ChatGptAccount> Accounts => credentials.Registrations.Select((x, index) => new ChatGptAccount(x.ClientId, Label(x, index + 1))).ToArray();
    public IReadOnlyList<ChatGptModel> Models { get; private set; } = [];
    public ModelCatalogState CatalogState { get; private set; }
    public AiException? CatalogFailure { get; private set; }
    public string? ActiveClientId => credentials.ActiveClientId;
    public ChatGptLoginOutcome LoginOutcome => credentials.LoginOutcome;
    public AiFailure LoginFailure => credentials.LoginFailure;
    public AuthenticationStep LoginStep => credentials.LoginStep;
    public OAuthFailureCode LoginCode => credentials.LoginCode;
    public int? LoginHttpStatus => credentials.LoginHttpStatus;
    public bool HasPendingRegistration => credentials.PendingClientId is not null;
    public bool CanReplaceUnverifiedRegistration => ready && HasPendingRegistration && credentials.Registrations.Count == 0;
    // OpenAI does not expose an included-only request flag. Enabling is an explicit
    // per-session user confirmation of external credit settings, not provider verification.
    public bool PlanUseEnabled => PlanGranted && AiSettingsRules.IsValidModel(Model) && confirmedPlanClientId == ActiveClientId;
    public bool CanGenerate => ready && !IsBusy && (Mode == AiConnectionMode.ApiKey || PlanUseEnabled && AiSettingsRules.IsValidModel(Model));
    public AnalysisSessionState Readiness => !ready ? AnalysisSessionState.Unavailable : Mode == AiConnectionMode.ApiKey ? AnalysisSessionState.Ready :
        HasPendingRegistration ? AnalysisSessionState.PendingRegistration : !Connected ? AnalysisSessionState.Disconnected :
        !PlanGranted ? AnalysisSessionState.PermissionRequired : !AiSettingsRules.IsValidModel(Model) ? AnalysisSessionState.ModelRequired :
        !PlanUseEnabled ? AnalysisSessionState.ConfirmationRequired : AnalysisSessionState.Ready;
    public string Status => !ready ? "Conexión no disponible: revisa el almacenamiento local." : Mode == AiConnectionMode.ApiKey ? "API key · de pago" :
        Connected ? PlanGranted ? PlanUseEnabled ? $"ChatGPT · {Model} · Uso del plan habilitado; créditos desactivados según tu confirmación." : "ChatGPT · Cuenta conectada · Confirma en Cuenta y consumo que has desactivado el uso de créditos para habilitar consultas." :
            "ChatGPT · Cuenta conectada sin permiso de uso del plan · Consultas bloqueadas" : "ChatGPT · solo uso incluido · Sin sesión";
    public string CacheScope => Mode == AiConnectionMode.ApiKey ? "api-key" : "chatgpt:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((Active?.Subject ?? "none") + "\n" + (Active?.ClientId ?? "none"))));
    public event EventHandler? Changed;
    private static string Label(ChatGptRegistration account, int number) => (string.IsNullOrWhiteSpace(account.Email) ? "Cuenta" : account.Email) + $" · Registro {number}" + (account.HasSession ? " · Conectada" : " · Desconectada");

    public Task InitializeAsync(CancellationToken token) => RunAsync(_ => Task.CompletedTask, token);
    public Task SetPlanUseAsync(bool enabled, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!enabled)
        {
            confirmedPlanClientId = null;
            authorization.Cancel(); authorization.Dispose(); authorization = new CancellationTokenSource();
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
        return RunAsync(_ =>
        {
            if (credentials.Mode != AiConnectionMode.ChatGptIncluded || Active is not { HasSession: true } account || !account.Scopes.Contains(ChatGptOAuthClient.PlanScope) || !AiSettingsRules.IsValidModel(account.Model))
                throw new AiException(AiFailure.InvalidSettings);
            confirmedPlanClientId = credentials.ActiveClientId;
            return Task.CompletedTask;
        }, token);
    }

    public async Task<SongInsight> RunIncludedRequestAsync(Func<string, string, CancellationToken, Task<SongInsight>> operation, CancellationToken token)
    {
        if (!await gate.WaitAsync(0, token)) throw new AiException(AiFailure.Busy);
        try
        {
            store.EnsurePrivateDirectory();
            await using var fileLock = await AcquireLockAsync(token);
            var saved = await store.ReadAsync(token);
            var account = saved.Registrations.FirstOrDefault(x => x.ClientId == saved.ActiveClientId);
            if (!ready || saved.Mode != AiConnectionMode.ChatGptIncluded || account is not { HasSession: true } ||
                account.ClientId != confirmedPlanClientId || !account.Scopes.Contains(ChatGptOAuthClient.PlanScope) || !AiSettingsRules.IsValidModel(account.Model))
                throw new AiException(AiFailure.PlanSafetyUnverified);
            // Re-read the protected record under the same cross-process lock as settings/API requests.
            if (account.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
            {
                try
                {
                    var replacement = await oauth.RefreshAsync(account, token);
                    saved.Registrations.Remove(account); saved.Registrations.Add(replacement); account = replacement;
                    await store.SaveAsync(saved, token);
                    credentials = saved;
                }
                catch (AiException error) when (error.Failure == AiFailure.SessionExpired)
                {
                    account.ClearTokens(); await store.SaveAsync(saved, CancellationToken.None);
                    credentials = saved; confirmedPlanClientId = null; throw;
                }
            }
            if (!account.Scopes.Contains(ChatGptOAuthClient.PlanScope)) throw new AiException(AiFailure.Authentication);
            using var current = CancellationTokenSource.CreateLinkedTokenSource(token, authorization.Token);
            return await operation(account.Model!, account.AccessToken!, current.Token);
        }
        catch (AiException error) when (error.Failure is AiFailure.RateLimited or AiFailure.Authentication or AiFailure.SessionExpired)
        { confirmedPlanClientId = null; throw; }
        finally { gate.Release(); }
    }
    public Task PrepareDifferentAccountAsync(CancellationToken token) => RunAsync(async current =>
    {
        if (credentials.PendingClientId is not { } pending || credentials.Registrations.Count != 0)
            throw new AiException(AiFailure.InvalidSettings);
        // Explicit recovery for a registration started with the wrong account/workspace.
        // Archive first; if either write fails, the existing durable registration is retained.
        await store.RetainPendingRegistrationAsync(credentials.HostId, pending, current);
        var replacement = new ChatGptCredentials { HostId = credentials.HostId, Mode = credentials.Mode };
        await store.SaveAsync(replacement, current);
        credentials = replacement;
    }, token);
    public Task SetModeAsync(AiConnectionMode mode, CancellationToken token) => RunAsync(async current =>
    {
        if (!Enum.IsDefined(mode)) throw new AiException(AiFailure.InvalidSettings);
        if (credentials.Mode != mode) confirmedPlanClientId = null;
        credentials.Mode = mode; await store.SaveAsync(credentials, current);
    }, token);
    public Task SelectAccountAsync(string clientId, CancellationToken token) => RunAsync(async current =>
    {
        if (credentials.Registrations.All(x => x.ClientId != clientId)) throw new AiException(AiFailure.InvalidSettings);
        if (credentials.ActiveClientId != clientId) confirmedPlanClientId = null;
        credentials.ActiveClientId = clientId; ResetCatalog(); await store.SaveAsync(credentials, current);
    }, token);
    public Task SetModelAsync(string model, CancellationToken token) => RunAsync(async current =>
    {
        if (!PlanGranted || Active is null || CatalogState != ModelCatalogState.Available || !Models.Any(x => x.Slug == model)) throw new AiException(AiFailure.IncompatibleModel);
        if (Active.Model != model) confirmedPlanClientId = null;
        Active.Model = model; await store.SaveAsync(credentials, current);
    }, token);
    public Task ConnectAsync(bool newAccount, Action<Uri> openBrowser, CancellationToken token) => RunAsync(async current =>
    {
        confirmedPlanClientId = null;
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
        credentials.Mode = AiConnectionMode.ChatGptIncluded; ResetCatalog();
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
        Models = []; CatalogFailure = null; CatalogState = ModelCatalogState.Loading;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
        if (Active is not { HasSession: true } account) throw new AiException(AiFailure.Authentication);
        if (!account.Scopes.Contains(ChatGptOAuthClient.PlanScope, StringComparer.Ordinal)) throw new AiException(AiFailure.Disabled);
        if (account.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
        {
            ChatGptRegistration replacement;
            try { replacement = await oauth.RefreshAsync(account, current); }
            catch (AiException error) when (error.Failure == AiFailure.SessionExpired)
            { account.ClearTokens(); confirmedPlanClientId = null; await store.SaveAsync(credentials, CancellationToken.None); throw; }
            credentials.Registrations.Remove(account); credentials.Registrations.Add(replacement); account = replacement;
            // Rotation must be durable before a replacement access token is used.
            await store.SaveAsync(credentials, current);
            if (!account.Scopes.Contains(ChatGptOAuthClient.PlanScope, StringComparer.Ordinal))
            { confirmedPlanClientId = null; throw new AiException(AiFailure.Disabled); }
        }
        Models = await oauth.ModelsAsync(account, current);
        if (account.Model is not null && Models.All(x => x.Slug != account.Model))
        { account.Model = null; confirmedPlanClientId = null; await store.SaveAsync(credentials, current); }
        CatalogState = Models.Count == 0 ? ModelCatalogState.Empty : ModelCatalogState.Available;
        }
        catch (OperationCanceledException) { Models = []; CatalogState = ModelCatalogState.Cancelled; throw; }
        catch (AiException error)
        {
            Models = []; CatalogState = ModelCatalogState.Failed; CatalogFailure = error;
            // A 403 alone does not prove that identity or plan permission has been revoked.
            if (error.Failure == AiFailure.Authentication && error.HttpStatus == 401 && Active is { } invalid)
            { invalid.ClearTokens(); confirmedPlanClientId = null; await store.SaveAsync(credentials, CancellationToken.None); }
            throw;
        }
        catch (Exception) { Models = []; CatalogState = ModelCatalogState.Failed; throw; }
    }, token);

    private void ResetCatalog() { Models = []; CatalogState = ModelCatalogState.NotRequested; CatalogFailure = null; }

    public async Task<bool> DisconnectAsync(CancellationToken token)
    {
        bool revoked = false;
        await RunAsync(async current =>
        {
            confirmedPlanClientId = null;
            if (Active is not { } account) { revoked = true; return; }
            try { revoked = await oauth.RevokeAsync(account, current); }
            catch (Exception) { revoked = false; }
            finally
            {
                account.ClearTokens(); ResetCatalog();
                // Local removal must still run after a canceled remote revocation.
                await store.SaveAsync(credentials, CancellationToken.None);
            }
        }, token);
        return revoked;
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken token)
    {
        if (!await gate.WaitAsync(0, token)) throw new AiException(AiFailure.Busy);
        IsBusy = true;
        authorization.Cancel(); Changed?.Invoke(this, EventArgs.Empty);
        bool loaded = ready;
        try
        {
            store.EnsurePrivateDirectory();
            await using var fileLock = await AcquireLockAsync(token);
            var saved = await store.ReadAsync(token);
            var previousIdentity = (Mode, ActiveClientId, Active?.Subject, Model);
            credentials = saved;
            if (previousIdentity != (Mode, ActiveClientId, Active?.Subject, Model))
            { confirmedPlanClientId = null; ResetCatalog(); }
            if (!PlanGranted) confirmedPlanClientId = null;
            loaded = true;
            // Persist the generated host before opening any authorization URL.
            await store.SaveAsync(credentials, token);
            await operation(token);
        }
        catch (AiException error) { if (error.Failure == AiFailure.LocalStorage) loaded = false; throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { loaded = false; throw new AiException(AiFailure.LocalStorage); }
        finally
        {
            ready = loaded;
            IsBusy = false;
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

public sealed class ConnectionInsightProvider(IAnalysisSession connection, IInsightProvider apiProvider, IInsightProvider? chatGptProvider = null) : IInsightProvider
{
    public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken cancellationToken)
        => GenerateAsync(track, lyrics, OutputLanguages.Default, cancellationToken);
    public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, string targetLanguage, CancellationToken cancellationToken)
    {
        _ = OutputLanguages.Get(targetLanguage);
        cancellationToken.ThrowIfCancellationRequested();
        if (!connection.CanGenerate)
            throw new AiException(AiFailure.PlanSafetyUnverified);
        if (connection.Mode == AiConnectionMode.ChatGptIncluded)
            return chatGptProvider?.GenerateAsync(track, lyrics, targetLanguage, cancellationToken) ?? throw new AiException(AiFailure.PlanSafetyUnverified);
        return apiProvider.GenerateAsync(track, lyrics, targetLanguage, cancellationToken);
    }
}
