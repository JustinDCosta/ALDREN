namespace Aldren.Perception;

public sealed record Candidate(ScreenElement Element, double Score);

/// <summary>
/// Name-first control resolution (ADR-0013): the words of a request against each control's name, automation ID and role.
/// One clear winner needs no model; otherwise the best few go to vision as numbered candidates.
/// </summary>
public static class NameMatcher
{
    private static readonly Dictionary<string, string[]> RoleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["button"] = ["Button", "SplitButton"],
        ["link"] = ["Hyperlink"],
        ["field"] = ["Edit", "ComboBox", "Document"],
        ["box"] = ["Edit", "ComboBox", "CheckBox"],
        ["textbox"] = ["Edit"],
        ["search"] = ["Edit", "ComboBox"],
        ["checkbox"] = ["CheckBox"],
        ["tab"] = ["TabItem"],
        ["menu"] = ["MenuItem", "MenuBar"],
        ["item"] = ["ListItem", "TreeItem", "DataItem", "MenuItem"],
        ["dropdown"] = ["ComboBox"],
        ["slider"] = ["Slider"],
    };

    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "on", "in", "to", "click", "press", "tap", "hit", "select", "choose", "open", "please", "that", "this",
        "it", "button", "link", "field", "box", "tab", "menu", "item", "type", "into", "enter", "write", "check", "toggle", "my",
    };

    public static IReadOnlyList<Candidate> Rank(IReadOnlyList<ScreenElement> elements, string description, int limit = 8)
    {
        var words = Words(description).Where(w => !Filler.Contains(w)).ToList();
        var roles = Words(description).Where(RoleWords.ContainsKey).SelectMany(w => RoleWords[w]).ToHashSet();
        if (words.Count == 0 && roles.Count == 0)
        {
            return [];
        }

        var phrase = string.Join(' ', words);
        return elements
            .Select(e => new Candidate(e, Score(e, words, phrase, roles)))
            .Where(c => c.Score > 0.2)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Element.Bounds.Top).ThenBy(c => c.Element.Bounds.Left)
            .Take(limit)
            .ToList();
    }

    /// <summary>The single obvious control, or null when it's ambiguous (then vision decides among the ranked few).</summary>
    public static ScreenElement? Clear(IReadOnlyList<Candidate> ranked) =>
        ranked.Count switch
        {
            0 => null,
            1 => ranked[0].Score >= 0.6 ? ranked[0].Element : null,
            _ => ranked[0].Score >= 0.9 && ranked[0].Score - ranked[1].Score >= 0.25 ? ranked[0].Element : null,
        };

    /// <summary>"the second one" / "the third result": the nth item in reading order among list-like controls.</summary>
    public static ScreenElement? Ordinal(IReadOnlyList<ScreenElement> elements, int n)
    {
        var items = elements.Where(e => e.Role is "ListItem" or "DataItem" or "TreeItem" or "Hyperlink")
            .OrderBy(e => Math.Round(e.Bounds.Top / 8)).ThenBy(e => e.Bounds.Left).ToList();
        return n >= 1 && n <= items.Count ? items[n - 1] : null;
    }

    private static double Score(ScreenElement e, List<string> words, string phrase, HashSet<string> roles)
    {
        var name = string.Join(' ', Words(e.Name));
        var id = string.Join(' ', Words(e.AutomationId));
        double score = 0;
        if (phrase.Length > 0)
        {
            if (name == phrase)
            {
                score = 1.0;
            }
            else if (name.StartsWith(phrase, StringComparison.Ordinal) || name.Contains(' ' + phrase, StringComparison.Ordinal))
            {
                score = 0.8;
            }
            else
            {
                var nameWords = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                var idWords = id.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                var hits = words.Count(w => nameWords.Contains(w) || nameWords.Any(n => n.StartsWith(w, StringComparison.Ordinal) && w.Length >= 4));
                var idHits = words.Count(w => idWords.Contains(w));
                score = 0.7 * hits / words.Count + 0.2 * idHits / words.Count;
            }
        }

        if (roles.Count > 0)
        {
            score += roles.Contains(e.Role) ? 0.15 : -0.1;
        }

        return e.Enabled ? score : score * 0.5;
    }

    private static IEnumerable<string> Words(string text) =>
        new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
