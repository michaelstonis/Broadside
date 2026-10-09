using Broadside.Content;
using Broadside.TestSupport;

namespace Broadside.Tests.Content;

/// <summary>
/// The content lexer and interpreter are hot paths and allocate nothing per operator (CLAUDE.md, "Code conventions"). This is
/// the build-breaking half of that rule; <c>ContentLexerBenchmarks</c> and <c>ContentInterpreterBenchmarks</c> in
/// <c>bench/Broadside.Benchmarks</c> are the measuring half. The lexer test drives the internal reader because no public seam
/// exposes it alone; the interpreter test goes through <see cref="PdfPage.ProcessContent(ContentProcessor)"/>.
/// </summary>
public class ContentAllocationTests
{
    private const int WarmUp = 50;

    [Fact]
    public void Reading_a_megabyte_of_path_operators_allocates_nothing()
    {
        byte[] content = ContentSamples.PathHeavy(1 << 20);
        var arena = new OperandArena();
        for (int pass = 0; pass < WarmUp; pass++)
        {
            ReadAll(content, arena);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int operators = ReadAll(content, arena);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(operators > 100_000, $"Only {operators} operators.");
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Reading_the_filter_chain_content_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("filter-chain.pdf"));
        byte[] content = document.DecodeStream(Filters.CorpusFilterTests.ContentStream(document)).ToArray();
        var arena = new OperandArena();
        for (int pass = 0; pass < WarmUp; pass++)
        {
            ReadAll(content, arena);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int operators = ReadAll(content, arena);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(5, operators);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Interpreting_a_page_allocates_nothing_once_warm()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(System.Text.Encoding.ASCII.GetString(ContentSamples.PathHeavy(1 << 20))));
        PdfPage page = document.Pages[0];
        var processor = new CountingProcessor();
        for (int pass = 0; pass < WarmUp; pass++)
        {
            page.ProcessContent(processor);
        }

        processor.Reset();
        long before = GC.GetAllocatedBytesForCurrentThread();
        page.ProcessContent(processor);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(processor.Paints > 20_000, $"Only {processor.Paints} paints.");
        Assert.True(processor.Clips > 5_000, $"Only {processor.Clips} clips.");
        Assert.Equal(0, allocated);
        Assert.Empty(document.Diagnostics);
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
