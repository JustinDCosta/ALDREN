using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aldren.App.Settings;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 640));
        WindowKeys.CloseOnEscape(this, Navigation);
        Navigation.SelectedItem = Navigation.MenuItems[0];
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag } item)
        {
            return;
        }

        Page.Content = tag switch
        {
            "General" => new GeneralPage(),
            "Voice" => new VoicePage(),
            "Memory" => new MemoryPage(),
            "Reminders" => new RemindersPage(),
            "Home" => new HomeAwayPage(),
            "Cloud" => new CloudPage(),
            "Permissions" => new PermissionsPage(),
            "Activity" => new ActivityPage(),
            "Diagnostics" => new DiagnosticsPage(),
            _ => new ComingLaterPage((string)item.Content, tag["Later:".Length..]),
        };
    }
}
