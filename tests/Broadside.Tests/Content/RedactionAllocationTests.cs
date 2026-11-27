using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Redaction;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Content;

/// <summary>
/// The redaction editor runs over every operator of a page, so on a page with nothing under its areas it allocates nothing per glyph
/// or operator once warm (issue #624; CLAUDE.md, "Code conventions"). <c>RedactionBenchmarks</c> is the measuring half. A run has a
/// small fixed cost of the interpreter's own (the content here names a font no resources hold, which it reports once per run), so the
/// test proves the editor's share does not grow with the content: four times the operators and glyphs allocate the same.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class RedactionAllocationTests
{
    [Fact]
    public void Redacting_content_with_nothing_under_the_area_allocates_nothing_per_operator_or_glyph()
    {
        byte[] block = [.. ContentSamples.TextHeavy(5_000), .. "\n"u8, .. ContentSamples.PathHeavy(64 * 1024), .. "\n"u8];
        ReadOnlyMemory<byte> small = block;
        ReadOnlyMemory<byte> large = (byte[])[.. block, .. block, .. block, .. block];
        using PdfDocument document = PdfDocument.Create();
        var session = new RedactionSession(document, new PdfRedactionOptions());
        var editor = new RedactionEditor(session);
        editor.Configure([Quad.FromRectangle(Matrix.Identity, -200, -200, -150, -150)], new CosDictionary(), null);
        int written = 0;

        long once = Allocations.Measure(() => written = ContentRewriting.RewriteBytes(small, document, editor, ContentInterpreter.DefaultOptions).Length, 20);
        long four = Allocations.Measure(() => written = ContentRewriting.RewriteBytes(large, document, editor, ContentInterpreter.DefaultOptions).Length, 20);

        Assert.Equal(large.Length, written);
        Assert.Equal(0, session.GlyphsRemoved + session.PathsRemoved);
        Assert.Equal(once, four);
        Assert.True(four < 1024, $"A run allocated {four} bytes.");
    }
}
