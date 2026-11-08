using Aldren.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Aldren.App.Settings;

/// <summary>A titled column of controls that loads from Core when shown.</summary>
public abstract class SettingsPage : UserControl
{
    protected SettingsPage(string title)
    {
        Body = new StackPanel { Spacing = 12, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        Body.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });
        Content = Body;
        Loaded += async (_, _) =>
        {
            if (ShellApp.Instance.Core is { } core)
            {
                try
                {
                    await LoadAsync(core);
                }
                catch (Exception ex)
                {
                    Note("Couldn't load this page from Core: " + ex.Message, InfoBarSeverity.Error);
                }
            }
            else
            {
                Note("ALDREN's background service isn't connected right now. This page will work once it is.", InfoBarSeverity.Warning);
            }
        };
    }

    protected StackPanel Body { get; }

    protected abstract Task LoadAsync(IAldrenCore core);

    protected TextBlock Paragraph(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        Body.Children.Add(block);
        return block;
    }

    protected InfoBar Note(string message, InfoBarSeverity severity)
    {
        var bar = new InfoBar { Message = message, Severity = severity, IsOpen = true, IsClosable = false };
        Body.Children.Insert(1, bar);
        return bar;
    }

    protected static T Named<T>(T element, string name) where T : UIElement
    {
        AutomationProperties.SetName(element, name);
        return element;
    }
}

public sealed class GeneralPage() : SettingsPage("General")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var status = await core.GetStatusAsync(CancellationToken.None);
        var settings = await core.GetSettingsAsync(CancellationToken.None);

        var startup = Named(new ToggleSwitch
        {
            Header = "Start with Windows",
            IsOn = status.StartWithWindows == StartupState.On,
            IsEnabled = status.StartWithWindows != StartupState.DisabledInWindows,
        }, "Start with Windows");
        Body.Children.Add(startup);
        if (status.StartWithWindows == StartupState.DisabledInWindows)
        {
            Paragraph("ALDREN was switched off in Windows. To turn it back on, use Task Manager > Startup apps, or Settings > Apps > Startup.");
        }

        startup.Toggled += async (_, _) =>
        {
            await core.SetStartWithWindowsAsync(startup.IsOn, CancellationToken.None);
            await ShellApp.Instance.RefreshTrayAsync();
        };

        var hotkey = Named(new TextBox { Header = "Hotkey for the text box", Text = settings.General.Hotkey, Width = 260, HorizontalAlignment = HorizontalAlignment.Left }, "Hotkey for the text box");
        Body.Children.Add(hotkey);
        var active = Paragraph(ActiveHotkeyText());

        var onStream = Named(new ToggleSwitch { Header = "Show ALDREN on stream", IsOn = settings.General.ShowOnStream }, "Show ALDREN on stream");
        Body.Children.Add(onStream);
        Paragraph("Off: the island is hidden from screenshots, recordings and OBS.");

        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var save = Named(new Button { Content = "Save" }, "Save general settings");
        save.Click += async (_, _) =>
        {
            var updated = settings with { General = new GeneralSettings(hotkey.Text.Trim(), onStream.IsOn) };
            var saved = await core.UpdateSettingsAsync(updated, CancellationToken.None);
            result.Text = saved.Saved ? "Saved." : string.Join(" ", saved.Errors);
            if (saved.Saved)
            {
                settings = updated;
                ShellApp.Instance.ApplySettings(updated);
                active.Text = ActiveHotkeyText();
            }
        };
        Body.Children.Add(save);
        Body.Children.Add(result);
    }

    private static string ActiveHotkeyText()
    {
        var hotkeys = ShellApp.Instance.Hotkeys;
        return hotkeys.Active is not { } activeKey
            ? "No hotkey is active: both the configured one and Ctrl+Alt+Shift+Space are taken by other apps."
            : hotkeys.UsedFallback
                ? $"Active hotkey: {activeKey}. The configured one is taken by another app, so ALDREN uses this fallback."
                : $"Active hotkey: {activeKey}.";
    }
}

