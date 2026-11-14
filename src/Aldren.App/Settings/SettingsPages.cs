using Aldren.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Aldren.App.Settings;

public sealed class GeneralPage() : SettingsPage("General")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var status = await core.GetStatusAsync(CancellationToken.None);
        var settings = await core.GetSettingsAsync(CancellationToken.None);

        Header("Startup");
        var startup = Switch(status.StartWithWindows == StartupState.On, "Start with Windows");
        startup.IsEnabled = status.StartWithWindows != StartupState.DisabledInWindows;
        Card(
            "Start with Windows",
            status.StartWithWindows == StartupState.DisabledInWindows
                ? "Switched off in Windows. Turn it back on in Task Manager > Startup apps, or Settings > Apps > Startup."
                : "ALDREN starts quietly in the tray when you sign in.",
            startup,
            "");
        startup.Toggled += async (_, _) =>
        {
            await core.SetStartWithWindowsAsync(startup.IsOn, CancellationToken.None);
            await ShellApp.Instance.RefreshTrayAsync();
        };

        Header("Text field");
        var hotkey = Named(new TextBox { Text = settings.General.Hotkey, Width = 180 }, "Hotkey for the text field");
        var apply = Named(new Button { Content = "Apply" }, "Apply the text field hotkey");
        var card = Card("Hotkey", ActiveHotkeyText(), Row(hotkey, apply), "");
        Paragraph("Ctrl and/or Alt, optionally Shift, plus a letter, a digit, F1–F24, Space, or the keypad's / and * (written \"Ctrl+/\" and \"Ctrl+*\"). In the field: Enter sends, ↑ recalls what you sent before, Ctrl+Enter opens the conversation, Esc closes.");

        async Task ApplyAsync()
        {
            if (await SaveAsync(core, s => s with { General = s.General with { Hotkey = hotkey.Text.Trim() } }))
            {
                card.Describe(ActiveHotkeyText());
            }
        }

        apply.Click += async (_, _) => await ApplyAsync();
        hotkey.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await ApplyAsync();
            }
        };

        Header("Streaming and recording");
        var onStream = Switch(settings.General.ShowOnStream, "Show ALDREN on stream");
        Card(
            "Show ALDREN on stream",
            "Off: the island is left out of screenshots, recordings and OBS, and reminder notifications hide their text while OBS runs.",
            onStream,
            "");
        SaveOnToggle(onStream, core, (s, on) => s with { General = s.General with { ShowOnStream = on } });
    }

    private static string ActiveHotkeyText()
    {
        var hotkeys = ShellApp.Instance.Hotkeys;
        return hotkeys.Active is not { } activeKey
            ? "No hotkey is active: both the one you chose and Ctrl+Alt+Shift+Space are taken by other apps."
            : hotkeys.UsedFallback
                ? $"Active: {activeKey}. The one you chose is taken by another app, so ALDREN uses this fallback."
                : $"Active: {activeKey}. Opens the text field wherever you are.";
    }
}

