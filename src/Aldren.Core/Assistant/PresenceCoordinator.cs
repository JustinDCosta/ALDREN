using System.Globalization;
using Aldren.Core.Settings;
using Aldren.Core.Supervision;
using Aldren.Core.Voice;
using Aldren.Presence;
using Aldren.Presence.Network;
using Aldren.Windows.Audio;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Aldren.Core.Assistant;

/// <summary>
/// The greeting and away mute (ARCHITECTURE §15.1). "Welcome back, boss." plays at sign-in or unlock when Home is
/// confirmed and the last greeting is at least the configured hours old (the first one of the day adds the briefing).
/// Away, ALDREN saves the volume and mutes, unless the output is headphones; back home it restores the sound only if it
/// did the muting and the PC is still muted. Nothing happens until a home network is saved.
/// </summary>
public sealed class PresenceCoordinator(
    PresenceMonitor presence,
    PresenceStore store,
    SettingsStore settings,
    VoiceLoop voice,
    Briefing briefing,
    SupervisorOptions supervisor,
    TimeProvider time,
    ILogger<PresenceCoordinator> log) : BackgroundService
{
    private const string LastGreetingKey = "last_greeting_utc";
    private const string AwayMuteKey = "away_mute";

    private CancellationToken _stopping;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        presence.StateChanged += OnStateChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        try
        {
            if (supervisor.StartedAtLogon)
            {
                _ = GreetWhenHomeAsync("sign-in");
            }

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            presence.StateChanged -= OnStateChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon)
        {
            _ = GreetWhenHomeAsync(e.Reason == SessionSwitchReason.SessionUnlock ? "unlock" : "sign-in");
        }
    }

    private void OnStateChanged(PresenceState before, PresenceState after) => _ = Task.Run(async () =>
    {
        try
        {
            if (presence.Snapshot().HomeNetworks == 0)
            {
                return;
            }

            if (after is PresenceState.Away or PresenceState.Unknown)
            {
                await MuteForAwayAsync();
            }
            else if (after == PresenceState.Home)
            {
                await RestoreAfterAwayAsync();
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Away mute handling failed");
        }
    });

    /// <summary>Gives the network check up to 30 s to settle, then greets if you're home.</summary>
    private async Task GreetWhenHomeAsync(string trigger)
    {
        try
        {
            var p = settings.Current.Presence;
            if (!p.Greeting)
            {
                return;
            }

            for (var i = 0; i < 30 && presence.State != PresenceState.Home; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _stopping);
            }

            if (presence.State != PresenceState.Home || presence.Snapshot().HomeNetworks == 0)
            {
                return;
            }

            var now = time.GetUtcNow();
            var last = await store.GetAsync(LastGreetingKey, _stopping) is { } text
                ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture)
                : (DateTimeOffset?)null;
            if (last is { } l && now - l < TimeSpan.FromHours(p.GreetingAfterHours))
            {
                return;
            }

            var firstToday = last is null || TimeZoneInfo.ConvertTime(last.Value, time.LocalTimeZone).Date < time.GetLocalNow().Date;
            var greeting = "Welcome back, boss.";
            if (firstToday && p.BriefingWithGreeting)
            {
                greeting += " " + await briefing.BuildAsync(_stopping);
            }

            log.LogInformation("Greeting after {Trigger}", trigger);
            await store.SetAsync(LastGreetingKey, now.ToString("O", CultureInfo.InvariantCulture), _stopping);
            await voice.AnnounceAsync(greeting, _stopping);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Greeting failed");
        }
    }

    private async Task MuteForAwayAsync()
    {
        var p = settings.Current.Presence;
        if (!p.AwayMute || await store.GetAsync(AwayMuteKey, _stopping) is not null)
        {
            return;
        }

        if (p.AwayMuteSkipHeadphones && SystemAudio.OutputIsHeadphones())
        {
            log.LogInformation("Away: not muting, the output is headphones");
            return;
        }

        var muted = SystemAudio.GetMute();
        if (muted)
        {
            return;
        }

        await store.SetAsync(AwayMuteKey, SystemAudio.GetVolume().ToString(CultureInfo.InvariantCulture), _stopping);
        SystemAudio.SetMute(true);
        log.LogInformation("Away: muted the PC");
    }

    private async Task RestoreAfterAwayAsync()
    {
        if (await store.GetAsync(AwayMuteKey, _stopping) is not { } saved)
        {
            return;
        }

        await store.SetAsync(AwayMuteKey, null, _stopping);
        // Only undo our own mute: if you unmuted or changed the volume meanwhile, leave it as you set it.
        if (SystemAudio.GetMute() && SystemAudio.GetVolume() == int.Parse(saved, CultureInfo.InvariantCulture))
        {
            SystemAudio.SetMute(false);
            log.LogInformation("Home: restored the sound ALDREN muted");
        }
    }
}
