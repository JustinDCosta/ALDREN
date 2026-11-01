namespace Aldren.Common.Shell;

public static class HotkeyChooser
{
    /// <summary>Tries the configured hotkey, then Ctrl+Alt+Shift+Space. Null when both are taken.</summary>
    public static (Hotkey? Active, bool UsedFallback) Choose(Hotkey configured, Func<Hotkey, bool> tryRegister)
    {
        if (tryRegister(configured))
        {
            return (configured, false);
        }

        if (configured != Hotkey.Fallback && tryRegister(Hotkey.Fallback))
        {
            return (Hotkey.Fallback, true);
        }

        return (null, false);
    }
}