public sealed class VoicePage() : SettingsPage("Voice & wake word")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var settings = await core.GetSettingsAsync(CancellationToken.None);
        var voice = settings.Voice;

        Header("Talking to ALDREN");
        var hotkey = Named(new TextBox { Text = voice.PushToTalkHotkey, Width = 180 }, "Push-to-talk hotkey");
        var apply = Named(new Button { Content = "Apply" }, "Apply the push-to-talk hotkey");
        var card = Card("Push-to-talk hotkey", ActiveText(), Row(hotkey, apply), "");
        Paragraph("Press it, speak, and stop talking: ALDREN answers, then listens a few seconds more for a follow-up. Press it again while ALDREN speaks to cut it off and talk; Esc or the Stop button just stops it.");

        async Task ApplyAsync()
        {
            if (await SaveAsync(core, s => s with { Voice = s.Voice with { PushToTalkHotkey = hotkey.Text.Trim() } }))
            {
                card.Describe(ActiveText());
            }
        }

        apply.Click += async (_, _) => await ApplyAsync();
        hotkey.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await ApplyAsync();
            }
        };

        var speak = Switch(voice.SpeakReplies, "Speak replies");
        Card("Speak replies", "Off: answers show in the island only. ALDREN never speaks in gaming mode or while a fullscreen app runs.", speak, "");
        SaveOnToggle(speak, core, (s, on) => s with { Voice = s.Voice with { SpeakReplies = on } });

        Header("Wake word");
        var wake = Switch(voice.WakeWord, "Wake word");
        Card(
            "Wake on \"ALDREN\"",
            "Off by default: it still triggers about 16 times an hour on everyday sound (the target is 0.5). When on, the microphone stays open and Windows shows its microphone indicator.",
            wake,
            "");
        SaveOnToggle(wake, core, (s, on) => s with { Voice = s.Voice with { WakeWord = on } });

        var check = Switch(voice.TranscriptCheck, "Transcript check");
        Card(
            "Double-check with speech recognition",
            "Fewer false wakes, but it also misses some real ones: the recogniser sometimes hears \"ALDREN\" as another word.",
            check);
        SaveOnToggle(check, core, (s, on) => s with { Voice = s.Voice with { TranscriptCheck = on } });

        Header("Things to say");
        Paragraph("\"Start dictation\" types what you say into the field you're in, until \"stop dictation\". \"Silent mode on\" keeps ALDREN to text. \"Stop\" or \"never mind\" ends a turn.");
    }

    private static string ActiveText() => ShellApp.Instance.PushToTalk.Active is { } key
        ? $"Active: {key}."
        : "Not active: that combination is taken by another app. Choose another.";
}

public sealed class PermissionsPage() : SettingsPage(
    "Permissions",
    "Each switch allows or blocks one kind of action. No model can change these; only you can, here. Changes are recorded in Activity history.")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        foreach (var permission in await core.GetPermissionsAsync(CancellationToken.None))
        {
            var toggle = Switch(permission.Granted, permission.Description);
            Card(permission.Description, permission.Name, toggle);
            var reverting = false;
            toggle.Toggled += async (_, _) =>
            {
                if (reverting)
                {
                    return;
                }

                try
                {
                    await core.SetPermissionAsync(permission.Name, toggle.IsOn, CancellationToken.None);
                    ClearNote();
                }
                catch (Exception ex)
                {
                    Note(ex.Message, InfoBarSeverity.Error);
                    reverting = true;
                    toggle.IsOn = !toggle.IsOn;
                    reverting = false;
                }
            };
        }
    }
}

public sealed class ActivityPage() : SettingsPage(
    "Activity history",
    "Everything ALDREN did, tried or refused, newest first. Kept on this PC for 180 days; passwords and keys are never written here.")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var entries = await core.GetAuditAsync(new AuditQuery(150), CancellationToken.None);
        if (entries.Count == 0)
        {
            Card("Nothing yet", "Every action ALDREN takes or refuses appears here.", glyph: "");
            return;
        }

        string? day = null;
        StackPanel? group = null;
        foreach (var entry in entries)
        {
            var local = entry.Timestamp.ToLocalTime();
            var label = local.Date == DateTime.Today ? "Today" : local.Date == DateTime.Today.AddDays(-1) ? "Yesterday" : local.ToString("dddd d MMMM");
            if (label != day)
            {
                day = label;
                Header(label);
                group = new StackPanel { Spacing = 10 };
                AddCard(group);
            }

            group!.Children.Add(Line(entry, local));
        }
    }

    private static Grid Line(AuditEntry entry, DateTimeOffset local)
    {
        var (glyph, colour) = entry.Outcome switch
        {
            "Succeeded" => ("", Microsoft.UI.Colors.MediumSeaGreen),
            "Declined" => ("", Microsoft.UI.Colors.Gray),
            "Denied" or "Forbidden" or "PermissionDenied" => ("", Microsoft.UI.Colors.Orange),
            _ => ("", Microsoft.UI.Colors.OrangeRed),
        };
        var who = entry.Origin == "User" ? "you" : "a model";
        var flags = (entry.Confirmed ? " · you confirmed" : "") + (entry.Tainted ? " · untrusted content in context" : "");
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 14,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(colour),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 0, 0),
        });
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = $"{entry.Tool}  ·  {entry.Outcome.ToLowerInvariant()}", TextWrapping = TextWrapping.Wrap });
        var detail = Secondary(
            (string.IsNullOrWhiteSpace(entry.Detail) ? "" : entry.Detail + "\n") + $"{entry.Tier}, asked by {who}, {entry.DurationMs} ms{flags}",
            default);
        detail.Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"];
        detail.IsTextSelectionEnabled = true;
        text.Children.Add(detail);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var time = Secondary(local.ToString("t"), default);
        time.Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"];
        Grid.SetColumn(time, 2);
        row.Children.Add(time);
        return row;
    }
}

