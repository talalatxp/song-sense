using System.IO;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class OutputLanguageTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "SongSense.LanguageTests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(directory, "data.db");
    private static CurrentTrack Track => new(1, "Own song", "Own artist", "Own album", TimeSpan.FromMinutes(3), PlaybackState.Playing);
    private static ResolvedLyrics Lyrics => new(1, "Abro una ventana", LyricsOrigin.Manual, null, false);
    private static string Phrase(string language) => language switch {
        "es" => "La ventana podría representar un cambio y una oportunidad para mirar con calma un nuevo comienzo.",
        "en" => "The window could represent a change and an opportunity to calmly consider a new beginning in life.",
        "fr" => "La fenêtre pourrait représenter un changement et une occasion de regarder avec calme un nouveau départ dans la vie.",
        "de" => "Das Fenster könnte eine Veränderung darstellen und eine Gelegenheit bieten einen neuen Anfang im Leben mit Ruhe zu betrachten.",
        "it" => "La finestra potrebbe rappresentare un cambiamento e una occasione per guardare con calma un nuovo inizio nella vita.",
        _ => "A janela poderia representar uma mudança e uma oportunidade para olhar com calma um novo começo na vida." };
    private static string Text(string language) => language switch { "es" => "Abro una ventana", "en" => "I open a window", "fr" => "J'ouvre une fenêtre", "de" => "Ich öffne ein Fenster", "it" => "Apro una finestra", _ => "Abro uma janela" };
    private static string Json(string target, string source = "es") => JsonSerializer.Serialize(new {
        language = source, translation = source == target ? Array.Empty<object>() : new object[] { new { id = 1, text = Text(target), source_language = source } },
        summary = string.Join(' ', Enumerable.Repeat(Phrase(target), 5)), themes = new[] { Text(target) },
        metaphors = Array.Empty<object>(), alternatives = Array.Empty<string>(), warnings = Array.Empty<string>() });
    private static SongInsight Result(string target) => InsightValidation.Parse(1, Lyrics.Text, Json(target), target) with { Model = "test-model", Usage = new(20, 10, 30) };
    private async Task<SqliteSettingsStore> Store()
    {
        var store = new SqliteSettingsStore(Database); await store.InitializeAsync(); await store.SaveSettingsAsync(new(true, "test-model")); return store;
    }

    [Fact]
    public async Task IdentifiedSourceSurvivesAllDestinationsAndCancellationButNotEditing()
    {
        var provider = new Provider(); var vm = new MainViewModel(await Store(),provider,insightProvider:provider);
        await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing,Track,null));vm.SaveManual(vm.BeginLyricsEdit()!,Lyrics.Text);
        Assert.Null(vm.IdentifiedSourceLanguage); await vm.GenerateInsightAsync(); Assert.Equal("es",vm.IdentifiedSourceLanguage);
        foreach(var target in OutputLanguages.All) { vm.TargetLanguage=target.Code;await vm.WaitForCacheAsync();Assert.Equal("es",vm.IdentifiedSourceLanguage); }
        var pending=new TaskCompletionSource<SongInsight>(TaskCreationOptions.RunContinuationsAsynchronously);provider.Generate=_=>pending.Task;
        var task=vm.GenerateInsightAsync();vm.CancelInsight();pending.SetResult(Result(vm.TargetLanguage));await task;
        Assert.Equal("es",vm.IdentifiedSourceLanguage);vm.SaveManual(vm.BeginLyricsEdit()!,Lyrics.Text+" editada");Assert.Null(vm.IdentifiedSourceLanguage);
        vm.SaveManual(vm.BeginLyricsEdit()!,Lyrics.Text);Assert.Null(vm.IdentifiedSourceLanguage);await vm.StopAsync();
    }
    [Fact]
    public async Task ConflictingSourceObservationsStayUnknownForTheSameOriginal()
    {
        var provider=new Provider();var vm=new MainViewModel(await Store(),provider,insightProvider:provider);
        await vm.InitializeAsync();vm.ApplyDetection(new(ApplicationState.Playing,Track,null));vm.SaveManual(vm.BeginLyricsEdit()!,Lyrics.Text);
        await vm.GenerateInsightAsync();Assert.Equal("es",vm.IdentifiedSourceLanguage);
        vm.TargetLanguage="en";await vm.WaitForCacheAsync();provider.Generate=t=>Task.FromResult(Result(t) with {Language="mixed"});await vm.GenerateInsightAsync();
        Assert.Equal("unknown",vm.IdentifiedSourceLanguage);vm.TargetLanguage="fr";await vm.WaitForCacheAsync();Assert.Equal("unknown",vm.IdentifiedSourceLanguage);await vm.StopAsync();
    }
    [Theory]
    [InlineData("es")][InlineData("en")][InlineData("fr")][InlineData("de")][InlineData("it")][InlineData("pt")]
    public async Task BothProvidersSendOneSharedLanguageWithoutChangingBillingMode(string target)
    {
        using var handler = new Handler(async request => {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var instructions = body.RootElement.GetProperty("instructions").GetString()!;
            Assert.Contains($"idioma de destino: {target}", instructions); Assert.Contains(OutputLanguages.Get(target).Name, instructions);
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            var response = new { status = "completed", output = new[] { new { type = "message", status = "completed", content = new[] { new { type = "output_text", text = Json(target) } } } }, usage = new { input_tokens = 20, output_tokens = 10, total_tokens = 30 } };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.RootElement.TryGetProperty("stream", out _) ?
                "data: " + JsonSerializer.Serialize(new { type = "response.completed", response }) + "\n\n" : JsonSerializer.Serialize(response)) };
        });
        using var http = new HttpClient(handler); var store = await Store();
        var api = new OpenAiInsightProvider(new AiConfigurationService(store, new Secrets(), store, new OpenAiConnectionProbe(http)), new OpenAiResponsesClient(http));
        var apiResult = await api.GenerateAsync(Track, Lyrics, target, CancellationToken.None);
        Assert.Equal(target, apiResult.TargetLanguage); Assert.Equal(new AiUsage(20, 10, 30), apiResult.Usage);
        var credentials = new ChatGptCredentialStore(Path.Combine(directory, "session.dpapi"));
        await credentials.SaveAsync(new ChatGptCredentials { ActiveClientId = "oaiapp_language_test", Registrations = [new ChatGptRegistration {
            ClientId = "oaiapp_language_test", Subject = "synthetic", Model = "test-model", AccessToken = "test-access", RefreshToken = "test-refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), Scopes = [ChatGptOAuthClient.PlanScope] }] }, CancellationToken.None);
        var connection = new ChatGptConnection(credentials, new ChatGptOAuthClient(http)); await connection.InitializeAsync(CancellationToken.None);
        await connection.SetPlanUseAsync(true, CancellationToken.None);
        var routed = new ConnectionInsightProvider(connection, new ForbiddenProvider(), new ChatGptInsightProvider(connection, http));
        var includedResult = await routed.GenerateAsync(Track, Lyrics, target, CancellationToken.None);
        Assert.Equal(target, includedResult.TargetLanguage); Assert.Equal(apiResult.Summary, includedResult.Summary); Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("es")][InlineData("en")][InlineData("fr")][InlineData("de")][InlineData("it")][InlineData("pt")]
    public void MatchingSourceHasNoDuplicateTranslation(string language)
    {
        var result = InsightValidation.Parse(1, Text(language), Json(language, language), language);
        Assert.Empty(result.Translation); Assert.Equal("", InsightValidation.TranslationText(Text(language), result));
    }

    [Fact]
    public async Task ChangingLanguagePersistsWithoutInferenceAndRestoresSeparateCaches()
    {
        var store = await Store(); var provider = new Provider();
        var vm = new MainViewModel(store, provider, insightProvider: provider, cache: new SqliteCacheStore(Database));
        await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing, Track, null));
        vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics.Text); await vm.WaitForCacheAsync();
        await vm.GenerateInsightAsync(); Assert.Equal("es", vm.Insight!.TargetLanguage); Assert.Equal(1, provider.Calls);
        vm.TargetLanguage = "en"; Assert.Null(vm.Insight); await vm.WaitForCacheAsync();
        Assert.Equal("en", await store.ReadOutputLanguageAsync()); Assert.Equal(1, provider.Calls);
        await vm.GenerateInsightAsync(); Assert.Equal("en", vm.Insight!.TargetLanguage); Assert.Equal(Text("en"), vm.ReadingBlocks[0].Translation);
        Assert.Equal(2, provider.Calls);
        vm.TargetLanguage = "es"; await vm.WaitForCacheAsync();
        Assert.Equal("es", vm.Insight!.TargetLanguage); Assert.False(vm.ReadingBlocks[0].HasTranslation); Assert.Contains("0 tokens", vm.UsageMessage);
        vm.TargetLanguage = "en"; await vm.WaitForCacheAsync(); Assert.Equal(2, provider.Calls); await vm.StopAsync();
        var reopened = new SqliteSettingsStore(Database); await reopened.InitializeAsync(); Assert.Equal("en", await reopened.ReadOutputLanguageAsync());
        await reopened.SaveSettingsAsync(new(false, "another-model", 42)); Assert.Equal("en", await reopened.ReadOutputLanguageAsync());
        await reopened.SaveAutomaticUpdatesAsync(false); Assert.Equal("en", await reopened.ReadOutputLanguageAsync());
    }

    [Fact]
    public async Task ChangingLanguageCancelsAndDiscardsOldResultWithoutSendingAnotherRequest()
    {
        var pending = new TaskCompletionSource<SongInsight>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider { Generate = _ => pending.Task }; var store = await Store();
        var vm = new MainViewModel(store, provider, insightProvider: provider);
        await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing, Track, null)); vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics.Text);
        var generation = vm.GenerateInsightAsync(); Assert.Equal(1, provider.Calls);
        vm.TargetLanguage = "fr"; await vm.WaitForCacheAsync(); pending.SetResult(Result("es")); await generation;
        Assert.Null(vm.Insight); Assert.Equal("fr", vm.TargetLanguage); Assert.Equal(1, provider.Calls); await vm.StopAsync();
    }

    [Fact]
    public async Task SchemaThreeMigrationKeepsSettingsCounterAndOldSpanishCache()
    {
        var store = await Store(); await store.TryReserveRequestAsync(DateOnly.FromDateTime(DateTime.Now));
        var cache = new SqliteCacheStore(Database); await cache.SaveInsightAsync(Track, Lyrics, "test-model", CacheIdentity.Version, Result("es"));
        using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        { await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = "ALTER TABLE app_settings DROP COLUMN output_language; PRAGMA user_version=3;"; command.ExecuteNonQuery(); }
        await store.InitializeAsync(); await store.InitializeAsync();
        Assert.Equal("es", await store.ReadOutputLanguageAsync()); Assert.True((await store.ReadSettingsAsync()).AiEnabled);
        Assert.Equal(1, await store.ReadRequestCountAsync(DateOnly.FromDateTime(DateTime.Now)));
        Assert.NotNull(await cache.ReadInsightAsync(Lyrics, "test-model", CacheIdentity.Version));
        Assert.Null(await cache.ReadInsightAsync(Lyrics, "test-model", CacheIdentity.Version, targetLanguage: "en"));
        await Assert.ThrowsAsync<AiException>(() => store.SaveOutputLanguageAsync("unsupported")); Assert.Equal("es", await store.ReadOutputLanguageAsync());
    }

    [Fact]
    public void MixedTextDoesNotRequireSpanishAndPreservesLinesAlreadyInTarget()
    {
        var payload = JsonSerializer.Serialize(new { language = "mixed", translation = new[] {
            new { id = 1, text = "  I open a window  ", source_language = "en" }, new { id = 2, text = "I open a window", source_language = "fr" } },
            summary = string.Join(' ', Enumerable.Repeat(Phrase("en"), 5)), themes = new[] { "Change" }, metaphors = Array.Empty<object>(), alternatives = Array.Empty<string>(), warnings = Array.Empty<string>() });
        var result = InsightValidation.Parse(1, "  I open a window  \nJ'ouvre une fenêtre", payload, "en");
        Assert.Equal("  I open a window  ", result.Translation[0].Text);
        Assert.Throws<AiException>(() => InsightValidation.Parse(1, "  I open a window  \nJ'ouvre une fenêtre", payload.Replace("  I open a window  ", "I open a window"), "en"));
    }

    [Fact]
    public async Task RapidChangesKeepLatestPreferenceEvenWhenModelChangesAndDoNotResetBudget()
    {
        var store = await Store(); var today = DateOnly.FromDateTime(DateTime.Now);
        await store.TryReserveRequestAsync(today);
        var provider = new Provider(); var vm = new MainViewModel(store, provider, insightProvider: provider);
        await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing, Track, null)); vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics.Text);
        vm.TargetLanguage = "fr"; vm.TargetLanguage = "de"; vm.TargetLanguage = "pt";
        vm.ApplyAiSettings(new(false, "other-model", 1)); await vm.WaitForCacheAsync();
        Assert.Equal("pt", await store.ReadOutputLanguageAsync()); Assert.Equal("pt", vm.TargetLanguage);
        Assert.Equal(1, await store.ReadRequestCountAsync(today)); Assert.Equal(0, provider.Calls);
        Assert.False(vm.GenerateInsightCommand.CanExecute(null));
        vm.ApplyAiSettings(new(true, "test-model", 20)); await vm.GenerateInsightAsync();
        Assert.Equal("pt", vm.Insight!.TargetLanguage); Assert.Equal(1, provider.Calls); await vm.StopAsync();
    }

    [Fact]
    public async Task FailedPreferenceSavePreventsInferenceUntilExplicitRetrySucceeds()
    {
        var store = new FailingStore(); var provider = new Provider(); var vm = new MainViewModel(store, provider, insightProvider: provider);
        await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing, Track, null)); vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics.Text);
        vm.TargetLanguage = "it"; await vm.WaitForCacheAsync(); await vm.GenerateInsightAsync();
        Assert.Equal(0, provider.Calls); Assert.NotEmpty(vm.LanguageMessage); Assert.False(vm.GenerateInsightCommand.CanExecute(null));
        Assert.True(vm.ShowLanguageMessage); Assert.False(vm.ShowPreparationNotice);
        Assert.Contains("Italiano", vm.LanguageSummary); Assert.Equal(PreparationAction.RetryLanguage, vm.Preparation.Action);
        store.Fail = false; await vm.RetryLanguageAsync(); await vm.GenerateInsightAsync();
        Assert.Equal("it", vm.Insight!.TargetLanguage); Assert.Equal(1, provider.Calls); Assert.False(vm.ShowLanguageMessage); await vm.StopAsync();
    }

    [Fact]
    public async Task LanguageTransitionOnlyClaimsConfirmedStorageAndReturningRestoresWithoutInference()
    {
        var store = await Store(); var provider = new Provider(); var vm = new MainViewModel(store, provider, insightProvider: provider, cache: new SqliteCacheStore(Database));
        await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing, Track, null)); vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics.Text); await vm.WaitForCacheAsync();
        await vm.GenerateInsightAsync(); Assert.Equal(ContentSaveState.Saved, vm.InsightSaveState);
        var blocks = vm.ReadingBlocks; await vm.RefreshPreparationTimeAsync(); Assert.Same(blocks, vm.ReadingBlocks);
        vm.ApplyAiSettings(new(true, "test-model")); await vm.WaitForCacheAsync(); Assert.Same(blocks, vm.ReadingBlocks);
        vm.TargetLanguage = "en"; await vm.WaitForCacheAsync(); Assert.Null(vm.Insight);
        Assert.Contains("Idioma cambiado a Inglés. No se ha enviado una consulta nueva.", vm.LanguageMessage); Assert.Contains("guardado confirmado", vm.LanguageMessage);
        Assert.True(vm.ShowLanguageMessage); Assert.False(vm.ShowPreparationNotice);
        Assert.Equal("es", vm.IdentifiedSourceLanguage); Assert.Equal(Lyrics.Text, vm.PreparedLyrics!.Text);
        vm.TargetLanguage = "es"; await vm.WaitForCacheAsync(); Assert.Equal("es", vm.Insight!.TargetLanguage);
        Assert.Contains("Español recuperado", vm.LanguageMessage); Assert.DoesNotContain("Aún no hay", vm.LanguageMessage);
        Assert.DoesNotContain("Idioma cambiado", vm.LanguageMessage); Assert.DoesNotContain("resultado anterior", vm.LanguageMessage);
        Assert.Equal(AiUsageState.RecuperadoDeCache, vm.Usage.State); Assert.Equal(1, provider.Calls); await vm.StopAsync();
    }
    [Fact]
    public async Task DeletingCacheRemovesThePreviouslyConfirmedLanguageStorageNotice()
    {
        var store = await Store(); var provider = new Provider(); var vm = new MainViewModel(store, provider, insightProvider: provider, cache: new SqliteCacheStore(Database));
        await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing, Track, null)); vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics.Text); await vm.WaitForCacheAsync(); await vm.GenerateInsightAsync();
        vm.TargetLanguage = "en"; await vm.WaitForCacheAsync(); Assert.Contains("guardado confirmado", vm.LanguageMessage);
        await vm.DeleteCacheAsync(true); Assert.DoesNotContain("guardado confirmado", vm.LanguageMessage); Assert.Equal(1, provider.Calls); await vm.StopAsync();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SessionOnlyOrFailedWriteNeverClaimsPreviousAnalysisIsSaved(bool failWrite)
    {
        var store = await Store(); var provider = new Provider(); var vm = new MainViewModel(store, provider, insightProvider: provider, cache: failWrite ? new SqliteCacheStore(Database) : null);
        await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing, Track, null)); vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics.Text); await vm.WaitForCacheAsync();
        if (failWrite) using (var connection = new SqliteConnection($"Data Source={Database};Pooling=False"))
        { await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TRIGGER fail_insight BEFORE INSERT ON insight_cache BEGIN SELECT RAISE(ABORT,'synthetic'); END;"; command.ExecuteNonQuery(); }
        await vm.GenerateInsightAsync(); Assert.Equal(failWrite ? ContentSaveState.Failed : ContentSaveState.SessionOnly, vm.InsightSaveState);
        Assert.Contains("no registrados", vm.AnalysisDetails); vm.TargetLanguage = "en"; await vm.WaitForCacheAsync();
        Assert.DoesNotContain("guardado confirmado", vm.LanguageMessage); Assert.Null(vm.Insight); Assert.Equal(1, provider.Calls); await vm.StopAsync();
    }
    [Theory]
    [InlineData("track")] [InlineData("lyrics")] [InlineData("model")]
    public async Task PendingPreferenceSaveCannotRecreateNoticeForAnOldContext(string change)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FailingStore { Fail = false, Save = async _ => { started.TrySetResult(); await release.Task; } };
        var provider = new Provider(); var vm = new MainViewModel(store, provider, insightProvider: provider);
        await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing, Track, null)); vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics.Text);
        vm.TargetLanguage = "en"; await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("Guardando", vm.LanguageMessage); Assert.False(vm.GenerateInsightCommand.CanExecute(null));
        if (change == "track") vm.ApplyDetection(new(ApplicationState.Playing, Track with { Revision = 2, Title = "Other song" }, null));
        else if (change == "lyrics") vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics.Text + " editada");
        else vm.ApplyAiSettings(new(true, "other-model"));
        release.SetResult(); await vm.WaitForCacheAsync();
        Assert.False(vm.ShowLanguageMessage); Assert.Empty(vm.LanguageSummary); Assert.Equal(0, provider.Calls);
        await vm.StopAsync();
    }
    [Fact]
    public async Task LanguageChangeExplainsCancellationAndRejectsLateResultUntilExplicitGeneration()
    {
        var pending = new TaskCompletionSource<SongInsight>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider { Generate = _ => pending.Task };
        var vm = new MainViewModel(await Store(), provider, insightProvider: provider);
        await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing, Track, null)); vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics.Text);
        var operation = vm.GenerateInsightAsync(); Assert.Equal(1, provider.Calls);
        vm.TargetLanguage = "en"; await vm.WaitForCacheAsync();
        Assert.Contains("canceló el análisis", vm.LanguageMessage); Assert.Contains("puede consumir", vm.LanguageMessage);
        Assert.Contains("puede haber consumo", vm.LanguageSummary); Assert.Contains("Inglés", vm.LanguageSummary);
        pending.SetResult(Result("es")); await operation;
        Assert.Null(vm.Insight); Assert.Equal("en", vm.TargetLanguage); Assert.Equal(Lyrics.Text, vm.PreparedLyrics!.Text);
        Assert.Equal(1, provider.Calls); Assert.True(vm.ShowLanguageMessage);
        provider.Generate = target => Task.FromResult(Result(target)); await vm.GenerateInsightAsync();
        Assert.Equal("en", vm.Insight!.TargetLanguage); Assert.Equal(2, provider.Calls); Assert.False(vm.ShowLanguageMessage);
        await vm.StopAsync();
    }
    [Fact]
    public async Task RapidChangesAndRetryNeverGenerateAndFailureHasAnActionWithoutATrack()
    {
        var store = new FailingStore(); var provider = new Provider(); var vm = new MainViewModel(store, provider, insightProvider: provider);
        await vm.InitializeAsync();
        vm.TargetLanguage = "fr"; vm.TargetLanguage = "de"; vm.TargetLanguage = "pt"; await vm.WaitForCacheAsync();
        Assert.Contains("Portugués", vm.LanguageMessage); Assert.DoesNotContain("Francés", vm.LanguageMessage);
        Assert.True(vm.ShowLanguageRetry); Assert.True(vm.RetryLanguageCommand.CanExecute(null));
        store.Fail = false; await vm.RetryLanguageAsync();
        Assert.Contains("Portugués", vm.LanguageMessage); Assert.False(vm.ShowLanguageRetry); Assert.Equal(0, provider.Calls);
        await vm.StopAsync();
    }
    private sealed class FailingStore : ISettingsStore, IOutputLanguageSettings
    {
        public bool Fail = true;
        public Func<string, Task>? Save;
        public Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken token = default) => Task.FromResult(new AppSettings(true, "test-model"));
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly date, CancellationToken token = default) => Task.FromResult(0);
        public Task<string> ReadOutputLanguageAsync(CancellationToken token = default) => Task.FromResult("es");
        public Task SaveOutputLanguageAsync(string language, CancellationToken token = default) => Save?.Invoke(language) ?? (Fail ? Task.FromException(new IOException()) : Task.CompletedTask);
    }

    private sealed class Provider : ILyricsProvider, IInsightProvider
    {
        public int Calls;
        public Func<string, Task<SongInsight>> Generate = target => Task.FromResult(Result(target));
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token) => throw new InvalidOperationException();
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token) => GenerateAsync(track, lyrics, "es", token);
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, string targetLanguage, CancellationToken token) { Calls++; return Generate(targetLanguage); }
    }
    private sealed class ForbiddenProvider : IInsightProvider
    { public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token) => throw new InvalidOperationException("Forbidden fallback"); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { public int Calls; protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return send(request); } }
    private sealed class Secrets : ISecretStore
    {
        public Task<bool> HasKeyAsync(CancellationToken token = default) => Task.FromResult(true);
        public Task<string?> ReadKeyAsync(CancellationToken token = default) => Task.FromResult<string?>("test-key");
        public Task SaveKeyAsync(SecureString key, CancellationToken token = default) => throw new NotSupportedException();
        public Task DeleteKeyAsync(CancellationToken token = default) => throw new NotSupportedException();
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
