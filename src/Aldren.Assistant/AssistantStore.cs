using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Aldren.Assistant;

/// <param name="Due">The next time it fires (UTC).</param>
public sealed record Reminder(long Id, string Text, Repeat Repeat, TimeOnly? LocalTime, DayOfWeek? Day, DateTimeOffset Due)
{
    public Repeating? AsRepeating => Repeat == Repeat.Once || LocalTime is null ? null : new Repeating(LocalTime.Value, Repeat, Day);
}

public sealed record TodoItem(long Id, string Text, bool Done, DateTimeOffset Created);

/// <summary>
/// Reminders and tasks in <c>aldren.db</c>, the source of truth (ARCHITECTURE §14.1). One-time reminders are stored as UTC
/// instants; recurring ones as local wall-clock time and the time zone, with the next instant precomputed.
/// </summary>
public sealed class AssistantStore(string databasePath, TimeProvider time)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        DefaultTimeout = 5,
    }.ToString();

    private readonly SemaphoreSlim _init = new(1, 1);
    private bool _ready;

    public TimeZoneInfo Zone => time.LocalTimeZone;

    /// <summary>The reminder that went off last, for "snooze" (kept in memory).</summary>
    public Reminder? LastFired { get; set; }

    public DateTime NowLocal => time.GetLocalNow().DateTime;

    public async Task<Reminder> AddReminderAsync(string text, When when, CancellationToken ct)
    {
        var (repeat, localTime, day, due) = when switch
        {
            AtLocal at => (Repeat.Once, (TimeOnly?)null, (DayOfWeek?)null, DateParser.ToInstant(at.Local, Zone)),
            Repeating r => (r.Repeat, r.Time, r.Day, DateParser.NextOccurrence(r, NowLocal, Zone)),
            _ => throw new ArgumentException("Only a resolved time can be stored.", nameof(when)),
        };

        await using var db = await OpenAsync(ct);
        await using var insert = db.CreateCommand();
        insert.CommandText = """
            INSERT INTO reminders (text, repeat, local_time, weekday, due_utc, tz, status, created_utc)
            VALUES ($text, $repeat, $local, $weekday, $due, $tz, 'active', $created);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$text", text);
        insert.Parameters.AddWithValue("$repeat", repeat.ToString());
        insert.Parameters.AddWithValue("$local", localTime is { } t ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : DBNull.Value);
        insert.Parameters.AddWithValue("$weekday", day is { } d ? (int)d : DBNull.Value);
        insert.Parameters.AddWithValue("$due", Utc(due));
        insert.Parameters.AddWithValue("$tz", Zone.Id);
        insert.Parameters.AddWithValue("$created", Utc(time.GetUtcNow()));
        var id = (long)(await insert.ExecuteScalarAsync(ct))!;
        return new Reminder(id, text, repeat, localTime, day, due);
    }

    public async Task<IReadOnlyList<Reminder>> ActiveRemindersAsync(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT id, text, repeat, local_time, weekday, due_utc FROM reminders WHERE status = 'active' ORDER BY due_utc;";
        return await ReadRemindersAsync(command, ct);
    }

    public async Task<IReadOnlyList<Reminder>> DueRemindersAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT id, text, repeat, local_time, weekday, due_utc FROM reminders WHERE status = 'active' AND due_utc <= $now ORDER BY due_utc;";
        command.Parameters.AddWithValue("$now", Utc(now));
        return await ReadRemindersAsync(command, ct);
    }

    /// <summary>After a reminder fires: one-time ones are done, recurring ones move to their next occurrence.</summary>
    public async Task MarkFiredAsync(Reminder reminder, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        if (reminder.AsRepeating is { } r)
        {
            var next = DateParser.NextOccurrence(r, NowLocal, Zone);
            command.CommandText = "UPDATE reminders SET due_utc = $due, fired_utc = $now WHERE id = $id;";
            command.Parameters.AddWithValue("$due", Utc(next));
        }
        else
        {
            command.CommandText = "UPDATE reminders SET status = 'done', fired_utc = $now WHERE id = $id;";
        }

        command.Parameters.AddWithValue("$now", Utc(time.GetUtcNow()));
        command.Parameters.AddWithValue("$id", reminder.Id);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> CancelReminderAsync(long id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "UPDATE reminders SET status = 'cancelled' WHERE id = $id AND status = 'active';";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>Snooze: a one-time copy of the reminder, <paramref name="delay"/> from now.</summary>
    public Task<Reminder> SnoozeAsync(string text, TimeSpan delay, CancellationToken ct) =>
        AddReminderAsync(text, new AtLocal(NowLocal + delay), ct);

    public async Task<TodoItem> AddTaskAsync(string text, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var insert = db.CreateCommand();
        var created = time.GetUtcNow();
        insert.CommandText = "INSERT INTO tasks (text, done, created_utc) VALUES ($text, 0, $created); SELECT last_insert_rowid();";
        insert.Parameters.AddWithValue("$text", text);
        insert.Parameters.AddWithValue("$created", Utc(created));
        return new TodoItem((long)(await insert.ExecuteScalarAsync(ct))!, text, false, created);
    }

    public async Task<IReadOnlyList<TodoItem>> OpenTasksAsync(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT id, text, done, created_utc FROM tasks WHERE done = 0 ORDER BY id;";
        var list = new List<TodoItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TodoItem(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2) != 0, Parse(reader.GetString(3))));
        }

        return list;
    }

    public async Task<bool> CompleteTaskAsync(long id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "UPDATE tasks SET done = 1, done_utc = $now WHERE id = $id AND done = 0;";
        command.Parameters.AddWithValue("$now", Utc(time.GetUtcNow()));
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>The closest text match among items, by shared words; null when nothing shares a word.</summary>
    public static T? BestMatch<T>(IEnumerable<T> items, Func<T, string> text, string query)
    {
        var words = Words(query);
        return items
            .Select(i => (Item: i, Score: Words(text(i)).Intersect(words).Count()))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Item)
            .FirstOrDefault();
    }

    private static HashSet<string> Words(string text) =>
        text.ToLowerInvariant().Split((char[])[' ', ',', '.', '!', '?', '\'', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && w is not "the" and not "and" and not "for" and not "reminder" and not "task" and not "remind")
            .ToHashSet();

    private static async Task<IReadOnlyList<Reminder>> ReadRemindersAsync(SqliteCommand command, CancellationToken ct)
    {
        var list = new List<Reminder>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new Reminder(
                reader.GetInt64(0),
                reader.GetString(1),
                Enum.Parse<Repeat>(reader.GetString(2)),
                reader.IsDBNull(3) ? null : TimeOnly.ParseExact(reader.GetString(3), "HH:mm", CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? null : (DayOfWeek)reader.GetInt64(4),
                Parse(reader.GetString(5))));
        }

        return list;
    }

    // Fixed width, so stored instants sort and compare as text.
    private static string Utc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

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
                        CREATE TABLE IF NOT EXISTS reminders (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            text TEXT NOT NULL,
                            repeat TEXT NOT NULL,
                            local_time TEXT NULL,
                            weekday INTEGER NULL,
                            due_utc TEXT NOT NULL,
                            tz TEXT NOT NULL,
                            status TEXT NOT NULL,
                            created_utc TEXT NOT NULL,
                            fired_utc TEXT NULL);
                        CREATE INDEX IF NOT EXISTS reminders_due ON reminders (status, due_utc);
                        CREATE TABLE IF NOT EXISTS tasks (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            text TEXT NOT NULL,
                            done INTEGER NOT NULL DEFAULT 0,
                            created_utc TEXT NOT NULL,
                            done_utc TEXT NULL);
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
