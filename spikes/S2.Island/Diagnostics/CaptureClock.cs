using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace S2.Island.Diagnostics;

/// <summary>
/// Records when Windows Graphics Capture receives a new frame of a monitor. DWM produces one only when something on
/// that monitor changed, so while the island morphs (and nothing else moves) the gaps between frames show whether
/// every refresh got a new frame. Frames are released at once; no pixels are read or kept.
/// </summary>
internal sealed class CaptureClock : IDisposable
{
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly List<TimeSpan> _frames = [];
    private readonly Lock _lock = new();

    public CaptureClock(nint monitor)
    {
        var item = CreateItemForMonitor(monitor);
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(CreateDevice(), DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        _pool.FrameArrived += (pool, _) =>
        {
            using var frame = pool.TryGetNextFrame();
            if (frame is not null)
            {
                lock (_lock)
                {
                    _frames.Add(frame.SystemRelativeTime);
                }
            }
        };
        _session = _pool.CreateCaptureSession(item);
        _session.IsCursorCaptureEnabled = false;
        _session.StartCapture();
    }

    /// <summary>Performance-counter time, the clock that SystemRelativeTime uses.</summary>
    public static TimeSpan Now => Stopwatch.GetElapsedTime(0);

    public List<TimeSpan> FramesBetween(TimeSpan from, TimeSpan to)
    {
        lock (_lock)
        {
            return [.. _frames.Where(t => t >= from && t <= to)];
        }
    }

    public void Dispose()
    {
        _session.Dispose();
        _pool.Dispose();
    }

    private static IDirect3DDevice CreateDevice()
    {
        const int hardware = 1;
        const uint bgraSupport = 0x20, sdkVersion = 7;
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, hardware, 0, bgraSupport, 0, 0, sdkVersion, out var device, out _, out var context));
        try
        {
            var dxgiDeviceId = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in dxgiDeviceId, out var dxgiDevice));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable));
                try
                {
                    return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            Marshal.Release(context);
            Marshal.Release(device);
        }
    }

    private static GraphicsCaptureItem CreateItemForMonitor(nint monitor)
    {
        var interop = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem").AsInterface<IGraphicsCaptureItemInterop>();
        var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        var pointer = interop.CreateForMonitor(monitor, in itemId);
        try
        {
            return GraphicsCaptureItem.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);

        nint CreateForMonitor(nint monitor, in Guid iid);
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels,
        uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
