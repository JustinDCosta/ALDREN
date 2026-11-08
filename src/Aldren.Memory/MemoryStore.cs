using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Aldren.Memory;

public sealed record MemoryEntry(long Id, string Text, string Kind, DateTimeOffset Created);

/// <summary>
/// Long-term memory in <c>aldren.db</c> (ARCHITECTURE §14.2): a table plus an FTS5 keyword index. Only what you ask ALDREN
/// to remember is stored; nothing here is ever sent off the PC.
/// </summary>
public sealed class MemoryStore(string databasePath)
{
    public const string Fact = "fact";
    public const string Profile = "profile";

    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        DefaultTimeout = 5,
    }.ToString();

    private readonly SemaphoreSlim _init = new(1, 1);
    private bool _ready;

    public async Task<MemoryEntry> AddAsync(string text, string kind, CancellationToken ct)
    {
        text = text.Trim();
        if (SensitiveDetector.Check(text) is { } reason)
        {
            throw new InvalidOperationException($"I won't store that: it looks like {reason}. Sensitive details stay out of my memory.");
        }

        await using var db = await OpenAsync(ct);
        var created = DateTimeOffset.UtcNow;
        await using var insert = db.CreateCommand();
        insert.CommandText = """
            INSERT INTO memories (text, kind, created_utc) VALUES ($text, $kind, $created);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$text", text);
        insert.Parameters.AddWithValue("$kind", kind);
        insert.Parameters.AddWithValue("$created", created.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        var id = (long)(await insert.ExecuteScalarAsync(ct))!;
        await using var index = db.CreateCommand();
        index.CommandText = "INSERT INTO memories_fts (rowid, text) VALUES ($id, $text);";
        index.Parameters.AddWithValue("$id", id);
        index.Parameters.AddWithValue("$text", text);
        await index.ExecuteNonQueryAsync(ct);
        return new MemoryEntry(id, text, kind, created);
    }

    /// <summary>Keyword matches, best first; with no usable words, the most recent memories.</summary>
    public async Task<IReadOnlyList<MemoryEntry>> SearchAsync(string? query, int limit, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        var match = FtsQuery(query);
        if (match is null)
        {
            command.CommandText = "SELECT id, text, kind, created_utc FROM memories ORDER BY id DESC LIMIT $limit;";
        }
        else
        {
            command.CommandText = """
                SELECT m.id, m.text, m.kind, m.created_utc
                FROM memories_fts f JOIN memories m ON m.id = f.rowid
                WHERE memories_fts MATCH $match
                ORDER BY bm25(memories_fts), m.id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$match", match);
        }

        command.Parameters.AddWithValue("$limit", limit);
        return await ReadAsync(command, ct);
    }

    public async Task<IReadOnlyList<MemoryEntry>> ProfileAsync(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT id, text, kind, created_utc FROM memories WHERE kind = $kind ORDER BY id DESC LIMIT 10;";
        command.Parameters.AddWithValue("$kind", Profile);
        return await ReadAsync(command, ct);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "DELETE FROM memories WHERE id = $id; DELETE FROM memories_fts WHERE rowid = $id;";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    private static async Task<IReadOnlyList<MemoryEntry>> ReadAsync(SqliteCommand command, CancellationToken ct)
    {
        var list = new List<MemoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new MemoryEntry(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)));
        }

        return list;
    }

    /// <summary>Words of three letters or more, quoted and OR'ed; FTS5 syntax characters can't get through.</summary>
    private static string? FtsQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in query.ToLowerInvariant() + " ")
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
            else
            {
                if (current.Length >= 3 && !StopWords.Contains(current.ToString()))
                {
                    words.Add('"' + current.ToString() + '"');
                }

                current.Clear();
            }
        }

        return words.Count == 0 ? null : string.Join(" OR ", words.Distinct());
    }

    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "you", "your", "what", "who", "how", "when", "where", "why", "that", "this", "with", "for", "are", "was",
        "remember", "know", "about", "tell", "can", "could", "would", "should", "have", "has", "had", "does", "did", "please",
        "aldren", "boss", "there", "they", "them", "from", "into", "just", "like",
    ];

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
                        CREATE TABLE IF NOT EXISTS memories (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            text TEXT NOT NULL,
                            kind TEXT NOT NULL,
                            created_utc TEXT NOT NULL);
                        CREATE VIRTUAL TABLE IF NOT EXISTS memories_fts USING fts5(text, tokenize = 'porter unicode61');
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
