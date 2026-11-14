namespace Aldren.App.Island;

public enum IslandState
{
    Hidden,
    Compact,

    /// <summary>The text field, with focus.</summary>
    Text,

    /// <summary>An answer: under the text field while you type, or on its own after you clicked away.</summary>
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

/// <summary>Sample text for previewing a state from Settings &gt; Diagnostics.</summary>
public static class IslandSamples
{
    public static string For(IslandState state) => state switch
    {
        IslandState.Listening => "Listening…",
        IslandState.Transcribing => "Got it…",
        IslandState.Thinking => "open VS Code and arrange my windows for coding",
        IslandState.Acting => "Opening VS Code, then arranging the windows…",
        IslandState.Researching => "Searching 3 sources…",
        IslandState.Speaking => "Your next meeting is at 14:00, boss. You have two reminders before it: call the bank at noon, and pick up the parcel on the way back.",
        IslandState.Reply => "It's 14:05. You have one reminder left today.",
        IslandState.Error => "Something went wrong. Details are in the log.",
        IslandState.Compact => "ALDREN",
        _ => "",
    };
}
