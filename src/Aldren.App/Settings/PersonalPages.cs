using Aldren.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aldren.App.Settings;

/// <summary>The memory inspector: everything ALDREN remembers, searchable, each deletable.</summary>
public sealed class MemoryPage() : SettingsPage("Memory")
{
    private readonly StackPanel _list = new() { Spacing = 6 };

    protected override async Task LoadAsync(IAldrenCore core)
    {
        Paragraph("What you asked ALDREN to remember. It stays on this PC and is never sent anywhere. Say \"remember that…\" to add, \"forget…\" to remove, or delete entries here.");
        var search = Named(new AutoSuggestBox { PlaceholderText = "Search memories", QueryIcon = new SymbolIcon(Symbol.Find), Width = 360, HorizontalAlignment = HorizontalAlignment.Left }, "Search memories");
        search.QuerySubmitted += async (_, e) => await FillAsync(core, e.QueryText);
        Body.Children.Add(search);
        Body.Children.Add(_list);
        await FillAsync(core, null);
    }

    private async Task FillAsync(IAldrenCore core, string? query)
    {
        _list.Children.Clear();
        var items = await core.GetMemoriesAsync(query, CancellationToken.None);
        if (items.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(query) ? "Nothing remembered yet." : "No memory matches that." });
            return;
        }

        foreach (var item in items)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = $"{item.Text}   ({(item.Kind == "profile" ? "about you, " : "")}{item.Created.ToLocalTime():d MMM yyyy})",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var delete = Named(new Button { Content = "Delete" }, $"Delete memory: {item.Text}");
            Grid.SetColumn(delete, 1);
            delete.Click += async (_, _) =>
            {
                await core.DeleteMemoryAsync(item.Id, CancellationToken.None);
                _list.Children.Remove(row);
            };
            row.Children.Add(delete);
            _list.Children.Add(row);
        }
    }
}

/// <summary>Upcoming reminders (cancel) and open tasks (done).</summary>
public sealed class RemindersPage() : SettingsPage("Reminders & tasks")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        Paragraph("Say \"remind me to… at…\", \"add… to my tasks\" or \"what's on my to-do list\". Reminders survive restarts; one missed while ALDREN was off shows when it starts.");

        Body.Children.Add(new TextBlock { Text = "Reminders", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        var reminders = await core.GetRemindersAsync(CancellationToken.None);
        if (reminders.Count == 0)
        {
            Paragraph("No reminders set.");
        }

        foreach (var r in reminders)
        {
            Body.Children.Add(Row($"{r.Text} — {r.When}", "Cancel", $"Cancel reminder: {r.Text}", () => core.CancelReminderAsync(r.Id, CancellationToken.None)));
        }

        Body.Children.Add(new TextBlock { Text = "Tasks", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Margin = new Thickness(0, 12, 0, 0) });
        var tasks = await core.GetTasksAsync(CancellationToken.None);
        if (tasks.Count == 0)
        {
            Paragraph("No open tasks.");
        }

        foreach (var t in tasks)
        {
            Body.Children.Add(Row(t.Text, "Done", $"Mark task done: {t.Text}", () => core.CompleteTaskAsync(t.Id, CancellationToken.None)));
        }
    }

    private Grid Row(string text, string action, string name, Func<Task> act)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var button = Named(new Button { Content = action }, name);
        Grid.SetColumn(button, 1);
        button.Click += async (_, _) =>
        {
            await act();
            Body.Children.Remove(row);
        };
        row.Children.Add(button);
        return row;
    }
}

/// <summary>Home network, the greeting and away mute.</summary>
public sealed class HomeAwayPage() : SettingsPage("Home & away")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var settings = await core.GetSettingsAsync(CancellationToken.None);
        var p = settings.Presence;
        var info = await core.GetPresenceAsync(CancellationToken.None);

        var status = Paragraph(Describe(info));
        Paragraph("ALDREN recognises home by the network you're on (a hashed network ID and router address, kept on this PC; VPNs such as Tailscale are ignored).");

        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var setHome = Named(new Button { Content = "This network is home" }, "This network is home");
        setHome.Click += async (_, _) =>
        {
            result.Text = await core.SetHomeNetworkAsync(CancellationToken.None);
            status.Text = Describe(await core.GetPresenceAsync(CancellationToken.None));
        };
        var forget = Named(new Button { Content = "Forget home networks" }, "Forget home networks");
        forget.Click += async (_, _) =>
        {
            await core.ForgetHomeNetworksAsync(CancellationToken.None);
            result.Text = "Home networks forgotten.";
            status.Text = Describe(await core.GetPresenceAsync(CancellationToken.None));
        };
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { setHome, forget } });
        Body.Children.Add(result);

        var greeting = Named(new ToggleSwitch { Header = "Greet me when I'm back home (\"Welcome back, boss.\")", IsOn = p.Greeting }, "Greeting");
        var briefing = Named(new ToggleSwitch { Header = "Add the daily briefing to the first greeting of the day", IsOn = p.BriefingWithGreeting }, "Briefing with greeting");
        var hours = Named(new NumberBox { Header = "Only after at least this many hours", Value = p.GreetingAfterHours, Minimum = 1, Maximum = 48, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 260, HorizontalAlignment = HorizontalAlignment.Left }, "Greeting interval in hours");
        var mute = Named(new ToggleSwitch { Header = "Mute the PC when I'm away from home", IsOn = p.AwayMute }, "Away mute");
        var headphones = Named(new ToggleSwitch { Header = "Don't mute when the sound goes to headphones", IsOn = p.AwayMuteSkipHeadphones }, "Skip headphones");
        foreach (var control in new UIElement[] { greeting, briefing, hours, mute, headphones })
        {
            Body.Children.Add(control);
        }

        Paragraph("Away mute restores the sound back home only if ALDREN did the muting and you haven't changed the volume since. Nothing happens until a home network is saved.");

        var saveResult = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var save = Named(new Button { Content = "Save" }, "Save home and away settings");
        save.Click += async (_, _) =>
        {
            var updated = settings with
            {
                Presence = new PresenceSettings(greeting.IsOn, briefing.IsOn, double.IsNaN(hours.Value) ? 4 : (int)hours.Value, mute.IsOn, headphones.IsOn),
            };
            var saved = await core.UpdateSettingsAsync(updated, CancellationToken.None);
            saveResult.Text = saved.Saved ? "Saved." : string.Join(" ", saved.Errors);
            if (saved.Saved)
            {
                settings = updated;
            }
        };
        Body.Children.Add(save);
        Body.Children.Add(saveResult);
    }

    private static string Describe(PresenceInfo info) => info.HomeNetworks == 0
        ? "No home network saved yet. While you're at home, click \"This network is home\" (or say \"this is my home network\")."
        : info.State switch
        {
            "Home" => info.Override ? "You're home (you said so)." : "You're home.",
            "Away" => "You're away from home.",
            _ => "Can't tell right now (no identifiable network).",
        } + $" {info.HomeNetworks} home network fingerprint(s) saved.";
}
