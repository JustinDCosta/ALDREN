using Aldren.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aldren.App.Settings;

/// <summary>The browser Companion (ARCHITECTURE §12): register the host on your click, load the extension, site levels.</summary>
public sealed class BrowserSettingsPage() : SettingsPage(
    "Browser",
    "The ALDREN Companion lets ALDREN read and operate pages in Chrome or Edge: open sites, read a page, click a result, fill a field, download a link. It never reads or fills password or card fields, and buttons that submit, buy, send or delete ask you first.")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var info = await core.GetBrowserStatusAsync(CancellationToken.None);
        var connect = Named(new Button { Content = "Connect", Style = (Style)Application.Current.Resources["AccentButtonStyle"] }, "Connect the browser");
        var disconnect = Named(new Button { Content = "Disconnect" }, "Disconnect the browser");
        var refresh = Named(new Button { Content = "Refresh" }, "Refresh browser status");
        var status = Card("", null, Row(connect, disconnect, refresh), "");
        var sites = new StackPanel { Spacing = 6 };

        Header("Setting it up");
        Card("1. Connect", "ALDREN registers its browser host for Chrome and Edge for your account (two entries under HKCU\\Software, removed again by Disconnect).");
        var copy = Named(new Button { Content = "Copy path" }, "Copy the extension folder path");
        Card("2. Load the extension", $"In chrome://extensions (or edge://extensions) switch on Developer mode, click \"Load unpacked\" and pick:\n{info.ExtensionFolder}", copy);
        copy.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(info.ExtensionFolder);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            copy.Content = "Copied";
        };
        Card("3. Refresh", $"Come back here and click Refresh. The extension's ID must be {info.ExtensionId}.");

        connect.Click += async (_, _) =>
        {
            try
            {
                await core.ConnectBrowserAsync(CancellationToken.None);
                Note("Registered. Now load the extension (step 2) and click Refresh; if Chrome or Edge is already open, the Companion connects within a few seconds.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                Note("Couldn't register: " + ex.Message, InfoBarSeverity.Error);
            }

            await ShowAsync();
        };
        disconnect.Click += async (_, _) =>
        {
            await core.DisconnectBrowserAsync(CancellationToken.None);
            Note("Disconnected. You can also remove the extension in chrome://extensions.", InfoBarSeverity.Informational);
            await ShowAsync();
        };
        refresh.Click += async (_, _) => await ShowAsync();

        Header("Site levels");
        Paragraph("Interact (the default): ALDREN may click and navigate; typing asks first. Forms: typing into fields is allowed without asking (submitting still asks). Read only: ALDREN may only read the page; best for banks and payment pages.");
        var site = Named(new TextBox { PlaceholderText = "example.com", Width = 240 }, "Site");
        var level = Named(new ComboBox { ItemsSource = new[] { "Read only", "Interact", "Forms" }, SelectedIndex = 0, Width = 130 }, "Level");
        var set = Named(new Button { Content = "Set" }, "Set the site level");
        set.Click += async (_, _) =>
        {
            if (site.Text.Trim().Length > 0)
            {
                await core.SetSiteLevelAsync(site.Text.Trim(), ((string)level.SelectedItem).Replace(" ", ""), CancellationToken.None);
                site.Text = "";
                await ShowAsync();
            }
        };
        var form = new StackPanel { Spacing = 12 };
        form.Children.Add(Row(site, level, set));
        form.Children.Add(sites);
        AddCard(form);

        Fill(info);

        async Task ShowAsync() => Fill(await core.GetBrowserStatusAsync(CancellationToken.None));

        void Fill(BrowserStatusInfo current)
        {
            (status.Title.Text, var detail) = (current.Registered, current.Connected) switch
            {
                (_, true) => ("Connected", "The Companion is running in your browser."),
                (true, false) => ("Registered, waiting for the extension", "Load the extension (step 2) and keep Chrome or Edge open."),
                _ => ("Not connected", "Follow the three steps below."),
            };
            status.Describe(detail);
            connect.Visibility = current.Registered ? Visibility.Collapsed : Visibility.Visible;
            disconnect.Visibility = current.Registered ? Visibility.Visible : Visibility.Collapsed;

            sites.Children.Clear();
            foreach (var item in current.Sites)
            {
                var reset = Named(new HyperlinkButton { Content = "Reset", Padding = new Thickness(6, 2, 6, 2) }, $"Reset {item.Site}");
                reset.Click += async (_, _) =>
                {
                    await core.SetSiteLevelAsync(item.Site, null, CancellationToken.None);
                    await ShowAsync();
                };
                var label = new TextBlock { Text = $"{item.Site}  ·  {(item.Level == "ReadOnly" ? "Read only" : item.Level)}", VerticalAlignment = VerticalAlignment.Center };
                sites.Children.Add(Row(label, reset));
            }

            if (current.Sites.Count == 0)
            {
                sites.Children.Add(Secondary("No site has its own level yet; every site is at Interact.", default));
            }
        }
    }
}

