using Aldren.Common;
using Aldren.Common.Logging;
using Aldren.Core;
using Aldren.Core.Assistant;
using Aldren.Core.Orchestration;
using Aldren.Core.Ipc;
using Aldren.Core.Maintenance;
using Aldren.Core.Settings;
using Aldren.Core.Startup;
using Aldren.Core.Supervision;
using Aldren.Core.Tools;
using Aldren.Core.Voice;
using Aldren.Voice;
using Aldren.Assistant;
using Aldren.Memory;
using Aldren.Presence;
using Aldren.Inference;
using Aldren.Windows.Apps;
using Aldren.Security.Audit;
using Aldren.Security.Gateway;
using Aldren.Security.Permissions;
using Aldren.Security.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Serilog;

using var instance = SingleInstance.TryAcquire(SingleInstance.CoreName);
if (instance is null)
{
    // Another Core is already running.
    return 0;
}

// Window and UI Automation coordinates in physical pixels on every monitor (Phase 5); a process setting, nothing persistent.
Aldren.Windows.Layouts.WindowManager.BecomeDpiAware();

var paths = AldrenPaths.FromEnvironment();
Directory.CreateDirectory(paths.SettingsDirectory);
DataFolderAcl.RestrictToCurrentUser(paths.DataDirectory);
Log.Logger = LogSetup.Configure(new LoggerConfiguration(), paths, "core").CreateLogger();

