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

    private static string Normalise(string text) => new([.. text.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
}
