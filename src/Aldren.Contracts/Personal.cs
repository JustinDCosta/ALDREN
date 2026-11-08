namespace Aldren.Contracts;

public sealed record MemoryItem(long Id, string Text, string Kind, DateTimeOffset Created);

/// <param name="When">Already in words: "tomorrow at 9:00 AM", "every weekday at 8:00 AM".</param>
public sealed record ReminderItem(long Id, string Text, string When);

public sealed record TaskItem(long Id, string Text);

/// <param name="State">Home, Away or Unknown.</param>
public sealed record PresenceInfo(string State, bool Override, int HomeNetworks, bool CurrentIsHome);
