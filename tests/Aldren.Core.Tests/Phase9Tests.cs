using System.Text.Json.Nodes;
using Aldren.Browser;
using Aldren.Core.Orchestration;
using Aldren.Core.Tools;

namespace Aldren.Core.Tests;

public sealed class FocusPhraseTests
{
    [Theory]
    [InlineData("Focus for 30 minutes on maths", 30, "maths", "study")]
    [InlineData("start a focus session for an hour on the report", 60, "the report", "work")]
    [InlineData("focus on physics for 45 minutes", 45, "physics", "study")]
    [InlineData("focus mode", 50, "", "study")]
    public void Start_focus_phrases_become_start_focus(string text, int minutes, string goal, string profile)
    {
        var command = Assert.IsType<FastToolCommand>(FastPath.Match(text));
        Assert.Equal("start_focus", command.Tool);
        Assert.Equal(minutes, (int)command.Arguments["minutes"]!);
        Assert.Equal(goal, (string)command.Arguments["goal"]!);
        Assert.Equal(profile, (string)command.Arguments["profile"]!);
    }

    [Theory]
    [InlineData("Emergency exit", "end_focus")]
    [InlineData("stop focus", "end_focus")]
    [InlineData("How is my focus going?", "focus_status")]
    [InlineData("what's on my calendar", "calendar_agenda")]
    [InlineData("list my routines", "list_routines")]
    public void Focus_and_calendar_phrases(string text, string tool) =>
        Assert.Equal(tool, Assert.IsType<FastToolCommand>(FastPath.Match(text)).Tool);

    [Fact]
    public void Switching_to_an_app_still_works() =>
        Assert.Equal("focus_app", Assert.IsType<FastToolCommand>(FastPath.Match("switch to chrome")).Tool);
}

public sealed class PersonaTests
{
    [Fact]
    public void Friendly_and_unknown_modes_keep_the_cached_prefix()
    {
        Assert.Equal(Persona.SystemPrompt, Persona.For("friendly"));
        Assert.Equal(Persona.SystemPrompt, Persona.For(null));
        Assert.Equal(Persona.SystemPrompt, Persona.For("pirate"));
    }

    [Fact]
    public void A_mode_is_an_overlay_after_the_prefix()
    {
        var concise = Persona.For("concise");
        Assert.StartsWith(Persona.SystemPrompt, concise, StringComparison.Ordinal);
        Assert.Contains("## Mode: concise", concise, StringComparison.Ordinal);
    }
}

public sealed class SitePolicyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("aldren-sites-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Default_is_interact_and_the_most_specific_entry_wins()
    {
        var sites = new SitePolicy(Path.Combine(_dir, "site-levels.json"));
        Assert.Equal(SiteLevel.Interact, sites.For("https://example.com/page"));

        sites.Set("example.com", SiteLevel.ReadOnly);
        sites.Set("https://www.forms.example.com", SiteLevel.Forms);

        Assert.Equal(SiteLevel.ReadOnly, sites.For("https://mail.example.com/inbox"));
        Assert.Equal(SiteLevel.Forms, sites.For("https://forms.example.com/a"));
        Assert.Equal(SiteLevel.Interact, sites.For("https://notexample.com"));

        sites.Set("example.com", null);
        Assert.Equal(SiteLevel.Interact, sites.For("https://mail.example.com"));
    }
}

public sealed class RoutineStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("aldren-routines-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Routines_are_found_by_their_spoken_name()
    {
        var store = new RoutineStore(Path.Combine(_dir, "routines.json"));
        store.Save("Study Time routine", ["open VS Code", "focus for 50 minutes on maths"]);

        var found = store.Find("study time");
        Assert.NotNull(found);
        Assert.Equal(2, found.Steps.Count);
        Assert.True(store.Delete("study time"));
        Assert.Empty(store.All());
    }

    [Fact]
    public void Corrupt_file_means_no_routines()
    {
        var path = Path.Combine(_dir, "routines.json");
        File.WriteAllText(path, "{ not json");
        Assert.Empty(new RoutineStore(path).All());
    }
}

public sealed class RecognitionSlipTests
{
    [Theory]
    [InlineData("Silent mode of.", "silent mode off")]
    [InlineData("gaming mode of", "gaming mode off")]
    [InlineData("turn of gaming mode", "turn off gaming mode")]
    public void Trailing_of_is_heard_as_off(string heard, string expected) => Assert.Equal(expected, FastPath.Normalize(heard));
}

public sealed class WebSiteTests
{
    [Theory]
    [InlineData("open github.com", "https://github.com")]
    [InlineData("Go to news.ycombinator.com/newest", "https://news.ycombinator.com/newest")]
    [InlineData("open youtube on chrome", "https://www.youtube.com")]
    [InlineData("open the reddit website", "https://www.reddit.com")]
    [InlineData("play lofi beats on youtube", "https://www.youtube.com/results?search_query=lofi%20beats")]
    public void Websites_open_in_the_browser(string text, string url)
    {
        var command = Assert.IsType<FastToolCommand>(FastPath.Match(text));
        Assert.Equal("browser_open", command.Tool);
        Assert.Equal(url, (string)command.Arguments["url"]!);
    }

    [Theory]
    [InlineData("open notepad")]
    [InlineData("open youtube")]
    public void Plain_names_stay_apps_first(string text) =>
        Assert.Equal("launch_app", Assert.IsType<FastToolCommand>(FastPath.Match(text)).Tool);

    [Fact]
    public void Routine_steps_split_on_then_commas_and_sentences()
    {
        Assert.Equal(["open Notepad", "set a timer for one minute"], CreateRoutineTool.SplitSteps("Open Notepad and then set a timer for one minute.").Select(s => s.Replace("Open", "open")));
        Assert.Equal(3, CreateRoutineTool.SplitSteps("open chrome, then mute. Then focus for 25 minutes").Count);
        Assert.Equal(2, CreateRoutineTool.SplitSteps("open vs code\nfocus for 50 minutes on maths").Count);
    }
}
