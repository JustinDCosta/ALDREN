using Aldren.Common;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Aldren.App;

public static class Program
{
    [STAThread]
    private static int Main()
    {
        using var instance = SingleInstance.TryAcquire(SingleInstance.AppName);
        if (instance is null)
        {
            // Another App is already running.
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new ShellApp();
        });
        return 0;
    }
}
