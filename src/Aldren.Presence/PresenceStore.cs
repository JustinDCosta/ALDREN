using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Aldren.Presence;

/// <summary>Home network fingerprints (hashed, ADR-0010) and a few presence values that must survive restarts.</summary>
public sealed class PresenceStore(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        DefaultTimeout = 5,
    }.ToString();

    private readonly SemaphoreSlim _init = new(1, 1);
    private bool _ready;

    public async Task<IReadOnlySet<string>> HomeFingerprintsAsync(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT fingerprint FROM home_networks;";
        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            set.Add(reader.GetString(0));
        }

        return set;
    }

    public async Task AddHomeAsync(IEnumerable<string> fingerprints, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        foreach (var fingerprint in fingerprints)
        {
            await using var command = db.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO home_networks (fingerprint, added_utc) VALUES ($f, $now);";
            command.Parameters.AddWithValue("$f", fingerprint);
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task ClearHomeAsync(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "DELETE FROM home_networks;";
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT value FROM presence_state WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    public async Task SetAsync(string key, string? value, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = value is null
            ? "DELETE FROM presence_state WHERE key = $key;"
            : "INSERT INTO presence_state (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        command.Parameters.AddWithValue("$key", key);
        if (value is not null)
        {
            command.Parameters.AddWithValue("$value", value);
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);
        if (!_ready)
        {
            await _init.WaitAsync(ct);
            try
            {
                if (!_ready)
                {
                    await using var create = db.CreateCommand();
                    create.CommandText = """
                        PRAGMA journal_mode = WAL;
                        CREATE TABLE IF NOT EXISTS home_networks (fingerprint TEXT PRIMARY KEY, added_utc TEXT NOT NULL);
                        CREATE TABLE IF NOT EXISTS presence_state (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                        """;
                    await create.ExecuteNonQueryAsync(ct);
                    _ready = true;
                }
            }
            finally
            {
                _init.Release();
            }
        }

        return db;
    }
}
