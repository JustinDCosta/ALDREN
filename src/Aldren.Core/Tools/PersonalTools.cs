using System.Text.RegularExpressions;
using Aldren.Assistant;
using Aldren.Core.Assistant;
using Aldren.Memory;
using Aldren.Presence;
using Aldren.Presence.Network;
using Aldren.Security.Permissions;
using Aldren.Security.Tools;

namespace Aldren.Core.Tools;

/// <summary>What the Phase 4 tools return: one sentence or two, ready to say.</summary>
public sealed record SpokenResult(string Message);

public sealed record TextArgs(string Text);

public sealed record QueryArgs(string Query);

public sealed partial class RememberTool(MemoryStore memory) : Tool<TextArgs, SpokenResult>
{
    public override string Name => "remember";
    public override string Description => "Save something the user asks you to remember (a fact, preference or detail about them)";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.MemoryWrite;

    [GeneratedRegex(@"^(my name is|call me|i am|i'm|i live|i work|i study|my birthday|my (?:wife|husband|partner|girlfriend|boyfriend|mum|mom|dad|brother|sister))\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileFact();

    public override IEnumerable<string> Validate(TextArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Text) || args.Text.Length > 500)
        {
            yield return "text must be 1–500 characters.";
        }
    }

    protected override async Task<SpokenResult> ExecuteAsync(TextArgs args, CancellationToken ct)
    {
        var text = args.Text.Trim().TrimEnd('.');
        var kind = ProfileFact().IsMatch(text) ? MemoryStore.Profile : MemoryStore.Fact;
        await memory.AddAsync(text, kind, ct);
        return new SpokenResult("Got it, I'll remember that.");
    }
}

public sealed class RecallTool(MemoryStore memory) : Tool<QueryArgs, SpokenResult>
{
    public override string Name => "recall";
    public override string Description => "Look up what the user asked you to remember (query: what to look for, or empty for the latest)";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.MemoryRead;

    protected override async Task<SpokenResult> ExecuteAsync(QueryArgs args, CancellationToken ct)
    {
        var found = await memory.SearchAsync(args.Query, 5, ct);
        return found.Count == 0
            ? new SpokenResult("I don't have anything saved about that.")
            : new SpokenResult("Here's what I remember: " + string.Join("; ", found.Select(m => m.Text)) + ".");
    }
}

public sealed class ForgetTool(MemoryStore memory) : Tool<QueryArgs, SpokenResult>
{
    public override string Name => "forget";
    public override string Description => "Delete a saved memory that matches the query (asks the user first)";
    public override RiskTier Tier => RiskTier.Impactful;
    public override string Permission => PermissionCatalog.MemoryWrite;

    public override string Summarize(QueryArgs args)
    {
        var match = memory.SearchAsync(args.Query, 1, CancellationToken.None).GetAwaiter().GetResult().FirstOrDefault();
        return match is null ? $"Forget \"{args.Query}\" (nothing matches)" : $"Forget: \"{match.Text}\"";
    }

    protected override async Task<SpokenResult> ExecuteAsync(QueryArgs args, CancellationToken ct)
    {
        var match = (await memory.SearchAsync(args.Query, 1, ct)).FirstOrDefault()
                    ?? throw new InvalidOperationException("I don't have anything saved that matches that.");
        await memory.DeleteAsync(match.Id, ct);
        return new SpokenResult($"Forgotten: \"{match.Text}\".");
    }
}

public sealed record ReminderArgs(string Text, string When);

public sealed class CreateReminderTool(AssistantStore store) : Tool<ReminderArgs, SpokenResult>
{
    public override string Name => "create_reminder";
    public override string Description =>
        "Create a reminder. text: what to remind about. when: the time as the user said it, in English (\"in 10 minutes\", \"tomorrow at 9 am\", \"Friday at 3 pm\", \"every weekday at 8:00\")";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.Reminders;

