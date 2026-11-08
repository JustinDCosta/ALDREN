using System.Globalization;
using Aldren.Assistant;

namespace Aldren.Core.Assistant;

/// <summary>
/// The daily briefing (ARCHITECTURE §14.4): the date, today's reminders and the open tasks, short enough to hear in under
/// a minute. Calendar joins when Google is connected; weather waits for the Phase 6 egress gate.
/// </summary>
public sealed class Briefing(AssistantStore store)
{
    public async Task<string> BuildAsync(CancellationToken ct)
    {
        var now = store.NowLocal;
        var parts = new List<string> { $"It's {now.ToString("dddd d MMMM", CultureInfo.CurrentCulture)}." };

        var endOfDay = DateParser.ToInstant(now.Date.AddDays(1), store.Zone);
        var today = (await store.ActiveRemindersAsync(ct)).Where(r => r.Due < endOfDay).ToList();
        parts.Add(today.Count switch
        {
            0 => "No reminders for the rest of today.",
            1 => $"One reminder today: {Item(today[0])}.",
            _ => $"{today.Count} reminders today: {string.Join("; ", today.Take(5).Select(Item))}.",
        });

        var tasks = await store.OpenTasksAsync(ct);
        parts.Add(tasks.Count switch
        {
            0 => "Your task list is clear.",
            1 => $"One open task: {tasks[0].Text}.",
            _ => $"{tasks.Count} open tasks, starting with {string.Join(", ", tasks.Take(3).Select(t => t.Text))}.",
        });

        return string.Join(" ", parts);

        string Item(Reminder r) =>
            $"{r.Text} at {TimeZoneInfo.ConvertTime(r.Due, store.Zone).ToString("t", CultureInfo.CurrentCulture)}";
    }
}
