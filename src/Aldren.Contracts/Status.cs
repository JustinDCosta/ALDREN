namespace Aldren.Contracts;

public enum StartupState
{
    Off,
    On,

    /// <summary>The Run entry exists but you turned it off in Task Manager or Windows Settings.</summary>
    DisabledInWindows,
}

public sealed record CoreStatus(string Version, int ProcessId, DateTimeOffset StartedAt, StartupState StartWithWindows);
