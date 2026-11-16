using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Broadside.Viewer.WinUI.Sample;

/// <summary>
/// The entry point: starts WinUI with the app's synchronization context (there is no App.xaml to generate one). Every startup step
/// and every unhandled exception is written to <see cref="StartupLog"/>, so an app that exits before its window opens says why.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            StartupLog.Write($"AppDomain.UnhandledException (terminating: {e.IsTerminating}): {e.ExceptionObject}");
        StartupLog.Write($"Starting (args: {string.Join(' ', args)}; base directory: {AppContext.BaseDirectory})");
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(callback =>
            {
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    _ = new App(args);
                    StartupLog.Write("App created");
                }
                catch (Exception e)
                {
                    StartupLog.Write("Creating the app failed: " + e);
                    throw;
                }
            });
            StartupLog.Write("Application.Start returned: the app exited");
        }
        catch (Exception e)
        {
            StartupLog.Write("Startup failed: " + e);
            throw;
        }
    }
}
