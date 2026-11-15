using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace Broadside.Viewer.Wpf.Tests;

/// <summary>Runs a test body on a fresh STA thread with a WPF dispatcher, as a WPF application's UI thread runs the view.</summary>
internal static class Sta
{
    public static void Run(Func<Task> body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await body();
                }
#pragma warning disable CA1031 // rethrown on the test's thread
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(3)))
        {
            Assert.Fail("The STA test body did not finish within three minutes.");
        }

        failure?.Throw();
    }

    /// <summary>Lets the dispatcher run what is queued (continuations, layout and render) once.</summary>
    public static async Task YieldAsync() => await Dispatcher.Yield(DispatcherPriority.Background);
}
