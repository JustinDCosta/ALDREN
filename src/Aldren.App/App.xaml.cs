using System.Diagnostics;
using System.Reflection;
using Aldren.App.Expanded;
using Aldren.App.Hotkeys;
using Aldren.App.Ipc;
using Aldren.App.Island;
using Aldren.App.Settings;
using Aldren.App.Tray;
using Aldren.Common;
using Aldren.Common.Logging;
using Aldren.Common.Shell;
using Aldren.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using Serilog.Extensions.Logging;

namespace Aldren.App;

/// <summary>Starts hidden: only the tray icon shows until you open a window.</summary>
public partial class ShellApp : Application
{
    public static string Version { get; } =
        typeof(ShellApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public static string IconPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "aldren.ico");

    private DispatcherQueue _ui = null!;
    private AldrenPaths _paths = null!;
    private SerilogLoggerFactory _loggers = null!;
    private Microsoft.Extensions.Logging.ILogger _log = null!;
    private HostWindow _host = null!;
    private TrayIcon _tray = null!;
    private CoreWatchdog _watchdog = null!;
    private CoreClient _client = null!;
    private ExpandedWindow? _expanded;
    private SettingsWindow? _settings;

    public ShellApp()
    {
        InitializeComponent();
    }

    public static ShellApp Instance => (ShellApp)Current;

    public IAldrenCore? Core => _client.Core;

    public string LogsDirectory => _paths.LogsDirectory;

    public Microsoft.Extensions.Logging.ILogger Log => _log;

    public IslandWindow Island { get; private set; } = null!;

    internal HotkeyService Hotkeys { get; private set; } = null!;

    private async Task OnConnectedAsync(IAldrenCore core)
    {
        await RefreshTrayAsync();
        try
        {
            ApplySettings(await core.GetSettingsAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            _log.LogWarning("Couldn't read settings from Core: {Message}", ex.Message);
        }
    }

    /// <summary>Applies settings the App acts on itself: the hotkey and whether the island shows on stream.</summary>
    public void ApplySettings(AldrenSettings settings)
    {
        Hotkeys.Apply(settings.General.Hotkey);
        Island.SetShowOnStream(settings.General.ShowOnStream);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        _ui = DispatcherQueue.GetForCurrentThread();
        _paths = AldrenPaths.FromEnvironment();
        Serilog.Log.Logger = LogSetup.Configure(new LoggerConfiguration(), _paths, "app").CreateLogger();
        _loggers = new SerilogLoggerFactory(Serilog.Log.Logger);
        _log = _loggers.CreateLogger("Aldren.App");
        _log.LogInformation("App starting (process {ProcessId})", Environment.ProcessId);
        UnhandledException += (_, e) => _log.LogError(e.Exception, "Unhandled UI exception");

        _host = new HostWindow();
        _host.Message += OnHostMessage;
        _host.HandlerFailed += ex => _log.LogError(ex, "Host window handler failed");
        _tray = new TrayIcon(_host, IconPath);
        // Leave the window procedure before touching WinUI.
        _tray.Command += command => _ui.TryEnqueue(() => RunLogged(() => OnTrayCommand(command), $"tray command {command}"));

        Island = new IslandWindow(_loggers.CreateLogger<IslandWindow>());
        Island.Submit = text => Core is { } core
            ? core.SubmitTextAsync(text, CancellationToken.None)
            : Task.FromResult("I'm not connected to ALDREN's background service right now.");
        Island.ExpandRequested += ShowExpanded;
        Hotkeys = new HotkeyService(_host, _loggers.CreateLogger<HotkeyService>());
        Hotkeys.Pressed += () => _ui.TryEnqueue(() => RunLogged(Island.ToggleText, "hotkey"));
        Hotkeys.Apply(AldrenSettings.Default.General.Hotkey);

        _watchdog = new CoreWatchdog(new CoreWatchdogPolicy(new RestartBackoff(TimeProvider.System)), _loggers.CreateLogger<CoreWatchdog>());
        _client = new CoreClient(new AppCallbacks(ConfirmAsync, (title, message) => _ui.TryEnqueue(() => _tray.ShowNotification(title, message))), _watchdog, _loggers.CreateLogger<CoreClient>());
        _client.Connected += core => _ui.TryEnqueue(() => _ = OnConnectedAsync(core));
        _client.Disconnected += () => _ui.TryEnqueue(() => _tray.StartWithWindowsAvailable = false);
        _client.Start();
    }

    private void OnHostMessage(uint msg, nuint wParam, nint lParam)
    {
        if (msg is HostWindow.WM_QUERYENDSESSION or HostWindow.WM_ENDSESSION)
        {
            _watchdog.SessionEnding = true;
            if (msg == HostWindow.WM_ENDSESSION && wParam != 0)
            {
                _log.LogInformation("Session ending; exiting");
                Serilog.Log.CloseAndFlush();
            }
        }
    }

    private void RunLogged(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed: {What}", what);
        }
    }