/// <summary>Google Calendar and Drive (ARCHITECTURE §14.3) with your own OAuth client; the token lives in Credential Manager.</summary>
public sealed class GooglePage() : SettingsPage(
    "Google account",
    "Calendar and Drive through your own Google Cloud OAuth client (a \"Desktop app\" client; docs/guides/google-oauth.md walks through it in about 10 minutes). The client secret and the sign-in token are kept in Windows Credential Manager, never in files.")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var signIn = Named(new Button { Content = "Sign in", Style = (Style)Application.Current.Resources["AccentButtonStyle"] }, "Sign in to Google");
        var disconnect = Named(new Button { Content = "Disconnect" }, "Disconnect Google");
        var status = Card("", null, Row(signIn, disconnect), "");

        Header("Your OAuth client");
        var id = Named(new TextBox { Header = "Client ID", PlaceholderText = "…apps.googleusercontent.com" }, "Google client ID");
        var secret = Named(new PasswordBox { Header = "Client secret" }, "Google client secret");
        var save = Named(new Button { Content = "Save client" }, "Save the Google client");
        var form = new StackPanel { Spacing = 12, Children = { id, secret, save } };
        AddCard(form);
        Paragraph("Access asked for: your calendar's events, read-only Drive search, and the Drive files ALDREN creates or you pick. Disconnect revokes the access at Google and deletes the token here.");

        save.Click += async (_, _) =>
        {
            if (id.Text.Trim().Length == 0 || secret.Password.Trim().Length == 0)
            {
                Note("Enter both the client ID and the secret.", InfoBarSeverity.Warning);
                return;
            }

            await core.SetGoogleClientAsync(id.Text, secret.Password, CancellationToken.None);
            secret.Password = "";
            Note("Client saved. Now click Sign in.", InfoBarSeverity.Success);
            await ShowAsync();
        };
        signIn.Click += async (_, _) =>
        {
            Note("Finish the sign-in in your browser…", InfoBarSeverity.Informational);
            try
            {
                var result = await core.SignInGoogleAsync(CancellationToken.None);
                Note(result, result.StartsWith("Signed in", StringComparison.Ordinal) ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            }
            catch (Exception ex)
            {
                Note("Sign-in failed: " + ex.Message, InfoBarSeverity.Error);
            }

            await ShowAsync();
        };
        disconnect.Click += async (_, _) =>
        {
            await core.DisconnectGoogleAsync(CancellationToken.None);
            Note("Disconnected: access revoked and the token deleted.", InfoBarSeverity.Informational);
            await ShowAsync();
        };

        await ShowAsync();

        async Task ShowAsync()
        {
            var info = await core.GetGoogleStatusAsync(CancellationToken.None);
            (status.Title.Text, var detail) = info switch
            {
                { SignedIn: true } => ("Signed in", "Calendar and Drive are available: \"what's on my calendar\", \"find my notes in Drive\"."),
                { HasClient: true } => ("Client saved, not signed in", "Click Sign in and finish in your browser."),
                _ => ("Not set up", "Add your OAuth client below first."),
            };
            status.Describe(detail);
            signIn.IsEnabled = info.HasClient && !info.SignedIn;
            disconnect.IsEnabled = info.SignedIn;
        }
    }
}

