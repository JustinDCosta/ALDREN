using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Aldren.App.Tray;

public enum TrayCommand
{
    Open = 1,
    Ask,
    Settings,
    Silent,
    GamingAuto,
    GamingOn,
    GamingOff,
    Focus,
    StartWithWindows,
    Quit,
}

/// <summary>The notification-area icon and its menu. It re-adds itself when Explorer restarts.</summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    /// <summary>
    /// Runs a menu command without the menu (wParam is the <see cref="TrayCommand"/>): what tools\dev\ui.ps1 posts to
    /// open Settings or the conversation for a scripted check. It does nothing the menu doesn't.
    /// </summary>
    public const uint CommandMessage = HostWindow.WM_APP + 2;

    private const uint CallbackMessage = HostWindow.WM_APP + 1;
    private const uint IconId = 1;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint NIN_SELECT = 0x0400;
    private const uint NIN_KEYSELECT = 0x0401;

    private readonly HostWindow _host;
    private readonly uint _taskbarCreated;
    private readonly HICON _icon;
    private readonly HICON _largeIcon;

    public TrayIcon(HostWindow host, string iconPath)
    {
        _host = host;
        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        var size = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        fixed (char* path = iconPath)
        {
            _icon = (HICON)(nint)PInvoke.LoadImage(HINSTANCE.Null, path, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, IMAGE_FLAGS.LR_LOADFROMFILE).Value;
            var large = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXICON);
            _largeIcon = (HICON)(nint)PInvoke.LoadImage(HINSTANCE.Null, path, GDI_IMAGE_TYPE.IMAGE_ICON, large, large, IMAGE_FLAGS.LR_LOADFROMFILE).Value;
        }

        FollowSystemTheme();
        _host.Message += OnMessage;
        Add();
    }

    /// <summary>False until Core answers: the items that need it are greyed out.</summary>
    public bool Connected { get; set; }

    /// <summary>Whether "Start with Windows" shows a check mark; set from <c>CoreStatus</c>.</summary>
    public bool StartWithWindows { get; set; }

    public bool StartWithWindowsAvailable { get; set; }

    public bool Silent { get; set; }

    /// <summary>"auto", "on" or "off": what you chose for gaming mode.</summary>
    public string Gaming { get; set; } = "auto";

    /// <summary>Gaming mode is in effect right now (chosen, or detected).</summary>
    public bool GamingActive { get; set; }

    /// <summary>What the running focus session is for; null when none runs.</summary>
    public string? FocusGoal { get; set; }

    /// <summary>The text field's hotkey, shown next to "Ask ALDREN".</summary>
    public string? AskHint { get; set; }

    public event Action<TrayCommand>? Command;

    /// <summary>The pointer is on the icon (raised often; the listener throttles).</summary>
    public event Action? Hovered;

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

    public void SetTooltip(string text)
    {
        var data = NewData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.szTip = text.Length > 127 ? text[..127] : text;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
    }

    /// <summary>A Windows notification from the tray icon (reminders, finished timers).</summary>
    public void ShowNotification(string title, string message)
    {
        var data = NewData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_INFO;
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = message.Length > 255 ? message[..255] : message;
        data.dwInfoFlags = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_USER | NOTIFY_ICON_INFOTIP_FLAGS.NIIF_LARGE_ICON;
        data.hBalloonIcon = _largeIcon;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
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

        if (msg == CommandMessage)
        {
            if (Enum.IsDefined((TrayCommand)(int)wParam))
            {
                Command?.Invoke((TrayCommand)(int)wParam);
            }

            return;
        }

        if (msg != CallbackMessage)
        {
            return;
        }

        // NOTIFYICON_VERSION_4: the event is in the low word of lParam.
        switch ((uint)(lParam & 0xFFFF))
        {
            case WM_MOUSEMOVE:
                Hovered?.Invoke();
                break;
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
        var needsCore = Connected ? 0 : MENU_ITEM_FLAGS.MF_GRAYED;
        using var menu = new MenuHandle(PInvoke.CreatePopupMenu());
        Append(menu, TrayCommand.Open, "Show conversation");
        Append(menu, TrayCommand.Ask, AskHint is null ? "Ask ALDREN…" : $"Ask ALDREN…\t{AskHint}");
        Append(menu, TrayCommand.Settings, "Settings");
        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (string?)null);

        Append(menu, TrayCommand.Silent, "Silent mode (text only)", (Silent ? MENU_ITEM_FLAGS.MF_CHECKED : 0) | needsCore);
        using (var gaming = new MenuHandle(PInvoke.CreatePopupMenu()))
        {
            Append(gaming, TrayCommand.GamingAuto, "Automatic (games and OBS)", Gaming == "auto" ? MENU_ITEM_FLAGS.MF_CHECKED : 0);
            Append(gaming, TrayCommand.GamingOn, "On", Gaming == "on" ? MENU_ITEM_FLAGS.MF_CHECKED : 0);
            Append(gaming, TrayCommand.GamingOff, "Off", Gaming == "off" ? MENU_ITEM_FLAGS.MF_CHECKED : 0);
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_POPUP | needsCore, (nuint)gaming.DangerousGetHandle(), GamingActive ? "Gaming mode (active)" : "Gaming mode");
            // The parent menu owns it from here and destroys it with itself.
            gaming.SetHandleAsInvalid();
        }

        var focus = FocusGoal switch
        {
            null => "End focus session",
            "" => "End focus session",
            { Length: > 28 } goal => $"End focus session ({goal[..27]}…)",
            var goal => $"End focus session ({goal})",
        };
        Append(menu, TrayCommand.Focus, focus, FocusGoal is null || !Connected ? MENU_ITEM_FLAGS.MF_GRAYED : 0);
        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (string?)null);

        Append(menu, TrayCommand.StartWithWindows, "Start with Windows",
            (StartWithWindows ? MENU_ITEM_FLAGS.MF_CHECKED : 0) | (StartWithWindowsAvailable ? 0 : MENU_ITEM_FLAGS.MF_GRAYED));
        Append(menu, TrayCommand.Quit, "Quit ALDREN");

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

    private static void Append(SafeHandle menu, TrayCommand command, string text, MENU_ITEM_FLAGS flags = 0) =>
        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING | flags, (nuint)command, text);

    /// <summary>
    /// Lets this process's classic menus follow Windows' light or dark app mode (the tray menu is one). These two
    /// uxtheme exports have no names, only ordinals; if a Windows build lacks them the menu simply stays light.
    /// </summary>
    private static void FollowSystemTheme()
    {
        try
        {
            SetPreferredAppMode(1); // AllowDark
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
    }

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    public void Dispose()
    {
        _host.Message -= OnMessage;
        var data = NewData();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
        PInvoke.DestroyIcon(_icon);
        PInvoke.DestroyIcon(_largeIcon);
    }
}

/// <summary>Owns a popup menu so it is destroyed even if showing it fails.</summary>
internal sealed class MenuHandle : SafeHandle
{
    public unsafe MenuHandle(HMENU menu) : base(IntPtr.Zero, ownsHandle: true) => SetHandle((nint)menu.Value);

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override unsafe bool ReleaseHandle() => PInvoke.DestroyMenu(new HMENU((void*)handle));
}
