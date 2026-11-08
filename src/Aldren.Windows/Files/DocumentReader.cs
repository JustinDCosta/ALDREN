using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace Aldren.Windows.Files;

/// <summary>
/// Text out of PDF (PdfPig), DOCX (Open XML SDK) and plain-text or code files, locally. What comes out is untrusted
/// document content: the caller passes it on without tool access (SECURITY.md).
/// </summary>
public static class DocumentReader
{
    public const int MaxCharacters = 12_000;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".log", ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".toml", ".cs", ".py", ".js", ".ts",
        ".tsx", ".jsx", ".java", ".c", ".cpp", ".h", ".hpp", ".go", ".rs", ".rb", ".php", ".html", ".css", ".sql", ".ps1", ".sh",
        ".bat", ".kt", ".swift", ".lua", ".r", ".m", ".tex", ".srt",
    };

    public static bool CanRead(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".pdf" or ".docx" || TextExtensions.Contains(Path.GetExtension(path));

    /// <summary>The text, cut to <see cref="MaxCharacters"/> (with a note when cut).</summary>
    public static async Task<string> ReadAsync(string path, CancellationToken ct)
    {
        var text = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => await Task.Run(() => Pdf(path, ct), ct),
            ".docx" => await Task.Run(() => Docx(path), ct),
            _ when TextExtensions.Contains(Path.GetExtension(path)) => await ReadTextAsync(path, ct),
            _ => throw new NotSupportedException($"I can read PDF, Word (.docx) and text or code files, not {Path.GetExtension(path)} files."),
        };

        text = text.Replace("\r\n", "\n").Trim();
        return text.Length <= MaxCharacters ? text : text[..MaxCharacters] + $"\n[… cut: the document has {text.Length:N0} characters]";
    }

    private static string Pdf(string path, CancellationToken ct)
    {
        using var document = PdfDocument.Open(path);
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            builder.AppendLine(page.Text);
            if (builder.Length > MaxCharacters * 2)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static string Docx(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return "";
        }

        var builder = new StringBuilder();
        foreach (var paragraph in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            builder.AppendLine(paragraph.InnerText);
        }

        return builder.ToString();
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (info.Length > 2_000_000)
        {
            using var reader = new StreamReader(path);
            var buffer = new char[MaxCharacters + 1];
            var read = await reader.ReadBlockAsync(buffer, ct);
            return new string(buffer, 0, read);
        }

        return await File.ReadAllTextAsync(path, ct);
    }
}
