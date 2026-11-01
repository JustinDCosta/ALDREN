using System.Globalization;
using Aldren.Common.Logging;
using Aldren.Contracts;
using Microsoft.Data.Sqlite;

namespace Aldren.Security.Audit;

/// <summary>The audit log in <c>aldren.db</c>. Arguments are redacted and cut to 2,000 characters before they are stored.</summary>
public sealed class SqliteAuditLog : IAuditLog
{
    public const int MaxArgumentLength = 2_000;

    // Fixed width, so stored timestamps sort and compare as text.
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private readonly string _connectionString;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public SqliteAuditLog(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString();
    }

    public async Task<long> AppendAsync(AuditRecord record, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO audit_log (ts_utc, request_id, tool, tier, origin, arguments, stage, outcome, detail, duration_ms, confirmed, tainted)
            VALUES ($ts, $request, $tool, $tier, $origin, $arguments, $stage, $outcome, $detail, $duration, $confirmed, $tainted);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$ts", FormatTimestamp(record.Timestamp));
        command.Parameters.AddWithValue("$request", record.RequestId);
        command.Parameters.AddWithValue("$tool", record.Tool);
        command.Parameters.AddWithValue("$tier", record.Tier.ToString());
        command.Parameters.AddWithValue("$origin", record.Origin.ToString());
        command.Parameters.AddWithValue("$arguments", Shorten(Redactor.Redact(record.Arguments)));
        command.Parameters.AddWithValue("$stage", record.Stage.ToString());
        command.Parameters.AddWithValue("$outcome", (object?)record.Outcome?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$detail", record.Detail is null ? DBNull.Value : Shorten(Redactor.Redact(record.Detail)));
        command.Parameters.AddWithValue("$duration", record.Duration is { } d ? (long)d.TotalMilliseconds : DBNull.Value);
        command.Parameters.AddWithValue("$confirmed", record.Confirmed ? 1 : 0);
        command.Parameters.AddWithValue("$tainted", record.Tainted ? 1 : 0);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, ts_utc, tool, tier, origin, outcome, detail, duration_ms, confirmed, tainted
            FROM audit_log
            WHERE stage = 'Finished' AND ($before IS NULL OR id < $before)
            ORDER BY id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$before", (object?)query.BeforeId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", query.EffectiveLimit);

        var entries = new List<AuditEntry>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entries.Add(new AuditEntry(
                reader.GetInt64(0),
                ParseTimestamp(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? 0 : (int)Math.Min(reader.GetInt64(7), int.MaxValue),
                reader.GetInt64(8) != 0,
                reader.GetInt64(9) != 0));
        }

        return entries;
    }

    public async Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM audit_log WHERE ts_utc < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", FormatTimestamp(olderThan));
        return await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(ct);
            if (!_initialized)
            {
                await InitializeAsync(connection, ct);
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task InitializeAsync(SqliteConnection connection, CancellationToken ct)
    {
        await _initGate.WaitAsync(ct);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS audit_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ts_utc TEXT NOT NULL,
                    request_id TEXT NOT NULL,
                    tool TEXT NOT NULL,
                    tier TEXT NOT NULL,
                    origin TEXT NOT NULL,
                    arguments TEXT NOT NULL,
                    stage TEXT NOT NULL,
                    outcome TEXT NULL,
                    detail TEXT NULL,
                    duration_ms INTEGER NULL,
                    confirmed INTEGER NOT NULL,
                    tainted INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS audit_log_ts ON audit_log (ts_utc);
                """;
            await command.ExecuteNonQueryAsync(ct);
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private static string Shorten(string text) =>
        text.Length <= MaxArgumentLength ? text : text[..MaxArgumentLength] + "…";

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string text) =>
        new(DateTime.ParseExact(text, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), TimeSpan.Zero);
}
