namespace Aldren.Bench.Gateway;

public record CanaryResult(bool Passed, IReadOnlyList<string> FoundIn, long BytesScanned, IReadOnlyList<string> Problems);

/// <summary>
/// Searches the gateway's database (with its -wal and -shm files) and logs for a canary. A missing or stale database is a
/// problem, never a pass: the bench resolves %LOCALAPPDATA% in its own context, which can differ from the gateway's.
/// </summary>
public static class CanaryScan
{
    public static CanaryResult Scan(string databasePath, IReadOnlyList<string> otherFiles, byte[] needle, DateTime sentUtc)
    {
        var problems = new List<string>();
        var found = new List<string>();
        long scanned = 0;

        if (!File.Exists(databasePath))
        {
            problems.Add($"no database at {databasePath}");
        }
        else
        {
            var written = new[] { databasePath, databasePath + "-wal" }.Where(File.Exists).Max(File.GetLastWriteTimeUtc);
            if (written < sentUtc)
            {
                problems.Add($"{Path.GetFileName(databasePath)} not written since the canary was sent (wrong file?)");
            }
        }

        foreach (var file in otherFiles.Where(f => !File.Exists(f)))
        {
            problems.Add($"no file at {file}");
        }

        foreach (var file in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" }.Concat(otherFiles).Where(File.Exists))
        {
            var bytes = ReadShared(file);
            scanned += bytes.Length;
            if (bytes.AsSpan().IndexOf(needle) >= 0)
            {
                found.Add(Path.GetFileName(file));
            }
        }

        return new CanaryResult(problems.Count == 0 && found.Count == 0, found, scanned, problems);
    }

    private static byte[] ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
