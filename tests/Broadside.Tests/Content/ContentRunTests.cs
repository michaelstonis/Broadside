using Broadside.Content;
using Broadside.Tests.Document;

namespace Broadside.Tests.Content;

/// <summary>
/// What a run reads and how processors take part: the page's <c>Contents</c> forms (Table 31), text object boundaries (Table 105),
/// fan-out to several processors and runs started from inside a callback. ISO 32000-2 §7.7.3.3, §7.8.2, §9.4.1.
/// </summary>
public sealed class ContentRunTests
{
    [Fact]
    public void Text_objects_are_reported_at_bt_and_et()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("BT ET BT ET", ContentEvents.Text);

        Assert.Equal(["BT", "ET", "BT", "ET"], events.Body);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Unbalanced_text_objects_are_repaired_and_recorded()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("ET BT BT", ContentEvents.Text);

        Assert.Equal(["BT", "ET"], events.Body);
        Assert.Equal(["ContentTextObjectUnbalanced"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Text_showing_outside_a_text_object_is_out_of_context()
    {
        Assert.Equal(["ContentOperatorOutOfContext", "ContentFontMissing"], ContentPdf.Codes(ContentPdf.Run("(x) Tj").Diagnostics));
    }

    [Fact]
    public void A_contents_array_reads_as_its_parts_joined_by_white_space()
    {
        // Without the separator the two parts would read as the keyword "Sq".
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run(["0 0 m 1 1 l S", "q Q"], ContentEvents.Operators);

        Assert.Equal(["Op 0 0 m", "Op 1 1 l", "Op S", "Op q", "Op Q"], events.Body);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_contents_array_element_that_is_not_a_stream_is_skipped()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents [4 0 R 5 0 R] >>",
            "<< /Length 1 >>\nstream\nq\nendstream",
            "(not a stream)");
        using PdfDocument document = PdfDocument.Open(file);
        var processor = new RecordingProcessor(ContentEvents.Operators);

        document.Pages[0].ProcessContent(processor);

        Assert.Equal(["Op q"], processor.Body);
        Assert.Equal(["ContentStreamInvalid", "ContentUnbalancedSave"], ContentPdf.Codes(document.Diagnostics));
    }

    [Fact]
    public void Contents_that_is_neither_a_stream_nor_an_array_gives_an_empty_page()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792] /Contents 42"));
        var processor = new RecordingProcessor();

        document.Pages[0].ProcessContent(processor);

        Assert.Equal(["BeginRun Page", "EndRun"], processor.Lines);
        Assert.Equal(["ContentStreamInvalid"], ContentPdf.Codes(document.Diagnostics));
    }

    [Fact]
    public void The_context_describes_the_page_run()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("q Q"));
        var probe = new ContextProbe();

        document.Pages[0].ProcessContent(probe);

        Assert.Same(document, probe.Document);
        Assert.Same(document.Pages[0].Dictionary, probe.PageDictionary);
        Assert.NotNull(probe.Resources);
        Assert.Equal(ContentRunKind.Page, probe.RunKind);
        Assert.Equal(0, probe.Depth);
        Assert.Equal(Broadside.Graphics.Matrix.Identity, probe.BaseMatrix);
        Assert.False(probe.Hidden);
    }

    [Fact]
    public void A_composite_gives_each_processor_only_the_events_it_asks_for()
    {
        var paths = new RecordingProcessor(ContentEvents.Paths);
        var text = new RecordingProcessor(ContentEvents.Text);
        var composite = new CompositeContentProcessor(paths, text);
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("BT ET 0 0 m 1 1 l S"));

        document.Pages[0].ProcessContent(composite);

        Assert.Equal(ContentEvents.Paths | ContentEvents.Text, composite.Events);
        Assert.Equal(["BeginRun Page", "Paint Stroke NonZero M 0,0 L 1,1 ctm=[1 0 0 1 0 0]", "EndRun"], paths.Lines);
        Assert.Equal(["BeginRun Page", "BT", "ET", "EndRun"], text.Lines);
        Assert.Equal([paths, text], composite.Processors);
    }

    [Fact]
    public void A_processor_can_run_another_page_from_inside_a_callback()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("0 0 m 1 1 l S 2 2 m 3 3 l S"));
        var inner = new RecordingProcessor(ContentEvents.Paths);
        var outer = new ReentrantProcessor(document.Pages[0], inner);

        document.Pages[0].ProcessContent(outer);

        Assert.Equal(2, outer.Paints);
        Assert.Equal(4, inner.Count("PaintPath"));
    }

    [Fact]
    public void Interpreting_a_page_twice_reports_the_same_events()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("q 2 0 0 2 0 0 cm 0 0 10 10 re W n 0 0 m 1 1 l S Q"));
        var first = new RecordingProcessor();
        var second = new RecordingProcessor();

        document.Pages[0].ProcessContent(first);
        document.Pages[0].ProcessContent(second);

        Assert.Equal(first.Lines, second.Lines);
    }
}

/// <summary>Records what the context says at the start of a run.</summary>
internal sealed class ContextProbe : ContentProcessor
{
    public PdfDocument? Document { get; private set; }

    public Broadside.Objects.CosDictionary? PageDictionary { get; private set; }

    public Broadside.Objects.CosDictionary? Resources { get; private set; }

    public ContentRunKind RunKind { get; private set; }

    public int Depth { get; private set; } = -1;

    public Broadside.Graphics.Matrix BaseMatrix { get; private set; }

    public bool Hidden { get; private set; } = true;

    public override void BeginRun(ContentContext context)
    {
        Document = context.Document;
        PageDictionary = context.Page?.Dictionary;
        Resources = context.Resources;
        RunKind = context.RunKind;
        Depth = context.Depth;
        BaseMatrix = context.StreamBaseMatrix;
        Hidden = context.IsHidden;
    }
}

/// <summary>Runs a page again, with another processor, from inside each paint callback.</summary>
internal sealed class ReentrantProcessor(PdfPage page, ContentProcessor inner) : ContentProcessor
{
    public int Paints { get; private set; }

    public override ContentEvents Events => ContentEvents.Paths;

    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        Paints++;
        page.ProcessContent(inner);
        Assert.Equal(2, path.Path.Verbs.Length);
    }
}
