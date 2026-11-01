using Aldren.Common;
using Aldren.Core.Ipc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Aldren.Core.Supervision;

public sealed record SupervisorOptions(bool StartedAtLogon, string AppPath, string LogsDirectory);

/// <summary>
/// Keeps the App running: launches it (or adopts one already running), and restarts it after each exit with
/// <see cref="RestartBackoff"/>, but never after <see cref="RequestShutdown"/>.
/// </summary>
public sealed class AppSupervisor(
    SupervisorOptions options,
    IProcessLauncher launcher,
    AppConnections connections,
    TimeProvider time,
    ILogger<AppSupervisor> log) : BackgroundService
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan AdoptionTimeout = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _shutdown = new();

    public bool ShutdownRequested => _shutdown.IsCancellationRequested;

    /// <summary>Quit or logoff: stop supervising, restart nothing.</summary>
    public void RequestShutdown()
    {
        log.LogInformation("Shutdown requested; the App won't be restarted");
        _shutdown.Cancel();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _shutdown.Token);
        var ct = linked.Token;
        try
        {
            if (options.StartedAtLogon)
            {
                log.LogInformation("Started at logon; waiting {Seconds} s before starting the App", StartupDelay.TotalSeconds);
                await Task.Delay(StartupDelay, time, ct);
            }

            var backoff = new RestartBackoff(time);
            while (!ct.IsCancellationRequested)
            {
                backoff.MarkStarted();
                using (var app = await StartOrAdoptAsync(ct))
                {
                    if (app is not null)
                    {
                        await app.WaitForExitAsync(ct);
                        log.LogWarning("The App (process {ProcessId}) exited", app.Id);
                    }
                }

                if (ct.IsCancellationRequested)
                {
                    break;
                }

                if (backoff.NextDelay() is not { } delay)
                {
                    log.LogError("The App keeps exiting; giving up until ALDREN is restarted");
                    ShowGiveUpMessage();
                    return;
                }

                log.LogInformation("Restarting the App in {Seconds} s", delay.TotalSeconds);
                await Task.Delay(delay, time, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task<IChildProcess?> StartOrAdoptAsync(CancellationToken ct)
    {
        if (SingleInstance.IsHeld(SingleInstance.AppName))
        {
            log.LogInformation("An App is already running; adopting it");
            try
            {
                var connection = await connections.WaitAsync(ct).WaitAsync(AdoptionTimeout, time, ct);
                return launcher.Attach(connection.ProcessId);
            }
            catch (TimeoutException)
            {
                log.LogWarning("The running App didn't connect within {Seconds} s", AdoptionTimeout.TotalSeconds);
                return null;
            }
        }

        try
        {
            var app = launcher.Start(options.AppPath, "", breakAwayFromJob: true);
            log.LogInformation("Started the App (process {ProcessId})", app.Id);
            return app;
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            log.LogError("Couldn't start the App from {Path}: {Message}", options.AppPath, ex.Message);
            return null;
        }
    }

    private void ShowGiveUpMessage() =>
        _ = Task.Run(() => PInvoke.MessageBox(
            HWND.Null,
            $"ALDREN's window keeps closing unexpectedly, so ALDREN stopped restarting it.\n\nThe logs are in:\n{options.LogsDirectory}",
            "ALDREN",
            MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONWARNING | MESSAGEBOX_STYLE.MB_SETFOREGROUND));

    public override void Dispose()
    {
        _shutdown.Dispose();
        base.Dispose();
    }
}
