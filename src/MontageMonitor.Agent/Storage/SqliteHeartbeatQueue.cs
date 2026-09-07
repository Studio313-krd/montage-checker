using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using MontageMonitor.Agent.Abstractions;
using MontageMonitor.Agent.Configuration;
using MontageMonitor.Shared.Contracts.Agent;

namespace MontageMonitor.Agent.Storage;

internal sealed class SqliteHeartbeatQueue : ILocalEventQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteHeartbeatQueue()
        : this(AgentPaths.QueueDatabase)
    {
    }

    internal SqliteHeartbeatQueue(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                CREATE TABLE IF NOT EXISTS outgoing_heartbeats (
                    event_id TEXT PRIMARY KEY,
                    payload TEXT NOT NULL,
                    created_at_unix_ms INTEGER NOT NULL,
                    attempt_count INTEGER NOT NULL DEFAULT 0,
                    next_attempt_at_unix_ms INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_outgoing_heartbeats_ready
                    ON outgoing_heartbeats(next_attempt_at_unix_ms, created_at_unix_ms);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnqueueAsync(HeartbeatRequest heartbeat, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO outgoing_heartbeats (
                    event_id, payload, created_at_unix_ms, attempt_count, next_attempt_at_unix_ms)
                VALUES ($event_id, $payload, $created_at, 0, $created_at);
                """;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            command.Parameters.AddWithValue("$event_id", heartbeat.EventId.ToString());
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(heartbeat, JsonOptions));
            command.Parameters.AddWithValue("$created_at", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<QueuedHeartbeat>> GetReadyAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT event_id, payload, attempt_count
                FROM outgoing_heartbeats
                WHERE next_attempt_at_unix_ms <= $now
                ORDER BY created_at_unix_ms
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
            var result = new List<QueuedHeartbeat>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var payload = JsonSerializer.Deserialize<HeartbeatRequest>(reader.GetString(1), JsonOptions);
                if (payload is not null)
                {
                    result.Add(new QueuedHeartbeat(
                        Guid.Parse(reader.GetString(0)),
                        payload,
                        reader.GetInt32(2)));
                }
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task MarkSentAsync(Guid eventId, CancellationToken cancellationToken) =>
        ExecuteByIdAsync(
            "DELETE FROM outgoing_heartbeats WHERE event_id = $event_id;",
            eventId,
            null,
            cancellationToken);

    public Task MarkFailedAsync(
        Guid eventId,
        int previousAttemptCount,
        CancellationToken cancellationToken)
    {
        var delaySeconds = Math.Min(300, 5 * Math.Pow(2, Math.Min(previousAttemptCount, 6)));
        var nextAttempt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds).ToUnixTimeMilliseconds();
        return ExecuteByIdAsync(
            """
            UPDATE outgoing_heartbeats
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
            command.CommandText = "SELECT COUNT(*) FROM outgoing_heartbeats;";
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
            command.CommandText = "DELETE FROM outgoing_heartbeats;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExecuteByIdAsync(
        string sql,
        Guid eventId,
        long? nextAttempt,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$event_id", eventId.ToString());
            if (nextAttempt.HasValue)
            {
                command.Parameters.AddWithValue("$next_attempt", nextAttempt.Value);
            }

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
}
