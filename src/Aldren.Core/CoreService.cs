using System.Reflection;
using Aldren.Contracts;
using Aldren.Core.Orchestration;
using Aldren.Core.Settings;
using Aldren.Core.Startup;
using Aldren.Core.Supervision;
using Aldren.Core.Voice;
using Aldren.Security.Audit;
using Aldren.Security.Gateway;
using Aldren.Security.Permissions;
using Aldren.Security.Tools;
using Microsoft.Extensions.Hosting;

namespace Aldren.Core;

/// <summary>What the App reaches over the pipe.</summary>
public sealed class CoreService(
    Orchestrator orchestrator,
    VoiceLoop voice,
    SettingsStore settings,
    IPermissionStore permissions,
    IAuditLog audit,
    StartupEntry startup,
    AppSupervisor supervisor,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    Aldren.Memory.MemoryStore memory,
    Aldren.Assistant.AssistantStore assistant,
    Aldren.Presence.PresenceMonitor presence,
    Aldren.Cloud.GatewaySupervisor gateway) : IAldrenCore
{
    public Task<CloudStatusInfo> GetCloudStatusAsync(CancellationToken cancellationToken) => Task.FromResult(CloudStatus());

    public Task<CloudStatusInfo> RecheckCloudAsync(CancellationToken cancellationToken)
    {
        gateway.Recheck();
        return Task.FromResult(CloudStatus());
    }

    private CloudStatusInfo CloudStatus()
    {
        var s = gateway.Status;
        var chains = s.Drift?.Chains.Select(c => c.State == Aldren.Cloud.ChainState.Ok
            ? $"{c.Name}: ready ({string.Join(", ", c.Models.Take(4))})"
            : $"{c.Name}: {c.State.ToString().ToLowerInvariant()}, {c.Problem}").ToList() ?? [];
        return new CloudStatusInfo(settings.Current.Cloud.Enabled, s.Running, s.Port, s.Problem, chains,
            s.Drift?.KeysOutsideAllowlist ?? [], settings.Current.Cloud.QuickFallback);
    }

    public async Task<IReadOnlyList<MemoryItem>> GetMemoriesAsync(string? query, CancellationToken cancellationToken) =>
        (await memory.SearchAsync(query, 200, cancellationToken)).Select(m => new MemoryItem(m.Id, m.Text, m.Kind, m.Created)).ToList();

    public async Task DeleteMemoryAsync(long id, CancellationToken cancellationToken)
    {
        await memory.DeleteAsync(id, cancellationToken);
        await AuditUserActionAsync("delete_memory", $$"""{"id":{{id}}}""", "Deleted from Settings > Memory");
    }

    public async Task<IReadOnlyList<ReminderItem>> GetRemindersAsync(CancellationToken cancellationToken) =>
        (await assistant.ActiveRemindersAsync(cancellationToken))
            .Select(r => new ReminderItem(r.Id, r.Text, Tools.ListRemindersTool.Describe(r, assistant))).ToList();

    public async Task CancelReminderAsync(long id, CancellationToken cancellationToken)
    {
        await assistant.CancelReminderAsync(id, cancellationToken);
        await AuditUserActionAsync("cancel_reminder", $$"""{"id":{{id}}}""", "Cancelled from Settings > Reminders & tasks");
    }

    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(CancellationToken cancellationToken) =>
        (await assistant.OpenTasksAsync(cancellationToken)).Select(t => new TaskItem(t.Id, t.Text)).ToList();

    public async Task CompleteTaskAsync(long id, CancellationToken cancellationToken)
    {
        await assistant.CompleteTaskAsync(id, cancellationToken);
        await AuditUserActionAsync("complete_task", $$"""{"id":{{id}}}""", "Completed from Settings > Reminders & tasks");
    }

    public Task<PresenceInfo> GetPresenceAsync(CancellationToken cancellationToken)
    {
        var s = presence.Snapshot();
        return Task.FromResult(new PresenceInfo(s.State.ToString(), s.Override, s.HomeNetworks, s.CurrentIsHome));
    }

    public async Task<string> SetHomeNetworkAsync(CancellationToken cancellationToken)
    {
        var saved = await presence.SaveCurrentAsHomeAsync(cancellationToken);
        await AuditUserActionAsync("set_home_network", "{}", $"Saved from Settings > Home & away ({saved} network(s))");
        return saved == 0
            ? "No network could be identified, so nothing was saved. Is the PC connected?"
            : "Saved: this network is now home.";
    }

    public async Task ForgetHomeNetworksAsync(CancellationToken cancellationToken)
    {
        await presence.ForgetHomeAsync(cancellationToken);
        await AuditUserActionAsync("forget_home_networks", "{}", "Cleared from Settings > Home & away");
    }

    /// <summary>Changes you make in Settings are audited like tool calls, as user-origin actions.</summary>
    private Task AuditUserActionAsync(string action, string arguments, string detail) =>
        audit.AppendAsync(
            new AuditRecord(
                time.GetUtcNow(), Guid.NewGuid().ToString("N"), action, RiskTier.Reversible, CallOrigin.User, arguments,
                AuditStage.Finished, ToolOutcomeStatus.Succeeded, detail, TimeSpan.Zero, Confirmed: false, Tainted: false),
            CancellationToken.None);

    private static readonly string Version =
        typeof(CoreService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    private readonly DateTimeOffset _startedAt = time.GetUtcNow();

    public static string ExecutablePath => Environment.ProcessPath!;

    public Task<CoreStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new CoreStatus(Version, Environment.ProcessId, _startedAt, startup.GetState(ExecutablePath)));

    public Task<string> SubmitTextAsync(string text, CancellationToken cancellationToken) =>
        orchestrator.HandleAsync(text, cancellationToken);

    public Task PushToTalkAsync(CancellationToken cancellationToken) => voice.PushToTalkAsync();

    public Task StopSpeakingAsync(CancellationToken cancellationToken) => voice.StopSpeakingAsync();

    public Task<AldrenSettings> GetSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(settings.Current);

    public Task<SettingsResult> UpdateSettingsAsync(AldrenSettings newSettings, CancellationToken cancellationToken) =>
        settings.SaveAsync(newSettings, cancellationToken);

    public Task<IReadOnlyList<PermissionState>> GetPermissionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(permissions.List());

    public async Task SetPermissionAsync(string permission, bool granted, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        string? failure = null;
        try
        {
            await permissions.SetAsync(permission, granted, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            failure = ex.Message;
            throw;
        }
        finally
        {
            await audit.AppendAsync(
                new AuditRecord(
                    time.GetUtcNow(), Guid.NewGuid().ToString("N"), "set_permission", RiskTier.Impactful, CallOrigin.User,
                    $$"""{"permission":"{{permission}}","granted":{{(granted ? "true" : "false")}}}""",
                    AuditStage.Finished,
                    failure is null ? ToolOutcomeStatus.Succeeded : ToolOutcomeStatus.Failed,
                    failure ?? $"{permission}: {(granted ? "on" : "off")}",
                    time.GetElapsedTime(started), Confirmed: false, Tainted: false),
                CancellationToken.None);
        }
    }

    public Task<IReadOnlyList<AuditEntry>> GetAuditAsync(AuditQuery query, CancellationToken cancellationToken) =>
        audit.QueryAsync(query, cancellationToken);

    public Task SetStartWithWindowsAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (enabled)
        {
            startup.Enable(ExecutablePath);
        }
        else
        {
            startup.Disable();
        }

        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        supervisor.RequestShutdown();
        lifetime.StopApplication();
        return Task.CompletedTask;
    }
}
