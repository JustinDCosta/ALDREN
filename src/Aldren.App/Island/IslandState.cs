using System.Numerics;

namespace Aldren.App.Island;

public enum IslandState
{
    Hidden,
    Compact,
    Text,
    Reply,
    Listening,
    Transcribing,
    Thinking,
    Acting,
    Researching,
    Speaking,
    Confirm,
    Error,
}

/// <summary>The pill's size (DIPs) and corner radius in each state.</summary>
public static class IslandShapes
{
    public static (Vector2 Size, float Radius) For(IslandState state) => state switch
    {
        IslandState.Hidden => (Vector2.Zero, 0),
        IslandState.Compact => (new Vector2(180, 36), 18),
        IslandState.Text => (new Vector2(440, 64), 28),
        IslandState.Reply => (new Vector2(520, 170), 30),
        IslandState.Confirm => (new Vector2(560, 230), 30),
        IslandState.Error => (new Vector2(420, 80), 28),
        _ => (new Vector2(420, 100), 30),
    };

    /// <summary>Sample text for previewing a state from Diagnostics; the real content arrives in Phase 3.</summary>
    public static string Sample(IslandState state) => state switch
    {
        IslandState.Listening => "Listening…",
        IslandState.Transcribing => "open VS Code and…",
        IslandState.Thinking => "Thinking…",
        IslandState.Acting => "Opening VS Code ✓\nArranging windows…",
        IslandState.Researching => "Searching 3 sources…",
        IslandState.Speaking => "Your next meeting is at 14:00.",
        IslandState.Error => "Something went wrong. Details are in the log.",
        IslandState.Compact => "ALDREN",
        _ => "",
    };
}
