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
using Aldren.Inference;
using Aldren.Windows.Apps;
using Aldren.Security.Audit;
using Aldren.Security.Gateway;
using Aldren.Security.Permissions;
using Aldren.Security.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using Serilog;

using var instance = SingleInstance.TryAcquire(SingleInstance.CoreName);
if (instance is null)
{
    // Another Core is already running.
    return 0;
}

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

    builder.Services.AddSingleton(PermissionCatalog.Phase2);
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

    builder.Services.AddSingleton(new InferenceOptions());
    builder.Services.AddSingleton<InferenceHost>();
    builder.Services.AddSingleton<IInferenceHost>(sp => sp.GetRequiredService<InferenceHost>());
    builder.Services.AddSingleton<ResidencyManager>();
    builder.Services.AddSingleton<AppIndex>();
    builder.Services.AddSingleton<TimerService>();
    builder.Services.AddSingleton<ICloudFallback, NoCloudFallback>();
    builder.Services.AddSingleton<ToolRegistry>();
    builder.Services.AddSingleton<IToolPolicy, TierPolicy>();
    builder.Services.AddSingleton<IToolPolicy, TaintPolicy>();
    builder.Services.AddSingleton<ToolGateway>();
    builder.Services.AddSingleton<Orchestrator>();

    builder.Services.AddHostedService<CoreRpcServer>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AppSupervisor>());
    builder.Services.AddHostedService<AuditRetentionService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ResidencyManager>());

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