try
{
    var startedAtLogon = args.Contains(StartupEntry.StartupFlag);
    var appIndex = Array.IndexOf(args, "--app");
    var appPath = appIndex >= 0 && appIndex + 1 < args.Length
        ? Path.GetFullPath(args[appIndex + 1])
        : Path.Combine(AppContext.BaseDirectory, "Aldren.App.exe");
    Log.Information("Core starting (process {ProcessId}, at logon: {AtLogon}, App: {AppPath})", Environment.ProcessId, startedAtLogon, appPath);

    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = [],
        ContentRootPath = AppContext.BaseDirectory,
    });
    builder.Services.AddSerilog();
    builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(10));

    builder.Services.AddSingleton(paths);
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton(new SupervisorOptions(startedAtLogon, appPath, paths.LogsDirectory));
    builder.Services.AddSingleton<IProcessLauncher, ProcessLauncher>();
    builder.Services.AddSingleton<AppConnections>();
    builder.Services.AddSingleton<AppSupervisor>();
    builder.Services.AddSingleton<CoreService>();
    builder.Services.AddSingleton<IConfirmationBroker, AppConfirmationBroker>();

    builder.Services.AddSingleton(SettingsMigrator.Default);
    builder.Services.AddSingleton<SettingsStore>();
    builder.Services.AddSingleton<IRegistryValues, CurrentUserRegistry>();
    builder.Services.AddSingleton<StartupEntry>();

    builder.Services.AddSingleton(PermissionCatalog.Phase5);
    builder.Services.AddSingleton<IPermissionStore>(sp => new PermissionStore(paths.PermissionsFile, sp.GetRequiredService<PermissionCatalog>()));
    builder.Services.AddSingleton<IAuditLog>(new SqliteAuditLog(paths.DatabasePath));
    builder.Services.AddSingleton<ITool, GetTimeTool>();
    builder.Services.AddSingleton<ITool, DiagnosticsConfirmTool>();
    builder.Services.AddSingleton<ITool, GetDateTool>();
    builder.Services.AddSingleton<ITool, CalculateTool>();
    builder.Services.AddSingleton<ITool, StartTimerTool>();
    builder.Services.AddSingleton<ITool, LaunchAppTool>();
    builder.Services.AddSingleton<ITool, FocusAppTool>();
    builder.Services.AddSingleton<ITool, CloseAppTool>();
    builder.Services.AddSingleton<ITool, SetVolumeTool>();
    builder.Services.AddSingleton<ITool, ChangeVolumeTool>();
    builder.Services.AddSingleton<ITool, MuteTool>();
    builder.Services.AddSingleton<ITool, MediaControlTool>();
    builder.Services.AddSingleton<ITool, DelegateToActionTool>();
    builder.Services.AddSingleton<ITool, RequestResearchTool>();
    builder.Services.AddSingleton<ITool, RememberTool>();
    builder.Services.AddSingleton<ITool, RecallTool>();
    builder.Services.AddSingleton<ITool, ForgetTool>();
    builder.Services.AddSingleton<ITool, CreateReminderTool>();
    builder.Services.AddSingleton<ITool, ListRemindersTool>();
    builder.Services.AddSingleton<ITool, CancelReminderTool>();
    builder.Services.AddSingleton<ITool, SnoozeReminderTool>();
    builder.Services.AddSingleton<ITool, AddTaskTool>();
    builder.Services.AddSingleton<ITool, ListTasksTool>();
    builder.Services.AddSingleton<ITool, CompleteTaskTool>();
    builder.Services.AddSingleton<ITool, DailyBriefingTool>();
    builder.Services.AddSingleton<ITool, PresenceStatusTool>();
    builder.Services.AddSingleton<ITool, SetHomeNetworkTool>();
    builder.Services.AddSingleton<ITool, ImHomeTool>();
    builder.Services.AddSingleton<ITool, ListWindowsTool>();
    builder.Services.AddSingleton<ITool, MoveWindowTool>();
    builder.Services.AddSingleton<ITool, SaveLayoutTool>();
    builder.Services.AddSingleton<ITool, ApplyLayoutTool>();
    builder.Services.AddSingleton<ITool, ListLayoutsTool>();
    builder.Services.AddSingleton<ITool, FindFilesTool>();
    builder.Services.AddSingleton<ITool, RecentFilesTool>();
    builder.Services.AddSingleton<ITool, DownloadsTool>();
    builder.Services.AddSingleton<ITool, VsCodeRecentTool>();
    builder.Services.AddSingleton<ITool, OpenFileTool>();
    builder.Services.AddSingleton<ITool, ReadDocumentTool>();
    builder.Services.AddSingleton<ITool, MoveFileTool>();
    builder.Services.AddSingleton<ITool, RenameFileTool>();
    builder.Services.AddSingleton<ITool, DeleteFileTool>();
    builder.Services.AddSingleton<ITool, FindDuplicatesTool>();
    builder.Services.AddSingleton<ITool, UndoFileTool>();
    builder.Services.AddSingleton<ITool, LookAtScreenTool>();
    builder.Services.AddSingleton<ITool, FindControlTool>();
    builder.Services.AddSingleton<ITool, PressControlTool>();
    builder.Services.AddSingleton<ITool, TypeIntoControlTool>();
    builder.Services.AddSingleton<ITool, OperateControlTool>();
    builder.Services.AddSingleton<ITool, ReadClipboardTool>();
    builder.Services.AddSingleton<ITool, CopyTextTool>();

    builder.Services.AddSingleton(new InferenceOptions());
    builder.Services.AddSingleton<InferenceHost>();
    builder.Services.AddSingleton<IInferenceHost>(sp => sp.GetRequiredService<InferenceHost>());
    builder.Services.AddSingleton<ResidencyManager>();
    builder.Services.AddSingleton<AppIndex>();
    builder.Services.AddSingleton<TimerService>();
    builder.Services.AddSingleton<ICloudFallback, CloudFallback>();
    builder.Services.AddSingleton<Aldren.Security.Secrets.ISecretStore, Aldren.Security.Secrets.CredentialManagerSecretStore>();
    builder.Services.AddSingleton<Aldren.Cloud.IProcessJob>(sp => sp.GetRequiredService<InferenceHost>());
    builder.Services.AddSingleton(sp => new Aldren.Cloud.GatewayOptions(
        Path.Combine(sp.GetRequiredService<InferenceOptions>().DataRoot, "gateway"),
        Path.Combine(paths.DataDirectory, "gateway", "freeapi.db"),
        Path.Combine(paths.LogsDirectory, "gateway.log")));
    builder.Services.AddSingleton(sp => new Aldren.Cloud.GatewaySupervisor(
        sp.GetRequiredService<Aldren.Cloud.GatewayOptions>(), sp.GetRequiredService<Aldren.Security.Secrets.ISecretStore>(),
        sp.GetRequiredService<Aldren.Cloud.IProcessJob>(), () => sp.GetRequiredService<SettingsStore>().Current.Cloud.Enabled,
        sp.GetRequiredService<ILogger<Aldren.Cloud.GatewaySupervisor>>()));
    builder.Services.AddSingleton<Aldren.Cloud.ICloudIndicator, AppCloudIndicator>();
    builder.Services.AddSingleton(sp => new Aldren.Cloud.CloudPolicy(
        sp.GetRequiredService<Aldren.Cloud.GatewaySupervisor>(), sp.GetRequiredService<IConfirmationBroker>(), sp.GetRequiredService<IAuditLog>(),
        sp.GetRequiredService<Aldren.Cloud.ICloudIndicator>(), () => sp.GetRequiredService<SettingsStore>().Current.Cloud.Enabled,
        sp.GetRequiredService<ILogger<Aldren.Cloud.CloudPolicy>>()));
    builder.Services.AddSingleton<Aldren.Research.ResearchPipeline>();
    builder.Services.AddSingleton<ResearchBrain>();
    builder.Services.AddSingleton(new VoiceOptions());
    builder.Services.AddSingleton<VoiceLoop>();

    builder.Services.AddSingleton(new MemoryStore(paths.DatabasePath));
    builder.Services.AddSingleton(sp => new AssistantStore(paths.DatabasePath, sp.GetRequiredService<TimeProvider>()));
    builder.Services.AddSingleton(new PresenceStore(paths.DatabasePath));
    builder.Services.AddSingleton<Briefing>();
    builder.Services.AddSingleton<IReminderSink, ReminderDelivery>();
    builder.Services.AddSingleton<ReminderScheduler>();
    builder.Services.AddSingleton<PresenceMonitor>();
    builder.Services.AddSingleton<Aldren.Perception.UiaService>();
    builder.Services.AddSingleton<Aldren.Core.Perception.ScreenReader>();
    builder.Services.AddSingleton<ActionBrain>();
    builder.Services.AddSingleton(sp => new Aldren.Windows.Layouts.LayoutStore(Path.Combine(paths.DataDirectory, "layouts.json"), sp.GetRequiredService<AppIndex>()));
    builder.Services.AddSingleton(new Aldren.Windows.Files.FileOperations(paths.DatabasePath));
    builder.Services.AddSingleton<ToolRegistry>();
    builder.Services.AddSingleton<IToolPolicy, TierPolicy>();
    builder.Services.AddSingleton<IToolPolicy, TaintPolicy>();
    builder.Services.AddSingleton<ToolGateway>();
    builder.Services.AddSingleton<Orchestrator>();

    builder.Services.AddHostedService<CoreRpcServer>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AppSupervisor>());
    builder.Services.AddHostedService<AuditRetentionService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ResidencyManager>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<VoiceLoop>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ReminderScheduler>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<PresenceMonitor>());
    builder.Services.AddHostedService<PresenceCoordinator>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<Aldren.Cloud.GatewaySupervisor>());

    using var host = builder.Build();
    var supervisor = host.Services.GetRequiredService<AppSupervisor>();
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

    // Logoff or shutdown: stop cleanly and restart nothing.
    SessionEndingEventHandler onSessionEnding = (_, e) =>
    {
        Log.Information("Session ending ({Reason}); stopping", e.Reason);
        supervisor.RequestShutdown();
        lifetime.StopApplication();
    };
    SystemEvents.SessionEnding += onSessionEnding;
    try
    {
        await host.RunAsync();
    }
    finally
    {
        SystemEvents.SessionEnding -= onSessionEnding;
    }

    Log.Information("Core stopped");
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Core failed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
