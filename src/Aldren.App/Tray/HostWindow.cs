using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Aldren.App.Tray;

/// <summary>
/// The App's hidden top-level window. It owns the tray icon and receives the hotkey and end-of-session messages; a
/// message-only window would miss the <c>TaskbarCreated</c> and end-of-session broadcasts.
/// </summary>
internal sealed unsafe class HostWindow : IDisposable
{
    public const uint WM_QUERYENDSESSION = 0x0011;
    public const uint WM_ENDSESSION = 0x0016;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_APP = 0x8000;

    private const string ClassName = "Aldren.App.Host";

    // Kept in a field: the delegate must outlive the window.
    private readonly WNDPROC _proc;

    public HostWindow()
    {
        _proc = WndProc;
        var instance = PInvoke.GetModuleHandle((string?)null);
        fixed (char* className = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _proc,
                hInstance = (HINSTANCE)instance.DangerousGetHandle(),
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            Handle = PInvoke.CreateWindowEx(
                0, ClassName, "ALDREN", WINDOW_STYLE.WS_OVERLAPPED, 0, 0, 0, 0,
                HWND.Null, null, instance, null);
        }

        if (Handle.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public HWND Handle { get; }

    /// <summary>
    /// Message, wParam, lParam. Handlers run inside the window procedure: keep them short and queue real work
    /// (opening windows especially) onto the dispatcher.
    /// </summary>
    public event Action<uint, nuint, nint>? Message;

    /// <summary>An exception thrown by a handler; it must never escape the window procedure.</summary>
    public event Action<Exception>? HandlerFailed;

    private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            Message?.Invoke(msg, wParam.Value, lParam.Value);
        }
        catch (Exception ex)
        {
            // An exception leaving a Win32 callback ends the process (STATUS_FATAL_USER_CALLBACK_EXCEPTION).
            HandlerFailed?.Invoke(ex);
        }

        return msg == WM_QUERYENDSESSION ? (LRESULT)1 : PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose() => PInvoke.DestroyWindow(Handle);
}
