using System.Globalization;
using Microsoft.Data.Sqlite;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed class SqliteSettingsStore(string databasePath) : ISettingsStore, IRequestBudget, IRecoverableSettingsStore, IAutomaticUpdateSettings
{
    public bool CanRecover { get; private set; }
    public string RecoveryMessage { get; private set; } = "";
    public const int SchemaVersion = 3;
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
        CanRecover = false;
        try { await InitializeCoreAsync(cancellationToken); }
        catch (SqliteException error) when (error.SqliteErrorCode is 11 or 26)
        {
            CanRecover = true; RecoveryMessage = "Base local corrupta. Recupera explícitamente conservando una copia del original.";
            throw new InvalidOperationException(RecoveryMessage);
        }
    }
    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA quick_check;";
            if (await check.ExecuteScalarAsync(cancellationToken) as string != "ok")
            { CanRecover = true; RecoveryMessage = "La base no supera la comprobación de integridad. Recupera conservando el original."; throw new InvalidOperationException(RecoveryMessage); }
        }
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
        if (version < 2)
        {
            command.CommandText = """
                CREATE TABLE song_cache (
                    signature TEXT PRIMARY KEY, lyrics TEXT NULL, origin INTEGER NULL,
                    lrclib_id INTEGER NULL, instrumental INTEGER NOT NULL,
                    expires_at INTEGER NOT NULL, last_access INTEGER NOT NULL
                );
                CREATE TABLE insight_cache (cache_key TEXT PRIMARY KEY, json TEXT NOT NULL, created_at INTEGER NOT NULL);
                CREATE TABLE song_insights (
                    signature TEXT NOT NULL REFERENCES song_cache(signature) ON DELETE CASCADE,
                    cache_key TEXT NOT NULL REFERENCES insight_cache(cache_key) ON DELETE CASCADE,
                    PRIMARY KEY(signature, cache_key)
                );
                CREATE INDEX ix_song_insights_key ON song_insights(cache_key);
                CREATE INDEX ix_song_cache_access ON song_cache(last_access);
                PRAGMA user_version = 2;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (version < 3)
        {
            command.CommandText = "ALTER TABLE app_settings ADD COLUMN automatic_updates INTEGER NOT NULL DEFAULT 1 CHECK(automatic_updates IN (0,1)); PRAGMA user_version = 3;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
    }

    public async Task RecoverAsync(CancellationToken token = default)
    {
        if (!CanRecover) throw new InvalidOperationException("La base no necesita recuperación.");
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.Combine(Path.GetDirectoryName(fullPath)!, "recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settings = new AppSettings();
        var counts = new List<(string Date, int Count)>();
        var readCounts = false;
        try
        {
            settings = (await ReadSettingsAsync(token)) with { AiEnabled = false };
            await using var source = CreateConnection(); await source.OpenAsync(token);
            using var read = source.CreateCommand(); read.CommandText = "SELECT local_date,request_count FROM daily_request_counts;";
            using var reader = await read.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) counts.Add((reader.GetString(0), reader.GetInt32(1)));
            readCounts = true;
        }
        catch (SqliteException) { }
        catch (InvalidOperationException) { }
        var replacementPath = Path.Combine(directory, "replacement.db");
        var replacement = new SqliteSettingsStore(replacementPath);
        await replacement.InitializeAsync(token); await replacement.SaveSettingsAsync(settings, token);
        await using (var target = replacement.CreateConnection())
        {
            await target.OpenAsync(token);
            using var transaction = target.BeginTransaction();
            if (!readCounts) counts = [(DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 100)];
            foreach (var (date, count) in counts)
            {
                using var write = target.CreateCommand(); write.Transaction = transaction;
                write.CommandText = "INSERT INTO daily_request_counts VALUES ($date,$count);";
                write.Parameters.AddWithValue("$date", date); write.Parameters.AddWithValue("$count", count);
                await write.ExecuteNonQueryAsync(token);
            }
            token.ThrowIfCancellationRequested(); transaction.Commit();
        }
        // Refuse if another instance holds the file. Originals and sidecars are
        // preserved locally before any replacement; no automatic recovery or deletion.
        using (var original = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.None))
        using (var backup = new FileStream(Path.Combine(directory, "original.db"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await original.CopyToAsync(backup, token);
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            if (File.Exists(fullPath + suffix)) File.Copy(fullPath + suffix, Path.Combine(directory, "original.db" + suffix));
        token.ThrowIfCancellationRequested();
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            if (File.Exists(fullPath + suffix)) File.Move(fullPath + suffix, Path.Combine(directory, "retained.db" + suffix));
        File.Replace(replacementPath, fullPath, Path.Combine(directory, "retained.db"));
        CanRecover = false;
        RecoveryMessage = $"Base recuperada. Original conservado en {directory}. IA desactivada. " +
            (readCounts ? "Contadores conservados." : "Contadores ilegibles conservados en el original; solicitudes bloqueadas hoy.");
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

    public async Task<bool> ReadAutomaticUpdatesAsync(CancellationToken token = default)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(token);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT automatic_updates FROM app_settings WHERE id=1;";
        var value = await command.ExecuteScalarAsync(token) ?? throw new InvalidOperationException("Falta la configuración automática.");
        return Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
    }
    public async Task SaveAutomaticUpdatesAsync(bool enabled, CancellationToken token = default)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(token);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE app_settings SET automatic_updates=$enabled WHERE id=1;";
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        if (await command.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Falta la configuración automática.");
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings.DailyRequestLimit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(settings), "El límite diario debe estar entre 1 y 100.");
        AiSettingsRules.Validate(settings);
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

    public async Task<bool> TryReserveRequestAsync(DateOnly localDate, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // One atomic statement arbitrates even across application processes. The saved
        // limit and enabled flag are checked at reservation time, never from a stale UI.
        command.CommandText = """
            INSERT INTO daily_request_counts (local_date, request_count)
            SELECT $date, 1 FROM app_settings WHERE id = 1 AND ai_enabled = 1
            ON CONFLICT(local_date) DO UPDATE SET request_count = request_count + 1
            WHERE request_count < (SELECT daily_request_limit FROM app_settings WHERE id = 1 AND ai_enabled = 1)
            RETURNING request_count;
            """;
        command.Parameters.AddWithValue("$date", localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var reserved = await command.ExecuteScalarAsync(cancellationToken) is not null;
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return reserved;
    }
}
