using System.Globalization;
using System.Text;

namespace Aldren.Perception;

public readonly record struct Box(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public (double X, double Y) Center => (Left + Width / 2, Top + Height / 2);

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

/// <param name="Id">Valid only for the snapshot it came from (see <see cref="ScreenSnapshot.Version"/>).</param>
/// <param name="Bounds">Physical screen pixels.</param>
public sealed record ScreenElement(int Id, string Name, string Role, string AutomationId, Box Bounds, bool Enabled, bool IsPassword);

/// <summary>What ALDREN sees of the window you're using: its title, app and interactive controls.</summary>
public sealed record ScreenSnapshot(
    int Version,
    nint Window,
    string Title,
    string App,
    Box WindowBounds,
    IReadOnlyList<ScreenElement> Elements,
    IReadOnlyList<string> Texts,
    int? FocusedId,
    int? UnderCursorId,
    TimeSpan Elapsed)
{
    public ScreenElement? Find(int id) => Elements.FirstOrDefault(e => e.Id == id);

    /// <summary>
    /// A compact description for the model: the window, then controls (the most relevant to the request first), then visible
    /// texts. Everything in it is untrusted screen content.
    /// </summary>
    public string Describe(IReadOnlyList<ScreenElement>? relevantFirst = null, int maxElements = 60, int maxTexts = 40)
    {
        var b = new StringBuilder();
        b.Append(CultureInfo.InvariantCulture, $"Window: \"{Title}\" ({App}), snapshot {Version}.\n");
        if (FocusedId is { } f && Find(f) is { } focused)
        {
            b.Append(CultureInfo.InvariantCulture, $"Focused: [{focused.Id}] {Line(focused)}\n");
        }

        if (UnderCursorId is { } u && Find(u) is { } under)
        {
            b.Append(CultureInfo.InvariantCulture, $"Under the mouse: [{under.Id}] {Line(under)}\n");
        }

        var ordered = (relevantFirst ?? []).Concat(Elements).DistinctBy(e => e.Id).Take(maxElements).ToList();
        b.Append(CultureInfo.InvariantCulture, $"Controls ({ordered.Count} of {Elements.Count}):\n");
        foreach (var e in ordered)
        {
            b.Append(CultureInfo.InvariantCulture, $"[{e.Id}] {Line(e)}\n");
        }

        if (Texts.Count > 0)
        {
            b.Append("Visible text:\n");
            foreach (var t in Texts.Take(maxTexts))
            {
                b.Append("- ").Append(t.Length > 160 ? t[..160] + "…" : t).Append('\n');
            }
        }

        return b.ToString();
    }

    private static string Line(ScreenElement e) =>
        $"{e.Role} \"{(e.Name.Length > 80 ? e.Name[..80] + "…" : e.Name)}\"{(e.Enabled ? "" : " (disabled)")}{(e.IsPassword ? " (password field)" : "")}";
}
