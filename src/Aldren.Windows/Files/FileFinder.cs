using System.Data.OleDb;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Aldren.Windows.Files;

public sealed record FoundFile(string Path, string Name, DateTimeOffset Modified, long Size);

/// <summary>
/// Finding files (ARCHITECTURE §11): the Windows Search index first (read-only OLE DB query, what Explorer's search box
/// uses), then a bounded walk of your own folders when the index has nothing; Recent items, Downloads and VS Code's recent
/// workspaces. Read-only throughout.
/// </summary>
public static class FileFinder
{
    public static string Downloads => KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B"))
                                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>The folders searched when the index has nothing: Desktop, Documents, Downloads, Pictures, Music, Videos.</summary>
    public static IReadOnlyList<string> UserFolders =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Downloads,
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
    ];

    public static async Task<IReadOnlyList<FoundFile>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => new string(w.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray()))
            .Where(w => w.Length > 0).Take(6).ToList();
        if (words.Count == 0)
        {
            return [];
        }

        try
        {
            var indexed = await Task.Run(() => QueryIndex(words, limit), ct);
            if (indexed.Count > 0)
            {
                return indexed;
            }
        }
        catch (Exception ex) when (ex is OleDbException or InvalidOperationException or COMException)
        {
            // Windows Search off or unavailable: fall through to the walk.
        }

        return await Task.Run(() => Walk(words, limit, ct), ct);
    }

    private static List<FoundFile> QueryIndex(List<string> words, int limit)
    {
        // Each word must appear in the file name; values are escaped for the index's SQL dialect (single quotes doubled).
        var conditions = string.Join(" AND ", words.Select(w => $"System.FileName LIKE '%{w.Replace("'", "''")}%'"));
        var sql = $"SELECT TOP {Math.Clamp(limit, 1, 100)} System.ItemPathDisplay, System.FileName, System.DateModified, System.Size " +
                  $"FROM SystemIndex WHERE SCOPE='file:' AND {conditions} ORDER BY System.DateModified DESC";
        using var connection = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
        connection.Open();
        using var command = new OleDbCommand(sql, connection);
        using var reader = command.ExecuteReader();
        var list = new List<FoundFile>();
        while (reader.Read())
        {
            var path = reader.GetValue(0) as string;
            if (path is null || !File.Exists(path))
            {
                continue;
            }

            var modified = reader.GetValue(2) is DateTime d ? new DateTimeOffset(d.ToUniversalTime(), TimeSpan.Zero) : DateTimeOffset.MinValue;
            var size = reader.GetValue(3) is { } s and not DBNull ? Convert.ToInt64(s) : 0;
            list.Add(new FoundFile(path, reader.GetValue(1) as string ?? Path.GetFileName(path), modified, size));
        }

        return list;
    }

    private static List<FoundFile> Walk(List<string> words, int limit, CancellationToken ct)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 5, AttributesToSkip = FileAttributes.System | FileAttributes.Hidden };
        var found = new List<FoundFile>();
        foreach (var folder in UserFolders.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                if (words.All(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                {
                    var info = new FileInfo(file);
                    found.Add(new FoundFile(file, name, info.LastWriteTimeUtc, info.Length));
                    if (found.Count >= limit * 4)
                    {
                        break;
                    }
                }
            }
        }

        return found.OrderByDescending(f => f.Modified).Take(limit).ToList();
    }

    /// <summary>Recent items from Windows' Recent folder (shortcuts resolved to the files they point to).</summary>
    public static IReadOnlyList<FoundFile> Recent(int limit)
    {
        var recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (!Directory.Exists(recent))
        {
            return [];
        }

        return new DirectoryInfo(recent).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(link => ShortcutTarget(link.FullName))
            .Where(target => target is not null && File.Exists(target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(target => { var i = new FileInfo(target!); return new FoundFile(i.FullName, i.Name, i.LastWriteTimeUtc, i.Length); })
            .ToList();
    }

    public static IReadOnlyList<FoundFile> LatestDownloads(int limit) =>
        Directory.Exists(Downloads)
            ? new DirectoryInfo(Downloads).EnumerateFiles().Where(f => !f.Name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) && !f.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.CreationTimeUtc > f.LastWriteTimeUtc ? f.CreationTimeUtc : f.LastWriteTimeUtc).Take(limit)
                .Select(f => new FoundFile(f.FullName, f.Name, f.LastWriteTimeUtc, f.Length)).ToList()
            : [];

    /// <summary>VS Code's recent folders and workspaces, from its own state database (opened read-only).</summary>
    public static IReadOnlyList<string> VsCodeRecent(int limit)
    {
        var db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Code\User\globalStorage\state.vscdb");
        if (!File.Exists(db))
        {
            return [];
        }

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Shared }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM ItemTable WHERE key = 'history.recentlyOpenedPathsList';";
            if (command.ExecuteScalar() is not string json)
            {
                return [];
            }

            using var doc = JsonDocument.Parse(json);
            var paths = new List<string>();
            foreach (var entry in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                var uri = entry.TryGetProperty("folderUri", out var f) ? f.GetString()
                    : entry.TryGetProperty("workspace", out var w) && w.TryGetProperty("configPath", out var c) ? c.GetString()
                    : null;
                if (uri is not null && Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.IsFile)
                {
                    paths.Add(u.LocalPath);
                }
            }

            return paths.Take(limit).ToList();
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }

    private static string? ShortcutTarget(string link)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null)
            {
                return null;
            }

            dynamic shell = Activator.CreateInstance(type)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(link);
                return (string)shortcut.TargetPath is { Length: > 0 } target ? target : null;
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
    }

    private static string? KnownFolder(Guid id) =>
        SHGetKnownFolderPath(id, 0, 0, out var pointer) == 0 ? Marshal.PtrToStringUni(pointer) is var s && FreeCo(pointer) ? s : null : null;

    private static bool FreeCo(nint p)
    {
        Marshal.FreeCoTaskMem(p);
        return true;
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, nint token, out nint path);
}
