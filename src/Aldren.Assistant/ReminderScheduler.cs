using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aldren.Assistant;

/// <summary>Where a due reminder goes: a notification, and speech when that's allowed.</summary>
public interface IReminderSink
{
    /// <param name="missed">It came due while ALDREN wasn't running.</param>
    Task DeliverAsync(Reminder reminder, bool missed, CancellationToken ct);
}

/// <summary>
/// Fires reminders from the database (ARCHITECTURE §14.1). At start-up, reminders that came due while ALDREN wasn't
/// running are delivered as "while you were away"; then due ones fire within a few seconds.
/// </summary>
public sealed class ReminderScheduler(AssistantStore store, IReminderSink sink, TimeProvider time, ILogger<ReminderScheduler> log)
    : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var missed = true;
        using var timer = new PeriodicTimer(Tick, time);
        do
        {
            try
            {
                foreach (var reminder in await store.DueRemindersAsync(time.GetUtcNow(), stoppingToken))
                {
                    // A one-time reminder more than a minute late at start-up was missed while ALDREN was off.
                    var late = missed && time.GetUtcNow() - reminder.Due > TimeSpan.FromMinutes(1);
                    log.LogInformation("Reminder {Id} due{Missed}", reminder.Id, late ? " (missed while ALDREN wasn't running)" : "");
                    await store.MarkFiredAsync(reminder, stoppingToken);
                    store.LastFired = reminder;
                    await sink.DeliverAsync(reminder, late, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Reminder check failed");
            }

            missed = false;
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
