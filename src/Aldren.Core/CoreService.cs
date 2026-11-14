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
    Aldren.Cloud.GatewaySupervisor gateway,
    Aldren.Browser.BrowserBridge browser,
    Aldren.Browser.SitePolicy sites,
    Aldren.Integrations.Google.GoogleAccount google,
    Aldren.Core.Focus.FocusService focus,
    Aldren.Inference.ResidencyManager residency,
    Aldren.Inference.InferenceOptions inference,
    ModeState modes,
    Tools.RoutineStore routines,
    Aldren.Common.AldrenPaths paths,
    Ipc.AppConnections connections) : IAldrenCore
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

    /// <summary>
    /// Typed text in, the reply out. While the model writes, what it has so far goes to the App (at most every 60 ms) so
    /// the island and the conversation window fill in live; the complete reply is still this call's result.
    /// </summary>
    public Task<string> SubmitTextAsync(string text, CancellationToken cancellationToken)
    {
        if (connections.Current is not { } app)
        {
            return orchestrator.HandleAsync(text, cancellationToken);
        }

        var written = new System.Text.StringBuilder();
        var lastSent = 0L;
        return orchestrator.HandleAsync(text, cancellationToken, delta =>
        {
            written.Append(delta);
            var now = Environment.TickCount64;
            if (now - lastSent < 60)
            {
                return;
            }

            lastSent = now;
            _ = app.App.ShowReplyProgressAsync(written.ToString(), CancellationToken.None)
                .ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        });
    }

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

    public async Task<BrowserStatusInfo> GetBrowserStatusAsync(CancellationToken cancellationToken) =>
        new(Aldren.Browser.HostRegistration.IsRegistered, await browser.PingAsync(cancellationToken), Path.Combine(AppContext.BaseDirectory, "extension"),
            Aldren.Browser.HostRegistration.ExtensionId, sites.All().Select(s => new SiteLevelItem(s.Key, s.Value.ToString())).OrderBy(s => s.Site).ToList());

    public async Task<string> ConnectBrowserAsync(CancellationToken cancellationToken)
    {
        var manifest = Aldren.Browser.HostRegistration.Register(Path.Combine(AppContext.BaseDirectory, "Aldren.BrowserHost.exe"));
        await AuditSettingAsync("connect_browser", "Registered the browser host for Chrome and Edge (HKCU)", cancellationToken);
        return $"Registered. Now load the extension: chrome://extensions → Developer mode → Load unpacked → {Path.Combine(AppContext.BaseDirectory, "extension")}. (Manifest: {manifest})";
    }

    public async Task DisconnectBrowserAsync(CancellationToken cancellationToken)
    {
        Aldren.Browser.HostRegistration.Unregister();
        await AuditSettingAsync("disconnect_browser", "Removed the browser host registration", cancellationToken);
    }

    public async Task SetSiteLevelAsync(string site, string? level, CancellationToken cancellationToken)
    {
        Aldren.Browser.SiteLevel? parsed = level is null ? null : Enum.Parse<Aldren.Browser.SiteLevel>(level, ignoreCase: true);
        sites.Set(site, parsed);
        await AuditSettingAsync("set_site_level", $"{Aldren.Browser.SitePolicy.Host(site)}: {level ?? "default"}", cancellationToken);
    }

    public Task<GoogleStatusInfo> GetGoogleStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new GoogleStatusInfo(google.HasClient, google.SignedIn));

    public async Task SetGoogleClientAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        google.SetClient(clientId.Trim(), clientSecret.Trim());
        await AuditSettingAsync("set_google_client", "Saved the Google OAuth client in Credential Manager", cancellationToken);
    }

    public async Task<string> SignInGoogleAsync(CancellationToken cancellationToken)
    {
        if (!google.HasClient)
        {
            return "Add your OAuth client ID and secret first (docs/guides/google-oauth.md).";
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await google.CredentialAsync(timeout.Token);
            await AuditSettingAsync("google_sign_in", "Signed in to Google (Calendar, Drive)", cancellationToken);
            return "Signed in. Calendar and Drive are ready.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "Sign-in timed out. Try again and finish it in the browser.";
        }
    }

    public async Task DisconnectGoogleAsync(CancellationToken cancellationToken)
    {
        await google.DisconnectAsync(cancellationToken);
        await AuditSettingAsync("google_disconnect", "Revoked Google access and deleted the token", cancellationToken);
    }

    public async Task<FocusInfo> GetFocusAsync(CancellationToken cancellationToken)
    {
        var stats = await focus.StatsAsync(7, cancellationToken);
        var active = focus.Active;
        return new FocusInfo(active is not null, active?.Goal, active is null ? null : focus.Local(active.Ends), active?.Slips ?? 0,
            stats.Sessions, stats.Minutes, stats.Slips, stats.EndedEarly);
    }

    public async Task EndFocusAsync(CancellationToken cancellationToken) => await focus.EndAsync(early: true, cancellationToken);

    private Task AuditSettingAsync(string action, string summary, CancellationToken ct) =>
        audit.AppendAsync(
            new AuditRecord(
                time.GetUtcNow(), Guid.NewGuid().ToString("N"), action, RiskTier.Impactful, CallOrigin.User, "{}",
                AuditStage.Finished, ToolOutcomeStatus.Succeeded, summary, TimeSpan.Zero, Confirmed: false, Tainted: false),
            CancellationToken.None);

    public async Task<SystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken)
    {
        var s = residency.Status;
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var modelMb = MemoryMb(residency.GpuProcessId) + MemoryMb(residency.CpuProcessId);
        return new SystemInfo(
            s.GpuLoaded ? Path.GetFileNameWithoutExtension(inference.QuickModelPath) : s.CpuLoaded ? Path.GetFileNameWithoutExtension(inference.CpuModelPath) : "",
            s.GpuLoaded, s.CpuLoaded, s.FreeVramMiB, s.OnAc,
            s.GamingOverride switch { true => "on", false => "off", null => "auto" }, s.GamingMode, modes.Silent,
            self.WorkingSet64 / (1024 * 1024), modelMb, (time.GetUtcNow() - _startedAt).TotalHours,
            paths.SettingsDirectory, paths.DataDirectory, inference.DataRoot,
            FileKb(paths.DatabasePath), FolderKb(paths.LogsDirectory),
            await memory.CountAsync(cancellationToken),
            (await assistant.ActiveRemindersAsync(cancellationToken)).Count,
            (await assistant.OpenTasksAsync(cancellationToken)).Count,
            routines.All().Count);

        static long MemoryMb(int? processId)
        {
            if (processId is not { } id)
            {
                return 0;
            }

            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(id);
                return process.WorkingSet64 / (1024 * 1024);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return 0;
            }
        }

        static long FileKb(string path) => File.Exists(path) ? new FileInfo(path).Length / 1024 : 0;

        static long FolderKb(string path)
        {
            try
            {
                return Directory.Exists(path) ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024 : 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }

    public Task NewConversationAsync(CancellationToken cancellationToken) => orchestrator.ResetAsync(cancellationToken);

    public Task SetGamingModeAsync(string mode, CancellationToken cancellationToken) =>
        residency.SetGamingOverrideAsync(mode.ToLowerInvariant() switch { "on" => true, "off" => false, _ => null }, cancellationToken);

    public Task SetSilentAsync(bool silent, CancellationToken cancellationToken)
    {
        modes.Silent = silent;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RoutineItem>> GetRoutinesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RoutineItem>>(routines.All().Select(r => new RoutineItem(r.Name, r.Steps)).ToList());

    public async Task<string> SaveRoutineAsync(string name, string steps, CancellationToken cancellationToken)
    {
        var parsed = Tools.CreateRoutineTool.SplitSteps(steps);
        if (string.IsNullOrWhiteSpace(name) || parsed.Count == 0)
        {
            return "A routine needs a name and at least one step.";
        }

        var routine = routines.Save(name, parsed);
        await AuditUserActionAsync("save_routine", "{}", $"Saved from Settings > Automations ({routine.Steps.Count} steps)");
        return $"Saved \"{routine.Name}\" with {routine.Steps.Count} step{(routine.Steps.Count == 1 ? "" : "s")}.";
    }

    public async Task DeleteRoutineAsync(string name, CancellationToken cancellationToken)
    {
        if (routines.Delete(name))
        {
            await AuditUserActionAsync("delete_routine", "{}", "Deleted from Settings > Automations");
        }
    }

    public async Task<int> DeleteAllMemoriesAsync(CancellationToken cancellationToken)
    {
        var deleted = await memory.DeleteAllAsync(cancellationToken);
        await AuditUserActionAsync("delete_all_memories", "{}", $"Deleted {deleted} memories from Settings > Privacy & data");
        return deleted;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        supervisor.RequestShutdown();
        lifetime.StopApplication();
        return Task.CompletedTask;
    }
}
