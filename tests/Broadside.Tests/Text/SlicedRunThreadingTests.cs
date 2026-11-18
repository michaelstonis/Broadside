using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Broadside.Content;
using Broadside.Layout;
using Broadside.Rendering;
using Broadside.TestSupport;
using Broadside.Tests.Layout;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// Issue #584: a run in slices may continue on another thread after each await, so the interpreter it holds can end on a thread other
/// than the one it began on. That interpreter must then be cached by one thread only, or two runs on two threads share it and one
/// reads the other's state (a NullReferenceException in <c>Tf</c> once the other run gave its document back). ISO 32000-2 §7.8.2.
/// </summary>
public sealed class SlicedRunThreadingTests
{
    private const string File = "layout-two-column-untagged.pdf";

    [Fact]
    public void A_run_ended_on_another_thread_is_reused_by_that_thread_only()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(File));
        PdfPage page = document.Pages[0];
        using var first = new WorkerThread();
        using var second = new WorkerThread();

        // The first thread has an interpreter cached; a run in slices begun there takes it, and is continued and ended on the
        // second thread, as an await between two slices does.
        ContentInterpreter cached = first.Run(() => BeginAndEnd(page, document));
        ContentInterpreter moved = first.Run(() => ContentInterpreter.BeginPageRun(page, document, new NoEvents(), ContentInterpreter.DefaultOptions));
        Assert.Same(cached, moved);
        second.Run(() =>
        {
            while (!moved.ContinuePageRun(long.MaxValue))
            {
            }

            moved.EndPageRun();
            return 0;
        });

        ContentInterpreter onFirst = first.Run(() => BeginAndEnd(page, document));
        ContentInterpreter onSecond = second.Run(() => BeginAndEnd(page, document));
        Assert.Same(moved, onSecond);
        Assert.NotSame(onFirst, onSecond);
    }

    [Fact]
    public async Task Threads_a_run_in_slices_passed_through_extract_text_at_once_without_sharing_an_interpreter()
    {
        using PdfDocument document = await PdfDocument.OpenAsync(Corpus.Bytes(File), TestContext.Current.CancellationToken);
        PdfPage page = document.Pages[0];
        string expected = Project(page.ExtractText());
        WorkerThread[] threads = [.. Enumerable.Range(0, Math.Max(4, Environment.ProcessorCount)).Select(_ => new WorkerThread())];
        try
        {
            // One run in slices handed from thread to thread, each slice on the next thread, as awaits between slices hand it on.
            threads[0].Run(() => BeginAndEnd(page, document));
            for (int round = 0; round < 20; round++)
            {
                TextExtraction extraction = threads[0].Run(() =>
                {
                    TextExtraction begun = page.BeginTextExtraction(new TextExtractionOptions());
                    begun.Continue(TimeSpan.Zero);
                    return begun;
                });
                int next = 1;
                while (!threads[next % threads.Length].Run(() => extraction.Continue(TimeSpan.Zero)))
                {
                    next++;
                }

                extraction.Dispose();

                // Then every thread extracts the page whole at the same moment.
                using var start = new Barrier(threads.Length);
                Task<string>[] wholes = [.. threads.Select(thread => thread.Start(() =>
                {
                    start.SignalAndWait(TimeSpan.FromSeconds(30));
                    return Project(page.ExtractText());
                }))];
                foreach (Task<string> whole in wholes)
                {
                    Assert.Equal(expected, await whole);
                }
            }
        }
        finally
        {
            foreach (WorkerThread thread in threads)
            {
                thread.Dispose();
            }
        }
    }

    [Fact]
    public void A_run_cannot_continue_after_it_ended_or_end_twice()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(File));
        PdfPage page = document.Pages[0];
        ContentInterpreter run = ContentInterpreter.BeginPageRun(page, document, new NoEvents(), ContentInterpreter.DefaultOptions);
        Assert.False(run.ContinuePageRun(0));
        run.EndPageRun();
        Assert.Throws<InvalidOperationException>(() => run.ContinuePageRun(long.MaxValue));
        Assert.Throws<InvalidOperationException>(run.EndPageRun);

        ContentInterpreter done = ContentInterpreter.BeginPageRun(page, document, new NoEvents(), ContentInterpreter.DefaultOptions);
        Assert.True(done.ContinuePageRun(long.MaxValue));
        Assert.Throws<InvalidOperationException>(() => done.ContinuePageRun(long.MaxValue));
        done.EndPageRun();
    }

    [Fact]
    public async Task A_slice_of_a_run_cannot_start_while_another_slice_of_it_runs()
    {
        using PdfDocument document = await PdfDocument.OpenAsync(Corpus.Bytes(File), TestContext.Current.CancellationToken);
        PdfPage page = document.Pages[0];
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var processor = new Blocking(inside, release);
        ContentInterpreter run = ContentInterpreter.BeginPageRun(page, document, processor, ContentInterpreter.DefaultOptions);
        Task<bool> slice = Task.Run(() => run.ContinuePageRun(long.MaxValue), TestContext.Current.CancellationToken);
        Assert.True(inside.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => run.ContinuePageRun(long.MaxValue));
        Assert.Throws<InvalidOperationException>(run.EndPageRun);
        release.Set();
        Assert.True(await slice);
        run.EndPageRun();
    }

    [Fact]
    public async Task Sliced_extractions_that_hop_threads_and_whole_ones_running_beside_them_all_read_the_page()
    {
        using PdfDocument document = await PdfDocument.OpenAsync(Corpus.Bytes(File), TestContext.Current.CancellationToken);
        PdfPage page = document.Pages[0];
        string expectedText = Project(page.ExtractText());
        string expectedLayout = LayoutProjection.Of(page.ExtractLayout());
        int expectedOperations = page.CreateDisplayList().OperationCount;
        CancellationToken token = TestContext.Current.CancellationToken;
        var failures = new ConcurrentQueue<string>();
        int workers = Math.Max(4, Environment.ProcessorCount);
        var tasks = new List<Task>();
        for (int worker = 0; worker < workers; worker++)
        {
            int kind = worker % 4;
            tasks.Add(Task.Run(
                async () =>
                {
                    for (int round = 0; round < 25; round++)
                    {
                        string? failure = kind switch
                        {
                            // Each slice after an await, so on whichever pool thread resumes it.
                            0 => Check(expectedText, Project(await SliceAsync(page.BeginTextExtraction(new TextExtractionOptions()), static work => work.Result))),
                            1 => Check(expectedLayout, LayoutProjection.Of(await SliceAsync(page.BeginLayoutExtraction(new LayoutOptions()), static work => work.Result))),
                            2 => Check(expectedOperations.ToString(CultureInfo.InvariantCulture), (await SliceAsync(page.BeginDisplayList(DisplayLayers.Content, DisplayIntent.View, ContentInterpreter.DefaultOptions), static work => work.Result)).OperationCount.ToString(CultureInfo.InvariantCulture)),
                            _ => Check(expectedText, Project(page.ExtractText())),
                        };
                        if (failure is not null)
                        {
                            failures.Enqueue(failure);
                        }

                        // Some runs are abandoned between two slices, on another thread than the one that began them.
                        TextExtraction abandoned = page.BeginTextExtraction(new TextExtractionOptions());
                        abandoned.Continue(TimeSpan.Zero);
                        await Task.Yield();
                        abandoned.Dispose();
                    }
                },
                token));
        }

        await Task.WhenAll(tasks);
        Assert.Empty(failures);
    }

    private static async Task<TResult> SliceAsync<TWork, TResult>(TWork work, Func<TWork, TResult> result)
        where TWork : IDisposable
    {
        using (work)
        {
            while (!Continue(work))
            {
                await Task.Yield();
            }

            return result(work);
        }
    }

    private static bool Continue<TWork>(TWork work) => work switch
    {
        TextExtraction text => text.Continue(TimeSpan.Zero),
        LayoutExtraction layout => layout.Continue(TimeSpan.Zero),
        DisplayListBuild build => build.Continue(TimeSpan.Zero),
        _ => throw new ArgumentException("Not a sliced step.", nameof(work)),
    };

    private static string? Check(string expected, string actual) => expected == actual ? null : actual;

    private static ContentInterpreter BeginAndEnd(PdfPage page, PdfDocument document)
    {
        ContentInterpreter run = ContentInterpreter.BeginPageRun(page, document, new NoEvents(), ContentInterpreter.DefaultOptions);
        run.EndPageRun();
        return run;
    }

    private static string Project(TextPage text)
    {
        var output = new StringBuilder();
        output.Append(text.Text).Append('\n');
        foreach (TextGlyph glyph in text.Glyphs)
        {
            output.Append(CultureInfo.InvariantCulture, $"{glyph.CharacterCode} {glyph.Quad} {glyph.Font.BaseFont} {glyph.FontSize}\n");
        }

        return output.ToString();
    }

    private sealed class NoEvents : ContentProcessor
    {
        public override ContentEvents Events => ContentEvents.None;
    }

    /// <summary>Holds the first glyph until released, so a slice is known to be running.</summary>
    private sealed class Blocking(ManualResetEventSlim inside, ManualResetEventSlim release) : ContentProcessor
    {
        public override ContentEvents Events => ContentEvents.Glyphs;

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
        {
            if (!inside.IsSet)
            {
                inside.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            }
        }
    }

    /// <summary>A thread that runs what it is given, one item at a time, so thread-static state stays put between two items.</summary>
    private sealed class WorkerThread : IDisposable
    {
        private readonly BlockingCollection<Action> _work = [];
        private readonly Thread _thread;

        public WorkerThread()
        {
            _thread = new Thread(() =>
            {
                foreach (Action action in _work.GetConsumingEnumerable())
                {
                    action();
                }
            })
            { IsBackground = true };
            _thread.Start();
        }

        public T Run<T>(Func<T> function) => Start(function).GetAwaiter().GetResult();

        public Task<T> Start<T>(Func<T> function)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _work.Add(() =>
            {
                try
                {
                    completion.SetResult(function());
                }
#pragma warning disable CA1031 // handed to the caller, which rethrows it
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    completion.SetException(exception);
                }
            });
            return completion.Task;
        }

        public void Dispose()
        {
            _work.CompleteAdding();
            _thread.Join();
            _work.Dispose();
        }
    }
}