public sealed class DiagnosticsPage() : SettingsPage("Diagnostics")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var status = await core.GetStatusAsync(CancellationToken.None);
        Card(
            "Processes",
            $"Core {status.Version} (process {status.ProcessId}, started {status.StartedAt.ToLocalTime():g})\nApp {ShellApp.Version} (process {Environment.ProcessId})",
            glyph: "");

        Header("Checks");
        var confirm = Named(new Button { Content = "Run" }, "Run the confirmation test");
        var confirmCard = Card("Confirmation prompt", "Shows \"Allow this?\" in the island; answer Yes, No, or wait for it to time out.", confirm);
        confirm.Click += async (_, _) => confirmCard.Describe(await core.SubmitTextAsync("test confirmation", CancellationToken.None));

        var redaction = Named(new Button { Content = "Write" }, "Write a redaction test line to the log");
        var redactionCard = Card("Log redaction", "Writes a fake API key to the log; it must appear there as apiKey=***.", redaction);
        redaction.Click += (_, _) =>
        {
            ShellApp.Instance.Log.LogInformation("Redaction test: {Line}", "apiKey=sk-redactiontest0123456789");
            redactionCard.Describe("Written. Open the log folder and check that the key shows as ***.");
        };

        var logs = Named(new Button { Content = "Open" }, "Open the log folder");
        Card("Log folder", ShellApp.Instance.LogsDirectory, logs);
        logs.Click += (_, _) => ShellApp.Instance.OpenLogsFolder();

        Header("Island states");
        Paragraph("Shows each state with sample content for a few seconds.");
        var states = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 132, ItemHeight = 40 };
        foreach (var state in new[]
                 {
                     Island.IslandState.Listening, Island.IslandState.Transcribing, Island.IslandState.Thinking,
                     Island.IslandState.Acting, Island.IslandState.Researching, Island.IslandState.Speaking,
                     Island.IslandState.Reply, Island.IslandState.Error,
                 })
        {
            var button = Named(new Button { Content = state.ToString(), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 8, 0) }, $"Preview the {state} state");
            button.Click += (_, _) => ShellApp.Instance.Island.Preview(state);
            states.Children.Add(button);
        }

        AddCard(states);
    }
}

public sealed class AboutPage() : SettingsPage("About")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var status = await core.GetStatusAsync(CancellationToken.None);
        var info = await core.GetSystemInfoAsync(CancellationToken.None);
        Card("ALDREN", $"Version {ShellApp.Version} (Core {status.Version})\nA local-first personal assistant for Windows 11.", glyph: "");
        Card(
            "Runs on this PC",
            "The assistant's model, your voice, memory, reminders and files stay on this computer. Going online happens only when you ask for research, through the gateway on this PC, and every such request is listed in Activity history.",
            glyph: "");

        var settings = Named(new Button { Content = "Open" }, "Open the settings folder");
        settings.Click += (_, _) => ShellApp.Instance.OpenFolder(info.SettingsFolder);
        Card("Settings", info.SettingsFolder, settings);
        var data = Named(new Button { Content = "Open" }, "Open the data folder");
        data.Click += (_, _) => ShellApp.Instance.OpenFolder(info.DataFolder);
        Card("Database and logs", info.DataFolder, data);
        var models = Named(new Button { Content = "Open" }, "Open the models folder");
        models.Click += (_, _) => ShellApp.Instance.OpenFolder(info.ModelsFolder);
        Card("Models, voices and the gateway", info.ModelsFolder, models);
    }
}
