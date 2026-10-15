using Broadside.Layout;
using Broadside.Text;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>layout-detection</c> target (issue #187): the input opened as a whole file in lenient mode and laid out from its
/// geometry, every page detected (<see cref="LayoutDetection.AllPages"/>), once for the document and once for its first page.
/// </summary>
/// <remarks>
/// Spec story 36; ISO 32000-2 §14.8.2.5. Invariants: detection never throws; the tree passes the layout checks of the
/// <c>structure-tree</c> target; every detected page's reordered text holds exactly the glyphs of its text extraction with
/// their text ranges following each other to the end of the text; and a second run gives the same paragraphs (detection is
/// deterministic).
/// </remarks>
internal static class LayoutDetectionTarget
{
    private const int MaxPages = 8;

    public static void Target(ReadOnlySpan<byte> data)
    {
        using PdfDocument? document = FuzzTargets.OpenOrNull(data);
        if (document is null || document.Pages.Count is 0 or > MaxPages)
        {
            return;
        }

        LayoutOptions options = new LayoutOptions().WithDetection(LayoutDetection.AllPages);
        LayoutTree tree = document.ExtractLayout(options);
        StructureTree.CheckLayout(tree);
        foreach (LayoutPage page in tree.Pages)
        {
            CheckPartition(page);
        }

        if (!Paragraphs(tree).SequenceEqual(Paragraphs(document.ExtractLayout(options))))
        {
            throw new InvalidOperationException("Detecting the same document twice gave different paragraphs.");
        }

        StructureTree.CheckLayout(document.Pages[0].ExtractLayout(options));
    }

    private static IEnumerable<string> Paragraphs(LayoutTree tree) => tree.Root.Children.Select(static node => node.Text);

    private static void CheckPartition(LayoutPage page)
    {
        TextPage detected = page.Text;
        TextPage content = page.Page.ExtractText();
        if (!page.IsDetected || detected.Glyphs.Count != content.Glyphs.Count)
        {
            throw new InvalidOperationException("A detected page does not hold exactly its content's glyphs.");
        }

        int start = 0;
        foreach (TextGlyph glyph in detected.Glyphs)
        {
            if (glyph.TextStart != start)
            {
                throw new InvalidOperationException("A detected page's glyph text ranges do not follow each other.");
            }

            start += glyph.TextLength;
        }

        if (start != detected.Text.Length)
        {
            throw new InvalidOperationException("A detected page's text has characters no glyph owns.");
        }
    }
}
