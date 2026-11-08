using System.Runtime.InteropServices;
using Aldren.Windows.Apps;

namespace Aldren.Windows.Layouts;

public sealed record MonitorInfo(int Index, nint Handle, bool Primary, ScreenRect Bounds, ScreenRect WorkArea);

public readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

public enum Placement
{
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Maximize,
    Minimize,
    Restore,
    Center,
    OtherMonitor,
}

/// <summary>
/// Monitors, window positions and snapping (ARCHITECTURE §11). Coordinates are physical pixels: Core runs per-monitor DPI
/// aware. Elevated windows can't be moved (by design) and the call reports it.
/// </summary>
public static partial class WindowManager
{
    private const int SwMinimize = 6, SwMaximize = 3, SwRestore = 9, SwShowNormal = 1;
    private const uint SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 2;

    public static IReadOnlyList<MonitorInfo> Monitors()
    {
        var handles = new List<nint>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) => { handles.Add(monitor); return true; }, 0);
        var list = new List<MonitorInfo>();
        foreach (var h in handles)
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(h, ref info))
            {
                list.Add(new MonitorInfo(0, h, (info.Flags & 1) != 0, info.Monitor.ToScreen(), info.Work.ToScreen()));
            }
        }

        // Primary first, then left to right: "monitor 2" means the same screen every time.
        return list.OrderByDescending(m => m.Primary).ThenBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top)
            .Select((m, i) => m with { Index = i + 1 }).ToList();
    }

    /// <summary>"2:1920x1080+1920x1080": the monitor configuration layouts are saved for.</summary>
    public static string ConfigurationKey()
    {
        var monitors = Monitors();
        return $"{monitors.Count}:{string.Join('+', monitors.Select(m => $"{m.Bounds.Width}x{m.Bounds.Height}"))}";
    }

    public static MonitorInfo MonitorOf(nint hwnd)
    {
        var handle = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var all = Monitors();
        return all.FirstOrDefault(m => m.Handle == handle) ?? all[0];
    }

    public static ScreenRect Bounds(nint hwnd) => GetWindowRect(hwnd, out var r) ? r.ToScreen() : default;

    public static bool IsMaximized(nint hwnd) => IsZoomed(hwnd);

    /// <summary>Places a window; returns false when Windows refused (an elevated or protected window).</summary>
    public static bool Place(nint hwnd, Placement placement, MonitorInfo? target = null)
    {
        var monitor = target ?? MonitorOf(hwnd);
        if (placement == Placement.OtherMonitor)
        {
            var all = Monitors();
            if (all.Count < 2)
            {
                return false;
            }

            var current = MonitorOf(hwnd);
            var index = all.Select((m, i) => (m, i)).First(x => x.m.Handle == current.Handle).i;
            monitor = all[(index + 1) % all.Count];
            return MoveKeepingShape(hwnd, current, monitor);
        }

        switch (placement)
        {
            case Placement.Maximize:
                if (target is not null)
                {
                    ShowWindow(hwnd, SwRestore);
                    MoveTo(hwnd, monitor.WorkArea);
                }

                return ShowWindow(hwnd, SwMaximize) || IsZoomed(hwnd);
            case Placement.Minimize:
                return ShowWindow(hwnd, SwMinimize) || IsIconic(hwnd);
            case Placement.Restore:
                return ShowWindow(hwnd, SwRestore) || !IsIconic(hwnd);
        }

        var w = monitor.WorkArea;
        var rect = placement switch
        {
            Placement.Left => w with { Width = w.Width / 2 },
            Placement.Right => w with { Left = w.Left + w.Width / 2, Width = w.Width - w.Width / 2 },
            Placement.Top => w with { Height = w.Height / 2 },
            Placement.Bottom => w with { Top = w.Top + w.Height / 2, Height = w.Height - w.Height / 2 },
            Placement.TopLeft => new ScreenRect(w.Left, w.Top, w.Width / 2, w.Height / 2),
            Placement.TopRight => new ScreenRect(w.Left + w.Width / 2, w.Top, w.Width - w.Width / 2, w.Height / 2),
            Placement.BottomLeft => new ScreenRect(w.Left, w.Top + w.Height / 2, w.Width / 2, w.Height - w.Height / 2),
            Placement.BottomRight => new ScreenRect(w.Left + w.Width / 2, w.Top + w.Height / 2, w.Width - w.Width / 2, w.Height - w.Height / 2),
            _ => Centered(Bounds(hwnd), w),
        };
        ShowWindow(hwnd, SwShowNormal);
        return MoveTo(hwnd, rect);
    }

    /// <summary>Moves a restored window to a rectangle given as fractions of a monitor's work area (layouts).</summary>
    public static bool PlaceFraction(nint hwnd, MonitorInfo monitor, double x, double y, double width, double height)
    {
        var w = monitor.WorkArea;
        ShowWindow(hwnd, SwShowNormal);
        return MoveTo(hwnd, new ScreenRect(
            w.Left + (int)Math.Round(x * w.Width), w.Top + (int)Math.Round(y * w.Height),
            (int)Math.Round(width * w.Width), (int)Math.Round(height * w.Height)));
    }

    private static bool MoveKeepingShape(nint hwnd, MonitorInfo from, MonitorInfo to)
    {
        var maximized = IsZoomed(hwnd);
        if (maximized)
        {
            ShowWindow(hwnd, SwRestore);
        }

        var b = Bounds(hwnd);
        var fx = (double)(b.Left - from.WorkArea.Left) / from.WorkArea.Width;
        var fy = (double)(b.Top - from.WorkArea.Top) / from.WorkArea.Height;
        var fw = Math.Min(1.0, (double)b.Width / from.WorkArea.Width);
        var fh = Math.Min(1.0, (double)b.Height / from.WorkArea.Height);
        var ok = PlaceFraction(hwnd, to, Math.Clamp(fx, 0, 1 - fw), Math.Clamp(fy, 0, 1 - fh), fw, fh);
        if (maximized)
        {
            ShowWindow(hwnd, SwMaximize);
        }

        return ok;
    }

    private static ScreenRect Centered(ScreenRect window, ScreenRect area)
    {
        var width = Math.Min(window.Width, area.Width);
        var height = Math.Min(window.Height, area.Height);
        return new ScreenRect(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
    }

    private static bool MoveTo(nint hwnd, ScreenRect r) =>
        SetWindowPos(hwnd, 0, r.Left, r.Top, r.Width, r.Height, SwpNoZOrder | SwpNoActivate);

    /// <summary>Everything Alt+Tab would show, with what it is and where.</summary>
    public static IReadOnlyList<(TopWindow Window, ScreenRect Bounds, int Monitor, bool Maximized, bool Minimized)> Describe()
    {
        var monitors = Monitors();
        return WindowFinder.List().Select(w =>
        {
            var handle = MonitorFromWindow(w.Handle, MonitorDefaultToNearest);
            var index = monitors.FirstOrDefault(m => m.Handle == handle)?.Index ?? 1;
            return (w, Bounds(w.Handle), index, IsZoomed(w.Handle), IsIconic(w.Handle));
        }).ToList();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;

        public readonly ScreenRect ToScreen() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    private delegate bool MonitorEnumProc(nint monitor, nint dc, nint rect, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    /// <summary>Per-monitor DPI awareness for the whole process, so window and UI Automation coordinates are physical pixels.</summary>
    public static void BecomeDpiAware() => SetProcessDpiAwarenessContext(-4 /* PER_MONITOR_AWARE_V2 */);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint value);
}
