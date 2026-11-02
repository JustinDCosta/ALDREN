using System.Runtime.InteropServices;

namespace Aldren.App.Island.Native;

public sealed record Monitor(nint Handle, string Device, RECT Bounds, RECT WorkArea, uint Dpi, uint RefreshHz, bool IsPrimary)
{
    public double Scale => Dpi / 96.0;
}

internal static class Monitors
{
    /// <summary>All monitors, primary first, with physical bounds, effective DPI and refresh rate.</summary>
    public static List<Monitor> All()
    {
        var list = new List<Monitor>();
        EnumDisplayMonitors(0, 0, (nint handle, nint hdc, ref RECT clip, nint data) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            GetMonitorInfo(handle, ref info);
            GetDpiForMonitor(handle, 0, out var dpi, out _);
            var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            EnumDisplaySettings(info.szDevice, -1, ref mode);
            list.Add(new Monitor(handle, info.szDevice, info.rcMonitor, info.rcWork, dpi, mode.dmDisplayFrequency, (info.dwFlags & 1) != 0));
            return true;
        }, 0);
        return [.. list.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.Left)];
    }

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, ref RECT rect, nint data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFOEX info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE devMode);
}