    public override IEnumerable<string> Validate(ReminderArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Text) || args.Text.Length > 200)
        {
            yield return "text must be 1–200 characters.";
        }

        if (string.IsNullOrWhiteSpace(args.When) || args.When.Length > 80)
        {
            yield return "when must be 1–80 characters.";
        }
    }

    protected override async Task<SpokenResult> ExecuteAsync(ReminderArgs args, CancellationToken ct)
    {
        var when = DateParser.Parse(args.When, store.NowLocal);
        if (when is Unclear unclear)
        {
            // Nothing is guessed: the question goes back to you.
            throw new InvalidOperationException(unclear.Question);
        }

        var text = Clean(args.Text);
        var reminder = await store.AddReminderAsync(text, when, ct);
        var description = when is Repeating r ? DateParser.Describe(r) : DateParser.Describe(((AtLocal)when).Local, store.NowLocal);
        return new SpokenResult($"Okay, I'll remind you to {text} {description}.");
    }

    internal static string Clean(string text)
    {
        var t = text.Trim().TrimEnd('.', '!');
        return t.StartsWith("to ", StringComparison.OrdinalIgnoreCase) ? t[3..] : t;
    }
}

public sealed record NoArguments;

public sealed class ListRemindersTool(AssistantStore store) : Tool<NoArguments, SpokenResult>
{
    public override string Name => "list_reminders";
    public override string Description => "List the user's upcoming reminders";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.Reminders;

    protected override async Task<SpokenResult> ExecuteAsync(NoArguments args, CancellationToken ct)
    {
        var reminders = await store.ActiveRemindersAsync(ct);
        if (reminders.Count == 0)
        {
            return new SpokenResult("You have no reminders set.");
        }

        var parts = reminders.Take(6).Select(r => $"{r.Text} {Describe(r, store)}");
        var more = reminders.Count > 6 ? $", and {reminders.Count - 6} more in Settings" : "";
        return new SpokenResult($"You have {reminders.Count} reminder{(reminders.Count == 1 ? "" : "s")}: {string.Join("; ", parts)}{more}.");
    }

    internal static string Describe(Reminder r, AssistantStore store) =>
        r.AsRepeating is { } rep ? DateParser.Describe(rep) : DateParser.Describe(TimeZoneInfo.ConvertTime(r.Due, store.Zone).DateTime, store.NowLocal);
}

public sealed class CancelReminderTool(AssistantStore store) : Tool<QueryArgs, SpokenResult>
{
    public override string Name => "cancel_reminder";
    public override string Description => "Cancel the reminder that best matches the query (asks the user first)";
    public override RiskTier Tier => RiskTier.Impactful;
    public override string Permission => PermissionCatalog.Reminders;

    public override string Summarize(QueryArgs args)
    {
        var match = AssistantStore.BestMatch(store.ActiveRemindersAsync(CancellationToken.None).GetAwaiter().GetResult(), r => r.Text, args.Query);
        return match is null ? $"Cancel a reminder matching \"{args.Query}\" (none found)" : $"Cancel the reminder \"{match.Text}\" ({ListRemindersTool.Describe(match, store)})";
    }

    protected override async Task<SpokenResult> ExecuteAsync(QueryArgs args, CancellationToken ct)
    {
        var match = AssistantStore.BestMatch(await store.ActiveRemindersAsync(ct), r => r.Text, args.Query)
                    ?? throw new InvalidOperationException("I couldn't find a reminder like that.");
        await store.CancelReminderAsync(match.Id, ct);
        return new SpokenResult($"Cancelled the reminder to {match.Text}.");
    }
}

public sealed record SnoozeArgs(int Minutes = 10);

public sealed class SnoozeReminderTool(AssistantStore store) : Tool<SnoozeArgs, SpokenResult>
{
    public override string Name => "snooze_reminder";
    public override string Description => "Snooze the reminder that just went off, for a number of minutes (default 10)";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.Reminders;

    public override IEnumerable<string> Validate(SnoozeArgs args)
    {
        if (args.Minutes is < 1 or > 24 * 60)
        {
            yield return "minutes must be 1–1440.";
        }
    }

    protected override async Task<SpokenResult> ExecuteAsync(SnoozeArgs args, CancellationToken ct)
    {
        var last = store.LastFired ?? throw new InvalidOperationException("No reminder has gone off yet.");
        await store.SnoozeAsync(last.Text, TimeSpan.FromMinutes(args.Minutes), ct);
        return new SpokenResult($"Snoozed: I'll remind you to {last.Text} in {args.Minutes} minutes.");
    }
}

public sealed class AddTaskTool(AssistantStore store) : Tool<TextArgs, SpokenResult>
{
    public override string Name => "add_task";
    public override string Description => "Add an item to the user's task list";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.Tasks;

