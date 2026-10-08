using Microsoft.Data.Sqlite;
using System.IO;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "SongSense.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(directory, "songsense.db");

    [Fact]
    public async Task FirstRunCreatesOnlyInitialSchemaAndSafeDefaults()
    {
        var store = new SqliteSettingsStore(DatabasePath);
        await store.InitializeAsync();
        Assert.Equal(new AppSettings(false, null, 20), await store.ReadSettingsAsync());
        Assert.Equal(0, await store.ReadRequestCountAsync(new DateOnly(2026, 10, 5)));
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(3L, command.ExecuteScalar());
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        using var reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read()) tables.Add(reader.GetString(0));
        Assert.Equal(new[] { "app_settings", "daily_request_counts", "insight_cache", "song_cache", "song_insights" }, tables);
    }

    [Fact]
    public async Task ReopeningPreservesSettingsAndDailyCounters()
    {
        var store = new SqliteSettingsStore(DatabasePath);
        await store.InitializeAsync();
        var settings = new AppSettings(false, "explicit-model", 37);
        await store.SaveSettingsAsync(settings);
        using (var connection = OpenDatabase())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO daily_request_counts VALUES ('2026-10-05', 7);";
            command.ExecuteNonQuery();
        }
        var reopened = new SqliteSettingsStore(DatabasePath);
        await reopened.InitializeAsync();
        await reopened.InitializeAsync();
        Assert.Equal(settings, await reopened.ReadSettingsAsync());
        Assert.Equal(7, await reopened.ReadRequestCountAsync(new DateOnly(2026, 10, 5)));
        Assert.Equal(0, await reopened.ReadRequestCountAsync(new DateOnly(2026, 10, 6)));
    }

    [Fact]
    public async Task NewerSchemaIsRejectedWithoutOverwritingData()
    {
        var store = new SqliteSettingsStore(DatabasePath);
        await store.InitializeAsync();
        await store.SaveSettingsAsync(new AppSettings(false, "keep-me", 42));
        using (var connection = OpenDatabase())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version = 99;";
            command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Equal("keep-me", (await store.ReadSettingsAsync()).Model);
        using var check = OpenDatabase();
        using var query = check.CreateCommand();
        query.CommandText = "PRAGMA user_version;";
        Assert.Equal(99L, query.ExecuteScalar());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task InvalidLimitDoesNotModifyConfiguration(int limit)
    {
        var store = new SqliteSettingsStore(DatabasePath);
        await store.InitializeAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveSettingsAsync(new AppSettings(DailyRequestLimit: limit)));
        Assert.Equal(20, (await store.ReadSettingsAsync()).DailyRequestLimit);
    }

    [Fact]
    public async Task CancellationDoesNotCreateHalfASchema()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var store = new SqliteSettingsStore(DatabasePath);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.InitializeAsync(source.Token));
        await store.InitializeAsync();
        Assert.Equal(new AppSettings(), await store.ReadSettingsAsync());
    }

    [Fact]
    public void AllApplicationStatesHaveDistinctSpanishLabels()
    {
        var labels = Enum.GetValues<ApplicationState>().Select(s => s.InSpanish()).ToArray();
        Assert.All(labels, label => Assert.False(string.IsNullOrWhiteSpace(label)));
        Assert.Equal(labels.Length, labels.Distinct().Count());
    }

    private SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
