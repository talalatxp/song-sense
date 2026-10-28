using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class ConnectionWorkflowTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "SongSense.ConnectionWorkflow", Guid.NewGuid().ToString("N"));
    ChatGptCredentialStore Store => new(Path.Combine(directory, "fixture.dpapi"));
    const string Client = "oaiapp_workflow_fixture";
    static ChatGptRegistration Account(string? model = "model-a", bool expired = false) => new()
    {
        ClientId = Client, Subject = "fixture-subject", Email = "fixture@example.invalid", Model = model,
        AccessToken = "fixture-access", RefreshToken = "fixture-refresh", Scopes = [ChatGptOAuthClient.PlanScope],
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1)
    };
    static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    static HttpResponseMessage Models(params string[] models) => Json(new { models = models.Select(m => new { slug = m, display_name = m, visibility = "list" }) });
    async Task<ChatGptConnection> Create(HttpClient http, ChatGptRegistration? account)
    {
        await Store.SaveAsync(new() { ActiveClientId = account?.ClientId, Registrations = account is null ? [] : [account] }, CancellationToken.None);
        var connection = new ChatGptConnection(Store, new ChatGptOAuthClient(http)); await connection.InitializeAsync(CancellationToken.None); return connection;
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CatalogCancellationKeepsIdentityAndConfirmedAuthorizationButBlocksRequestsWhileBusy(bool enabled)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, token) => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return Models(); }));
        var connection = await Create(http, Account()); using var view = new ConnectionViewModel(connection, CancellationToken.None);
        view.CreditsDisabledConfirmed = true; if (enabled) await view.EnablePlanAsync();
        var scope = connection.CacheScope;
        var loading = view.ModelsAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ConnectionOperation.Catalog, view.Operation); Assert.True(view.IsOperating);
        Assert.Equal(2, view.Step); Assert.True(connection.Connected); Assert.True(connection.PlanGranted);
        Assert.Equal(enabled, connection.PlanUseEnabled); Assert.False(connection.CanGenerate);
        Assert.True(view.CreditsDisabledConfirmed); Assert.False(view.HasModels); Assert.Equal(scope, connection.CacheScope);
        view.Cancel(); await loading;
        Assert.Equal(ModelCatalogState.Cancelled, view.CatalogState); Assert.Equal(2, view.Step);
        Assert.Contains("Consulta de modelos cancelada", view.Message); Assert.DoesNotContain("login", view.Message);
        Assert.Equal("model-a", connection.Model); Assert.Equal(enabled, connection.CanGenerate);
        Assert.True(view.CreditsDisabledConfirmed); Assert.False(view.ShowOperationResult);
        Assert.Contains("cancelada", view.ModelMessage); Assert.Equal("Consultar de nuevo", view.PrimaryLabel);
        using var reopened = new ConnectionViewModel(connection, CancellationToken.None);
        Assert.Equal(enabled ? 4 : 3, reopened.Step); Assert.False(reopened.ShowOperationResult);
        Assert.DoesNotContain("Conecta", reopened.PrimaryReason);
    }
    [Theory]
    [InlineData("available", ModelCatalogState.Available, true, true)]
    [InlineData("empty", ModelCatalogState.Empty, true, false)]
    [InlineData("missing", ModelCatalogState.Available, true, false)]
    [InlineData("forbidden", ModelCatalogState.Failed, true, true)]
    [InlineData("unauthorized", ModelCatalogState.Failed, false, false)]
    [InlineData("invalid", ModelCatalogState.Failed, true, true)]
    public async Task CatalogOutcomeNeverGrantsOrInventsAuthorization(string scenario, ModelCatalogState state, bool connected, bool enabled)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(scenario switch
        {
            "available" => Models("model-a"), "empty" => Models(), "missing" => Models("model-b"),
            "forbidden" => Json(new { error = new { code = "unrecognized", message = "private-provider-text" } }, HttpStatusCode.Forbidden),
            "unauthorized" => Json(new { error = "invalid_token" }, HttpStatusCode.Unauthorized),
            _ => Json(new { wrong = "private-provider-text" })
        })));
        var connection = await Create(http, Account()); using var view = new ConnectionViewModel(connection, CancellationToken.None);
        view.CreditsDisabledConfirmed = true; await view.EnablePlanAsync();
        await view.ModelsAsync();
        Assert.Equal(state, view.CatalogState); Assert.Equal(connected, connection.Connected); Assert.Equal(enabled, connection.CanGenerate);
        Assert.Equal(connected ? 2 : 1, view.Step); Assert.False(view.CanCancel); Assert.False(view.IsOperating);
        Assert.Equal(scenario is "available" or "missing", view.HasModels);
        if (scenario is "missing" or "empty")
        {
            Assert.Null(connection.Model); Assert.False(view.CreditsDisabledConfirmed); view.SelectedModel = "model-a";
            Assert.False(view.CanSaveModel); await view.SelectModelAsync(); Assert.Null(connection.Model);
        }
        if (scenario == "forbidden")
        {
            Assert.Contains("no confirma la causa", view.Message); Assert.DoesNotContain("HTTP", view.Message);
            Assert.Contains("HTTP: 403", view.DiagnosticDetails); Assert.Contains("ModelCatalog", view.DiagnosticDetails);
        }
        Assert.DoesNotContain("private-provider-text", view.Message + view.DiagnosticDetails);
        Assert.DoesNotContain("fixture-access", view.DiagnosticDetails); Assert.DoesNotContain("fixture-refresh", view.DiagnosticDetails);
    }
    [Fact]
    public async Task RealSimulatedExpiryInvalidatesSessionAndConfirmation()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.EndsWith("/oauth/token", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(new { error = "invalid_grant", error_description = "private-expiry-detail" }, HttpStatusCode.BadRequest));
        }));
        var connection = await Create(http, Account(expired: true)); using var view = new ConnectionViewModel(connection, CancellationToken.None);
        view.CreditsDisabledConfirmed = true; await view.EnablePlanAsync(); await view.ModelsAsync();
        Assert.False(connection.Connected); Assert.False(connection.PlanGranted); Assert.False(connection.CanGenerate);
        Assert.False(view.CreditsDisabledConfirmed); Assert.Equal(1, view.Step); Assert.Contains("caducado", view.Message);
        Assert.Contains("HTTP: 400", view.DiagnosticDetails); Assert.Contains("TokenExchange", view.DiagnosticDetails);
        Assert.DoesNotContain("private-expiry", view.DiagnosticDetails);
        Assert.False((await Store.ReadAsync(CancellationToken.None)).Registrations.Single().HasSession);
    }
    [Fact]
    public async Task RefreshLosingPlanScopeDoesNotKeepAnEnabledSession()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.EndsWith("/oauth/token", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(new { access_token = "replacement-access", refresh_token = "replacement-refresh", token_type = "Bearer", expires_in = 3600, scope = "openid" }));
        }));
        var connection = await Create(http, Account(expired: true)); using var view = new ConnectionViewModel(connection, CancellationToken.None);
        view.CreditsDisabledConfirmed = true; await view.EnablePlanAsync(); await view.ModelsAsync();
        Assert.True(connection.Connected); Assert.False(connection.PlanGranted); Assert.False(connection.CanGenerate);
        Assert.Equal(1, view.Step); Assert.False(view.CreditsDisabledConfirmed); Assert.False(view.HasModels);
    }
    [Fact]
    public async Task SavingDifferentModelRequiresConfirmationAgainAndClearsObsoleteResult()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Models("model-a", "model-b"))));
        var connection = await Create(http, Account()); using var view = new ConnectionViewModel(connection, CancellationToken.None);
        view.CreditsDisabledConfirmed = true; await view.EnablePlanAsync(); await view.ModelsAsync();
        Assert.True(view.CreditsDisabledConfirmed); Assert.True(connection.CanGenerate);
        view.SelectedModel = "model-b"; Assert.False(view.ShowOperationResult); await view.SelectModelAsync();
        Assert.Equal("model-b", connection.Model); Assert.Equal(3, view.Step); Assert.False(connection.CanGenerate); Assert.False(view.CreditsDisabledConfirmed);
        view.Review(2); Assert.False(view.ShowOperationResult);
    }
    [Fact]
    public async Task ExternalIdentityChangeInvalidatesPriorConfirmationEvenWithSameClient()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Models("model-a"))));
        var connection = await Create(http, Account()); using var view = new ConnectionViewModel(connection, CancellationToken.None);
        view.CreditsDisabledConfirmed = true; await view.EnablePlanAsync();
        var saved = await Store.ReadAsync(CancellationToken.None); saved.Registrations[0].Subject = "another-fixture-subject";
        await Store.SaveAsync(saved, CancellationToken.None); await view.ModelsAsync();
        Assert.True(connection.Connected); Assert.False(connection.CanGenerate); Assert.False(view.CreditsDisabledConfirmed);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task InitialAndPendingGuidanceComesFromCurrentState(bool pending)
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("No network")));
        await Store.SaveAsync(new() { PendingClientId = pending ? Client : null, LoginOutcome = ChatGptLoginOutcome.Cancelled }, CancellationToken.None);
        var connection = new ChatGptConnection(Store, new ChatGptOAuthClient(http)); await connection.InitializeAsync(CancellationToken.None);
        using var view = new ConnectionViewModel(connection, CancellationToken.None);
        Assert.Equal(1, view.Step); Assert.False(view.ShowOperationResult); Assert.False(view.HasModels);
        Assert.Contains(pending ? "registro pendiente" : "Conecta la cuenta", view.PrimaryReason);
        Assert.DoesNotContain("plazo", view.PrimaryReason);
    }
    [Fact]
    public async Task EnableFailureNamesTheOperationAndNeverConfirmsCredits()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("No network")));
        var connection = await Create(http, Account()); using var view = new ConnectionViewModel(connection, CancellationToken.None);
        await view.EnablePlanAsync();
        Assert.Equal(ConnectionOperation.EnableSession, view.Operation); Assert.Equal(3, view.Step);
        Assert.Contains("No se pudo habilitar esta sesión", view.Message);
        Assert.False(view.CreditsDisabledConfirmed); Assert.False(connection.CanGenerate);
        await view.ModeAsync(AiConnectionMode.ApiKey);
        Assert.Equal(5, view.Step); Assert.Contains("Configura la clave", view.PrimaryReason);
    }
    [Fact]
    public async Task CancelledAccountChangeBeforeAcquiringStorageKeepsTheActualIdentity()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("No network")));
        var connection = await Create(http, Account());
        var saved = await Store.ReadAsync(CancellationToken.None); var second = Account(); second.ClientId = "oaiapp_second_fixture"; second.Subject = "second-subject";
        saved.Registrations.Add(second); await Store.SaveAsync(saved, CancellationToken.None); await connection.InitializeAsync(CancellationToken.None);
        using var view = new ConnectionViewModel(connection, CancellationToken.None);
        using var held = new FileStream(Store.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        view.SelectedClientId = second.ClientId; var changing = view.SelectAccountAsync();
        Assert.True(view.IsOperating); Assert.Equal(ConnectionOperation.AccountChange, view.Operation); Assert.Contains("Cambiando de cuenta", view.OperationTitle);
        Assert.True(connection.Connected); Assert.False(connection.CanGenerate);
        view.Cancel(); await changing;
        Assert.Equal(Client, connection.ActiveClientId); Assert.True(connection.Connected);
        Assert.Contains("Cambio de cuenta cancelado", view.Message); Assert.False(view.IsOperating);
    }
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t) => send(r, t); }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
