using System.Runtime.InteropServices;

namespace Aldren.App.Island.Native;

/// <summary>
/// Keyboard focus for the island: it normally never activates, but takes focus while you type or confirm, then hands
/// it back to the window you were in. Added for ALDREN; S2's helpers stay as they were.
/// </summary>
internal static class Focus
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    public static void SetNoActivate(nint hwnd, bool on)
    {
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, on ? ex | WS_EX_NOACTIVATE : ex & ~WS_EX_NOACTIVATE);
    }

    /// <summary>The monitor holding the foreground window, or the primary one.</summary>
    public static Monitor ForegroundMonitor()
    {
        var monitors = Monitors.All();
        var handle = MonitorFromWindow(GetForegroundWindow(), MONITOR_DEFAULTTONEAREST);
        return monitors.FirstOrDefault(m => m.Handle == handle) ?? monitors[0];
    }

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern long SetWindowLongPtr(nint hwnd, int index, long value);
}
