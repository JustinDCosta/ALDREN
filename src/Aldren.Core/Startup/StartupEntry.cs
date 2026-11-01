using Aldren.Contracts;

namespace Aldren.Core.Startup;

/// <summary>
/// The <c>ALDREN</c> value under the current user's Run key, written only when you switch it on. If you switched
/// ALDREN off in Task Manager or Settings &gt; Apps &gt; Startup, that choice wins: ALDREN reports it and never overrides it.
/// </summary>
public sealed class StartupEntry(IRegistryValues registry)
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string ValueName = "ALDREN";
    public const string StartupFlag = "--startup";

    public StartupState GetState(string executablePath)
    {
        if (registry.Get(RunKey, ValueName) is not string command || !PointsTo(command, executablePath))
        {
            return StartupState.Off;
        }

        // Windows writes 0x02 or 0x06 as the first byte when the entry is enabled, anything else when you turned it off.
        return registry.Get(ApprovedKey, ValueName) is byte[] { Length: > 0 } approved && approved[0] is not (0x02 or 0x06)
            ? StartupState.DisabledInWindows
            : StartupState.On;
    }

    public void Enable(string executablePath) =>
        registry.Set(RunKey, ValueName, Command(executablePath));

    public void Disable() => registry.Delete(RunKey, ValueName);

    public static string Command(string executablePath) => $"\"{Path.GetFullPath(executablePath)}\" {StartupFlag}";

    private static bool PointsTo(string command, string executablePath)
    {
        var trimmed = command.TrimStart();
        string path;
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            if (end < 0) return false;
            path = trimmed[1..end];
        }
        else
        {
            var space = trimmed.IndexOf(' ');
            path = space < 0 ? trimmed : trimmed[..space];
        }

        try
        {
            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
