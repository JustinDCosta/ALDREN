using System.Collections.ObjectModel;
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
using Serilog;
using Serilog.Extensions.Logging;

namespace Aldren.App;

/// <summary>Starts hidden: only the tray icon shows until you open a window.</summary>
public partial class ShellApp : Application
{
    private const int MaxConversationEntries = 400;

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
    private StopKey _stopKey = null!;
    private ConversationEntry? _voiceReply;
    private long _trayRefreshed;

    // Typed requests waiting for their reply, oldest first: Core answers them in order, so progress belongs to the first.
    private readonly List<ConversationEntry> _pendingReplies = [];

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

    /// <summary>This session's conversation, typed and spoken, shown in the conversation window. In memory only.</summary>
    public ObservableCollection<ConversationEntry> Conversation { get; } = [];

    /// <summary>True from the first sound of a spoken reply until it ends or is stopped.</summary>
    public bool IsSpeaking { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="IsSpeaking"/> changes.</summary>
    public event Action<bool>? SpeakingChanged;

    /// <summary>
    /// A typed request, from the island or the conversation window: both turns go into the conversation, the reply fills
    /// in while ALDREN writes it, and the finished reply is returned.
    /// </summary>
    public async Task<string> AskAsync(string text)
    {
        Add(new ConversationEntry(fromUser: true, text));
        var entry = new ConversationEntry(fromUser: false, "", pending: true);
        Add(entry);
        _pendingReplies.Add(entry);
        string reply;
        try
        {
            reply = Core is { } core
                ? await core.SubmitTextAsync(text, CancellationToken.None)
                : "I'm not connected to ALDREN's background service right now. It restarts on its own; try again in a moment.";
        }
        catch (Exception ex)
        {
            _log.LogWarning("A typed request failed: {Message}", ex.Message);
            reply = "Something went wrong: " + ex.Message;
        }
        finally
        {
            _pendingReplies.Remove(entry);
        }

        entry.Text = reply;
        entry.Pending = false;
        return reply;
    }

    /// <summary>"New conversation": clears what the window shows and what ALDREN carries over from earlier turns.</summary>
    public async Task NewConversationAsync()
    {
        Conversation.Clear();
        _pendingReplies.Clear();
        _voiceReply = null;
        if (Core is { } core)
        {
            try
            {
                await core.NewConversationAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Couldn't start a new conversation in Core: {Message}", ex.Message);
            }
        }
    }

    private void Add(ConversationEntry entry)
    {
        Conversation.Add(entry);
        while (Conversation.Count > MaxConversationEntries)
        {
            Conversation.RemoveAt(0);
        }
    }

    /// <summary>The reply to the typed request in flight, as far as Core has it.</summary>
    private void OnReplyProgress(string text)
    {
        if (_pendingReplies.Count > 0)
        {
            _pendingReplies[0].Text = text;
        }

        Island.ShowTypedProgress(text);
    }

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
            Add(new ConversationEntry(fromUser: true, text, voice: true));
            _voiceReply = null;
        }
        else if (state == VoiceState.Speaking && text.Length > 0)
        {
            // The reply arrives twice: what was written when the first sound played, then all of it. One entry.
            if (_voiceReply is { } entry && Conversation.Contains(entry))
            {
                entry.Text = text;
            }
            else
            {
                _voiceReply = new ConversationEntry(fromUser: false, text, voice: true);
                Add(_voiceReply);
            }
        }
        else if (state == VoiceState.Idle)
        {
            _voiceReply = null;
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

    /// <summary>Applies settings the App acts on itself: the hotkeys and whether the island shows on stream.</summary>
    public void ApplySettings(AldrenSettings settings)
    {
        Hotkeys.Apply(settings.General.Hotkey);
        PushToTalk.Apply(settings.Voice.PushToTalkHotkey);
        Island.SetShowOnStream(settings.General.ShowOnStream);
        _tray.AskHint = Hotkeys.Active?.ToString();
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
        UnhandledException += (_, e) =>
        {
            // A failed handler must not take the tray, the island and the hotkeys down with it.
            _log.LogError(e.Exception, "Unhandled UI exception");
            e.Handled = true;
        };

        _host = new HostWindow();
        _host.Message += OnHostMessage;
        _host.HandlerFailed += ex => _log.LogError(ex, "Host window handler failed");
        _tray = new TrayIcon(_host, IconPath);
        // Leave the window procedure before touching WinUI.
        _tray.Command += command => _ui.TryEnqueue(() => RunLogged(() => OnTrayCommand(command), $"tray command {command}"));
        _tray.Hovered += () => _ui.TryEnqueue(() =>
        {
            // The pointer on the icon comes before the menu: the menu's check marks are fresh by the time it opens.
            if (Environment.TickCount64 - _trayRefreshed > 2000)
            {
                _ = RefreshTrayAsync();
            }
        });

        Island = new IslandWindow(_loggers.CreateLogger<IslandWindow>());
        Island.Submit = AskAsync;
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
        _tray.AskHint = Hotkeys.Active?.ToString();

        _watchdog = new CoreWatchdog(new CoreWatchdogPolicy(new RestartBackoff(TimeProvider.System)), _loggers.CreateLogger<CoreWatchdog>());
        _client = new CoreClient(
            new AppCallbacks(
                ConfirmAsync,
                (title, message) => _ui.TryEnqueue(() => _tray.ShowNotification(title, message)),
                (state, text) => _ui.TryEnqueue(() => RunLogged(() => OnVoiceState(state, text), "voice state")),
                activity => _ui.TryEnqueue(() => RunLogged(() =>
                {
                    // The cloud indicator: the tray says what's online, and the island shows it unless you're typing in it.
                    _tray.SetTooltip(activity is null ? "ALDREN" : $"ALDREN · online: {activity}");
                    Island.ShowCloud(activity);
                }, "cloud indicator")),
                text => _ui.TryEnqueue(() => RunLogged(() => OnReplyProgress(text), "reply progress")),
                YieldFocusAsync),
            _watchdog, _loggers.CreateLogger<CoreClient>());
        _client.Connected += core => _ui.TryEnqueue(() => _ = OnConnectedAsync(core));
        _client.Disconnected += () => _ui.TryEnqueue(() =>
        {
            _tray.StartWithWindowsAvailable = false;
            _tray.Connected = false;
            // Core went away mid-reply: release Escape and close any voice state it can no longer end.
            OnVoiceState(VoiceState.Idle, "");
        });
        _client.Start();
    }

    /// <summary>Core is about to type into the window you were using: the island hands the keyboard back to it.</summary>
    private Task YieldFocusAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_ui.TryEnqueue(() =>
            {
                try
                {
                    Island.YieldFocus();
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Couldn't hand focus back");
                }
                finally
                {
                    done.TrySetResult();
                }
            }))
        {
            done.TrySetResult();
        }

