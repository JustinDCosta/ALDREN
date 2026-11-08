using System.Net.NetworkInformation;
using Aldren.Presence.Network;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aldren.Presence;

public sealed record PresenceSnapshot(PresenceState State, bool Override, int HomeNetworks, int PhysicalNetworks, bool CurrentIsHome);

/// <summary>
/// Home or away by network identity (ADR-0010, ARCHITECTURE §15.1): a physical network whose hashed fingerprint is saved as
/// home means Home, at once; anything else must last a 20 s grace period before it counts (Windows briefly reports a
/// reconnecting network as unidentified). Checked at start, on every network change and every minute. "I'm home"
/// overrides until the next network change.
/// </summary>
public sealed class PresenceMonitor(PresenceStore store, TimeProvider time, ILogger<PresenceMonitor> log) : BackgroundService
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _changed = new(0, 1);
    private PresenceState _raw = PresenceState.Unknown;
    private DateTimeOffset _rawSince;
    private volatile bool _override;
    private IReadOnlyList<NetworkObservation> _observations = [];
    private IReadOnlySet<string> _home = new HashSet<string>();

    /// <summary>The confirmed state (after the grace period).</summary>
    public PresenceState State { get; private set; } = PresenceState.Unknown;

    /// <summary>Raised with (before, after) when the confirmed state changes.</summary>
    public event Action<PresenceState, PresenceState>? StateChanged;

    public PresenceSnapshot Snapshot()
    {
        var physical = _observations.Count(o => !o.IsVirtual && o.NetworkId is not null && o.GatewayMac is not null);
        var current = FingerprintMatcher.Evaluate(_observations, _home) == PresenceState.Home;
        return new PresenceSnapshot(State, _override, _home.Count, physical, current);
    }

    /// <summary>Saves the networks you're on now as home. Returns how many physical networks were saved.</summary>
    public async Task<int> SaveCurrentAsHomeAsync(CancellationToken ct)
    {
        var (views, _) = await Task.Run(NetworkSnapshot.Take, ct);
        var fingerprints = views.Select(v => v.Observation)
            .Where(o => !o.IsVirtual && o.NetworkId is not null && o.GatewayMac is not null)
            .Select(o => Fingerprint.Of(o.NetworkId!.Value, o.GatewayMac!))
            .ToList();
        await store.AddHomeAsync(fingerprints, ct);
        Signal();
        return fingerprints.Count;
    }

    public async Task ForgetHomeAsync(CancellationToken ct)
    {
        await store.ClearHomeAsync(ct);
        Signal();
    }

    /// <summary>"I'm home": Home until the next network change.</summary>
    public void OverrideHome()
    {
        _override = true;
        Signal();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NetworkAddressChangedEventHandler onAddress = (_, _) => OnNetworkChanged();
        NetworkAvailabilityChangedEventHandler onAvailability = (_, _) => OnNetworkChanged();
        NetworkChange.NetworkAddressChanged += onAddress;
        NetworkChange.NetworkAvailabilityChanged += onAvailability;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await EvaluateAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Presence check failed");
                }

                // While a change is settling, look again soon; otherwise wait for a network event or the poll.
                var wait = _raw != State ? Settle : Poll;
                await _changed.WaitAsync(wait, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            NetworkChange.NetworkAddressChanged -= onAddress;
            NetworkChange.NetworkAvailabilityChanged -= onAvailability;
        }
    }

    private void OnNetworkChanged()
    {
        // A real network change ends a manual "I'm home".
        _override = false;
        Signal();
    }

    private void Signal()
    {
        try
        {
            _changed.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task EvaluateAsync(CancellationToken ct)
    {
        _home = await store.HomeFingerprintsAsync(ct);
        var (views, _) = await Task.Run(NetworkSnapshot.Take, ct);
        _observations = views.Select(v => v.Observation).ToList();
        var raw = _override ? PresenceState.Home : FingerprintMatcher.Evaluate(_observations, _home);
        var now = time.GetUtcNow();
        if (raw != _raw)
        {
            _raw = raw;
            _rawSince = now;
        }

        var confirmed = raw == PresenceState.Home || now - _rawSince >= Grace ? raw : State;
        if (confirmed != State)
        {
            var before = State;
            State = confirmed;
            log.LogInformation("Presence: {Before} → {After}{Override}", before, confirmed, _override ? " (you said you're home)" : "");
            StateChanged?.Invoke(before, confirmed);
        }
    }
}
