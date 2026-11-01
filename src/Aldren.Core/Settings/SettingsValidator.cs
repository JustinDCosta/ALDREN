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

        return errors;
    }
}