public sealed class VoicePage() : SettingsPage("Voice & wake word")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var settings = await core.GetSettingsAsync(CancellationToken.None);
        var voice = settings.Voice;

        var hotkey = Named(new TextBox { Header = "Push-to-talk hotkey", Text = voice.PushToTalkHotkey, Width = 260, HorizontalAlignment = HorizontalAlignment.Left }, "Push-to-talk hotkey");
        Body.Children.Add(hotkey);
        var active = Paragraph(ActiveText());
        Paragraph("Press it, speak, and stop talking: ALDREN answers, then listens 8 s more for a follow-up. Press it again while ALDREN speaks to stop it.");

        var speak = Named(new ToggleSwitch { Header = "Speak replies", IsOn = voice.SpeakReplies }, "Speak replies");
        Body.Children.Add(speak);
        Paragraph("ALDREN never speaks during gaming mode or while a fullscreen app runs; the reply shows in the island instead.");

        var wake = Named(new ToggleSwitch { Header = "Wake word \"ALDREN\"", IsOn = voice.WakeWord }, "Wake word");
        Body.Children.Add(wake);
        Paragraph("Off by default: the wake word still triggers about 16 times an hour on everyday sound (the target is 0.5). When on, the microphone stays open and the Windows microphone indicator shows.");

        var check = Named(new ToggleSwitch { Header = "Double-check the wake word with speech recognition", IsOn = voice.TranscriptCheck }, "Transcript check");
        Body.Children.Add(check);
        Paragraph("Fewer false wakes, but it also misses some real ones, because the recogniser sometimes hears \"ALDREN\" as another word.");

        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var save = Named(new Button { Content = "Save" }, "Save voice settings");
        save.Click += async (_, _) =>
        {
            var updated = settings with { Voice = new VoiceSettings(wake.IsOn, check.IsOn, speak.IsOn, hotkey.Text.Trim()) };
            var saved = await core.UpdateSettingsAsync(updated, CancellationToken.None);
            result.Text = saved.Saved ? "Saved." : string.Join(" ", saved.Errors);
            if (saved.Saved)
            {
                settings = updated;
                ShellApp.Instance.ApplySettings(updated);
                active.Text = ActiveText();
            }
        };
        Body.Children.Add(save);
        Body.Children.Add(result);
    }

    private static string ActiveText() => ShellApp.Instance.PushToTalk.Active is { } key
        ? $"Active: {key}."
        : "No push-to-talk hotkey is active: that combination is taken by another app. Choose another.";
}

public sealed class PermissionsPage() : SettingsPage("Permissions")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        Paragraph("Each switch allows or blocks one kind of action. A model can never change these; only you can, here.");
        foreach (var permission in await core.GetPermissionsAsync(CancellationToken.None))
        {
            var toggle = Named(new ToggleSwitch { Header = $"{permission.Description} ({permission.Name})", IsOn = permission.Granted }, permission.Description);
            toggle.Toggled += async (_, _) =>
            {
                try
                {
                    await core.SetPermissionAsync(permission.Name, toggle.IsOn, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Note(ex.Message, InfoBarSeverity.Error);
                }
            };
            Body.Children.Add(toggle);
        }
    }
}

public sealed class ActivityPage() : SettingsPage("Activity history")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var entries = await core.GetAuditAsync(new AuditQuery(100), CancellationToken.None);
        if (entries.Count == 0)
        {
            Paragraph("Nothing yet. Every action ALDREN takes or refuses appears here.");
            return;
        }

        var list = Named(new ListView { SelectionMode = ListViewSelectionMode.None }, "Latest actions");
        foreach (var entry in entries)
        {
            var who = entry.Origin == "User" ? "you" : "a model";
            var flags = (entry.Confirmed ? " · confirmed" : "") + (entry.Tainted ? " · untrusted content in context" : "");
            list.Items.Add(new TextBlock
            {
                Text = $"{entry.Timestamp.ToLocalTime():g}  {entry.Tool}  {entry.Outcome}  ({entry.Tier}, by {who}, {entry.DurationMs} ms{flags})\n{entry.Detail}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4),
                IsTextSelectionEnabled = true,
            });
        }

        Body.Children.Add(list);
    }
}

public sealed class DiagnosticsPage() : SettingsPage("Diagnostics")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var status = await core.GetStatusAsync(CancellationToken.None);
        Paragraph($"Core {status.Version} (process {status.ProcessId}, started {status.StartedAt.ToLocalTime():g}) · App {ShellApp.Version} (process {Environment.ProcessId})");

        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };

        var confirm = Named(new Button { Content = "Run the confirmation test" }, "Run the confirmation test");
        confirm.Click += async (_, _) => result.Text = await core.SubmitTextAsync("test confirmation", CancellationToken.None);

        var redaction = Named(new Button { Content = "Write a redaction test line to the log" }, "Write a redaction test line to the log");
        redaction.Click += (_, _) =>
        {
            ShellApp.Instance.Log.LogInformation("Redaction test: {Line}", "apiKey=sk-redactiontest0123456789");
            result.Text = "Written. The log should show apiKey=*** instead of the key.";
        };

        var logs = Named(new Button { Content = "Open the log folder" }, "Open the log folder");
        logs.Click += (_, _) => ShellApp.Instance.OpenLogsFolder();

        Body.Children.Add(confirm);
        Body.Children.Add(redaction);
        Body.Children.Add(logs);
        Body.Children.Add(result);

        Paragraph("Preview each island state with sample content (the real ones arrive with voice in Phase 3):");
        var states = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 130, ItemHeight = 40 };
        foreach (var state in new[]
                 {
                     Island.IslandState.Compact, Island.IslandState.Listening, Island.IslandState.Transcribing,
                     Island.IslandState.Thinking, Island.IslandState.Acting, Island.IslandState.Researching,
                     Island.IslandState.Speaking, Island.IslandState.Error,
                 })
        {
            var button = Named(new Button { Content = state.ToString() }, $"Preview the {state} state");
            button.Click += (_, _) => ShellApp.Instance.Island.Preview(state);
            states.Children.Add(button);
        }

        Body.Children.Add(states);
    }
}

public sealed class ComingLaterPage : SettingsPage
{
    public ComingLaterPage(string title, string phase) : base(title)
    {
        Paragraph($"This section arrives in {phase} of the roadmap.");
    }

    protected override Task LoadAsync(IAldrenCore core) => Task.CompletedTask;
}
