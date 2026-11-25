using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Broadside.TestSupport;

namespace Broadside.Viewer.Avalonia.Tests;

/// <summary>A viewer that leaves a window which outlives it (a tabbed app's main window) is not kept alive by that window.</summary>
public sealed class LifetimeTests
{
    [Fact]
    public Task A_view_removed_from_a_window_that_stays_open_is_collected() => Ui.RunAsync(async () =>
    {
        // #599: the view subscribed to the window's ScalingChanged on attach and never unsubscribed, because the window can no longer
        // be found from the view once it is detached; the window's invocation list then kept every view (and its session) alive.
        var host = new ContentControl();
        var window = new Window { Width = 600, Height = 800, Content = host };
        window.Show();
        try
        {
            var views = new List<WeakReference>();
            for (int i = 0; i < 3; i++)
            {
                views.Add(await ShowAndRemoveAsync(host));
            }

            // Let the window lay out and render without the views (it drops what it last drew of them).
            for (int i = 0; i < 5; i++)
            {
                Pump();
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            GC.Collect();
            Assert.Equal(0, views.Count(v => v.IsAlive));
        }
        finally
        {
            window.Close();
        }
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> ShowAndRemoveAsync(ContentControl host)
    {
        DocumentSession session = DocumentSession.Open(
            await File.ReadAllBytesAsync(ViewerHarness.CorpusFile("text-standard14.pdf"), TestContext.Current.CancellationToken),
            ViewerHarness.Options(ViewerThreading.Cooperative));
        var view = new PdfView { Options = ViewerHarness.Options(ViewerThreading.Cooperative), Session = session };
        host.Content = view;
        var watch = Stopwatch.StartNew();
        while (view.State is not { PageCount: > 0, IsLoading: false, IsRendering: false } && watch.Elapsed < TimeSpan.FromMinutes(1))
        {
            Pump();
            await Task.Delay(1);
        }

        Assert.True(view.State.PageCount > 0, "the document did not open");
        host.Content = null;
        view.Session = null;
        await session.DisposeAsync();
        view.Dispose();
        Pump();
        return new WeakReference(view);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
