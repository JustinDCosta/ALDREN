using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Aldren.Core.Orchestration;

public abstract record FastCommand;

public sealed record FastToolCommand(string Tool, JsonObject Arguments) : FastCommand;

public sealed record GamingCommand(bool? On) : FastCommand;

public sealed record StatusCommand : FastCommand;

/// <summary>
/// Frequent commands matched by grammar, with no model (ARCHITECTURE §5.1 step 2). Anything it doesn't fully recognise goes
/// on to the router, so a false match costs more than a miss: patterns are anchored to the whole utterance.
/// </summary>
public static partial class FastPath
{
    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.Ordinal)
    {
        ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["fifteen"] = 15, ["twenty"] = 20, ["thirty"] = 30,
        ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["ninety"] = 90, ["hundred"] = 100,
    };

    [GeneratedRegex(@"^(?:please )?(?:open|launch|start|run)(?: up)? (?<app>.{2,60}?)(?: for me)?(?: please)?$")]
    private static partial Regex Open();

    [GeneratedRegex(@"^(?:please )?(?:close|quit|exit|shut down|kill) (?<app>.{2,60}?)(?: please)?$")]
    private static partial Regex Close();

    [GeneratedRegex(@"^(?:please )?(?:switch to|go to|focus|show me|bring up) (?<app>.{2,60}?)(?: please)?$")]
    private static partial Regex Focus();

    [GeneratedRegex(@"^(?:set (?:the )?)?volume (?:to |at )?(?<n>\d{1,3}|[a-z]+)(?: percent| %|%)?$")]
    private static partial Regex VolumeTo();

    [GeneratedRegex(@"^(?:turn (?:the )?volume|volume|turn it) (?<dir>up|down)(?: by (?<n>\d{1,3}))?$|^(?<dir>louder|quieter)$")]
    private static partial Regex VolumeStep();

    [GeneratedRegex(@"^(?:set (?:a |an )?)?(?:timer (?:for )?(?<n>\d+|[a-z]+) (?<unit>seconds?|secs?|minutes?|mins?|hours?|hrs?)|(?<n>\d+|[a-z]+) (?<unit>seconds?|secs?|minutes?|mins?|hours?|hrs?) timer)(?: for (?<label>.{1,60}))?$")]
    private static partial Regex Timer();

    [GeneratedRegex(@"^(?:whats |what is |calculate |compute |how much is )(?<expr>[\d\s\.\+\-\*/x×÷%\^\(\)]+)$")]
    private static partial Regex Calc();

    public static FastCommand? Match(string text)
    {
        var raw = text.Trim().TrimEnd('?', '.', '!');
        var expression = Calc().Match(raw.ToLowerInvariant().Replace("what's", "whats"));
        if (expression.Success && expression.Groups["expr"].Value.Any(char.IsDigit) && expression.Groups["expr"].Value.Any(c => "+-*/x×÷%^".Contains(c)))
        {
            return Tool("calculate", new JsonObject { ["expression"] = expression.Groups["expr"].Value.Trim() });
        }

        var p = Normalize(text);
        switch (p)
        {
            case "time" or "what time is it" or "whats the time" or "what is the time" or "tell me the time":
                return Tool("get_time", []);
            case "date" or "whats the date" or "what is the date" or "whats todays date" or "what day is it" or "what day is it today" or "whats the date today":
                return Tool("get_date", []);
            case "mute" or "mute the sound" or "mute sound" or "mute volume" or "mute the volume":
                return Tool("mute", new JsonObject { ["mute"] = true });
            case "unmute" or "unmute the sound" or "unmute sound" or "unmute volume":
                return Tool("mute", new JsonObject { ["mute"] = false });
            case "pause" or "play" or "resume" or "pause the music" or "play the music" or "resume the music" or "pause music" or "play music" or "resume music"
                or "play the song" or "play a song" or "play some music" or "pause the song" or "resume the song" or "pause it" or "play it"
                or "resume playback" or "pause playback" or "unpause" or "play something":
                return Tool("media_control", new JsonObject { ["action"] = "play_pause" });
            case "next" or "next song" or "next track" or "skip" or "skip this song" or "skip song" or "skip this one" or "skip it" or "skip this"
                or "skip the song" or "skip this track" or "next one" or "play the next song" or "play the next one":
                return Tool("media_control", new JsonObject { ["action"] = "next" });
            case "previous" or "previous song" or "previous track" or "last song" or "go back a song":
                return Tool("media_control", new JsonObject { ["action"] = "previous" });
            case "stop the music" or "stop music" or "stop playback":
                return Tool("media_control", new JsonObject { ["action"] = "stop" });
            case "max volume" or "volume max" or "full volume" or "volume full":
                return Tool("set_volume", new JsonObject { ["level"] = 100 });
            case "gaming mode on" or "gaming mode" or "enable gaming mode" or "turn on gaming mode" or "start gaming mode":
                return new GamingCommand(true);
            case "gaming mode off" or "disable gaming mode" or "turn off gaming mode" or "stop gaming mode" or "exit gaming mode":
                return new GamingCommand(false);
            case "gaming mode auto" or "gaming mode automatic":
                return new GamingCommand(null);
            case "status" or "model status" or "aldren status":
                return new StatusCommand();
        }

        if (VolumeTo().Match(p) is { Success: true } v && ParseNumber(v.Groups["n"].Value) is { } level && level <= 100)
        {
            return Tool("set_volume", new JsonObject { ["level"] = level });
        }

        if (VolumeStep().Match(p) is { Success: true } s)
        {
            var step = s.Groups["n"].Success ? int.Parse(s.Groups["n"].Value, CultureInfo.InvariantCulture) : 10;
            var up = s.Groups["dir"].Value is "up" or "louder";
            return step is > 0 and <= 100 ? Tool("change_volume", new JsonObject { ["delta"] = up ? step : -step }) : null;
        }

        if (Timer().Match(p) is { Success: true } t && ParseNumber(t.Groups["n"].Value) is { } amount)
        {
            var unit = t.Groups["unit"].Value;
            var seconds = unit.StartsWith('h') ? amount * 3600 : unit.StartsWith('m') ? amount * 60 : amount;
            var args = new JsonObject { ["seconds"] = seconds };
            if (t.Groups["label"].Success)
            {
                args["label"] = t.Groups["label"].Value;
            }

            return Tool("start_timer", args);
        }

        if (Open().Match(p) is { Success: true } o && !IsVague(o.Groups["app"].Value))
        {
            return Tool("launch_app", new JsonObject { ["name"] = o.Groups["app"].Value });
        }

        if (Close().Match(p) is { Success: true } c && !IsVague(c.Groups["app"].Value))
        {
            return Tool("close_app", new JsonObject { ["name"] = c.Groups["app"].Value });
        }

        if (Focus().Match(p) is { Success: true } f && !IsVague(f.Groups["app"].Value))
        {
            return Tool("focus_app", new JsonObject { ["name"] = f.Groups["app"].Value });
        }

        return null;
    }

    /// <summary>"open this", "close that window", "start a timer"…: not an app name, so the model or a later phase decides.</summary>
    private static bool IsVague(string app)
    {
        var words = app.Split(' ');
        return words[0] is "this" or "that" or "it" or "the" or "a" or "an" or "my" or "all" or "some" or "these" or "those" or "every"
               || words.Length > 5;
    }

    private static FastToolCommand Tool(string name, JsonObject args) => new(name, args);

    private static int? ParseNumber(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n
        : NumberWords.TryGetValue(text, out var w) ? w : null;

    /// <summary>Lower case, punctuation dropped (apostrophes vanish: "what's" → "whats"), spaces collapsed.</summary>
    public static string Normalize(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '%')
            {
                builder.Append(c);
            }
            else if (c is '\'' or '’')
            {
                continue;
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        var result = builder.ToString().Trim();
        // Courtesy and the name in front don't change the command.
        foreach (var prefix in (string[])["hey aldren ", "aldren ", "ok aldren ", "can you ", "could you ", "would you ", "please "])
        {
            if (result.StartsWith(prefix, StringComparison.Ordinal))
            {
                result = result[prefix.Length..];
            }
        }

        return result;
    }
}
