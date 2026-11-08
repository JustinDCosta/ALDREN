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
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, int propertyId)
    {
        public Guid FormatId = formatId;
        public int PropertyId = propertyId;
    }

    /// <summary>Only the VT_UI4 case is read (the endpoint form factor); nothing to free for it.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VarType;
        [FieldOffset(8)] public uint UInt;
    }

    /// <summary>True when the default output is headphones or a headset (PKEY_AudioEndpoint_FormFactor 3 or 5).</summary>
    public static bool OutputIsHeadphones()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice? device = null;
        IPropertyStore? store = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device) != 0
                || device.OpenPropertyStore(0 /* STGM_READ */, out store) != 0)
            {
                return false;
            }

            var key = new PropertyKey(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);
            return store.GetValue(ref key, out var value) == 0 && value.VarType == 19 /* VT_UI4 */ && value.UInt is 3 or 5;
        }
        finally
        {
            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }

            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            Marshal.ReleaseComObject(enumerator);
        }
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
