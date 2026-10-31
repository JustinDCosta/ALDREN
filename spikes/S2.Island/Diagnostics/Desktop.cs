using System.Runtime.InteropServices;
using System.Text;
using S2.Island.Native;

namespace S2.Island.Diagnostics;

/// <summary>Input injection, window queries and screen sampling for the automated checklist. Nothing is saved to disk.</summary>
internal static class Desktop
{
    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4;
    private const uint WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E;
    private const uint GA_ROOT = 2;
    private const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    public const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SPACE = 0x20;

    /// <summary>Private working set (what Task Manager's Memory column shows), total working set and private bytes, in MB.</summary>
    public static (double PrivateWorkingSet, double WorkingSet, double PrivateBytes) Memory()
    {
        var counters = new PROCESS_MEMORY_COUNTERS_EX2 { cb = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX2>() };
        GetProcessMemoryInfo(-1, ref counters, counters.cb);
        const double mb = 1024 * 1024;
        return (counters.PrivateWorkingSetSize / mb, counters.WorkingSetSize / mb, counters.PrivateUsage / mb);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS_EX2
    {
        public uint cb, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
        public nuint PrivateUsage, PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
    private static extern bool GetProcessMemoryInfo(nint process, ref PROCESS_MEMORY_COUNTERS_EX2 counters, uint size);

    public static nint Foreground() => GetForegroundWindow();

    public static void TypeChar(char c)
    {
        var inputs = new[]
        {
            Key(0, c, KEYEVENTF_UNICODE),
            Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Sends a full key chord in one call so modifiers can never be left pressed.</summary>
    public static void Chord(params ushort[] keys)
    {
        var inputs = keys.Select(k => Key(k, 0, 0)).Concat(keys.Reverse().Select(k => Key(k, 0, KEYEVENTF_KEYUP))).ToArray();
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        var inputs = new[] { Mouse(MOUSEEVENTF_LEFTDOWN), Mouse(MOUSEEVENTF_LEFTUP) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void MoveCursor(int x, int y) => SetCursorPos(x, y);

    /// <summary>The top-level window that would receive a click at this point (click-through windows are skipped).</summary>
    public static nint RootWindowAt(int x, int y) => GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), GA_ROOT);

    public static string Title(nint hwnd)
    {
        var sb = new StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static nint FindTopLevel(string titlePrefix)
    {
        nint found = 0;
        EnumWindows((hwnd, _) =>
        {
            if (Title(hwnd).StartsWith(titlePrefix, StringComparison.Ordinal))
            {
                found = hwnd;
                return false;
            }

            return true;
        }, 0);
        return found;
    }

    /// <summary>Position in the desktop z-order, 0 = topmost.</summary>
    public static int ZIndex(nint target)
    {
        var index = 0;
        var result = -1;
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == target)
            {
                result = index;
                return false;
            }

            index++;
            return true;
        }, 0);
        return result;
    }

    /// <summary>Text of the first edit control inside a window (WinForms text boxes are real EDIT windows).</summary>
    public static string EditText(nint window)
    {
        nint edit = 0;
        EnumChildWindows(window, (child, _) =>
        {
            var cls = new StringBuilder(256);
            GetClassName(child, cls, cls.Capacity);
            if (cls.ToString().Contains("EDIT", StringComparison.OrdinalIgnoreCase))
            {
                edit = child;
                return false;
            }

            return true;
        }, 0);
        if (edit == 0)
        {
            return "";
        }

        var length = (int)SendMessage(edit, WM_GETTEXTLENGTH, 0, 0);
        var buffer = Marshal.AllocHGlobal((length + 1) * 2);
        try
        {
            SendMessage(edit, WM_GETTEXT, length + 1, buffer);
            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Copies a screen rectangle into memory as 0xAARRGGBB pixels; layered windows included.</summary>
    public static uint[] Capture(RECT r)
    {
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var info = new BITMAPINFOHEADER { biSize = 40, biWidth = r.Width, biHeight = -r.Height, biPlanes = 1, biBitCount = 32 };
        var bitmap = CreateDIBSection(screen, ref info, 0, out var bits, 0, 0);
        var old = SelectObject(memory, bitmap);
        try
        {
            BitBlt(memory, 0, 0, r.Width, r.Height, screen, r.Left, r.Top, SRCCOPY | CAPTUREBLT);
            var raw = new int[r.Width * r.Height];
            Marshal.Copy(bits, raw, 0, raw.Length);
            return Array.ConvertAll(raw, p => (uint)p);
        }
        finally
        {
            SelectObject(memory, old);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    public static bool Near(uint pixel, (int R, int G, int B) colour, int tolerance) =>
        Math.Abs((int)((pixel >> 16) & 0xFF) - colour.R) <= tolerance
        && Math.Abs((int)((pixel >> 8) & 0xFF) - colour.G) <= tolerance
        && Math.Abs((int)(pixel & 0xFF) - colour.B) <= tolerance;

    public static string Rgb(uint pixel) => $"({(pixel >> 16) & 0xFF},{(pixel >> 8) & 0xFF},{pixel & 0xFF})";

    private static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    private static INPUT Mouse(uint flags) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } },
    };

    /// <summary>Records every foreground-window change while it is alive (out-of-context WinEvent hook).</summary>
    public sealed class ForegroundWatch : IDisposable
    {
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private readonly WinEventProc _proc;
        private readonly nint _hook;

        public ForegroundWatch()
        {
            _proc = (_, _, hwnd, _, _, _, _) => Changes.Add(hwnd);
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, _proc, 0, 0, 0);
        }

        public List<nint> Changes { get; } = [];

        public void Dispose() => UnhookWinEvent(_hook);
    }

    private delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hook);

    private delegate bool EnumProc(nint hwnd, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nint dwExtraInfo;
    }

    // Every field must stay blittable: a char here is marshalled as a 1-byte ANSI char, the union stops being copied
    // as raw memory, and the keyboard view overwrites the mouse flags (clicks silently become no-ops).
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder name, int max);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, EnumProc proc, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint hdc, ref BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint dest, int x, int y, int cx, int cy, nint src, int x1, int y1, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);
}
