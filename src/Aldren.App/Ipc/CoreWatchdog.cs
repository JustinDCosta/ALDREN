using System.Diagnostics;
using Aldren.Common;
using Aldren.Common.Shell;
using Microsoft.Extensions.Logging;

namespace Aldren.App.Ipc;

/// <summary>Relaunches Core when it dies, except after Quit, during logoff, or while Core is still running.</summary>
public sealed class CoreWatchdog(CoreWatchdogPolicy policy, ILogger<CoreWatchdog> log)
{
    public bool QuitRequested { get; set; }

    public bool SessionEnding { get; set; }

    public void MarkConnected() => policy.MarkConnected();

    public async Task OnCoreLostAsync(CancellationToken ct)
    {
        var coreStillRunning = SingleInstance.IsHeld(SingleInstance.CoreName);
        if (policy.OnCoreLost(QuitRequested, SessionEnding, coreStillRunning) is not { } delay)
        {
            if (!coreStillRunning && !QuitRequested && !SessionEnding)
            {
                log.LogError("Core keeps exiting; not relaunching it again");
            }

            return;
        }

        log.LogWarning("Core is gone; relaunching it in {Seconds} s", delay.TotalSeconds);
        await Task.Delay(delay, ct);
        if (QuitRequested || SessionEnding || SingleInstance.IsHeld(SingleInstance.CoreName))
        {
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Aldren.Core.exe");
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(path, "--relaunched") { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
            log.LogInformation("Relaunched Core");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            log.LogError("Couldn't relaunch Core from {Path}: {Message}", path, ex.Message);
        }
    }
}
