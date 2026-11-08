using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Aldren.Windows.Files;

public sealed record FileChange(long Id, string Kind, string From, string To, DateTimeOffset When);

public sealed record DuplicateGroup(long Size, IReadOnlyList<string> Paths);

/// <summary>
/// File changes with safety rails (ARCHITECTURE §11, SECURITY.md): system folders are refused, moves and renames are
/// journaled so they can be undone, deletes go to the Recycle Bin only, nothing is overwritten silently.
/// </summary>
public sealed class FileOperations(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        DefaultTimeout = 5,
    }.ToString();

    private bool _ready;

    /// <summary>Why a path is off limits, or null. Windows, program folders, app data and the drive roots are refused.</summary>
    public static string? Refusal(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "that isn't a valid path";
        }

        var protectedRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        }.Where(p => p.Length > 0);
        if (protectedRoots.Any(root => full.StartsWith(root.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase) || full.Equals(root, StringComparison.OrdinalIgnoreCase)))
        {
            return "it's a system or app folder, which ALDREN never changes";
        }

        if (Path.GetPathRoot(full) is { } root && full.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            return "it's a whole drive";
        }

        return full.TrimEnd('\\').Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            ? "it's your whole user folder"
            : null;
    }

    public async Task<FileChange> MoveAsync(string from, string toFolder, CancellationToken ct)
    {
        Check(from, mustExist: true);
        Check(toFolder, mustExist: false);
        Directory.CreateDirectory(toFolder);
        var target = Path.Combine(toFolder, Path.GetFileName(from.TrimEnd('\\')));
        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new InvalidOperationException($"\"{Path.GetFileName(target)}\" already exists there; I won't overwrite it.");
        }

        Move(from, target);
        return await JournalAsync("move", from, target, ct);
    }

    public async Task<FileChange> RenameAsync(string path, string newName, CancellationToken ct)
    {
        Check(path, mustExist: true);
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName.Trim().Length == 0)
        {
            throw new InvalidOperationException($"\"{newName}\" isn't a valid file name.");
        }

        var target = Path.Combine(Path.GetDirectoryName(path.TrimEnd('\\'))!, newName.Trim());
        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new InvalidOperationException($"\"{newName}\" already exists; I won't overwrite it.");
        }

        Move(path, target);
        return await JournalAsync("rename", path, target, ct);
    }

    /// <summary>To the Recycle Bin, never permanently.</summary>
    public async Task<FileChange> RecycleAsync(string path, CancellationToken ct)
    {
        Check(path, mustExist: true);
        var op = new ShFileOpStruct
        {
            Func = FoDelete,
            From = Path.GetFullPath(path) + "\0\0",
            Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi,
        };
        var result = SHFileOperation(ref op);
        if (result != 0 || op.AnyOperationsAborted || File.Exists(path) || Directory.Exists(path))
        {
            throw new InvalidOperationException($"Windows couldn't move it to the Recycle Bin (code {result}).");
        }

        return await JournalAsync("recycle", path, "Recycle Bin", ct);
    }

    /// <summary>Undoes the last move or rename that hasn't been undone. A recycled file is restored from the Recycle Bin by you.</summary>
    public async Task<string> UndoLastAsync(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var find = db.CreateCommand();
        find.CommandText = "SELECT id, kind, from_path, to_path FROM file_changes WHERE undone = 0 ORDER BY id DESC LIMIT 1;";
        await using var reader = await find.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return "There's nothing to undo.";
        }

        var (id, kind, from, to) = (reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
        await reader.DisposeAsync();
        if (kind == "recycle")
        {
            return $"\"{Path.GetFileName(from)}\" is in the Recycle Bin: open the Recycle Bin and choose Restore to bring it back.";
        }

        if (!File.Exists(to) && !Directory.Exists(to))
        {
            return $"\"{Path.GetFileName(to)}\" isn't where I left it any more, so I can't undo that.";
        }

        if (File.Exists(from) || Directory.Exists(from))
        {
            return $"Something called \"{Path.GetFileName(from)}\" is back at the original place, so I won't overwrite it.";
        }

        Move(to, from);
        await using var mark = db.CreateCommand();
        mark.CommandText = "UPDATE file_changes SET undone = 1 WHERE id = $id;";
        mark.Parameters.AddWithValue("$id", id);
        await mark.ExecuteNonQueryAsync(ct);
        return $"Undone: \"{Path.GetFileName(from)}\" is back in {Path.GetDirectoryName(from)}.";
    }

    /// <summary>Files with the same content in a folder: grouped by size first, then by SHA-256. Read-only.</summary>
    public static async Task<IReadOnlyList<DuplicateGroup>> DuplicatesAsync(string folder, CancellationToken ct)
    {
        if (!Directory.Exists(folder))
        {
            throw new InvalidOperationException("That folder doesn't exist.");
        }

        var files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 })
            .Select(f => new FileInfo(f)).Where(f => f.Length > 0).Take(20_000).ToList();
        var groups = new List<DuplicateGroup>();
        foreach (var sameSize in files.GroupBy(f => f.Length).Where(g => g.Count() > 1))
        {
            var byHash = new Dictionary<string, List<string>>();
            foreach (var file in sameSize)
            {
                ct.ThrowIfCancellationRequested();
                await using var stream = File.OpenRead(file.FullName);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                (byHash.TryGetValue(hash, out var list) ? list : byHash[hash] = []).Add(file.FullName);
            }

            groups.AddRange(byHash.Values.Where(v => v.Count > 1).Select(v => new DuplicateGroup(sameSize.Key, v)));
        }

        return groups.OrderByDescending(g => g.Size * (g.Paths.Count - 1)).ToList();
    }

    private static void Check(string path, bool mustExist)
    {
        if (Refusal(path) is { } reason)
        {
            throw new InvalidOperationException($"I won't touch {path}: {reason}.");
        }

        if (mustExist && !File.Exists(path) && !Directory.Exists(path))
        {
            throw new InvalidOperationException($"{path} doesn't exist.");
        }
    }

    private static void Move(string from, string to)
    {
        if (Directory.Exists(from))
        {
            Directory.Move(from, to);
        }
        else
        {
            File.Move(from, to, overwrite: false);
        }
    }

    private async Task<FileChange> JournalAsync(string kind, string from, string to, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var insert = db.CreateCommand();
        var now = DateTimeOffset.UtcNow;
        insert.CommandText = "INSERT INTO file_changes (kind, from_path, to_path, ts_utc, undone) VALUES ($k, $f, $t, $ts, 0); SELECT last_insert_rowid();";
        insert.Parameters.AddWithValue("$k", kind);
        insert.Parameters.AddWithValue("$f", from);
        insert.Parameters.AddWithValue("$t", to);
        insert.Parameters.AddWithValue("$ts", now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        return new FileChange((long)(await insert.ExecuteScalarAsync(ct))!, kind, from, to, now);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);
        if (!_ready)
        {
            await using var create = db.CreateCommand();
            create.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS file_changes (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, from_path TEXT NOT NULL, to_path TEXT NOT NULL,
                    ts_utc TEXT NOT NULL, undone INTEGER NOT NULL DEFAULT 0);
                """;
            await create.ExecuteNonQueryAsync(ct);
            _ready = true;
        }

        return db;
    }

    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x0004, FofNoConfirmation = 0x0010, FofAllowUndo = 0x0040, FofNoErrorUi = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public nint Hwnd;
        public uint Func;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;
        public nint NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShFileOpStruct op);
}