/// <summary>Focus mode (ARCHITECTURE §14.6): the running session, this week's stats and the emergency exit.</summary>
public sealed class FocusPage() : SettingsPage("Focus mode")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var end = Named(new Button { Content = "End now" }, "End the focus session");
        var status = Card("", null, end, "");

        Header("Last 7 days");
        var stats = new Grid { ColumnSpacing = 12 };
        TextBlock[] values = new TextBlock[4];
        string[] labels = ["Sessions", "Minutes", "Slips", "Ended early"];
        for (var i = 0; i < 4; i++)
        {
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            values[i] = new TextBlock { Text = "–", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] };
            var label = Secondary(labels[i], default);
            label.Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"];
            var cell = new StackPanel { Spacing = 2, Children = { values[i], label } };
            Grid.SetColumn(cell, i);
            stats.Children.Add(cell);
        }

        AddCard(stats);

        Header("How it works");
        Paragraph("Start one by voice or text: \"focus for 50 minutes on maths\" (the study profile) or \"focus for an hour on the report\" (work). ALDREN blocks distracting sites in the browser (with the Companion) and steers you off distracting apps: the first slip is forgiven, the second minimises the app, the third closes it. \"Emergency exit\" ends it at once.");
        Paragraph("Study keeps Discord, Steam, Epic, Battle.net, the Riot client, WhatsApp, Telegram and Netflix away, plus YouTube, Reddit, X, Instagram, TikTok, Facebook, Netflix, Twitch and 9GAG. Work blocks the games and social sites but leaves chat apps alone.");

        end.Click += async (_, _) =>
        {
            await core.EndFocusAsync(CancellationToken.None);
            await ShowAsync();
            await ShellApp.Instance.RefreshTrayAsync();
        };
        await ShowAsync();

        async Task ShowAsync()
        {
            var info = await core.GetFocusAsync(CancellationToken.None);
            status.Title.Text = info.Active ? $"Focusing on {info.Goal}" : "No focus session is running";
            status.Describe(info.Active ? $"Until {info.Ends}  ·  {info.Slips} slip{(info.Slips == 1 ? "" : "s")} so far" : "Say \"focus for 25 minutes on…\" to start one.");
            end.Visibility = info.Active ? Visibility.Visible : Visibility.Collapsed;
            values[0].Text = info.WeekSessions.ToString();
            values[1].Text = info.WeekMinutes.ToString();
            values[2].Text = info.WeekSlips.ToString();
            values[3].Text = info.WeekEndedEarly.ToString();
        }
    }
}

/// <summary>Personality, silent mode and gaming mode: one ALDREN, different behaviour.</summary>
public sealed class ModesPage() : SettingsPage("Modes")
{
    private static readonly string[] Personalities = ["friendly", "professional", "sarcastic", "serious", "concise", "detailed"];
    private static readonly string[] GamingChoices = ["Automatic", "On", "Off"];

    protected override async Task LoadAsync(IAldrenCore core)
    {
        var settings = await core.GetSettingsAsync(CancellationToken.None);
        var info = await core.GetSystemInfoAsync(CancellationToken.None);

        Header("Personality");
        var personality = Named(new ComboBox
        {
            ItemsSource = Personalities.Select(p => char.ToUpperInvariant(p[0]) + p[1..]).ToArray(),
            SelectedIndex = Math.Max(0, Array.IndexOf(Personalities, settings.General.Personality)),
            Width = 170,
        }, "Personality");
        Card("How ALDREN talks", "One assistant, different tone. Or say it: \"switch to sarcastic mode\", \"be more concise\".", personality, "");
        personality.SelectionChanged += async (_, _) =>
        {
            if (personality.SelectedIndex >= 0)
            {
                await SaveAsync(core, s => s with { General = s.General with { Personality = Personalities[personality.SelectedIndex] } });
            }
        };

        Header("Quiet");
        var silent = Switch(info.Silent, "Silent mode");
        Card("Silent mode", "Answers as text only, until you switch it off or ALDREN restarts. Or say \"silent mode on\" / \"silent mode off\".", silent, "");
        silent.Toggled += async (_, _) =>
        {
            await core.SetSilentAsync(silent.IsOn, CancellationToken.None);
            await ShellApp.Instance.RefreshTrayAsync();
        };

        Header("Gaming");
        var gaming = Named(new ComboBox
        {
            ItemsSource = GamingChoices,
            SelectedIndex = info.Gaming switch { "on" => 1, "off" => 2, _ => 0 },
            Width = 170,
        }, "Gaming mode");
        var gamingCard = Card("Gaming mode", GamingText(info), gaming, "");
        gaming.SelectionChanged += async (_, _) =>
        {
            await core.SetGamingModeAsync(gaming.SelectedIndex switch { 1 => "on", 2 => "off", _ => "auto" }, CancellationToken.None);
            gamingCard.Describe(GamingText(await core.GetSystemInfoAsync(CancellationToken.None)));
            await ShellApp.Instance.RefreshTrayAsync();
        };
        Paragraph("In gaming mode ALDREN frees the graphics memory, stays silent and never clicks or types. Automatic turns it on while a fullscreen game or OBS runs.");
    }

