using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Broadside.Viewer.WinUI.Sample;

/// <summary>The entry point: starts WinUI with the app's synchronization context (there is no App.xaml to generate one).</summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(args);
        });
    }
}