    private void OnTrayCommand(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.Open:
                ShowExpanded();
                break;
            case TrayCommand.Settings:
                ShowSettings();
                break;
            case TrayCommand.StartWithWindows:
                _ = ToggleStartWithWindowsAsync();
                break;
            case TrayCommand.Quit:
                _ = QuitAsync();
                break;
        }
    }

    public async Task RefreshTrayAsync()
    {
        if (Core is not { } core)
        {
            return;
        }

        try
        {
            var status = await core.GetStatusAsync(CancellationToken.None);
            _tray.StartWithWindows = status.StartWithWindows == StartupState.On;
            _tray.StartWithWindowsAvailable = status.StartWithWindows != StartupState.DisabledInWindows;
        }
        catch (Exception ex)
        {
            _log.LogWarning("Couldn't read Core's status: {Message}", ex.Message);
        }
    }

    private async Task ToggleStartWithWindowsAsync()
    {
        if (Core is not { } core)
        {
            return;
        }

        try
        {
            await core.SetStartWithWindowsAsync(!_tray.StartWithWindows, CancellationToken.None);
            await RefreshTrayAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning("Couldn't change Start with Windows: {Message}", ex.Message);
        }
    }

    public void ShowExpanded()
    {
        _expanded ??= CreateWindow(new ExpandedWindow(), () => _expanded = null);
        _expanded.Activate();
    }

    public void ShowSettings()
    {
        _settings ??= CreateWindow(new SettingsWindow(), () => _settings = null);
        _settings.Activate();
    }

    private static T CreateWindow<T>(T window, Action onClosed) where T : Window
    {
        window.AppWindow.SetIcon(IconPath);
        window.Closed += (_, _) => onClosed();
        return window;
    }

    /// <summary>Core asks: the island shows the confirm state. Any failure counts as No.</summary>
    private Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.TryEnqueue(async () =>
        {
            try
            {
                answer.TrySetResult(await Island.ConfirmAsync(request, ct));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Confirmation prompt failed");
                answer.TrySetResult(false);
            }
        });
        return answer.Task;
    }

    public void OpenLogsFolder()
    {
        Directory.CreateDirectory(LogsDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LogsDirectory}\"") { UseShellExecute = true })?.Dispose();
    }

    public async Task QuitAsync()
    {
        _log.LogInformation("Quit requested");
        _watchdog.QuitRequested = true;
        if (Core is { } core)
        {
            try
            {
                await core.ShutdownAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                _log.LogWarning("Core didn't confirm the shutdown: {Message}", ex.Message);
            }
        }

        _expanded?.Close();
        _settings?.Close();
        Hotkeys.Dispose();
        Island.Close();
        _tray.Dispose();
        _host.Dispose();
        await _client.DisposeAsync();
        await Serilog.Log.CloseAndFlushAsync();
        Exit();
    }
}
