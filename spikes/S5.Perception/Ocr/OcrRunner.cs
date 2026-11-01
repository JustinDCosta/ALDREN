using System.Diagnostics;
using S5.Perception.Grounding;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace S5.Perception.Ocr;

internal sealed record OcrWord(string Text, Grounding.Rect Bounds);

/// <summary>Windows' built-in OCR (Windows.Media.Ocr), runs locally.</summary>
internal static class OcrRunner
{
    public static bool FrenchAvailable => OcrEngine.IsLanguageSupported(new Language("fr-FR"));

    public static async Task<(List<OcrWord> Words, double Milliseconds)> ReadAsync(SoftwareBitmap bitmap)
    {
        var engine = OcrEngine.TryCreateFromLanguage(new Language("en-US")) ?? OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("No OCR language is installed.");
        var clock = Stopwatch.StartNew();
        var result = await engine.RecognizeAsync(bitmap);
        var words = result.Lines.SelectMany(l => l.Words)
            .Select(w => new OcrWord(w.Text, new Grounding.Rect(w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height)))
            .ToList();
        return (words, clock.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Whether OCR read an element's UI Automation name inside its bounds. Icon-only controls have names but no visible
    /// text, so only elements whose name looks like a visible label are asked.
    /// </summary>
    public static bool? Agrees(UiElementInfo element, IReadOnlyList<OcrWord> words)
    {
        var name = Normalise(element.Name);
        if (name.Length < 2 || name.Length > 40 || element.ControlType is "Edit" or "Document")
        {
            return null;
        }

        var inside = words.Where(w => GroundingScore.PointInElement(w.Bounds.Center, element)).Select(w => Normalise(w.Text));
        var read = string.Concat(inside);
        return read.Length == 0 ? false : read.Contains(name, StringComparison.Ordinal) || name.Contains(read, StringComparison.Ordinal);
    }

    /// <summary>True when OCR read no word at all inside the element (as opposed to reading something else there).</summary>
    public static bool NothingInside(UiElementInfo element, IReadOnlyList<OcrWord> words) =>
        !words.Any(w => GroundingScore.PointInElement(w.Bounds.Center, element));

    /// <summary>
    /// Where OCR read the first word of an element's name (4+ characters) within <paramref name="reach"/> px of its box: the word's
    /// left edge relative to the box's, its vertical centre relative to the box's, and whether its centre is inside the box. A steady
    /// offset across many labels means the screenshot and UI Automation coordinates disagree.
    /// </summary>
    public static (double Dx, double Dy, bool Inside)? FirstWordOffset(UiElementInfo element, IReadOnlyList<OcrWord> words, double reach = 40)
    {
        var first = element.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Normalise).FirstOrDefault(t => t.Length >= 4);
        if (first is null)
        {
            return null;
        }

        var b = element.Bounds;
        var match = words
            .Where(w => Normalise(w.Text) == first && w.Bounds.Center.X >= b.Left - reach && w.Bounds.Center.X <= b.Right + reach
                        && w.Bounds.Center.Y >= b.Top - reach && w.Bounds.Center.Y <= b.Bottom + reach)
            .OrderBy(w => Math.Abs(w.Bounds.Left - b.Left) + Math.Abs(w.Bounds.Center.Y - b.Center.Y))
            .FirstOrDefault();
        return match is null ? null : (match.Bounds.Left - b.Left, match.Bounds.Center.Y - b.Center.Y, GroundingScore.PointInElement(match.Bounds.Center, element));
    }

    private static string Normalise(string text) => new([.. text.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
}
