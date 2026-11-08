using System.Text.RegularExpressions;

namespace Aldren.Memory;

/// <summary>
/// Refuses what must never be stored in memory (ARCHITECTURE §14.2): passwords, API keys and tokens, payment card numbers
/// (Luhn-checked), IBANs and national ID numbers. Errs on the side of refusing.
/// </summary>
public static partial class SensitiveDetector
{
    [GeneratedRegex(@"\b(password|passcode|passwd|pwd|pin code|pin is|mot de passe|secret key|api key|token)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SecretWords();

    [GeneratedRegex(@"\b(?:sk|pk|rk|ghp|gho|xox[abp]|AKIA|AIza)[-_A-Za-z0-9]{12,}")]
    private static partial Regex KeyPrefix();

    [GeneratedRegex(@"\b[A-Za-z0-9+/_-]{32,}={0,2}\b")]
    private static partial Regex LongToken();

    [GeneratedRegex(@"(?:\d[ -]?){13,19}")]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?: ?[A-Z0-9]{4}){3,7}\b")]
    private static partial Regex Iban();

    [GeneratedRegex(@"\b(passport|social security|ssn|national id|carte vitale|numéro de sécurité sociale)\b", RegexOptions.IgnoreCase)]
    private static partial Regex IdWords();

    /// <summary>Null when the text is fine to store; otherwise what it looks like.</summary>
    public static string? Check(string text)
    {
        if (SecretWords().IsMatch(text))
        {
            return "a password or secret";
        }

        if (KeyPrefix().IsMatch(text) || LongToken().Matches(text).Any(m => m.Value.Any(char.IsDigit) && m.Value.Any(char.IsLetter)))
        {
            return "an API key or token";
        }

        if (Iban().IsMatch(text.ToUpperInvariant()))
        {
            return "a bank account number";
        }

        foreach (Match m in DigitRun().Matches(text))
        {
            var digits = new string(m.Value.Where(char.IsDigit).ToArray());
            if (digits.Length is >= 13 and <= 19 && Luhn(digits))
            {
                return "a payment card number";
            }

            if (digits.Length is 13 or 15)
            {
                return "an ID number";
            }
        }

        return IdWords().IsMatch(text) && text.Any(char.IsDigit) ? "an ID number" : null;
    }

    private static bool Luhn(string digits)
    {
        var sum = 0;
        var doubleIt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
            doubleIt = !doubleIt;
        }

        return sum % 10 == 0;
    }
}
