using Broadside.Content;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Content;

/// <summary>
/// The content lexer and interpreter are hot paths and allocate nothing per operator (CLAUDE.md, "Code conventions"). This is
/// the build-breaking half of that rule; <c>ContentLexerBenchmarks</c> and <c>ContentInterpretationBenchmarks</c> in
/// <c>bench/Broadside.Benchmarks</c> are the measuring half. The lexer test drives the internal reader because no public seam
/// exposes it alone; the interpreter test goes through <see cref="PdfPage.ProcessContent(ContentProcessor)"/>.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class ContentAllocationTests
{
    private const int WarmUp = 50;

    [Fact]
    public void Reading_a_megabyte_of_path_operators_allocates_nothing()
    {
        byte[] content = ContentSamples.PathHeavy(1 << 20);
        var arena = new OperandArena();
        int operators = 0;

        long allocated = Allocations.Measure(() => operators = ReadAll(content, arena), WarmUp);

        Assert.True(operators > 100_000, $"Only {operators} operators.");
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Reading_the_filter_chain_content_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("filter-chain.pdf"));
        byte[] content = document.DecodeStream(Filters.CorpusFilterTests.ContentStream(document)).ToArray();
        var arena = new OperandArena();
        int operators = 0;

        long allocated = Allocations.Measure(() => operators = ReadAll(content, arena), WarmUp);

        Assert.Equal(5, operators);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Interpreting_a_page_allocates_nothing_once_warm()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(System.Text.Encoding.ASCII.GetString(ContentSamples.PathHeavy(1 << 20))));
        PdfPage page = document.Pages[0];
        var processor = new CountingProcessor();

        long allocated = Allocations.Measure(
            () =>
            {
                processor.Reset();
                page.ProcessContent(processor);
            },
            WarmUp);

        Assert.True(processor.Paints > 20_000, $"Only {processor.Paints} paints.");
        Assert.True(processor.Clips > 5_000, $"Only {processor.Clips} clips.");
        Assert.Equal(0, allocated);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Showing_text_allocates_nothing_per_glyph_once_warm()
    {
        string content = System.Text.Encoding.ASCII.GetString(ContentSamples.TextHeavy(10_000));
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(ContentPdf.HelveticaResources, content, ContentPdf.Helvetica));
        PdfPage page = document.Pages[0];
        var processor = new GlyphCounter();

        long allocated = Allocations.Measure(
            () =>
            {
                processor.Glyphs = 0;
                page.ProcessContent(processor);
            },
            WarmUp);

        Assert.True(processor.Glyphs >= 10_000, $"Only {processor.Glyphs} glyphs.");
        Assert.Equal(0, allocated);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Running_forms_marked_content_and_graphics_states_allocates_nothing_once_warm()
    {
        string form = ContentPdf.Stream("/Span << /MCID 0 >> BDC 0 0 1 1 re f EMC /G gs", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]");
        string content = string.Concat(Enumerable.Repeat("q /Fm Do /G gs /P /Pr BDC 0 0 1 1 re f EMC Q\n", 2_000));
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            "/XObject << /Fm 5 0 R >> /ExtGState << /G 6 0 R >> /Properties << /Pr 7 0 R >>",
            content,
            form,
            "<< /LW 2 /CA 0.5 /BM /Multiply /D [[1 1] 0] >>",
            "<< /MCID 1 >>"));
        PdfPage page = document.Pages[0];
        var processor = new CountingProcessor();

        long allocated = Allocations.Measure(
            () =>
            {
                processor.Reset();
                page.ProcessContent(processor);
            },
            WarmUp);

        Assert.Equal(4_000, processor.Paints);
        Assert.Equal(0, allocated);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Running_type3_glyph_descriptions_allocates_nothing_once_warm()
    {
        string font = "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a 6 0 R /b 7 0 R >> "
            + "/Encoding << /Type /Encoding /Differences [97 /a /b] >> /FirstChar 97 /LastChar 98 /Widths [1000 500] /Resources << /XObject << /Fm 8 0 R >> >> >>";
        string content = "BT /T3 12 Tf " + string.Concat(Enumerable.Repeat("(abab) Tj\n", 1_000)) + "ET";
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            "/Font << /T3 5 0 R >>",
            content,
            font,
            ContentPdf.Stream("1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f /Fm Do"),
            ContentPdf.Stream("500 0 d0 1 0 0 rg 0 0 m 500 1000 l 500 0 l f"),
            ContentPdf.Stream("0 0 10 10 re f", "/Type /XObject /Subtype /Form /BBox [0 0 100 100]")));
        PdfPage page = document.Pages[0];
        var processor = new Type3Counter();

        long allocated = Allocations.Measure(
            () =>
            {
                processor.Paints = 0;
                page.ProcessContent(processor);
            },
            WarmUp);

        Assert.Equal(6_000, processor.Paints);
        Assert.Equal(0, allocated);
        Assert.Empty(document.Diagnostics);
    }

    /// <summary>
    /// The minimal-corpus pages the content benchmark falls back to (issue #80), each a different part of the interpreter: text in
    /// four font kinds, forms, transparency and graphics states, marked and optional content, colour spaces, shadings including
    /// meshes, patterns and image XObjects (not inline images: their image view copies the data, by design of #56).
    /// </summary>
    public static TheoryData<string> WarmPassFiles => new(
        "text-standard14.pdf",
        "text-truetype-embedded.pdf",
        "text-type1-embedded.pdf",
        "text-cid-identity-h.pdf",
        "pattern-in-form.pdf",
        "extgstate-params.pdf",
        "marked-content.pdf",
        "optional-content.pdf",
        "colorspace-families.pdf",
        "shading-type2-axial.pdf",
        "shading-type4-freeform.pdf",
        "shading-type6-coons.pdf",
        "pattern-tiling-colored.pdf",
        "pattern-shading-axial.pdf",
        "image-smask.pdf",
        "image-stencil-mask.pdf");

    [Theory]
    [MemberData(nameof(WarmPassFiles))]
    public void Interpreting_a_page_with_every_event_requested_allocates_nothing_once_warm(string file)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(file)); // from memory: a file source rereads stream data per run (#45), a per-run cost
        PdfPage page = document.Pages[0];
        var processor = new EverythingSink();

        long allocated = Allocations.Measure(
            () =>
            {
                processor.Operators = 0;
                page.ProcessContent(processor);
            },
            WarmUp);

        Assert.True(processor.Operators > 0, "No operator was interpreted.");
        Assert.Equal(0, allocated / processor.Operators);
        Assert.Equal(0, allocated);
    }

    private static int ReadAll(ReadOnlySpan<byte> content, OperandArena arena)
    {
        var reader = new ContentReader(content, arena);
        int count = 0;
        while (reader.Next(out _))
        {
            arena.Clear();
            count++;
        }

        arena.Clear();
        return count;
    }
}

