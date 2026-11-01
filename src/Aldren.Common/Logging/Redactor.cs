using System.Text.RegularExpressions;

namespace Aldren.Common.Logging;

/// <summary>Masks secrets in text before it reaches a log file.</summary>
public static partial class Redactor
{
    public const string Mask = "***";

    private const string SecretName =
        @"password|passwd|secret|token|api[_\- ]?key|access[_\- ]?key|private[_\- ]?key|master[_\- ]?key|unified[_\- ]?key|encryption[_\- ]?key|authorization|cookie|credential";

    [GeneratedRegex("(\"[^\"]*(?:" + SecretName + ")[^\"]*\"\\s*:\\s*)(\"(?:[^\"\\\\]|\\\\.)*\"|[^,}\\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex JsonProperty();

    [GeneratedRegex(@"(\b[\w\-]*(?:" + SecretName + @")[\w\-]*\s*[=:]\s*)(?!""\*\*\*)(?:Bearer\s+)?[^\s,;&""']+", RegexOptions.IgnoreCase)]
    private static partial Regex NameValue();

    [GeneratedRegex(@"\bBearer\s+(?!\*\*\*)[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\b(?:sk-[A-Za-z0-9_\-]{8,}|gsk_[A-Za-z0-9]{8,}|AIza[0-9A-Za-z_\-]{20,}|ghp_[A-Za-z0-9]{20,})")]
    private static partial Regex KnownKey();

    // GUIDs survive: their dashes split them into runs of at most 12.
    [GeneratedRegex(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{32,}(?![0-9A-Fa-f])")]
    private static partial Regex LongHex();

    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = JsonProperty().Replace(text, m => m.Groups[1].Value + "\"" + Mask + "\"");
        text = NameValue().Replace(text, m => m.Groups[1].Value + Mask);
        text = BearerToken().Replace(text, "Bearer " + Mask);
        text = KnownKey().Replace(text, Mask);
        return LongHex().Replace(text, Mask);
    }
}
