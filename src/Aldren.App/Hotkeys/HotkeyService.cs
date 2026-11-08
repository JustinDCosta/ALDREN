using Aldren.App.Tray;
using Aldren.Common.Shell;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Aldren.App.Hotkeys;

/// <summary>
/// The text-box hotkey, registered with RegisterHotKey on the App's hidden window (no keyboard hooks). Falls back to
/// Ctrl+Alt+Shift+Space when the configured one is taken, and says which one is active.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private readonly int _id;
    private readonly bool _allowFallback;

    private readonly HostWindow _host;
    private readonly ILogger _log;
    private bool _registered;

    /// <param name="allowFallback">Only the text box falls back to Ctrl+Alt+Shift+Space.</param>
    public HotkeyService(HostWindow host, ILogger log, int id = 1, bool allowFallback = true)
    {
        _host = host;
        _log = log;
        _id = id;
        _allowFallback = allowFallback;
        _host.Message += OnMessage;
    }

    public Hotkey? Active { get; private set; }

    public bool UsedFallback { get; private set; }

    /// <summary>Raised inside the window procedure: queue the real work.</summary>
    public event Action? Pressed;

    public void Apply(string configured)
    {
        if (!Hotkey.TryParse(configured, out var hotkey))
        {
            _log.LogWarning("Configured hotkey {Hotkey} is invalid; using {Default}", configured, Hotkey.Default);
            hotkey = Hotkey.Default;
        }

        if (_registered && Active == hotkey)
        {
            return;
        }

        Unregister();
        (Active, UsedFallback) = _allowFallback ? HotkeyChooser.Choose(hotkey, Register) : (Register(hotkey) ? hotkey : null, false);
        _registered = Active is not null;
        if (Active is null)
        {
            _log.LogError("Neither {Hotkey} nor {Fallback} could be registered; the text box has no hotkey", hotkey, Hotkey.Fallback);
        }
        else
        {
            _log.LogInformation("Hotkey {Hotkey} active{Fallback}", Active, UsedFallback ? $" ({hotkey} is taken)" : "");
        }
    }

    private bool Register(Hotkey hotkey) =>
        PInvoke.RegisterHotKey(_host.Handle, _id, (HOT_KEY_MODIFIERS)hotkey.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT, hotkey.VirtualKey);

    private void Unregister()
    {
        if (_registered)
        {
            PInvoke.UnregisterHotKey(_host.Handle, _id);
            _registered = false;
        }
    }

    private void OnMessage(uint msg, nuint wParam, nint lParam)
    {
        if (msg == HostWindow.WM_HOTKEY && wParam == (nuint)_id)
        {
            Pressed?.Invoke();
        }
    }

    public void Dispose()
    {
        _host.Message -= OnMessage;
        Unregister();
    }
}
