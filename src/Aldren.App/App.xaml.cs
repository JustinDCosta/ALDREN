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

    internal HotkeyService PushToTalk { get; private set; } = null!;

    /// <summary>This session's conversation, typed and spoken, shown in the conversation window. In memory only (memory is Phase 4).</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> Conversation { get; } = [];

    /// <summary>True from the first sound of a spoken reply until it ends or is stopped.</summary>
    public bool IsSpeaking { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="IsSpeaking"/> changes.</summary>
    public event Action<bool>? SpeakingChanged;

    private StopKey _stopKey = null!;
    private int? _replyEntry;

    /// <summary>The Stop buttons and Escape: ALDREN goes quiet at once and the voice turn ends.</summary>
    public void StopSpeaking()
    {
        if (Core is { } core)
        {
            _ = core.StopSpeakingAsync(CancellationToken.None);
        }
    }

    /// <summary>Voice states from Core: the conversation log, the speaking flag (Stop button, Escape) and the island.</summary>
    private void OnVoiceState(VoiceState state, string text)
    {
        if (state == VoiceState.Thinking)
        {
            Conversation.Add("You (voice): " + text);
            _replyEntry = null;
        }
        else if (state == VoiceState.Speaking && text.Length > 0)
        {
            // The reply arrives twice: what was written when the first sound played, then all of it. One entry.
            if (_replyEntry is int index && index < Conversation.Count)
            {
                Conversation[index] = "ALDREN: " + text;
            }
            else
            {
                Conversation.Add("ALDREN: " + text);
                _replyEntry = Conversation.Count - 1;
            }
        }

        var speaking = state == VoiceState.Speaking;
        if (speaking != IsSpeaking)
        {
            IsSpeaking = speaking;
            _stopKey.SetActive(speaking);
            SpeakingChanged?.Invoke(speaking);
        }

        Island.ShowVoice(state, text);
    }

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
        PushToTalk.Apply(settings.Voice.PushToTalkHotkey);
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
        Island.Submit = async text =>
        {
            Conversation.Add("You: " + text);
            var reply = Core is { } core
                ? await core.SubmitTextAsync(text, CancellationToken.None)
                : "I'm not connected to ALDREN's background service right now.";
            Conversation.Add("ALDREN: " + reply);
            return reply;
        };
        Island.ExpandRequested += ShowExpanded;
        Hotkeys = new HotkeyService(_host, _loggers.CreateLogger<HotkeyService>());
        Hotkeys.Pressed += () => _ui.TryEnqueue(() => RunLogged(Island.ToggleText, "hotkey"));
        Hotkeys.Apply(AldrenSettings.Default.General.Hotkey);
        PushToTalk = new HotkeyService(_host, _loggers.CreateLogger<HotkeyService>(), id: 2, allowFallback: false);
        PushToTalk.Pressed += () => _ui.TryEnqueue(() =>
        {
            if (Core is { } core)
            {
                _ = core.PushToTalkAsync(CancellationToken.None);
            }
        });
        PushToTalk.Apply(AldrenSettings.Default.Voice.PushToTalkHotkey);
        _stopKey = new StopKey(_host, _loggers.CreateLogger<StopKey>());
        _stopKey.Pressed += () => _ui.TryEnqueue(StopSpeaking);
        Island.StopRequested += StopSpeaking;

        _watchdog = new CoreWatchdog(new CoreWatchdogPolicy(new RestartBackoff(TimeProvider.System)), _loggers.CreateLogger<CoreWatchdog>());
        _client = new CoreClient(
            new AppCallbacks(
                ConfirmAsync,
                (title, message) => _ui.TryEnqueue(() => _tray.ShowNotification(title, message)),
                (state, text) => _ui.TryEnqueue(() => RunLogged(() => OnVoiceState(state, text), "voice state"))),
            _watchdog, _loggers.CreateLogger<CoreClient>());
        _client.Connected += core => _ui.TryEnqueue(() => _ = OnConnectedAsync(core));
        _client.Disconnected += () => _ui.TryEnqueue(() =>
        {
            _tray.StartWithWindowsAvailable = false;
            // Core went away mid-reply: release Escape and close any voice state it can no longer end.
            OnVoiceState(VoiceState.Idle, "");
        });
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
        // Opened from a click on the never-activated island: make sure it comes to the front rather than only flashing.
        Aldren.App.Island.Native.Focus.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(_expanded));
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
        PushToTalk.Dispose();
        _stopKey.Dispose();
        Island.Close();
        _tray.Dispose();
        _host.Dispose();
        await _client.DisposeAsync();
        await Serilog.Log.CloseAndFlushAsync();
        Exit();
    }
}
