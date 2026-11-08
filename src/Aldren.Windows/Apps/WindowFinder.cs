using System.Diagnostics;
using System.Runtime.InteropServices;


namespace Aldren.Windows.Apps;

public sealed record TopWindow(nint Handle, string Title, int ProcessId, string ProcessName, string Description);

/// <summary>Visible top-level app windows, and focusing or politely closing them. No input is ever sent.</summary>
public static partial class WindowFinder
{
    private const int SwRestore = 9;
    private const uint WmClose = 0x0010;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(nint hwnd, [Out] char[] text, int max);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out int processId);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint hwnd, uint command);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial void SwitchToThisWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool altTab);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Exists(nint hwnd);

    public static IReadOnlyList<TopWindow> List()
    {
        var handles = new List<nint>();
        EnumWindows((hwnd, _) =>
        {
            // Visible, unowned, not a tool window: what Alt+Tab shows.
            if (IsWindowVisible(hwnd) && GetWindow(hwnd, 4 /* GW_OWNER */) == 0
                && (GetWindowLongPtr(hwnd, GwlExStyle) & WsExToolWindow) == 0)
            {
                handles.Add(hwnd);
            }

            return true;
        }, 0);

        var buffer = new char[512];
        var windows = new List<TopWindow>();
        var processes = new Dictionary<int, (string Name, string Description)>();
        foreach (var hwnd in handles)
        {
            var length = GetWindowText(hwnd, buffer, buffer.Length);
            if (length <= 0)
            {
                continue;
            }

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId)
            {
                continue;
            }

            if (!processes.TryGetValue(pid, out var info))
            {
                info = Describe(pid);
                processes[pid] = info;
            }

            windows.Add(new TopWindow(hwnd, new string(buffer, 0, length), pid, info.Name, info.Description));
        }

        return windows;
    }

    /// <summary>The windows that best match a spoken app name, by description, process name, then title.</summary>
    public static IReadOnlyList<TopWindow> Find(string appName)
    {
        var q = AppIndex.Normalize(appName);
        var scored = List()
            .Select(w => (Window: w, Score: new[]
            {
                AppIndex.Score(AppIndex.Normalize(w.Description), q),
                AppIndex.Score(AppIndex.Normalize(w.ProcessName), q),
                AppIndex.Score(AppIndex.Normalize(w.Title), q) * 0.9,
            }.Max()))
            .Where(x => x.Score >= 0.7)
            .ToList();
        if (scored.Count == 0)
        {
            return [];
        }

        var top = scored.Max(x => x.Score);
        return scored.Where(x => x.Score >= top - 0.05).Select(x => x.Window).ToList();
    }

    public static bool Focus(nint hwnd)
    {
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SwRestore);
        }

        if (!SetForegroundWindow(hwnd))
        {
            // A background process may be refused; the shell's switch still works in most cases.
            SwitchToThisWindow(hwnd, true);
        }

        return GetForegroundWindow() == hwnd;
    }

    /// <summary>Asks the window to close, as its close button would; the app may still ask to save.</summary>
    public static bool RequestClose(nint hwnd) => PostMessage(hwnd, WmClose, 0, 0);

    private static (string Name, string Description) Describe(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var name = process.ProcessName;
            string description = name;
            try
            {
                var file = process.MainModule?.FileName;
                if (file is not null)
                {
                    description = FileVersionInfo.GetVersionInfo(file).FileDescription is { Length: > 0 } d ? d : name;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Elevated or protected process: ALDREN never controls those anyway.
            }

            return (name, description);
        }
        catch (ArgumentException)
        {
            return ("", "");
        }
    }

    public static string Describe(TopWindow w) => w.Description.Length > 0 ? w.Description : w.ProcessName;
}
