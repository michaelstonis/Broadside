using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;
using Broadside.Text.Editing;

namespace Broadside.Tests.Content;

/// <summary>
/// The text edit editor runs over every operator of a page, so on a page without the match it allocates nothing per glyph or operator
/// once warm (issue #641; CLAUDE.md, "Code conventions"). <c>TextReplaceBenchmarks</c> is the measuring half. A run has a small fixed
/// cost of the interpreter's own (the content names a font no resources hold, which it reports once per run), so the test proves the
/// editor's share does not grow with the content: four times the operators and glyphs allocate the same.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class TextReplaceAllocationTests
{
    [Fact]
    public void Looking_for_a_match_the_content_does_not_hold_allocates_nothing_per_operator_or_glyph()
    {
        byte[] block = [.. ContentSamples.TextHeavy(5_000), .. "\n"u8, .. ContentSamples.PathHeavy(64 * 1024), .. "\n"u8];
        ReadOnlyMemory<byte> small = block;
        ReadOnlyMemory<byte> large = (byte[])[.. block, .. block, .. block, .. block];
        using PdfDocument document = PdfDocument.Create();
        var session = new TextEditSession(document, new PdfTextReplaceOptions());
        TextEditMatch match = TextEditMatch.OfGlyph(0xFFFF, new PathPoint(-500, -500), document.Pages[0].ExtractText());
        Assert.True(session.Prepare([match]));
        var editor = new TextEditEditor(session);
        editor.Configure(new CosDictionary(), null);
        int written = 0;

        long once = Allocations.Measure(() => written = ContentRewriting.RewriteBytes(small, document, editor, ContentInterpreter.DefaultOptions).Length, 20);
        long four = Allocations.Measure(() => written = ContentRewriting.RewriteBytes(large, document, editor, ContentInterpreter.DefaultOptions).Length, 20);

        Assert.Equal(large.Length, written);
        Assert.False(match.Anchored);
        Assert.Equal(once, four);
        Assert.True(four < 1024, $"A run allocated {four} bytes.");
    }
}
