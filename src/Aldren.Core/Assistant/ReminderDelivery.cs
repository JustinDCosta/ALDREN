using System.Globalization;
using Aldren.Assistant;
using Aldren.Core.Ipc;
using Aldren.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Aldren.Core.Assistant;

/// <summary>A due reminder: a Windows notification from the tray, and spoken when ALDREN may speak.</summary>
public sealed class ReminderDelivery(AppConnections connections, VoiceLoop voice, AssistantStore store, ILogger<ReminderDelivery> log)
    : IReminderSink
{
    public async Task DeliverAsync(Reminder reminder, bool missed, CancellationToken ct)
    {
        var due = TimeZoneInfo.ConvertTime(reminder.Due, store.Zone).ToString("t", CultureInfo.CurrentCulture);
        var notification = missed ? $"While you were away (due {due}): {reminder.Text}" : reminder.Text;
        try
        {
            if (connections.Current is { } app)
            {
                await app.App.NotifyAsync(missed ? "Missed reminder" : "Reminder", notification, app.Disconnected);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "The reminder notification didn't reach the App");
        }

        var spoken = missed ? $"While you were away, you had a reminder to {reminder.Text}." : $"Reminder, boss: {reminder.Text}.";
        await voice.AnnounceAsync(spoken, ct);
    }
}
