using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Aldren.App;

internal static class WindowKeys
{
    public static void CloseOnEscape(Window window, UIElement root)
    {
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, e) =>
        {
            e.Handled = true;
            window.Close();
        };
        root.KeyboardAccelerators.Add(escape);
    }
}
