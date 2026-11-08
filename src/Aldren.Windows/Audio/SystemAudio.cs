using System.Runtime.InteropServices;

namespace Aldren.Windows.Audio;

/// <summary>The default output device's master volume and mute, through Core Audio.</summary>
public static class SystemAudio
{
    public static int GetVolume() => Use(v =>
    {
        Marshal.ThrowExceptionForHR(v.GetMasterVolumeLevelScalar(out var level));
        return (int)Math.Round(level * 100);
    });

    public static bool GetMute() => Use(v =>
    {
        Marshal.ThrowExceptionForHR(v.GetMute(out var muted));
        return muted;
    });

    public static void SetVolume(int percent) => Use(v =>
    {
        var context = Guid.Empty;
        Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref context));
        if (percent > 0)
        {
            Marshal.ThrowExceptionForHR(v.SetMute(false, ref context));
        }

        return 0;
    });

    public static void SetMute(bool mute) => Use(v =>
    {
        var context = Guid.Empty;
        Marshal.ThrowExceptionForHR(v.SetMute(mute, ref context));
        return 0;
    });

    private static T Use<T>(Func<IAudioEndpointVolume, T> action)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice? device = null;
        object? endpoint = null;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23 /* CLSCTX_ALL */, 0, out endpoint));
            return action((IAudioEndpointVolume)endpoint);
        }
        finally
        {
            if (endpoint is not null)
            {
                Marshal.ReleaseComObject(endpoint);
            }

            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify);
        [PreserveSig] int UnregisterControlChangeNotify(nint notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