    private static string GamingText(SystemInfo info) => info.GamingActive
        ? info.Gaming == "on" ? "Active (you switched it on): the GPU is free and ALDREN stays out of the way." : "Active right now: a fullscreen app or OBS is running."
        : info.Gaming == "off" ? "Off (you switched it off): ALDREN keeps its model even during games." : "Not active. Automatic: it turns on by itself for fullscreen games and OBS.";
}

/// <summary>Routines: named lists of requests, made here or by voice, run by saying their name.</summary>
public sealed class AutomationsPage() : SettingsPage(
    "Automations",
    "A routine is a name and a list of requests ALDREN runs in order, each through the same checks as when you ask yourself (anything risky still asks you). Run one by saying or typing its name.")
{
    private readonly StackPanel _list = new() { Spacing = 4 };

    protected override async Task LoadAsync(IAldrenCore core)
    {
        Header("Your routines");
        Body.Children.Add(_list);

        Header("New routine");
        var name = Named(new TextBox { Header = "Name", PlaceholderText = "study time", MaxLength = 60 }, "Routine name");
        var steps = Named(new TextBox
        {
            Header = "Steps, one per line",
            PlaceholderText = "open VS Code\nfocus for 50 minutes on maths",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96,
        }, "Routine steps");
        var save = Named(new Button { Content = "Save routine", Style = (Style)Application.Current.Resources["AccentButtonStyle"] }, "Save the routine");
        AddCard(new StackPanel { Spacing = 12, Children = { name, steps, save } });
        Paragraph("Or say it: \"create a routine called study time: open VS Code, then focus for 50 minutes on maths\". A routine can't start another routine.");

        save.Click += async (_, _) =>
        {
            var result = await core.SaveRoutineAsync(name.Text, steps.Text, CancellationToken.None);
            var saved = result.StartsWith("Saved", StringComparison.Ordinal);
            Note(result, saved ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            if (saved)
            {
                name.Text = "";
                steps.Text = "";
                await FillAsync(core);
            }
        };

        await FillAsync(core);
    }

    private async Task FillAsync(IAldrenCore core)
    {
        _list.Children.Clear();
        var routines = await core.GetRoutinesAsync(CancellationToken.None);
        if (routines.Count == 0)
        {
            _list.Children.Add(Secondary("No routines yet.", new Thickness(2, 4, 0, 4)));
            return;
        }

        foreach (var routine in routines)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new FontIcon { Glyph = "", FontSize = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 3, 0, 0) });
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = routine.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
            var list = Secondary(string.Join("\n", routine.Steps.Select((s, i) => $"{i + 1}. {s}")), default);
            list.IsTextSelectionEnabled = true;
            text.Children.Add(list);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var delete = Named(new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, VerticalAlignment = VerticalAlignment.Top }, $"Delete the {routine.Name} routine");
            ToolTipService.SetToolTip(delete, "Delete this routine");
            Grid.SetColumn(delete, 2);
            row.Children.Add(delete);
            var card = new Border { Style = (Style)Application.Current.Resources["AldrenCardStyle"], Child = row };
            delete.Click += async (_, _) =>
            {
                await core.DeleteRoutineAsync(routine.Name, CancellationToken.None);
                await FillAsync(core);
            };
            _list.Children.Add(card);
        }
    }
}