    public override IEnumerable<string> Validate(TextArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Text) || args.Text.Length > 200)
        {
            yield return "text must be 1–200 characters.";
        }
    }

    protected override async Task<SpokenResult> ExecuteAsync(TextArgs args, CancellationToken ct)
    {
        var task = await store.AddTaskAsync(args.Text.Trim().TrimEnd('.'), ct);
        return new SpokenResult($"Added to your tasks: {task.Text}.");
    }
}

public sealed class ListTasksTool(AssistantStore store) : Tool<NoArguments, SpokenResult>
{
    public override string Name => "list_tasks";
    public override string Description => "List the user's open tasks";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.Tasks;

    protected override async Task<SpokenResult> ExecuteAsync(NoArguments args, CancellationToken ct)
    {
        var tasks = await store.OpenTasksAsync(ct);
        return tasks.Count == 0
            ? new SpokenResult("Your task list is empty.")
            : new SpokenResult($"You have {tasks.Count} open task{(tasks.Count == 1 ? "" : "s")}: {string.Join("; ", tasks.Take(8).Select(t => t.Text))}.");
    }
}

public sealed class CompleteTaskTool(AssistantStore store) : Tool<QueryArgs, SpokenResult>
{
    public override string Name => "complete_task";
    public override string Description => "Mark the open task that best matches the query as done";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.Tasks;

    protected override async Task<SpokenResult> ExecuteAsync(QueryArgs args, CancellationToken ct)
    {
        var match = AssistantStore.BestMatch(await store.OpenTasksAsync(ct), t => t.Text, args.Query)
                    ?? throw new InvalidOperationException("I couldn't find a task like that.");
        await store.CompleteTaskAsync(match.Id, ct);
        return new SpokenResult($"Done: {match.Text}. Nicely done, boss.");
    }
}

public sealed class DailyBriefingTool(Briefing briefing) : Tool<NoArguments, SpokenResult>
{
    public override string Name => "daily_briefing";
    public override string Description => "Give the user's daily briefing: the date, today's reminders and open tasks";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.Briefing;

    protected override async Task<SpokenResult> ExecuteAsync(NoArguments args, CancellationToken ct) =>
        new(await briefing.BuildAsync(ct));
}

public sealed class PresenceStatusTool(PresenceMonitor presence) : Tool<NoArguments, SpokenResult>
{
    public override string Name => "presence_status";
    public override string Description => "Say whether the user is home or away, by network";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.Presence;

    protected override Task<SpokenResult> ExecuteAsync(NoArguments args, CancellationToken ct)
    {
        var s = presence.Snapshot();
        var text = s.HomeNetworks == 0
            ? "I don't know your home network yet. When you're home, say \"this is my home network\"."
            : s.State switch
            {
                PresenceState.Home => s.Override ? "You told me you're home." : "You're home: you're on your home network.",
                PresenceState.Away => "You're away: this isn't your home network.",
                _ => "I can't tell right now: there's no network connection I can identify.",
            };
        return Task.FromResult(new SpokenResult(text));
    }
}

public sealed class SetHomeNetworkTool(PresenceMonitor presence) : Tool<NoArguments, SpokenResult>
{
    public override string Name => "set_home_network";
    public override string Description => "Save the network the PC is on now as the user's home network (asks first)";
    public override RiskTier Tier => RiskTier.Impactful;
    public override string Permission => PermissionCatalog.Presence;

    public override string Summarize(NoArguments args) =>
        "Save the network you're on now as home. ALDREN stores only a hashed fingerprint (network ID and router address).";

    protected override async Task<SpokenResult> ExecuteAsync(NoArguments args, CancellationToken ct)
    {
        var saved = await presence.SaveCurrentAsHomeAsync(ct);
        return saved == 0
            ? new SpokenResult("I couldn't identify the network you're on, so nothing was saved. Is the PC connected?")
            : new SpokenResult("Saved. I'll know you're home whenever you're on this network.");
    }
}

public sealed class ImHomeTool(PresenceMonitor presence) : Tool<NoArguments, SpokenResult>
{
    public override string Name => "im_home";
    public override string Description => "The user says they're home: treat them as home until the next network change";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.Presence;

    protected override Task<SpokenResult> ExecuteAsync(NoArguments args, CancellationToken ct)
    {
        presence.OverrideHome();
        return Task.FromResult(new SpokenResult("Welcome home, boss."));
    }
}
