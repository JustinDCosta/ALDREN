using Aldren.Common.Shell;
using Aldren.Contracts;

namespace Aldren.Core.Settings;

public static class SettingsValidator
{
    public static IReadOnlyList<string> Validate(AldrenSettings settings)
    {
        var errors = new List<string>();
        if (settings.General is null)
        {
            errors.Add("General settings are missing.");
        }
        else if (!Hotkey.TryParse(settings.General.Hotkey, out _))
        {
            errors.Add($"'{settings.General.Hotkey}' isn't a valid hotkey. Use Ctrl and/or Alt, optionally Shift, plus a letter, digit, F1–F24 or Space.");
        }

        if (settings.Voice is null)
        {
            errors.Add("Voice settings are missing.");
        }
        else if (!Hotkey.TryParse(settings.Voice.PushToTalkHotkey, out _))
        {
            errors.Add($"'{settings.Voice.PushToTalkHotkey}' isn't a valid push-to-talk hotkey.");
        }

        if (settings.Cloud is null)
        {
            errors.Add("Cloud settings are missing.");
        }

        if (settings.Presence is null)
        {
            errors.Add("Home & away settings are missing.");
        }
        else if (settings.Presence.GreetingAfterHours is < 1 or > 48)
        {
            errors.Add("The greeting interval must be 1–48 hours.");
        }

        return errors;
    }
}