        return done.Task;
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
            case TrayCommand.Ask:
                if (Island.State is not (IslandState.Text or IslandState.Reply))
                {
                    Island.ToggleText();
                }

                break;
            case TrayCommand.Settings:
                ShowSettings();
                break;
            case TrayCommand.Silent:
                _ = ChangeAsync(core => core.SetSilentAsync(!_tray.Silent, CancellationToken.None), "silent mode");
                break;
            case TrayCommand.GamingAuto:
                _ = ChangeAsync(core => core.SetGamingModeAsync("auto", CancellationToken.None), "gaming mode");
                break;
            case TrayCommand.GamingOn:
                _ = ChangeAsync(core => core.SetGamingModeAsync("on", CancellationToken.None), "gaming mode");
                break;
            case TrayCommand.GamingOff:
                _ = ChangeAsync(core => core.SetGamingModeAsync("off", CancellationToken.None), "gaming mode");
                break;
            case TrayCommand.Focus:
                _ = ChangeAsync(core => core.EndFocusAsync(CancellationToken.None), "the focus session");
                break;
            case TrayCommand.StartWithWindows:
                _ = ChangeAsync(core => core.SetStartWithWindowsAsync(!_tray.StartWithWindows, CancellationToken.None), "Start with Windows");
                break;
            case TrayCommand.Quit:
                _ = QuitAsync();
                break;
        }
    }

    /// <summary>A change made from the tray menu, then the menu's state read back from Core.</summary>
    private async Task ChangeAsync(Func<IAldrenCore, Task> change, string what)
    {
        if (Core is not { } core)
        {
            return;
        }

        try
        {
            await change(core);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Couldn't change {What}: {Message}", what, ex.Message);
        }

        await RefreshTrayAsync();
    }

    /// <summary>Reads from Core what the tray menu shows: the check marks and whether a focus session runs.</summary>
    public async Task RefreshTrayAsync()
    {
        if (Core is not { } core)
        {
            return;
        }

        _trayRefreshed = Environment.TickCount64;
        try
        {
            var status = await core.GetStatusAsync(CancellationToken.None);
            var system = await core.GetSystemInfoAsync(CancellationToken.None);
            var focus = await core.GetFocusAsync(CancellationToken.None);
            _tray.Connected = true;
            _tray.StartWithWindows = status.StartWithWindows == StartupState.On;
            _tray.StartWithWindowsAvailable = status.StartWithWindows != StartupState.DisabledInWindows;
            _tray.Silent = system.Silent;
            _tray.Gaming = system.Gaming;
            _tray.GamingActive = system.GamingActive;
            _tray.FocusGoal = focus.Active ? focus.Goal ?? "" : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning("Couldn't read Core's status: {Message}", ex.Message);
        }
    }

    public void ShowExpanded()
    {
        _expanded ??= CreateWindow(new ExpandedWindow(), () => _expanded = null);
        _expanded.Activate();
        // Opened from a click on the never-activated island: make sure it comes to the front rather than only flashing.
        Aldren.App.Island.Native.Focus.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(_expanded));
    }

    /// <param name="page">A page tag ("General", "Voice", "Browser"…) to open on; null keeps the page it shows.</param>
    public void ShowSettings(string? page = null)
    {
        _settings ??= CreateWindow(new SettingsWindow(), () => _settings = null);
        if (page is not null)
        {
            _settings.Show(page);
        }

        _settings.Activate();
        Aldren.App.Island.Native.Focus.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(_settings));
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

    public void OpenLogsFolder() => OpenFolder(LogsDirectory);

    /// <summary>Opens a folder of ALDREN's own (settings, data, logs, models) in Explorer.</summary>
    public void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _log.LogWarning("Couldn't open {Folder}: {Message}", folder, ex.Message);
        }
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
