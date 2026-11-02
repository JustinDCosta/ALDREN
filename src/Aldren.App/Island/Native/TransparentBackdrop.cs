using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Aldren.App.Island.Native;

/// <summary>
/// A fully transparent window background. WinUI has no transparent system backdrop, so this sets a clear brush from
/// the system compositor and asks DWM to honour per-pixel alpha (blur-behind with an empty, off-screen region).
/// </summary>
internal sealed class TransparentBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? _compositor;
    private static nint _dispatcherQueueController;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        EnsureSystemDispatcherQueue();
        _compositor ??= new Windows.UI.Composition.Compositor();
        connectedTarget.SystemBackdrop = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        disconnectedTarget.SystemBackdrop = null;
        base.OnTargetDisconnected(disconnectedTarget);
    }

    public static void ConfigureDwm(nint hwnd)
    {
        var margins = new MARGINS();
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var region = CreateRectRgn(-2, -2, -1, -1);
        var blur = new DWM_BLURBEHIND { dwFlags = DWM_BB_ENABLE | DWM_BB_BLURREGION, fEnable = 1, hRgnBlur = region };
        DwmEnableBlurBehindWindow(hwnd, ref blur);
        DeleteObject(region);
    }

    /// <summary>For WM_ERASEBKGND: black is fully transparent once DWM blends the window's alpha.</summary>
    public static void EraseBackground(nint hwnd, nint hdc)
    {
        GetClientRect(hwnd, out var rect);
        var brush = CreateSolidBrush(0);
        FillRect(hdc, ref rect, brush);
        DeleteObject(brush);
    }

    // The system compositor needs a Windows.System.DispatcherQueue on this thread; WinUI only creates its own kind.
    private static void EnsureSystemDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null || _dispatcherQueueController != 0)
        {
            return;
        }

        var options = new DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2, // DQTYPE_THREAD_CURRENT
            apartmentType = 2, // DQTAT_COM_STA
        };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _dispatcherQueueController));
    }

    private const uint DWM_BB_ENABLE = 0x1, DWM_BB_BLURREGION = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_BLURBEHIND
    {
        public uint dwFlags;
        public int fEnable;
        public nint hRgnBlur;
        public int fTransitionOnMaximized;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(nint hwnd, ref DWM_BLURBEHIND blur);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint hdc, ref RECT rect, nint brush);

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);
}
