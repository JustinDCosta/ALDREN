using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aldren.App.Settings;

public sealed partial class SettingsWindow : Window
{
    /// <summary>The sections in the pane: a null tag is a group heading.</summary>
    private static readonly (string? Tag, string Title, string Glyph)[] Sections =
    [
        (null, "Assistant", ""),
        ("General", "General", ""),
        ("Voice", "Voice & wake word", ""),
        ("Modes", "Modes", ""),
        ("Automations", "Automations", ""),
        ("Focus", "Focus mode", ""),
        (null, "Your things", ""),
        ("Memory", "Memory", ""),
        ("Reminders", "Reminders & tasks", ""),
        ("Home", "Home & away", ""),
        (null, "Connections", ""),
        ("Browser", "Browser", ""),
        ("Google", "Google account", ""),
        ("Cloud", "Cloud", ""),
        (null, "Privacy", ""),
        ("Permissions", "Permissions", ""),
        ("Privacy", "Privacy & data", ""),
        ("Activity", "Activity history", ""),
    ];

    private static readonly (string Tag, string Title, string Glyph)[] Footer =
    [
        ("System", "Models & performance", ""),
        ("Diagnostics", "Diagnostics", ""),
        ("About", "About", ""),
    ];

    public SettingsWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this, Root, TitleBar);
        WindowChrome.CentreOn(this, WindowChrome.CurrentMonitor(), 1040, 720);
        WindowKeys.CloseOnEscape(this, Navigation);

        foreach (var (tag, title, glyph) in Sections)
        {
            Navigation.MenuItems.Add(tag is null
                ? new NavigationViewItemHeader { Content = title }
                : Item(tag, title, glyph));
        }

        foreach (var (tag, title, glyph) in Footer)
        {
            Navigation.FooterMenuItems.Add(Item(tag, title, glyph));
        }

        Show("General");
    }

    /// <summary>Selects a section by its tag ("General", "Voice", "Browser"…).</summary>
    public void Show(string tag)
    {
        var item = Navigation.MenuItems.Concat(Navigation.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
        if (item is not null)
        {
            Navigation.SelectedItem = item;
        }
    }

    private static NavigationViewItem Item(string tag, string title, string glyph) =>
        new() { Content = title, Tag = tag, Icon = new FontIcon { Glyph = glyph } };

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
        {
            return;
        }

        Page.Content = tag switch
        {
            "General" => new GeneralPage(),
            "Voice" => new VoicePage(),
            "Modes" => new ModesPage(),
            "Automations" => new AutomationsPage(),
            "Focus" => new FocusPage(),
            "Memory" => new MemoryPage(),
            "Reminders" => new RemindersPage(),
            "Home" => new HomeAwayPage(),
            "Browser" => new BrowserSettingsPage(),
            "Google" => new GooglePage(),
            "Cloud" => new CloudPage(),
            "Permissions" => new PermissionsPage(),
            "Privacy" => new PrivacyPage(),
            "Activity" => new ActivityPage(),
            "System" => new SystemPage(),
            "Diagnostics" => new DiagnosticsPage(),
            _ => new AboutPage(),
        };
        Page.ChangeView(null, 0, null, disableAnimation: true);
    }
}
