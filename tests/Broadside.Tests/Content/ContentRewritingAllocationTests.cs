using Broadside.Content;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Content;

/// <summary>
/// Content rewriting is a hot path and allocates nothing per operator (CLAUDE.md, "Code conventions"); <c>ContentRewriteBenchmarks</c>
/// is the measuring half. The first test drives the internal byte-level rewrite, which keeps its output in the editor, because the
/// public seam stores a new copy of the content on every call; the second bounds the public seam by that copy.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ContentRewritingAllocationTests
{
    private const int WarmUp = 20;

    [Fact]
    public void Rewriting_a_megabyte_of_operators_with_edits_allocates_nothing_once_the_editor_is_warm()
    {
        ReadOnlyMemory<byte> content = ContentSamples.PathHeavy(1 << 20);
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("q Q"));
        var editor = new EveryTenthEditor();
        int written = 0;

        long allocated = Allocations.Measure(
            () => written = ContentRewriting.RewriteBytes(content, document, editor, ContentInterpreter.DefaultOptions).Length,
            WarmUp);

        Assert.True(editor.Edits > 10_000, $"Only {editor.Edits} edits.");
        Assert.True(written > content.Length / 2, $"Only {written} bytes written.");
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Rewriting_a_page_allocates_only_the_new_content()
    {
        byte[] content = ContentSamples.PathHeavy(1 << 20);
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(System.Text.Encoding.ASCII.GetString(content)));
        PdfPage page = document.Pages[0];
        var editor = new WholeWrapper();

        long allocated = Allocations.Measure(() => page.RewriteContent(editor), WarmUp);

        Assert.True(allocated >= content.Length, $"Allocated {allocated} bytes, less than the content.");
        Assert.True(allocated < content.Length + 65_536, $"Allocated {allocated} bytes for {content.Length} bytes of content.");
    }

    /// <summary>Wraps every tenth operator in <c>q</c>/<c>Q</c> and removes the one after it.</summary>
    private sealed class EveryTenthEditor : ContentEditor
    {
        public int Edits { get; private set; }

        public override ContentEvents Events => ContentEvents.Operators;

        public override void BeginRun(ContentContext context) => Edits = 0;

        public override void VisitOperator(in ContentOperator op, ContentContext context)
        {
            int index = CurrentOperator;
            if (index % 10 == 0)
            {
                Wrap(index, index, "q"u8, "Q"u8);
                Edits++;
            }
            else if (index % 10 == 1)
            {
                Remove(index);
                Edits++;
            }
        }
    }

    /// <summary>Wraps the whole content in <c>q</c>/<c>Q</c>.</summary>
    private sealed class WholeWrapper : ContentEditor
    {
        public override ContentEvents Events => ContentEvents.None;

        public override void EndRun(ContentContext context) => Wrap(0, OperatorCount - 1, "q"u8, "Q"u8);
    }
}
