using System.Runtime.InteropServices;

namespace Aldren.App.Island.Native;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;

    public RECT(int left, int top, int right, int bottom) => (Left, Top, Right, Bottom) = (left, top, right, bottom);

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
    public readonly bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    public readonly RECT Inflate(int by) => new(Left - by, Top - by, Right + by, Bottom + by);
    public override readonly string ToString() => $"({Left},{Top})–({Right},{Bottom}) {Width}×{Height}";
}

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X, Y;
}

/// <summary>Window styles, capture exclusion, click-through, topmost, hotkeys and subclassing for the overlay.</summary>
internal static class WindowStyles
{
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_NCCALCSIZE = 0x0083;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_POINTERDOWN = 0x0246;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_DWMCOMPOSITIONCHANGED = 0x031E;
    public const uint WM_CLOSE = 0x0010;
    public const nint HTTRANSPARENT = -1;
    public const nint MA_NOACTIVATE = 3;

    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_POPUP = 0x80000000L;
    private const long WS_BORDER = 0x00800000L, WS_DLGFRAME = 0x00400000L, WS_THICKFRAME = 0x00040000L, WS_SYSMENU = 0x00080000L;
    private const long WS_MINIMIZEBOX = 0x00020000L, WS_MAXIMIZEBOX = 0x00010000L;
    private const long WS_EX_DLGMODALFRAME = 0x1L, WS_EX_TOPMOST = 0x8L, WS_EX_TRANSPARENT = 0x20L, WS_EX_TOOLWINDOW = 0x80L;
    private const long WS_EX_WINDOWEDGE = 0x100L, WS_EX_CLIENTEDGE = 0x200L, WS_EX_STATICEDGE = 0x20000L;
    private const long WS_EX_APPWINDOW = 0x40000L, WS_EX_LAYERED = 0x80000L, WS_EX_NOACTIVATE = 0x08000000L;
    private const long FrameStyles = WS_BORDER | WS_DLGFRAME | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
    private const long FrameExStyles = WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE | WS_EX_APPWINDOW;
    private const uint LWA_ALPHA = 0x2;
    private const uint WDA_NONE = 0x0, WDA_EXCLUDEFROMCAPTURE = 0x11;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_DONOTROUND = 1;
    private const int DWMWA_BORDER_COLOR = 34;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
    private static readonly nint HWND_TOPMOST = -1, HWND_NOTOPMOST = -2, HWND_TOP = 0;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
    private const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;

    public delegate nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData);

    /// <summary>
    /// Popup without frame, never activated, kept out of Alt+Tab and the taskbar, always on top, and layered so it can
    /// be made click-through. A layered window that never sets its attributes is invisible, hence alpha 255.
    /// </summary>
    public static void ApplyOverlay(nint hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GWL_STYLE) & ~FrameStyles;
        SetWindowLongPtr(hwnd, GWL_STYLE, style | WS_POPUP);

        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE) & ~FrameExStyles;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_LAYERED);
        SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);

        // No Windows 11 rounded corners or accent border around the (invisible) window rectangle.
        var corner = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        var border = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(uint));

        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    /// <summary>Layered + transparent windows let every mouse event through to whatever is underneath.</summary>
    public static void SetClickThrough(nint hwnd, bool on)
    {
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, on ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT);
    }

    public static bool IsClickThrough(nint hwnd)
    {
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        return (ex & (WS_EX_LAYERED | WS_EX_TRANSPARENT)) == (WS_EX_LAYERED | WS_EX_TRANSPARENT);
    }

    public static string Describe(nint hwnd) =>
        $"style 0x{GetWindowLongPtr(hwnd, GWL_STYLE):X8}, exstyle 0x{GetWindowLongPtr(hwnd, GWL_EXSTYLE):X8}";

    public static bool SetCaptureExcluded(nint hwnd, bool excluded) =>
        SetWindowDisplayAffinity(hwnd, excluded ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);

    public static void ShowNoActivate(nint hwnd)
    {
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        BringToTopmost(hwnd);
    }

    public static void Hide(nint hwnd) => ShowWindow(hwnd, SW_HIDE);

    public static void BringToTopmost(nint hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public static void SetTopmost(nint hwnd, bool topmost) =>
        SetWindowPos(hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public static void MoveTo(nint hwnd, RECT r) =>
        SetWindowPos(hwnd, HWND_TOP, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);

    public static RECT WindowRect(nint hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return r;
    }

    public static bool IsHotkeyRegistered(nint hwnd, int id, uint modifiers, uint vk) =>
        RegisterHotKey(hwnd, id, modifiers | MOD_NOREPEAT, vk);

    public static List<nint> Children(nint hwnd)
    {
        var children = new List<nint>();
        EnumChildWindows(hwnd, (child, _) =>
        {
            children.Add(child);
            return true;
        }, 0);
        return children;
    }

    private delegate bool EnumProc(nint hwnd, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, EnumProc proc, nint data);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern long SetWindowLongPtr(nint hwnd, int index, long value);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref uint value, int size);

    [DllImport("comctl32.dll")]
    public static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint refData);

    [DllImport("comctl32.dll")]
    public static extern nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);
}
