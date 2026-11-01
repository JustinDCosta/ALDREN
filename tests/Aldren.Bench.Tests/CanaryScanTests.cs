using System.Text;
using Aldren.Bench.Gateway;

namespace Aldren.Bench.Tests;

public sealed class CanaryScanTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("canary-").FullName;
    private static readonly byte[] Needle = Encoding.UTF8.GetBytes("ALDREN-CANARY-1234");

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Missing_database_is_a_problem_not_a_pass()
    {
        var result = CanaryScan.Scan(Path.Combine(dir, "freeapi.db"), [], Needle, DateTime.UtcNow.AddMinutes(-1));

        Assert.False(result.Passed);
        Assert.Contains(result.Problems, p => p.Contains("freeapi.db", StringComparison.Ordinal));
    }

    [Fact]
    public void Stale_database_is_a_problem()
    {
        var db = Write("freeapi.db", "rows");

        var result = CanaryScan.Scan(db, [], Needle, DateTime.UtcNow.AddMinutes(5));

        Assert.False(result.Passed);
        Assert.NotEmpty(result.Problems);
    }

    [Fact]
    public void Found_canary_is_reported()
    {
        var sent = DateTime.UtcNow.AddMinutes(-1);
        var db = Write("freeapi.db", "rows");
        var log = Write("gateway.log", "prompt was ALDREN-CANARY-1234");

        var result = CanaryScan.Scan(db, [log], Needle, sent);

        Assert.False(result.Passed);
        Assert.Equal(["gateway.log"], result.FoundIn);
    }

    [Fact]
    public void Clean_fresh_scan_passes_and_counts_bytes()
    {
        var sent = DateTime.UtcNow.AddMinutes(-1);
        var db = Write("freeapi.db", "0123456789");
        Write("freeapi.db-wal", "abcde");

        var result = CanaryScan.Scan(db, [], Needle, sent);

        Assert.True(result.Passed);
        Assert.Equal(15, result.BytesScanned);
    }
}
