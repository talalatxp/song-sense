using System.Globalization;
using Microsoft.Data.Sqlite;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed class SqliteSettingsStore(string databasePath) : ISettingsStore
{
    public const int SchemaVersion = 1;
    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SongSense", "songsense.db");

    private SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = false,
        DefaultTimeout = 5
    }.ToString());

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (version > SchemaVersion)
            throw new InvalidOperationException("La base pertenece a una versión más reciente de SongSense. Usa esa versión de la aplicación.");
        if (version < 1)
        {
            command.CommandText = """
                CREATE TABLE app_settings (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    ai_enabled INTEGER NOT NULL DEFAULT 0 CHECK (ai_enabled IN (0, 1)),
                    model TEXT NULL,
                    daily_request_limit INTEGER NOT NULL DEFAULT 20 CHECK (daily_request_limit BETWEEN 1 AND 100)
                );
                INSERT INTO app_settings (id) VALUES (1);
                CREATE TABLE daily_request_counts (
                    local_date TEXT PRIMARY KEY,
                    request_count INTEGER NOT NULL CHECK (request_count >= 0)
                );
                PRAGMA user_version = 1;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
    }

    public async Task<AppSettings> ReadSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ai_enabled, model, daily_request_limit FROM app_settings WHERE id = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Falta la configuración inicial. Revisa la base local antes de continuar.");
        return new AppSettings(reader.GetBoolean(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt32(2));
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings.DailyRequestLimit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(settings), "El límite diario debe estar entre 1 y 100.");
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE app_settings SET ai_enabled = $enabled, model = $model, daily_request_limit = $limit WHERE id = 1;";
        command.Parameters.AddWithValue("$enabled", settings.AiEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$model", (object?)settings.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", settings.DailyRequestLimit);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("No se encontró la configuración inicial.");
    }

    public async Task<int> ReadRequestCountAsync(DateOnly localDate, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT request_count FROM daily_request_counts WHERE local_date = $date;";
        command.Parameters.AddWithValue("$date", localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }
}
