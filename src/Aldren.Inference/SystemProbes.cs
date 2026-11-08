using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aldren.Inference;

/// <summary>Free VRAM from the NVIDIA driver's NVML (ships with the driver; nothing installed).</summary>
public static unsafe partial class Nvml
{
    private static readonly Lazy<bool> Initialised = new(() =>
    {
        try
        {
            return nvmlInit_v2() == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    });

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInfo
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [LibraryImport("nvml.dll")]
    private static partial int nvmlInit_v2();

    [LibraryImport("nvml.dll")]
    private static partial int nvmlDeviceGetHandleByIndex_v2(uint index, out nint device);

    [LibraryImport("nvml.dll")]
    private static partial int nvmlDeviceGetMemoryInfo(nint device, MemoryInfo* memory);

    /// <summary>Free MiB on the first GPU; null when NVML isn't available.</summary>
    public static long? FreeMiB()
    {
        if (!Initialised.Value || nvmlDeviceGetHandleByIndex_v2(0, out var device) != 0)
        {
            return null;
        }

        MemoryInfo info;
        return nvmlDeviceGetMemoryInfo(device, &info) == 0 ? (long)(info.Free / (1024 * 1024)) : null;
    }
}

public static partial class PowerStatus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>True on AC power, or when Windows can't tell.</summary>
    public static bool OnAcPower() => !GetSystemPowerStatus(out var s) || s.ACLineStatus != 0;
}

/// <summary>Gaming and streaming detection: Windows' fullscreen state and the OBS process (ARCHITECTURE §15.2).</summary>
public static partial class GamingDetector
{
    private const int QunsBusy = 2;
    private const int QunsRunningD3DFullScreen = 3;
    private const int QunsPresentationMode = 4;

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);

    public static bool FullscreenAppRunning() =>
        SHQueryUserNotificationState(out var state) == 0 && state is QunsBusy or QunsRunningD3DFullScreen or QunsPresentationMode;

    public static bool ObsRunning()
    {
        foreach (var name in (string[])["obs64", "obs"])
        {
            var processes = Process.GetProcessesByName(name);
            var found = processes.Length > 0;
            foreach (var p in processes)
            {
                p.Dispose();
            }

            if (found)
            {
                return true;
            }
        }

        return false;
    }
}
