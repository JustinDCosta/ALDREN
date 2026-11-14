using Aldren.App.Island.Native;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.UI;
using Monitor = Aldren.App.Island.Native.Monitor;

namespace Aldren.App;

/// <summary>
/// What ALDREN's ordinary windows share: content drawn into the title bar (so the bar follows the light or dark theme
/// like the rest of the window), caption buttons coloured to match, and sizes given in DIPs for the monitor in use.
/// </summary>
internal static class WindowChrome
{
    /// <param name="dragRegion">The part of the custom title bar you can drag the window by; buttons stay outside it.</param>
    public static void Apply(Window window, FrameworkElement root, UIElement dragRegion)
    {
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(dragRegion);
        var bar = window.AppWindow.TitleBar;
        Colour();
        root.ActualThemeChanged += (_, _) => Colour();

        void Colour()
        {
            var dark = root.ActualTheme == ElementTheme.Dark;
            var ink = dark ? Colors.White : Colors.Black;
            bar.ButtonBackgroundColor = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
            bar.ButtonForegroundColor = ink;
            bar.ButtonInactiveForegroundColor = Color.FromArgb(255, 128, 128, 128);
            bar.ButtonHoverForegroundColor = ink;
            bar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(32, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0);
            bar.ButtonPressedForegroundColor = ink;
            bar.ButtonPressedBackgroundColor = dark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(14, 0, 0, 0);
        }
    }

    /// <summary>The monitor you're working on (the one holding the foreground window).</summary>
    public static Monitor CurrentMonitor() => Focus.ForegroundMonitor();

    /// <summary>Centres the window on the monitor in use at the given size in DIPs, never larger than its work area.</summary>
    public static void CentreOn(Window window, Monitor monitor, int widthDip, int heightDip)
    {
        var work = monitor.WorkArea;
        var width = Math.Min((int)Math.Round(widthDip * monitor.Scale), work.Width - 40);
        var height = Math.Min((int)Math.Round(heightDip * monitor.Scale), work.Height - 40);
        window.AppWindow.MoveAndResize(new RectInt32(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, width, height));
    }

    /// <summary>Docks the window to the right edge of the monitor's work area, full height, at the given width in DIPs.</summary>
    public static void DockRight(Window window, Monitor monitor, int widthDip)
    {
        var work = monitor.WorkArea;
        var width = Math.Min((int)Math.Round(widthDip * monitor.Scale), work.Width);
        window.AppWindow.MoveAndResize(new RectInt32(work.Right - width, work.Top, width, work.Height));
    }
}
