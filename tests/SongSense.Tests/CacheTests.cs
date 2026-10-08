using System.IO;
using Microsoft.Data.Sqlite;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class CacheTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "SongSense.CacheTests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(directory, "songsense.db");
    private readonly Clock clock = new();
    private static CurrentTrack Track(int id = 1) => new(id, "Own song " + id, "Test artist", "Test album", TimeSpan.FromSeconds(180), PlaybackState.Playing);
    private static ResolvedLyrics Lyrics(CurrentTrack track) => new(track.Revision, "Abro una ventana\n\nAbro una ventana", LyricsOrigin.Manual, null, false);
    private static SongInsight Insight(CurrentTrack track) => new(track.Revision, "es", [],
        string.Join(' ', Enumerable.Repeat("La ventana podría representar una oportunidad de cambio mientras la repetición sugiere una decisión todavía incierta.", 5)), ["Cambio"], [], ["Una acción cotidiana es otra lectura posible."], ["La lectura es tentativa."]);
    private async Task<(SqliteSettingsStore Settings, SqliteCacheStore Cache)> Setup()
    {
        var settings = new SqliteSettingsStore(Database); await settings.InitializeAsync();
        return (settings, new SqliteCacheStore(Database, clock));
    }
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()); connection.Open(); return connection;
    }
    private long Scalar(string sql)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = sql; return (long)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task AuthenticationRoutesAndAccountsNeverShareAnalysisCache()
    {
        var (_, cache) = await Setup(); var track = Track(); var lyrics = Lyrics(track);
        await cache.SaveInsightAsync(track, lyrics, "chosen-model", "v1", Insight(track), scope: "chatgpt:account-a");
        Assert.NotNull(await cache.ReadInsightAsync(lyrics, "chosen-model", "v1", scope: "chatgpt:account-a"));
        Assert.Null(await cache.ReadInsightAsync(lyrics, "chosen-model", "v1", scope: "chatgpt:account-b"));
        Assert.Null(await cache.ReadInsightAsync(lyrics, "chosen-model", "v1"));
        await cache.SaveInsightAsync(track, lyrics, "chosen-model", "v1", Insight(track) with { Usage = new AiUsage(100, 50, 150) });
        Assert.Null((await cache.ReadInsightAsync(lyrics, "chosen-model", "v1"))!.Usage);
    }

    [Fact]
    public async Task LegacyMigrationPreservesPreferencesCountersAndIsRepeatable()
    {
        Directory.CreateDirectory(directory);
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE app_settings(id INTEGER PRIMARY KEY,ai_enabled INTEGER,model TEXT,daily_request_limit INTEGER);
                INSERT INTO app_settings VALUES(1,0,'chosen-model',42);
                CREATE TABLE daily_request_counts(local_date TEXT PRIMARY KEY,request_count INTEGER);
                INSERT INTO daily_request_counts VALUES('2026-10-07',17);
                PRAGMA user_version=1;
                """; command.ExecuteNonQuery();
        }
        var (store, _) = await Setup(); await store.InitializeAsync();
        Assert.Equal(new AppSettings(false, "chosen-model", 42), await store.ReadSettingsAsync());
        Assert.Equal(17, await store.ReadRequestCountAsync(new(2026, 10, 7)));
        Assert.Equal(3, Scalar("PRAGMA user_version;"));
        Assert.True(await store.ReadAutomaticUpdatesAsync());
    }

    [Fact]
    public async Task RestartRestoresSelectedLyricsAndExactResultWithoutNetworkOrSpending()
    {
        var (store, cache) = await Setup();
        await store.SaveSettingsAsync(new(false, "chosen-model"));
        var track = Track();
        await cache.SaveInsightAsync(track, Lyrics(track), "chosen-model", CacheIdentity.Version, Insight(track));
        var provider = new NeverProvider();
        var vm = new MainViewModel(new SqliteSettingsStore(Database), provider, insightProvider: provider, cache: new SqliteCacheStore(Database));
        vm.ApplyDetection(new(ApplicationState.Playing, track with { Revision = 99 }, null));
        await vm.InitializeAsync(); await vm.WaitForCacheAsync();
        Assert.Equal(Lyrics(track).Text, vm.OriginalLyrics);
        Assert.True(vm.HasInsight);
        Assert.Equal(99, vm.Insight!.TrackRevision);
        Assert.Equal(vm.LyricsRevision, vm.Insight.LyricsRevision);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, await store.ReadRequestCountAsync(new(2026, 10, 7)));
        await vm.StopAsync();
    }

    [Fact]
    public async Task ExactKeyDistinguishesWhitespaceLineModelPromptAndVersionLabels()
    {
        var (_, cache) = await Setup(); var track = Track(); var lyrics = Lyrics(track);
        await cache.SaveInsightAsync(track, lyrics, "chosen-model", "v1", Insight(track));
        Assert.NotNull(await cache.ReadInsightAsync(lyrics with { Text = lyrics.Text.Replace("\n", "\r\n") }, "chosen-model", "v1"));
        Assert.Null(await cache.ReadInsightAsync(lyrics with { Text = lyrics.Text + " " }, "chosen-model", "v1"));
        Assert.Null(await cache.ReadInsightAsync(lyrics with { Text = "Otra línea" }, "chosen-model", "v1"));
        Assert.Null(await cache.ReadInsightAsync(lyrics, "another-model", "v1"));
        Assert.Null(await cache.ReadInsightAsync(lyrics, "chosen-model", "v2"));
        Assert.Equal(CacheIdentity.Song(track), CacheIdentity.Song(track with { Title = "  OWN   song 1  ", Revision = 88 }));
        Assert.NotEqual(CacheIdentity.Song(track), CacheIdentity.Song(track with { Title = track.Title + " (Live)" }));
        Assert.NotEqual(CacheIdentity.Song(track), CacheIdentity.Song(track with { Album = "Other album" }));
        Assert.NotEqual(CacheIdentity.Song(track), CacheIdentity.Song(track with { Duration = TimeSpan.FromSeconds(181) }));
    }

    [Fact]
    public async Task LyricsAndAbsenceHaveSeparateTtlWhileInsightDoesNotExpire()
    {
        var (_, cache) = await Setup(); var track = Track(); var absent = Track(2);
        await cache.SaveInsightAsync(track, Lyrics(track), "chosen-model", "v1", Insight(track));
        await cache.SaveLyricsAsync(absent, null);
        clock.Now += TimeSpan.FromMinutes(59);
        Assert.True((await cache.ReadSongAsync(absent))!.IsAbsence);
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.Null(await cache.ReadSongAsync(absent));
        clock.Now += TimeSpan.FromDays(29);
        Assert.NotNull(await cache.ReadSongAsync(track));
        clock.Now += TimeSpan.FromDays(1);
        Assert.Null(await cache.ReadSongAsync(track));
        Assert.NotNull(await cache.ReadInsightAsync(Lyrics(track), "chosen-model", "v1"));
    }

    [Fact]
    public async Task AmbiguousAssociationRequiresExplicitConfirmationInViewModel()
    {
        var (store, cache) = await Setup(); var track = Track() with { Album = null, Duration = null };
        await cache.SaveLyricsAsync(track, Lyrics(track));
        var provider = new NeverProvider();
        var vm = new MainViewModel(store, provider, cache: cache);
        vm.ApplyDetection(new(ApplicationState.Playing, track, null));
        await vm.InitializeAsync(); await vm.WaitForCacheAsync();
        Assert.Null(vm.PreparedLyrics); Assert.True(vm.HasPendingCachedLyrics);
        vm.ConfirmCachedLyricsCommand.Execute(null); await vm.WaitForCacheAsync();
        Assert.Equal(Lyrics(track).Text, vm.OriginalLyrics);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task LruEvictsLeastRecentlyAccessedAndCleansOrphans()
    {
        var (_, cache) = await Setup();
        for (var id = 1; id <= 200; id++)
        {
            clock.Now += TimeSpan.FromSeconds(1);
            await cache.SaveInsightAsync(Track(id), Lyrics(Track(id)) with { Text = "Abro una ventana " + id }, "chosen-model", "v1", Insight(Track(id)));
        }
        clock.Now += TimeSpan.FromSeconds(1); Assert.NotNull(await cache.ReadSongAsync(Track(1)));
        clock.Now += TimeSpan.FromSeconds(1); await cache.SaveLyricsAsync(Track(201), Lyrics(Track(201)));
        Assert.NotNull(await cache.ReadSongAsync(Track(1))); Assert.Null(await cache.ReadSongAsync(Track(2)));
        Assert.Equal(200, Scalar("SELECT count(*) FROM song_cache;"));
        Assert.Equal(199, Scalar("SELECT count(*) FROM insight_cache;"));
    }

    [Fact]
    public async Task DeleteAndClearKeepSharedInsightsUntilOrphanedAndPreserveBudgetAndSecretFile()
    {
        var (store, cache) = await Setup(); await store.SaveSettingsAsync(new(true, "chosen-model", 30));
        Assert.True(await store.TryReserveRequestAsync(new(2026, 10, 7)));
        var secret = Path.Combine(directory, "credentials.dpapi"); await File.WriteAllBytesAsync(secret, [1, 2, 3]);
        await cache.SaveInsightAsync(Track(), Lyrics(Track()), "chosen-model", "v1", Insight(Track()));
        await cache.SaveInsightAsync(Track(2), Lyrics(Track(2)), "chosen-model", "v1", Insight(Track(2)));
        await cache.DeleteSongAsync(Track());
        Assert.NotNull(await cache.ReadInsightAsync(Lyrics(Track(2)), "chosen-model", "v1"));
        await cache.ClearAsync();
        Assert.Equal(0, Scalar("SELECT count(*) FROM insight_cache;"));
        Assert.Equal(0, Scalar("SELECT count(*) FROM song_insights;"));
        Assert.Equal(new AppSettings(true, "chosen-model", 30), await store.ReadSettingsAsync());
        Assert.Equal(1, await store.ReadRequestCountAsync(new(2026, 10, 7)));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(secret));
    }

    [Fact]
    public async Task InterruptedWriteRollsBackAssociationLyricsAndResultTogether()
    {
        var (_, cache) = await Setup(); var track = Track();
        await cache.SaveLyricsAsync(track, Lyrics(track));
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        { command.CommandText = "CREATE TRIGGER interrupt_write BEFORE INSERT ON insight_cache BEGIN SELECT RAISE(ABORT,'synthetic interruption'); END;"; command.ExecuteNonQuery(); }
        await Assert.ThrowsAsync<SqliteException>(() => cache.SaveInsightAsync(track, Lyrics(track) with { Text = "Otra letra propia" }, "chosen-model", "v1", Insight(track)));
        Assert.Equal(Lyrics(track).Text, (await cache.ReadSongAsync(track))!.Lyrics!.Text);
        Assert.Equal(0, Scalar("SELECT count(*) FROM insight_cache;"));
        Assert.Equal(0, Scalar("SELECT count(*) FROM song_insights;"));
    }

    [Fact]
    public async Task InvalidResultCannotBeSavedAndCorruptedJsonCannotBeUsed()
    {
        var (_, cache) = await Setup(); var track = Track();
        await Assert.ThrowsAsync<AiException>(() => cache.SaveInsightAsync(track, Lyrics(track), "chosen-model", "v1", Insight(track) with { Summary = "Short" }));
        Assert.Null(await cache.ReadSongAsync(track));
        await cache.SaveInsightAsync(track, Lyrics(track), "chosen-model", "v1", Insight(track));
        using (var connection = Open()) using (var command = connection.CreateCommand()) { command.CommandText = "UPDATE insight_cache SET json='{}';"; command.ExecuteNonQuery(); }
        await Assert.ThrowsAsync<AiException>(() => cache.ReadInsightAsync(Lyrics(track), "chosen-model", "v1"));
    }

    [Fact]
    public async Task ActualGenerationModelAndPromptMustMatchTheCacheKey()
    {
        var (_, cache) = await Setup(); var track = Track();
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.SaveInsightAsync(track, Lyrics(track), "chosen-model", "v1", Insight(track) with { Model = "another-model" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.SaveInsightAsync(track, Lyrics(track), "chosen-model", "v1", Insight(track) with { PromptVersion = "v2" }));
        Assert.Null(await cache.ReadSongAsync(track));
    }

    [Fact]
    public async Task CorruptionRecoveryPreservesOriginalBytesAndBlocksUnknownSpending()
    {
        Directory.CreateDirectory(directory); var bytes = System.Text.Encoding.UTF8.GetBytes("synthetic corrupt database fixture");
        await File.WriteAllBytesAsync(Database, bytes);
        var store = new SqliteSettingsStore(Database);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.True(store.CanRecover); Assert.Equal(bytes, await File.ReadAllBytesAsync(Database));
        await store.RecoverAsync(); await store.InitializeAsync();
        Assert.False((await store.ReadSettingsAsync()).AiEnabled);
        Assert.Equal(100, await store.ReadRequestCountAsync(DateOnly.FromDateTime(DateTime.Now)));
        var recovery = Assert.Single(Directory.GetDirectories(directory, "recovery-*"));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(recovery, "original.db")));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(recovery, "retained.db")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecoverAsync());
    }

    [Fact]
    public async Task ManualEditPersistsAndDeleteDuringLateGenerationCannotResurrectIt()
    {
        var (store, cache) = await Setup(); await store.SaveSettingsAsync(new(true, "chosen-model"));
        var pending = new PendingProvider(); var vm = new MainViewModel(store, pending, insightProvider: pending, cache: cache);
        var track = Track(); vm.ApplyDetection(new(ApplicationState.Playing, track, null));
        await vm.InitializeAsync(); await vm.WaitForCacheAsync();
        Assert.Null(vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics(track).Text)); await vm.WaitForCacheAsync();
        Assert.Equal(Lyrics(track).Text, (await cache.ReadSongAsync(track))!.Lyrics!.Text);
        var generation = vm.GenerateInsightAsync(); await pending.Started.Task;
        await vm.DeleteCacheAsync(true); pending.Result.SetResult(Insight(track)); await generation; await vm.WaitForCacheAsync();
        Assert.Null(vm.Insight); Assert.Null(await cache.ReadSongAsync(track)); Assert.Equal(0, Scalar("SELECT count(*) FROM insight_cache;"));
    }

    [Fact]
    public async Task ExplicitRegenerationBypassesCacheWhileRepeatedSearchAndRestartDoNotSpend()
    {
        var (store, cache) = await Setup(); await store.SaveSettingsAsync(new(true, "chosen-model"));
        var track = Track(); await cache.SaveInsightAsync(track, Lyrics(track), "chosen-model", CacheIdentity.Version, Insight(track));
        var provider = new CountingProvider(store);
        var vm = new MainViewModel(store, provider, insightProvider: provider, cache: cache);
        vm.ApplyDetection(new(ApplicationState.Playing, track, null)); await vm.InitializeAsync(); await vm.WaitForCacheAsync();
        Assert.True(vm.HasInsight);
        await vm.SearchLyricsAsync(); await vm.WaitForCacheAsync();
        Assert.True(vm.HasInsight); Assert.Equal(0, provider.Calls);
        await vm.RegenerateInsightAsync(); await vm.WaitForCacheAsync();
        Assert.Equal(1, provider.Calls); Assert.Equal("Nueva lectura", Assert.Single(vm.Insight!.Themes));
        Assert.Equal(1, await store.ReadRequestCountAsync(DateOnly.FromDateTime(DateTime.Now)));
        vm.ApplyAiSettings(new(true, "different-model")); await vm.WaitForCacheAsync(); Assert.Null(vm.Insight);
        vm.ApplyAiSettings(new(true, "chosen-model")); await vm.WaitForCacheAsync(); Assert.Equal("Nueva lectura", Assert.Single(vm.Insight!.Themes));
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ReusedResultLinksNewAssociationAndSurvivesDeletingOriginalSong()
    {
        var (_, cache) = await Setup(); var first = Track(); var second = Track(2);
        await cache.SaveInsightAsync(first, Lyrics(first), "chosen-model", "v1", Insight(first));
        await cache.SaveLyricsAsync(second, Lyrics(second));
        Assert.NotNull(await cache.ReadInsightAsync(Lyrics(second), "chosen-model", "v1", track: second));
        await cache.DeleteSongAsync(first);
        Assert.NotNull(await cache.ReadInsightAsync(Lyrics(second), "chosen-model", "v1"));
    }

    private sealed class CountingProvider(SqliteSettingsStore store) : ILyricsProvider, IInsightProvider
    {
        public int Calls { get; private set; }
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token) => throw new InvalidOperationException("Cached lyrics should prevent network");
        public async Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token)
        {
            Calls++; Assert.True(await store.TryReserveRequestAsync(DateOnly.FromDateTime(DateTime.Now), token));
            return Insight(track) with { Themes = ["Nueva lectura"] };
        }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class NeverProvider : ILyricsProvider, IInsightProvider
    {
        public int Calls { get; private set; }
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token) { Calls++; throw new InvalidOperationException("Unexpected network"); }
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token) { Calls++; throw new InvalidOperationException("Unexpected paid request"); }
    }
    private sealed class PendingProvider : ILyricsProvider, IInsightProvider
    {
        public TaskCompletionSource<bool> Started { get; } = new();
        public TaskCompletionSource<SongInsight> Result { get; } = new();
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token) => throw new InvalidOperationException();
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token) { Started.SetResult(true); return Result.Task; }
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
