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
    TimeProvider time) : IAldrenCore
{
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
