namespace Aldren.Common;

/// <summary>
/// Delay before restarting a crashed process: the n-th consecutive crash waits min(2^(n−1), 60) s,
/// staying up five minutes resets the count, and the ninth consecutive crash gives up.
/// </summary>
public sealed class RestartBackoff(TimeProvider time)
{
    private static readonly TimeSpan StableUptime = TimeSpan.FromMinutes(5);
    private const int GiveUpAt = 9;

    private DateTimeOffset _startedAt = time.GetUtcNow();
    private int _consecutiveCrashes;

    public void MarkStarted() => _startedAt = time.GetUtcNow();

    /// <summary>Called on each crash; null means stop restarting.</summary>
    public TimeSpan? NextDelay()
    {
        if (time.GetUtcNow() - _startedAt >= StableUptime)
        {
            _consecutiveCrashes = 0;
        }

        _consecutiveCrashes++;
        if (_consecutiveCrashes >= GiveUpAt)
        {
            return null;
        }

        return TimeSpan.FromSeconds(Math.Min(1 << (_consecutiveCrashes - 1), 60));
    }
}
