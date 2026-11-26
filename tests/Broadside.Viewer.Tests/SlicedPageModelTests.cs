using System.Globalization;
using System.Text;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>
/// Issue #546: in cooperative mode a page's selectable text (hit-test index, reading order, logical text) and its accessible tree
/// are built in slices between frames, and are what the background mode builds in one step.
/// </summary>
public sealed class SlicedPageModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Files => new()
    {
        "text-truetype-embedded.pdf",
        "text-tagged-replacements.pdf",
        "text-tagged-spanning.pdf",
        "tagged-structure.pdf",
        "layout-two-column-untagged.pdf",
        "layout-tables.pdf",
        "layout-headings-lists.pdf",
        "annotations-link.pdf",
    };

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Selectable_text_built_in_slices_is_the_text_built_in_one_step(string fileName)
    {
        await using DocumentSession whole = DocumentSession.Open(Corpus.Bytes(fileName), ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Background));
        int yields = 0;
        await using DocumentSession sliced = DocumentSession.Open(Corpus.Bytes(fileName), Sliced(fileName, () => yields++));
        var expected = new TextSelection(whole);
        var actual = new TextSelection(sliced);
        for (int page = 0; page < whole.PageCount; page++)
        {
            int before = yields;
            SelectableText text = await actual.ReadPageAsync(page, Token);
            Assert.Equal(Project(await expected.ReadPageAsync(page, Token)), Project(text));

            // The list, the index, the layout's text run, the order and the logical text's run each take a slice at least.
            Assert.True(yields - before >= 5, $"Page {page} of {fileName} loaded in {yields - before} yields.");
        }

        expected.SelectAll();
        actual.SelectAll();
        Assert.Equal(await expected.GetTextAsync(Token), await actual.GetTextAsync(Token));
    }

    [Fact]
    public async Task Selectable_text_with_another_reading_order_is_built_in_slices_too()
    {
        const string fileName = "layout-two-column-untagged.pdf";
        await using DocumentSession whole = DocumentSession.Open(Corpus.Bytes(fileName), ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Background));
        await using DocumentSession sliced = DocumentSession.Open(Corpus.Bytes(fileName), Sliced(fileName, static () => { }));
        SelectableText expected = await new TextSelection(whole, ContentReadingOrder.Instance).ReadPageAsync(0, Token);
        SelectableText actual = await new TextSelection(sliced, ContentReadingOrder.Instance).ReadPageAsync(0, Token);
        Assert.Equal(Project(expected), Project(actual));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task An_accessible_tree_built_in_slices_is_the_tree_built_in_one_step(string fileName)
    {
        await using DocumentSession whole = DocumentSession.Open(Corpus.Bytes(fileName), ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Background));
        int yields = 0;
        await using DocumentSession sliced = DocumentSession.Open(Corpus.Bytes(fileName), Sliced(fileName, () => yields++));
        var expected = new AccessibilityBridge(whole);
        var actual = new AccessibilityBridge(sliced);
        for (int page = 0; page < whole.PageCount; page++)
        {
            int before = yields;
            AccessibleNode node = await actual.ReadPageAsync(page, Token);
            Assert.Equal(Project(await expected.ReadPageAsync(page, Token)), Project(node));
            Assert.True(yields - before >= 3, $"Page {page} of {fileName} loaded in {yields - before} yields.");
        }
    }

    [Fact]
    public async Task An_edit_between_two_slices_starts_the_load_again_from_the_edited_page()
    {
        const string fileName = "text-truetype-embedded.pdf";
        DocumentSession? session = null;
        int yields = 0;
        bool edited = false;
        ViewerOptions options = Sliced(fileName, () =>
        {
            // Between two slices of the page's load, text is added to the page.
            if (++yields == 4 && session is { } open)
            {
                using IDisposable edit = open.BeginEdit();
                PdfCanvasFont helvetica = open.Document.GetStandard14Font(Standard14Font.Helvetica);
                using PdfCanvas canvas = open.Document.Pages[0].CreateCanvas();
                canvas.BeginText();
                canvas.SetFont(helvetica, 12);
                canvas.MoveText(72, 100);
                canvas.ShowText("Added");
                canvas.EndText();
                edited = true;
            }
        });
        await using (session = DocumentSession.Open(Corpus.Bytes(fileName), options))
        {
            var selection = new TextSelection(session);
            var bridge = new AccessibilityBridge(session);
            SelectableText text = await selection.ReadPageAsync(0, Token);
            Assert.True(edited);
            selection.SelectAll();
            Assert.EndsWith("Added", await selection.GetTextAsync(Token), StringComparison.Ordinal);
            Assert.Equal(text.Index.GlyphCount, text.Length);

            yields = 0;
            edited = false;
            AccessibleNode page = await bridge.ReadPageAsync(0, Token);
            Assert.True(edited);
            Assert.Contains("Added", Project(page), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_session_closed_between_two_slices_ends_the_load_with_the_closing_and_nothing_else()
    {
        // Issue #584: the slices resume on pool threads, and the session is closed from another thread between two of them (or while
        // one runs). The load ends with the session's closing or completes; nothing else escapes, observed or not.
        const string fileName = "layout-two-column-untagged.pdf";
        var unobserved = new List<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            lock (unobserved)
            {
                unobserved.AddRange(e.Exception.InnerExceptions);
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            for (int closeAt = 1; closeAt <= 24; closeAt++)
            {
                DocumentSession? session = null;
                Task? closing = null;
                int yields = 0;
                int at = closeAt;
                ViewerOptions options = ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Cooperative)
                    .WithSliceBudget(TimeSpan.FromTicks(1))
                    .UseYield(async _ =>
                    {
                        if (Interlocked.Increment(ref yields) == at && session is { } open)
                        {
                            closing = Task.Run(async () => await open.DisposeAsync(), CancellationToken.None);
                        }

                        await Task.Yield();
                    });
                session = DocumentSession.Open(Corpus.Bytes(fileName), options);
                var selection = new TextSelection(session);
                var bridge = new AccessibilityBridge(session);
                Task<SelectableText> text = selection.ReadPageAsync(0, Token).AsTask();
                Task<AccessibleNode> node = bridge.ReadPageAsync(0, Token).AsTask();
                foreach (Task load in new Task[] { text, node })
                {
                    try
                    {
                        await load;
                    }
                    catch (Exception exception) when (exception is ObjectDisposedException or OperationCanceledException)
                    {
                    }
                }

                await session.DisposeAsync();
                if (closing is not null)
                {
                    await closing;
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        lock (unobserved)
        {
            Assert.DoesNotContain(unobserved, exception => exception is not (ObjectDisposedException or OperationCanceledException));
        }
    }

    /// <summary>Cooperative options with a one-tick slice budget and a yield that counts (or acts) and returns at once.</summary>
    private static ViewerOptions Sliced(string fileName, Action onYield) =>
        ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Cooperative)
            .WithSliceBudget(TimeSpan.FromTicks(1))
            .UseYield(_ =>
            {
                onYield();
                return ValueTask.CompletedTask;
            });

    private static string Project(SelectableText text)
    {
        var output = new StringBuilder();
        output.Append(CultureInfo.InvariantCulture, $"{text.Length} of {text.Index.GlyphCount}\n");
        Span<Quad> quads = new Quad[Math.Max(text.Length, 1)];
        for (int position = 0; position < text.Length; position++)
        {
            output.Append(CultureInfo.InvariantCulture, $"{text.GetGlyphIndex(position)} '{text.GetGlyphText(position)}'\n");
        }

        int count = text.GetQuads(0, text.Length, quads);
        foreach (Quad quad in quads[..Math.Min(count, quads.Length)])
        {
            output.Append(CultureInfo.InvariantCulture, $"{quad}\n");
        }

        return output.ToString();
    }

    private static string Project(AccessibleNode node)
    {
        var output = new StringBuilder();
        Append(output, node, 0);
        return output.ToString();
    }

    private static void Append(StringBuilder output, AccessibleNode node, int depth)
    {
        output.Append(' ', depth * 2).Append(CultureInfo.InvariantCulture, $"{node.Role} {node.Name} {node.PageBounds} {node.Level}\n");
        foreach (AccessibleNode child in node.Children)
        {
            Append(output, child, depth + 1);
        }
    }
}
