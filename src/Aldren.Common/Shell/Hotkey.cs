namespace Aldren.Common.Shell;

/// <summary>The <c>RegisterHotKey</c> modifier values.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
}

/// <summary>
/// Ctrl and/or Alt, optional Shift, plus A–Z, 0–9, F1–F24 or Space. The Windows key is refused (Windows reserves it),
/// and so is Ctrl+Alt+Delete.
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private const uint VkSpace = 0x20;
    private const uint VkF1 = 0x70;

    public static readonly Hotkey Default = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, VkSpace);
    public static readonly Hotkey Fallback = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, VkSpace);

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            HotkeyModifiers? modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Ctrl,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                _ => null,
            };
            if (modifier is null || modifiers.HasFlag(modifier.Value))
            {
                return false;
            }

            modifiers |= modifier.Value;
        }

        if ((modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt)) == 0 || !TryParseKey(parts[^1], out var key))
        {
            return false;
        }

        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    private static bool TryParseKey(string text, out uint key)
    {
        key = 0;
        var upper = text.ToUpperInvariant();
        if (upper == "SPACE")
        {
            key = VkSpace;
            return true;
        }

        if (upper.Length == 1 && upper[0] is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
        {
            key = upper[0];
            return true;
        }

        if (upper.Length is 2 or 3 && upper[0] == 'F' && upper[1] != '0'
            && int.TryParse(upper.AsSpan(1), out var n) && n is >= 1 and <= 24)
        {
            key = VkF1 + (uint)(n - 1);
            return true;
        }

        return false;
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(VirtualKey switch
        {
            VkSpace => "Space",
            >= VkF1 and < VkF1 + 24 => "F" + (VirtualKey - VkF1 + 1),
            _ => ((char)VirtualKey).ToString(),
        });
        return string.Join('+', parts);
    }
}
