using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Agent.Configuration;
using MontageMonitor.Shared.Contracts.Agent;

namespace MontageMonitor.Agent.Storage;

internal sealed class SqliteScreenshotQueue : ILocalScreenshotQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = AgentPaths.QueueDatabase,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AgentPaths.DataDirectory);
        Directory.CreateDirectory(AgentPaths.ScreenshotQueueDirectory);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                CREATE TABLE IF NOT EXISTS outgoing_screenshots (
                    event_id TEXT PRIMARY KEY,
                    metadata TEXT NOT NULL,
                    local_file_path TEXT NOT NULL,
                    created_at_unix_ms INTEGER NOT NULL,
                    attempt_count INTEGER NOT NULL DEFAULT 0,
                    next_attempt_at_unix_ms INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_outgoing_screenshots_ready
                    ON outgoing_screenshots(next_attempt_at_unix_ms, created_at_unix_ms);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnqueueAsync(
        ScreenshotUploadMetadata metadata,
        ReadOnlyMemory<byte> jpegData,
        CancellationToken cancellationToken)
    {
        var finalPath = Path.Combine(AgentPaths.ScreenshotQueueDirectory, metadata.EventId.ToString("N") + ".jpg");
        var temporaryPath = finalPath + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, jpegData, cancellationToken);
        File.Move(temporaryPath, finalPath, true);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO outgoing_screenshots (
                    event_id, metadata, local_file_path, created_at_unix_ms,
                    attempt_count, next_attempt_at_unix_ms)
                VALUES ($event_id, $metadata, $path, $created_at, 0, $created_at);
                """;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            command.Parameters.AddWithValue("$event_id", metadata.EventId.ToString());
            command.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(metadata, JsonOptions));
            command.Parameters.AddWithValue("$path", finalPath);
            command.Parameters.AddWithValue("$created_at", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            TryDelete(finalPath);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<QueuedScreenshot>> GetReadyAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT event_id, metadata, local_file_path, attempt_count
                FROM outgoing_screenshots
                WHERE next_attempt_at_unix_ms <= $now
                ORDER BY created_at_unix_ms
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 20));
            var result = new List<QueuedScreenshot>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var metadata = JsonSerializer.Deserialize<ScreenshotUploadMetadata>(reader.GetString(1), JsonOptions);
                if (metadata is not null)
                {
                    result.Add(new QueuedScreenshot(
                        Guid.Parse(reader.GetString(0)),
                        metadata,
                        reader.GetString(2),
                        reader.GetInt32(3)));
                }
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkSentAsync(Guid eventId, CancellationToken cancellationToken)
    {
        string? localPath = null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var select = connection.CreateCommand())
            {
                select.Transaction = (SqliteTransaction)transaction;
                select.CommandText = "SELECT local_file_path FROM outgoing_screenshots WHERE event_id = $event_id;";
                select.Parameters.AddWithValue("$event_id", eventId.ToString());
                localPath = (string?)await select.ExecuteScalarAsync(cancellationToken);
            }

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = (SqliteTransaction)transaction;
                delete.CommandText = "DELETE FROM outgoing_screenshots WHERE event_id = $event_id;";
                delete.Parameters.AddWithValue("$event_id", eventId.ToString());
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        if (localPath is not null)
        {
            TryDelete(localPath);
        }
    }

    public Task MarkFailedAsync(
        Guid eventId,
        int previousAttemptCount,
        CancellationToken cancellationToken)
    {
        var delaySeconds = Math.Min(300, 10 * Math.Pow(2, Math.Min(previousAttemptCount, 5)));
        var nextAttempt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds).ToUnixTimeMilliseconds();
        return ExecuteAsync(
            """
            UPDATE outgoing_screenshots
            SET attempt_count = attempt_count + 1,
                next_attempt_at_unix_ms = $next_attempt
            WHERE event_id = $event_id;
            """,
            eventId,
            nextAttempt,
            cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM outgoing_screenshots;";
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM outgoing_screenshots;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        if (Directory.Exists(AgentPaths.ScreenshotQueueDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(AgentPaths.ScreenshotQueueDirectory, "*.jpg"))
            {
                TryDelete(file);
            }
        }
    }

    private async Task ExecuteAsync(
        string sql,
        Guid eventId,
        long nextAttempt,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$event_id", eventId.ToString());
            command.Parameters.AddWithValue("$next_attempt", nextAttempt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Запись уже удалена: оставшийся файл можно безопасно очистить при обслуживании профиля.
        }
    }
}
