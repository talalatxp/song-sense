using Microsoft.Data.Sqlite;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed class SqliteCacheStore(string databasePath, TimeProvider? timeProvider = null) : ICacheStore
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private long Now => clock.GetUtcNow().UtcTicks;
    private SqliteConnection Connection() => new(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false, ForeignKeys = true, DefaultTimeout = 5 }.ToString());
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    private static void Cleanup(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = Command(connection, transaction, """
            DELETE FROM song_cache WHERE signature IN (SELECT signature FROM song_cache ORDER BY last_access DESC, rowid DESC LIMIT -1 OFFSET 200);
            DELETE FROM insight_cache WHERE cache_key NOT IN (SELECT cache_key FROM song_insights);
            """); command.ExecuteNonQuery();
    }
    private void Upsert(SqliteConnection connection, SqliteTransaction transaction, CurrentTrack track, ResolvedLyrics? lyrics)
    {
        using var command = Command(connection, transaction, """
            INSERT INTO song_cache VALUES ($signature,$lyrics,$origin,$id,$instrumental,$expires,$access)
            ON CONFLICT(signature) DO UPDATE SET lyrics=$lyrics,origin=$origin,lrclib_id=$id,instrumental=$instrumental,expires_at=$expires,last_access=$access;
            """, ("$signature", CacheIdentity.Song(track)), ("$lyrics", lyrics?.Text), ("$origin", lyrics is null ? null : (int)lyrics.Origin), ("$id", lyrics?.LrclibId), ("$instrumental", lyrics?.IsInstrumental == true ? 1 : 0), ("$expires", Now + (lyrics is null ? TimeSpan.FromHours(1) : TimeSpan.FromDays(30)).Ticks), ("$access", Now));
        command.ExecuteNonQuery();
    }
    private static void Validate(CurrentTrack track, ResolvedLyrics? lyrics)
    {
        if (lyrics is not null && (lyrics.TrackRevision != track.Revision || !Enum.IsDefined(lyrics.Origin) || lyrics.Text.Length > 20_000 || (!lyrics.IsInstrumental && string.IsNullOrWhiteSpace(lyrics.Text))))
            throw new InvalidOperationException("No se puede guardar una letra inválida o de otra canción.");
    }
    public async Task SaveLyricsAsync(CurrentTrack track, ResolvedLyrics? lyrics, CancellationToken token = default)
    {
        Validate(track, lyrics);
        await using var connection = Connection(); await connection.OpenAsync(token);
        using var transaction = connection.BeginTransaction(deferred: false);
        Upsert(connection, transaction, track, lyrics); Cleanup(connection, transaction);
        token.ThrowIfCancellationRequested(); transaction.Commit();
    }
    public async Task<CachedSong?> ReadSongAsync(CurrentTrack track, CancellationToken token = default)
    {
        await using var connection = Connection(); await connection.OpenAsync(token);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = Command(connection, transaction, "SELECT lyrics,origin,lrclib_id,instrumental,expires_at FROM song_cache WHERE signature=$signature;", ("$signature", CacheIdentity.Song(track)));
        CachedSong? result = null;
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read() && reader.GetInt64(4) > Now)
            {
                var lyrics = reader.IsDBNull(0) ? null : new ResolvedLyrics(track.Revision, reader.GetString(0), (LyricsOrigin)reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.GetBoolean(3));
                Validate(track, lyrics);
                result = new(lyrics, lyrics is null, !CacheIdentity.IsUnambiguous(track));
            }
        }
        using var touch = Command(connection, transaction, "UPDATE song_cache SET last_access=$access WHERE signature=$signature;", ("$access", Now), ("$signature", CacheIdentity.Song(track)));
        if (result is not null) touch.ExecuteNonQuery();
        token.ThrowIfCancellationRequested(); transaction.Commit(); return result;
    }
    public async Task<SongInsight?> ReadInsightAsync(ResolvedLyrics lyrics, string model, string version, CancellationToken token = default, CurrentTrack? track = null, string scope = "api-key")
    {
        await using var connection = Connection(); await connection.OpenAsync(token);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT json FROM insight_cache WHERE cache_key=$key;";
        command.Parameters.AddWithValue("$key", CacheIdentity.Analysis(lyrics, model, version, scope));
        var json = await command.ExecuteScalarAsync(token) as string;
        var insight = json is null ? null : InsightValidation.Parse(lyrics.TrackRevision, lyrics.Text, json) with { Model = model, PromptVersion = version };
        if (insight is not null && track is not null)
        {
            Validate(track, lyrics);
            using var link = Command(connection, transaction, "INSERT OR IGNORE INTO song_insights SELECT signature,$key FROM song_cache WHERE signature=$signature;", ("$key", CacheIdentity.Analysis(lyrics, model, version, scope)), ("$signature", CacheIdentity.Song(track)));
            link.ExecuteNonQuery();
        }
        token.ThrowIfCancellationRequested(); transaction.Commit(); return insight;
    }
    public async Task SaveInsightAsync(CurrentTrack track, ResolvedLyrics lyrics, string model, string version, SongInsight insight, CancellationToken token = default, string scope = "api-key")
    {
        Validate(track, lyrics);
        if (lyrics.IsInstrumental || insight.TrackRevision != track.Revision || !AiSettingsRules.IsValidModel(model) || string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("Resultado incompatible.");
        if (insight.Model is not null && insight.Model != model || insight.PromptVersion is not null && insight.PromptVersion != version)
            throw new InvalidOperationException("El resultado pertenece a otro modelo o versión.");
        var json = CacheIdentity.Serialize(insight);
        _ = InsightValidation.Parse(track.Revision, lyrics.Text, json);
        await using var connection = Connection(); await connection.OpenAsync(token);
        using var transaction = connection.BeginTransaction(deferred: false);
        Upsert(connection, transaction, track, lyrics);
        var key = CacheIdentity.Analysis(lyrics, model, version, scope);
        using var write = Command(connection, transaction, """
            INSERT INTO insight_cache VALUES ($key,$json,$created) ON CONFLICT(cache_key) DO UPDATE SET json=$json,created_at=$created;
            INSERT OR IGNORE INTO song_insights VALUES ($signature,$key);
            """, ("$key", key), ("$json", json), ("$created", Now), ("$signature", CacheIdentity.Song(track)));
        write.ExecuteNonQuery(); Cleanup(connection, transaction);
        token.ThrowIfCancellationRequested(); transaction.Commit();
    }
    public async Task DeleteSongAsync(CurrentTrack track, CancellationToken token = default) => await DeleteAsync(CacheIdentity.Song(track), token);
    public async Task ClearAsync(CancellationToken token = default) => await DeleteAsync(null, token);
    private async Task DeleteAsync(string? signature, CancellationToken token)
    {
        await using var connection = Connection(); await connection.OpenAsync(token);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var delete = Command(connection, transaction, signature is null ? "DELETE FROM song_cache;" : "DELETE FROM song_cache WHERE signature=$signature;", ("$signature", signature));
        delete.ExecuteNonQuery(); Cleanup(connection, transaction);
        token.ThrowIfCancellationRequested(); transaction.Commit();
    }
}
