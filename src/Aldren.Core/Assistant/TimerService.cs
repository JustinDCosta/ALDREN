using Aldren.Core.Ipc;
using Microsoft.Extensions.Logging;

namespace Aldren.Core.Assistant;

/// <summary>In-memory countdown timers; a finished one shows a notification through the App. Timers don't survive a restart.</summary>
public sealed class TimerService(AppConnections connections, TimeProvider time, ILogger<TimerService> log) : IDisposable
{
    private readonly List<ITimer> _timers = [];
    private readonly Lock _lock = new();

    public void Start(TimeSpan duration, string? label)
    {
        ITimer? timer = null;
        timer = time.CreateTimer(_ =>
        {
            lock (_lock)
            {
                _timers.Remove(timer!);
            }

            timer!.Dispose();
            _ = NotifyAsync(duration, label);
        }, null, duration, Timeout.InfiniteTimeSpan);
        lock (_lock)
        {
            _timers.Add(timer);
        }

        log.LogInformation("Timer started: {Duration}", duration);
    }

    private async Task NotifyAsync(TimeSpan duration, string? label)
    {
        var message = label is { Length: > 0 } ? $"{label} ({Describe(duration)})" : $"Your {Describe(duration)} timer is done.";
        log.LogInformation("Timer finished: {Duration}", duration);
        try
        {
            if (connections.Current is { } app)
            {
                await app.App.NotifyAsync("Timer done", message, app.Disconnected);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "The timer notification didn't reach the App");
        }
    }

    public static string Describe(TimeSpan d)
    {
        var parts = new List<string>();
        if (d.Hours > 0) { parts.Add(d.Hours == 1 ? "1 hour" : $"{d.Hours} hours"); }
        if (d.Minutes > 0) { parts.Add(d.Minutes == 1 ? "1 minute" : $"{d.Minutes} minutes"); }
        if (d.Seconds > 0) { parts.Add(d.Seconds == 1 ? "1 second" : $"{d.Seconds} seconds"); }
        return parts.Count == 0 ? "0 seconds" : string.Join(" ", parts);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var t in _timers)
            {
                t.Dispose();
            }

            _timers.Clear();
        }
    }
}
