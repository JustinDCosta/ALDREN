using Aldren.App.Tray;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Aldren.App.Hotkeys;

/// <summary>
/// Escape as a system-wide "stop talking" key, registered only while ALDREN speaks and released the moment it stops,
/// so Escape keeps its normal meaning in every app the rest of the time. RegisterHotKey, no keyboard hook.
/// </summary>
internal sealed class StopKey : IDisposable
{
    private const int Id = 3;
    private const uint VkEscape = 0x1B;

    private readonly HostWindow _host;
    private readonly ILogger _log;
    private bool _registered;

    public StopKey(HostWindow host, ILogger log)
    {
        _host = host;
        _log = log;
        _host.Message += OnMessage;
    }

    /// <summary>Raised inside the window procedure: queue the real work.</summary>
    public event Action? Pressed;

    public void SetActive(bool active)
    {
        if (active == _registered)
        {
            return;
        }

        if (active)
        {
            _registered = PInvoke.RegisterHotKey(_host.Handle, Id, HOT_KEY_MODIFIERS.MOD_NOREPEAT, VkEscape);
            if (!_registered)
            {
                _log.LogWarning("Escape couldn't be registered as the stop key (another app holds it); the Stop button still works");
            }
        }
        else
        {
            PInvoke.UnregisterHotKey(_host.Handle, Id);
            _registered = false;
        }
    }

    private void OnMessage(uint msg, nuint wParam, nint lParam)
    {
        if (msg == HostWindow.WM_HOTKEY && wParam == Id)
        {
            Pressed?.Invoke();
        }
    }

    public void Dispose()
    {
        _host.Message -= OnMessage;
        SetActive(false);
    }
}
