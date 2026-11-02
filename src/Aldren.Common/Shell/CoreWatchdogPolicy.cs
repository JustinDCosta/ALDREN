namespace Aldren.Common.Shell;

/// <summary>Decides whether the App relaunches Core after losing its connection. Null means don't relaunch.</summary>
public sealed class CoreWatchdogPolicy(RestartBackoff backoff)
{
    public void MarkConnected() => backoff.MarkStarted();

    public TimeSpan? OnCoreLost(bool quitRequested, bool sessionEnding, bool coreStillRunning)
    {
        if (quitRequested || sessionEnding)
        {
            return null;
        }

        // Core is alive and only the connection dropped: reconnect instead of starting a second one.
        if (coreStillRunning)
        {
            return null;
        }

        return backoff.NextDelay();
    }
}
