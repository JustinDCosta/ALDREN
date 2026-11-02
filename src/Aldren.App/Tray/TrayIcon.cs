using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Aldren.App.Tray;

public enum TrayCommand
{
    Open = 1,
    Settings,
    PauseListening,
    GamingMode,
    Focus,
    StartWithWindows,
    Quit,
}

/// <summary>The notification-area icon and its menu. It re-adds itself when Explorer restarts.</summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const uint CallbackMessage = HostWindow.WM_APP + 1;
    private const uint IconId = 1;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint NIN_SELECT = 0x0400;
    private const uint NIN_KEYSELECT = 0x0401;

    private readonly HostWindow _host;
    private readonly uint _taskbarCreated;
    private readonly HICON _icon;

    public TrayIcon(HostWindow host, string iconPath)
    {
        _host = host;
        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        var size = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        fixed (char* path = iconPath)
        {
            _icon = (HICON)(nint)PInvoke.LoadImage(HINSTANCE.Null, path, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, IMAGE_FLAGS.LR_LOADFROMFILE).Value;
        }

        _host.Message += OnMessage;
        Add();
    }

    /// <summary>Whether "Start with Windows" shows a check mark; set from <c>CoreStatus</c>.</summary>
    public bool StartWithWindows { get; set; }

    public bool StartWithWindowsAvailable { get; set; }

    public event Action<TrayCommand>? Command;

    private void Add()
    {
        var data = NewData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = "ALDREN";
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &data);

        data.Anonymous.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, &data);
    }

    private NOTIFYICONDATAW NewData() => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = _host.Handle,
        uID = IconId,
    };

    private void OnMessage(uint msg, nuint wParam, nint lParam)
    {
        if (msg == _taskbarCreated)
        {
            Add();
            return;
        }

        if (msg != CallbackMessage)
        {
            return;
        }

        // NOTIFYICON_VERSION_4: the event is in the low word of lParam.
        switch ((uint)(lParam & 0xFFFF))
        {
            case WM_CONTEXTMENU:
                ShowMenu();
                break;
            case NIN_SELECT or NIN_KEYSELECT or WM_LBUTTONDBLCLK:
                Command?.Invoke(TrayCommand.Open);
                break;
        }
    }

    private void ShowMenu()
    {
        using var menu = new MenuHandle(PInvoke.CreatePopupMenu());
        Append(menu, TrayCommand.Open, "Open ALDREN");
        Append(menu, TrayCommand.Settings, "Settings");
        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (string?)null);
        Append(menu, TrayCommand.PauseListening, "Pause listening (Phase 3)", MENU_ITEM_FLAGS.MF_GRAYED);
        Append(menu, TrayCommand.GamingMode, "Gaming mode (Phase 2)", MENU_ITEM_FLAGS.MF_GRAYED);
        Append(menu, TrayCommand.Focus, "Focus (Phase 8)", MENU_ITEM_FLAGS.MF_GRAYED);
        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (string?)null);
        Append(menu, TrayCommand.StartWithWindows, "Start with Windows",
            (StartWithWindows ? MENU_ITEM_FLAGS.MF_CHECKED : 0) | (StartWithWindowsAvailable ? 0 : MENU_ITEM_FLAGS.MF_GRAYED));
        Append(menu, TrayCommand.Quit, "Quit");

        PInvoke.GetCursorPos(out var point);
        // Without this the menu doesn't close when you click elsewhere.
        PInvoke.SetForegroundWindow(_host.Handle);
        var chosen = PInvoke.TrackPopupMenuEx(
            menu, (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON),
            point.X, point.Y, _host.Handle, null);
        if (chosen.Value != 0)
        {
            Command?.Invoke((TrayCommand)chosen.Value);
        }
    }

    private static void Append(System.Runtime.InteropServices.SafeHandle menu, TrayCommand command, string text, MENU_ITEM_FLAGS flags = 0) =>
        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING | flags, (nuint)command, text);

    public void Dispose()
    {
        _host.Message -= OnMessage;
        var data = NewData();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
        PInvoke.DestroyIcon(_icon);
    }
}

/// <summary>Owns a popup menu so it is destroyed even if showing it fails.</summary>
internal sealed class MenuHandle : System.Runtime.InteropServices.SafeHandle
{
    public unsafe MenuHandle(HMENU menu) : base(IntPtr.Zero, ownsHandle: true) => SetHandle((nint)menu.Value);

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override unsafe bool ReleaseHandle() => PInvoke.DestroyMenu(new HMENU((void*)handle));
}
