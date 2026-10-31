using Microsoft.UI.Xaml;
using S2.Island.Diagnostics;

namespace S2.Island;

public partial class App : Application
{
    private IslandWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    // No arguments: interactive. Ctrl+Alt+Space cycles the states, Ctrl+Alt+C toggles visibility to screen capture,
    // Ctrl+Alt+M moves to the next monitor, Ctrl+Alt+Q quits.
    // --test runs the automated checklist and exits; --nchittest switches click-through to the WM_NCHITTEST variant.
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var argv = Environment.GetCommandLineArgs();
        _window = new IslandWindow(hitTestMode: argv.Contains("--nchittest"));
        if (argv.Contains("--test"))
        {
            _ = new Checklist(_window).RunAndExitAsync();
        }
        else
        {
            _window.SetState(IslandState.Compact);
        }
    }
}
