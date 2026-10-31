using System.Globalization;
using System.Text.RegularExpressions;

namespace S3.Voice.Pipeline;

/// <summary>
/// Word error rate between a prompt and a transcript. Both sides are normalised the same way first: case,
/// punctuation, and numbers ("thirty percent", "30%", "cinquante pour cent" and "50 %" all become "30/50 percent").
/// </summary>
public static partial class Wer
{
    private static readonly Dictionary<string, int> Numbers = new()
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
        ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20,
        ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
        ["zéro"] = 0, ["un"] = 1, ["une"] = 1, ["deux"] = 2, ["trois"] = 3, ["quatre"] = 4, ["cinq"] = 5, ["sept"] = 7,
        ["huit"] = 8, ["neuf"] = 9, ["dix"] = 10, ["onze"] = 11, ["douze"] = 12, ["treize"] = 13, ["quatorze"] = 14,
        ["quinze"] = 15, ["seize"] = 16, ["vingt"] = 20, ["trente"] = 30, ["quarante"] = 40, ["cinquante"] = 50,
        ["soixante"] = 60, ["quatre-vingt"] = 80, ["quatre-vingts"] = 80,
    };

    /// <summary>Edit distance in words (substitutions + deletions + insertions) and the reference length.</summary>
    public static (int Errors, int Words) Count(string reference, string hypothesis)
    {
        var r = Words(reference);
        var h = Words(hypothesis);
        var d = new int[r.Length + 1, h.Length + 1];
        for (var i = 0; i <= r.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= h.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= r.Length; i++)
        {
            for (var j = 1; j <= h.Length; j++)
            {
                var substitution = d[i - 1, j - 1] + (r[i - 1] == h[j - 1] ? 0 : 1);
                d[i, j] = Math.Min(substitution, Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1));
            }
        }

        return (d[r.Length, h.Length], r.Length);
    }

    public static string[] Words(string text)
    {
        var s = text.ToLowerInvariant().Replace('’', '\'');
        s = s.Replace("%", " percent ").Replace("pour cent", "percent");
        s = Clock().Replace(s, "$1 $2");          // 7:30 -> 7 30
        s = FrenchHour().Replace(s, "$1 heures $2"); // 18h30 -> 18 heures 30, 18h -> 18 heures
        s = Punctuation().Replace(s, " ");

        var tokens = new List<string>();
        foreach (var raw in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.Trim('\'', '-');
            if (word.Length == 0)
            {
                continue;
            }

            // A hyphenated number ("dix-huit", "thirty-five") becomes one number; other hyphenated words split.
            tokens.AddRange(HyphenatedNumber(word) is int n ? [n.ToString(CultureInfo.InvariantCulture)] : word.Split('-', StringSplitOptions.RemoveEmptyEntries));
        }

        return MergeTensAndUnits(tokens.Select(t => Numbers.TryGetValue(t, out var n) ? n.ToString(CultureInfo.InvariantCulture) : t).ToList());
    }

    private static int? HyphenatedNumber(string word)
    {
        if (Numbers.TryGetValue(word, out var whole))
        {
            return whole;
        }

        var parts = word.Replace("quatre-vingts", "quatre-vingt").Replace("quatre-vingt", "quatrevingt").Split('-');
        if (parts.Length < 2)
        {
            return null;
        }

        var sum = 0;
        foreach (var part in parts)
        {
            if (part == "et")
            {
                continue;
            }

            if (part == "quatrevingt")
            {
                sum += 80;
            }
            else if (Numbers.TryGetValue(part, out var value))
            {
                sum += value;
            }
            else
            {
                return null;
            }
        }

        return sum;
    }

    // "thirty five" / "vingt et un" -> 35 / 21
    private static string[] MergeTensAndUnits(List<string> tokens)
    {
        var merged = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var hasTens = int.TryParse(tokens[i], out var tens) && tens >= 20 && tens % 10 == 0 && tens < 100;
            var skip = hasTens && i + 2 < tokens.Count && tokens[i + 1] == "et" ? 1 : 0;
            if (hasTens && i + 1 + skip < tokens.Count && int.TryParse(tokens[i + 1 + skip], out var unit) && unit is >= 1 and <= 9)
            {
                merged.Add((tens + unit).ToString(CultureInfo.InvariantCulture));
                i += 1 + skip;
                continue;
            }

            merged.Add(tokens[i]);
        }

        return [.. merged];
    }

    [GeneratedRegex(@"(\d{1,2}):(\d{2})")]
    private static partial Regex Clock();

    [GeneratedRegex(@"(\d{1,2})h(\d{2})?\b")]
    private static partial Regex FrenchHour();

    [GeneratedRegex(@"[^\p{L}\p{N}'\- ]")]
    private static partial Regex Punctuation();
}
