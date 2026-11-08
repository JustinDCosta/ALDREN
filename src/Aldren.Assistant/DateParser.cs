using System.Globalization;
using System.Text.RegularExpressions;

namespace Aldren.Assistant;

public enum Repeat
{
    Once,
    Daily,
    Weekdays,
    Weekly,
}

/// <summary>What a time expression means. Exactly one of the three shapes.</summary>
public abstract record When;

/// <summary>A single moment, as local wall-clock time (converted with the time zone when stored).</summary>
public sealed record AtLocal(DateTime Local) : When;

/// <summary>A wall-clock time that repeats; <see cref="Day"/> only for weekly.</summary>
public sealed record Repeating(TimeOnly Time, Repeat Repeat, DayOfWeek? Day) : When;

/// <summary>Not enough to act on: the question to ask instead of guessing.</summary>
public sealed record Unclear(string Question) : When;

/// <summary>
/// Deterministic English time expressions (speech is English-only, ADR-0011), resolved against the actual local time:
/// "in 10 minutes", "in an hour and a half", "at 7 pm", "at 18:30", "tomorrow at 9", "tonight", "Friday at 3 pm",
/// "on 12 November at 10", "every day at 8", "every weekday at 9", "every Monday at 10". A bare hour without am/pm
/// ("at 7") is asked about, never guessed.
/// </summary>
public static partial class DateParser
{
    private static readonly string[] Weekdays = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];

    private static readonly string[] Months =
        ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"];

    private static readonly Dictionary<string, int> NumberWords = new()
    {
        ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["fifteen"] = 15, ["twenty"] = 20,
        ["thirty"] = 30, ["forty"] = 40, ["forty five"] = 45, ["fifty"] = 50, ["sixty"] = 60, ["ninety"] = 90,
    };

    [GeneratedRegex(@"^(?:in|after) (?<n>\d+|half an?|an?|[a-z]+(?: five)?) (?<unit>seconds?|secs?|minutes?|mins?|hours?|hrs?|days?|weeks?)(?: and (?:a half|(?<n2>\d+|[a-z]+) (?<unit2>minutes?|mins?|seconds?)))?$")]
    private static partial Regex Relative();

    [GeneratedRegex(@"^(?:every|each) ?(?<what>day|morning|afternoon|evening|night|weekday|weekdays|sunday|monday|tuesday|wednesday|thursday|friday|saturday)(?: at)?(?: (?<time>.+))?$|^daily(?: at)?(?: (?<time>.+))?$")]
    private static partial Regex Recurring();

    [GeneratedRegex(@"^(?<h>\d{1,2})(?:[:.h](?<m>\d{2}))? ?(?<ampm>am|pm)?$")]
    private static partial Regex Clock();

    [GeneratedRegex(@"\b(?<day>today|tonight|tomorrow|day after tomorrow)\b")]
    private static partial Regex DayWord();

    [GeneratedRegex(@"\b(?:this |tomorrow |in the )?(?<part>morning|afternoon|evening|night)\b")]
    private static partial Regex PartOfDay();

    [GeneratedRegex(@"\b(?:on )?(?:(?<next>next|this) )?(?<wd>sunday|monday|tuesday|wednesday|thursday|friday|saturday)\b")]
    private static partial Regex Weekday();

    [GeneratedRegex(@"\b(?:on )?(?:the )?(?<d>\d{1,2})(?:st|nd|rd|th)? (?:of )?(?<mon>january|february|march|april|may|june|july|august|september|october|november|december)\b|\b(?:on )?(?<mon2>january|february|march|april|may|june|july|august|september|october|november|december) (?:the )?(?<d2>\d{1,2})(?:st|nd|rd|th)?\b")]
    private static partial Regex MonthDate();

    public static When Parse(string text, DateTime nowLocal)
    {
        var s = Normalize(text);
        if (s.Length == 0)
        {
            return new Unclear("When should I remind you?");
        }

        if (Relative().Match(s) is { Success: true } rel)
        {
            return RelativeTime(rel, nowLocal);
        }

        if (Recurring().Match(s) is { Success: true } rec)
        {
            return RecurringTime(rec);
        }

        return AbsoluteTime(s, nowLocal);
    }

    private static When RelativeTime(Match m, DateTime now)
    {
        var amount = Amount(m.Groups["n"].Value);
        if (amount is null)
        {
            return new Unclear("How long from now?");
        }

        var span = Span(amount.Value, m.Groups["unit"].Value);
        if (m.Value.EndsWith("and a half", StringComparison.Ordinal))
        {
            span += Span(0.5, m.Groups["unit"].Value);
        }
        else if (m.Groups["n2"].Success && Amount(m.Groups["n2"].Value) is { } extra)
        {
            span += Span(extra, m.Groups["unit2"].Value);
        }

        return span <= TimeSpan.Zero || span > TimeSpan.FromDays(366) ? new Unclear("How long from now?") : new AtLocal(now + span);
    }

    private static When RecurringTime(Match m)
    {
        var what = m.Groups["what"].Success ? m.Groups["what"].Value : "day";
        var timeText = m.Groups["time"].Success ? m.Groups["time"].Value.Trim() : "";
        var part = what is "morning" or "afternoon" or "evening" or "night" ? what : null;
        TimeOnly time;
        if (timeText.Length == 0)
        {
            if (part is null)
            {
                return new Unclear("At what time?");
            }

            time = DefaultFor(part);
        }
        else
        {
            var clock = ParseClock(timeText, part);
            if (clock is Unclear u)
            {
                return u;
            }

            time = ((ClockTime)clock).Time;
        }

        return what switch
        {
            "weekday" or "weekdays" => new Repeating(time, Repeat.Weekdays, null),
            "day" or "morning" or "afternoon" or "evening" or "night" => new Repeating(time, Repeat.Daily, null),
            _ => new Repeating(time, Repeat.Weekly, (DayOfWeek)Array.IndexOf(Weekdays, what)),
        };
    }

    private static When AbsoluteTime(string s, DateTime now)
    {
        DateTime? date = null;
        string? part = null;
        var rest = s;

        if (MonthDate().Match(rest) is { Success: true } md)
        {
            var day = int.Parse(md.Groups["d"].Success ? md.Groups["d"].Value : md.Groups["d2"].Value, CultureInfo.InvariantCulture);
            var month = Array.IndexOf(Months, md.Groups["mon"].Success ? md.Groups["mon"].Value : md.Groups["mon2"].Value) + 1;
            if (day < 1 || day > DateTime.DaysInMonth(now.Year, month))
            {
                return new Unclear("That date doesn't exist. Which day did you mean?");
            }

            var candidate = new DateTime(now.Year, month, day);
            date = candidate < now.Date ? candidate.AddYears(1) : candidate;
            rest = rest.Remove(md.Index, md.Length);
        }
        else if (Weekday().Match(rest) is { Success: true } wd)
        {
            var target = (DayOfWeek)Array.IndexOf(Weekdays, wd.Groups["wd"].Value);
            var ahead = ((int)target - (int)now.DayOfWeek + 7) % 7;
            date = now.Date.AddDays(ahead);
            if (wd.Groups["next"].Value == "next" && ahead == 0)
            {
                date = date.Value.AddDays(7);
            }

            rest = rest.Remove(wd.Index, wd.Length);
        }

        if (DayWord().Match(rest) is { Success: true } dw)
        {
            switch (dw.Groups["day"].Value)
            {
                case "today":
                    date = now.Date;
                    break;
                case "tonight":
                    date = now.Date;
                    part = "night";
                    break;
                case "tomorrow":
                    date = now.Date.AddDays(1);
                    break;
                default:
                    date = now.Date.AddDays(2);
                    break;
            }

            rest = rest.Remove(dw.Index, dw.Length);
        }

        if (PartOfDay().Match(rest) is { Success: true } pd)
        {
            part = pd.Groups["part"].Value;
            rest = rest.Remove(pd.Index, pd.Length);
        }

        rest = Regex.Replace(rest, @"\b(at|on|by|around|about)\b", " ");
        rest = Regex.Replace(rest, @"\s+", " ").Trim();

        TimeOnly time;
        if (rest.Length == 0)
        {
            if (part is null)
            {
                return new Unclear(date is null ? "When should I remind you?" : "At what time?");
            }

            time = DefaultFor(part);
        }
        else
        {
            var clock = ParseClock(rest, part);
            if (clock is Unclear u)
            {
                return u;
            }

            time = ((ClockTime)clock).Time;
        }

        var when = (date ?? now.Date) + time.ToTimeSpan();
        if (date is null && when <= now)
        {
            // "at 9 pm" with no day, already past today: the next one.
            when = when.AddDays(1);
        }

        return when <= now.AddSeconds(5) ? new Unclear("That time has already passed. When should I remind you?") : new AtLocal(when);
    }

    private sealed record ClockTime(TimeOnly Time) : When;

    private static When ParseClock(string text, string? part)
    {
        switch (text)
        {
            case "noon" or "midday":
                return new ClockTime(new TimeOnly(12, 0));
            case "midnight":
                return new ClockTime(new TimeOnly(23, 59));
        }

        var m = Clock().Match(text);
        if (!m.Success)
        {
            return new Unclear($"I didn't understand the time \"{text}\". Could you say it like \"7 pm\" or \"18:30\"?");
        }

        var hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        if (minute > 59 || hour > 23)
        {
            return new Unclear("That isn't a valid time.");
        }

        var ampm = m.Groups["ampm"].Value;
        if (ampm.Length > 0)
        {
            if (hour is < 1 or > 12)
            {
                return new Unclear("That isn't a valid time.");
            }

            hour = ampm == "pm" ? hour % 12 + 12 : hour % 12;
        }
        else if (hour is >= 1 and <= 12 && !m.Groups["h"].Value.StartsWith('0') && !m.Groups["m"].Success)
        {
            // A bare 1–12 ("at 7"): the part of the day decides; otherwise ask, never guess. With minutes ("9:00", "7:30")
            // it is read as the 24-hour clock used in France.
            switch (part)
            {
                case "morning":
                    hour %= 12;
                    break;
                case "afternoon" or "evening" or "night":
                    hour = hour % 12 + 12;
                    break;
                default:
                    return new Unclear($"Do you mean {hour} am or {hour} pm?");
            }
        }

        return new ClockTime(new TimeOnly(hour, minute));
    }

    private static TimeOnly DefaultFor(string part) => part switch
    {
        "morning" => new TimeOnly(9, 0),
        "afternoon" => new TimeOnly(15, 0),
        "evening" => new TimeOnly(19, 0),
        _ => new TimeOnly(20, 0),
    };

    private static double? Amount(string text)
    {
        if (text.StartsWith("half", StringComparison.Ordinal))
        {
            return 0.5;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n
            : NumberWords.TryGetValue(text, out var w) ? w : null;
    }

    private static TimeSpan Span(double amount, string unit) => unit[0] switch
    {
        's' => TimeSpan.FromSeconds(amount),
        'm' => TimeSpan.FromMinutes(amount),
        'h' => TimeSpan.FromHours(amount),
        'd' => TimeSpan.FromDays(amount),
        _ => TimeSpan.FromDays(7 * amount),
    };

    private static string Normalize(string text)
    {
        var s = text.ToLowerInvariant().Trim().TrimEnd('.', '!', '?', ',');
        s = s.Replace("a.m.", "am").Replace("p.m.", "pm").Replace("o'clock", "").Replace("o’clock", "").Replace(",", " ");
        s = Regex.Replace(s, @"\b([ap]) m\b", "$1m"); // "7 p m", as punctuation-stripped text arrives
        s = Regex.Replace(s, @"(\d)\s*(am|pm)\b", "$1$2");
        s = Regex.Replace(s, @"(\d)(am|pm)\b", "$1 $2");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    /// <summary>Local wall-clock time to an instant, DST-safe: a time skipped by the spring change moves forward an hour, and
    /// a time that happens twice in autumn means the first one.</summary>
    public static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    /// <summary>The next time a repeating reminder fires after <paramref name="afterLocal"/>.</summary>
    public static DateTimeOffset NextOccurrence(Repeating r, DateTime afterLocal, TimeZoneInfo zone)
    {
        for (var i = 0; i < 9; i++)
        {
            var day = afterLocal.Date.AddDays(i);
            var fits = r.Repeat switch
            {
                Repeat.Daily => true,
                Repeat.Weekdays => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday,
                _ => day.DayOfWeek == r.Day,
            };
            var candidate = day + r.Time.ToTimeSpan();
            if (fits && candidate > afterLocal)
            {
                return ToInstant(candidate, zone);
            }
        }

        throw new InvalidOperationException("No next occurrence found.");
    }

    /// <summary>"today at 9:00 PM", "tomorrow at 8:00 AM", "on Friday at 3:00 PM", "on Friday 20 November at 10:00 AM".</summary>
    public static string Describe(DateTime local, DateTime nowLocal)
    {
        var time = local.ToString("t", CultureInfo.CurrentCulture);
        var days = (local.Date - nowLocal.Date).Days;
        return days switch
        {
            0 => $"today at {time}",
            1 => $"tomorrow at {time}",
            < 7 => $"on {local:dddd} at {time}",
            _ => $"on {local.ToString("dddd d MMMM", CultureInfo.CurrentCulture)} at {time}",
        };
    }

    public static string Describe(Repeating r)
    {
        var time = r.Time.ToString("t", CultureInfo.CurrentCulture);
        return r.Repeat switch
        {
            Repeat.Daily => $"every day at {time}",
            Repeat.Weekdays => $"every weekday at {time}",
            _ => $"every {r.Day} at {time}",
        };
    }
}