/// <summary>Counts glyphs and reads what a text extractor reads from each, without allocating.</summary>
internal sealed class GlyphCounter : ContentProcessor
{
    public int Glyphs { get; set; }

    public double Checksum { get; private set; }

    public override ContentEvents Events => ContentEvents.Glyphs | ContentEvents.Text | ContentEvents.Clips;

    public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
    {
        Glyphs++;
        Checksum += glyph.TextMatrix.E + glyph.AdvanceX + glyph.CharacterCode + glyph.Adjustment;
    }
}

/// <summary>Enters every Type 3 glyph and counts the paths its description paints, reading the fill colour.</summary>
internal sealed class Type3Counter : ContentProcessor
{
    public int Paints { get; set; }

    public double Checksum { get; private set; }

    public override ContentEvents Events => ContentEvents.Glyphs | ContentEvents.Paths | ContentEvents.Forms | ContentEvents.Type3GlyphContent;

    public override ContentVisit BeginType3Glyph(in GlyphEvent glyph, ContentContext context) => ContentVisit.Enter;

    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        Paints++;
        Checksum += context.State.Ctm.E + context.State.FillColor[0];
    }
}

/// <summary>Counts events of every kind and reads every operand, the work a real processor does per event, without allocating.</summary>
internal sealed class CountingProcessor : ContentProcessor
{
    public int Operators { get; private set; }

    public int Paints { get; private set; }

    public int Clips { get; private set; }

    public int Saves { get; private set; }

    public double Checksum { get; private set; }

    public void Reset() => (Operators, Paints, Clips, Saves, Checksum) = (0, 0, 0, 0, 0);

    public override void VisitOperator(in ContentOperator op, ContentContext context)
    {
        Operators++;
        foreach (ContentOperand operand in op.Operands)
        {
            Checksum += operand.Number;
        }
    }

    public override void SaveState(ContentContext context) => Saves++;

    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        Paints++;
        Checksum += path.Path.Bounds.Width + context.State.Ctm.E + context.State.DashArray.Length;
    }

    public override void IntersectClip(in ClipEvent clip, ContentContext context)
    {
        Clips++;
        Checksum += context.GetClip(clip.ParentHandle).Path.Points.Length;
    }
}

/// <summary>Requests every event and does nothing with it but count operators: the benchmark's processor (issue #80).</summary>
internal sealed class EverythingSink : ContentProcessor
{
    public int Operators { get; set; }

    public override ContentEvents Events => ContentEvents.All;

    public override void VisitOperator(in ContentOperator op, ContentContext context) => Operators++;
}
