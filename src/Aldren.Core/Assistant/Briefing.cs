using System.Globalization;
using Aldren.Assistant;
using Aldren.Integrations.Google;
using Microsoft.Extensions.Logging;

namespace Aldren.Core.Assistant;

/// <summary>
/// The daily briefing (ARCHITECTURE §14.4): the date, today's calendar (when Google is connected), today's reminders and
/// the open tasks, short enough to hear in under a minute.
/// </summary>
public sealed class Briefing(AssistantStore store, GoogleAccount google, GoogleCalendar calendar, ILogger<Briefing> log)
{
    public async Task<string> BuildAsync(CancellationToken ct)
    {
        var now = store.NowLocal;
        var parts = new List<string> { $"It's {now.ToString("dddd d MMMM", CultureInfo.CurrentCulture)}." };

        var endOfDay = DateParser.ToInstant(now.Date.AddDays(1), store.Zone);
        if (google.SignedIn)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                var events = await calendar.AgendaAsync(DateTimeOffset.UtcNow, endOfDay, timeout.Token);
                parts.Add(events.Count switch
                {
                    0 => "Nothing else on your calendar today.",
                    1 => $"On your calendar: {Event(events[0])}.",
                    _ => $"{events.Count} things on your calendar: {string.Join("; ", events.Take(5).Select(Event))}.",
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log.LogWarning("The briefing couldn't read the calendar: {Error}", ex.GetType().Name);
                parts.Add("I couldn't reach your calendar.");
            }
        }

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

        string Event(CalendarItem e) =>
            e.AllDay || e.Start is null
                ? $"{e.Title} (all day)"
                : $"{e.Title} at {TimeZoneInfo.ConvertTime(e.Start.Value, store.Zone).ToString("t", CultureInfo.CurrentCulture)}";
    }
}
