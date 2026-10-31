namespace Aldren.Bench.Commands;

internal static class Args
{
    /// <summary>Returns the value following <paramref name="name"/>, e.g. "--model x.gguf".</summary>
    public static string? Value(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static bool Flag(string[] args, string name) => Array.IndexOf(args, name) >= 0;
}
