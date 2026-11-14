using Aldren.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aldren.App.Settings;

/// <summary>The memory inspector: everything ALDREN remembers, searchable, each deletable.</summary>
public sealed class MemoryPage() : SettingsPage(
    "Memory",
    "What you asked ALDREN to remember. It stays on this PC and is never sent anywhere. Say \"remember that…\" to add and \"forget…\" to remove, or delete entries here.")
{
    private readonly StackPanel _list = new() { Spacing = 4 };

    protected override async Task LoadAsync(IAldrenCore core)
    {
        var search = Named(new AutoSuggestBox { PlaceholderText = "Search memories", QueryIcon = new SymbolIcon(Symbol.Find), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) }, "Search memories");
        search.QuerySubmitted += async (_, e) => await FillAsync(core, e.QueryText);
        search.TextChanged += async (_, e) =>
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput && search.Text.Length == 0)
            {
                await FillAsync(core, null);
            }
        };
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
            _list.Children.Add(Secondary(string.IsNullOrWhiteSpace(query) ? "Nothing remembered yet." : "No memory matches that.", new Thickness(2, 8, 0, 0)));
            return;
        }

        foreach (var item in items)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            var meta = Secondary($"{(item.Kind == "profile" ? "About you  ·  " : "")}{item.Created.ToLocalTime():d MMM yyyy}", default);
            meta.Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"];
            text.Children.Add(meta);
            row.Children.Add(text);
            var delete = Named(new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, VerticalAlignment = VerticalAlignment.Center }, $"Delete memory: {item.Text}");
            ToolTipService.SetToolTip(delete, "Delete this memory");
            Grid.SetColumn(delete, 1);
            row.Children.Add(delete);
            var card = new Border { Style = (Style)Application.Current.Resources["AldrenCardStyle"], Child = row };
            delete.Click += async (_, _) =>
            {
                await core.DeleteMemoryAsync(item.Id, CancellationToken.None);
                _list.Children.Remove(card);
            };
            _list.Children.Add(card);
        }
    }
}

/// <summary>Upcoming reminders (cancel) and open tasks (done).</summary>
public sealed class RemindersPage() : SettingsPage(
    "Reminders & tasks",
    "Say \"remind me to… at…\", \"add… to my tasks\" or \"what's on my to-do list\". Reminders survive restarts; one missed while ALDREN was off shows when it starts.")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        Header("Reminders");
        var reminders = await core.GetRemindersAsync(CancellationToken.None);
        if (reminders.Count == 0)
        {
            Paragraph("No reminders set.");
        }

        foreach (var r in reminders)
        {
            Line(r.Text, r.When, "Cancel", $"Cancel reminder: {r.Text}", "", () => core.CancelReminderAsync(r.Id, CancellationToken.None));
        }

        Header("Tasks");
        var tasks = await core.GetTasksAsync(CancellationToken.None);
        if (tasks.Count == 0)
        {
            Paragraph("No open tasks.");
        }

        foreach (var t in tasks)
        {
            Line(t.Text, null, "Done", $"Mark task done: {t.Text}", "", () => core.CompleteTaskAsync(t.Id, CancellationToken.None));
        }
    }

    private void Line(string text, string? detail, string action, string name, string glyph, Func<Task> act)
    {
        var button = Named(new Button { Content = action }, name);
        var card = Card(text, detail, button, glyph);
        button.Click += async (_, _) =>
        {
            await act();
            Body.Children.Remove(card.Root);
        };
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

        var setHome = Named(new Button { Content = "This network is home" }, "This network is home");
        var forget = Named(new Button { Content = "Forget" }, "Forget home networks");
        var status = Card(Title(info), Detail(info), Row(setHome, forget), "");
        Paragraph("ALDREN recognises home by the network you're on: a hashed network ID and router address, kept on this PC. VPNs such as Tailscale are ignored.");

        async Task RefreshAsync(string? result)
        {
            var now = await core.GetPresenceAsync(CancellationToken.None);
            status.Title.Text = Title(now);
            status.Describe(result is null ? Detail(now) : $"{result} {Detail(now)}");
        }

        setHome.Click += async (_, _) => await RefreshAsync(await core.SetHomeNetworkAsync(CancellationToken.None));
        forget.Click += async (_, _) =>
        {
            await core.ForgetHomeNetworksAsync(CancellationToken.None);
            await RefreshAsync("Home networks forgotten.");
        };

        Header("Coming home");
        var greeting = Switch(p.Greeting, "Greeting");
        Card("Greet me when I'm back", "\"Welcome back, boss.\" at sign-in or unlock, at home.", greeting, "");
        SaveOnToggle(greeting, core, (s, on) => s with { Presence = s.Presence with { Greeting = on } });

        var briefing = Switch(p.BriefingWithGreeting, "Briefing with greeting");
        Card("Add the daily briefing", "With the first greeting of the day: calendar, reminders and tasks.", briefing);
        SaveOnToggle(briefing, core, (s, on) => s with { Presence = s.Presence with { BriefingWithGreeting = on } });

        var hours = Named(new NumberBox
        {
            Value = p.GreetingAfterHours,
            Minimum = 1,
            Maximum = 48,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 130,
        }, "Greeting interval in hours");
        Card("Hours between greetings", "Only greet after at least this long since the last one.", hours);
        hours.ValueChanged += async (_, e) =>
        {
            if (!double.IsNaN(e.NewValue))
            {
                await SaveAsync(core, s => s with { Presence = s.Presence with { GreetingAfterHours = (int)e.NewValue } });
            }
        };

        Header("Away from home");
        var mute = Switch(p.AwayMute, "Away mute");
        Card("Mute the PC when I'm away", "Restored back home only if ALDREN did the muting and you haven't changed the volume since. Nothing happens until a home network is saved.", mute, "");
        SaveOnToggle(mute, core, (s, on) => s with { Presence = s.Presence with { AwayMute = on } });

        var headphones = Switch(p.AwayMuteSkipHeadphones, "Skip headphones");
        Card("Leave headphones alone", "Don't mute while the sound goes to headphones.", headphones);
        SaveOnToggle(headphones, core, (s, on) => s with { Presence = s.Presence with { AwayMuteSkipHeadphones = on } });
    }

    private static string Title(PresenceInfo info) => info.HomeNetworks == 0
        ? "No home network saved yet"
        : info.State switch
        {
            "Home" => info.Override ? "You're home (you said so)" : "You're home",
            "Away" => "You're away from home",
            _ => "Can't tell right now",
        };

    private static string Detail(PresenceInfo info) => info.HomeNetworks == 0
        ? "While you're at home, click \"This network is home\" (or say \"this is my home network\")."
        : $"{info.HomeNetworks} home network{(info.HomeNetworks == 1 ? "" : "s")} saved." + (info.State is "Home" or "Away" ? "" : " No identifiable network at the moment.");
}
