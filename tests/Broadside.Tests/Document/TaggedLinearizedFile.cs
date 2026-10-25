using System.Globalization;
using System.Text;

namespace Broadside.Tests.Document;

/// <summary>
/// A 30-page tagged file linearized by the writer, for reading through range requests (issue #417). Each content stream shows
/// "Page N" and is padded with comments to 40 KiB, so the file is about 1.2 MB and the main cross-reference section at its end
/// stays unread unless a read needs it. Page 1 has a link to page 25 (an explicit destination) and a link to the named destination
/// <c>ten</c> (page 10); the outline has items for pages 10 and 30; the structure tree has a paragraph on page 1 and one on page 20,
/// each with its <c>Pg</c>. The outline, the structure tree, the name tree and the page tree lie after the pages (Annex F, F.3.10).
/// </summary>
internal static class TaggedLinearizedFile
{
    /// <summary>The number of pages.</summary>
    public const int PageCount = 30;

    /// <summary>The page the explicit link on page 1 goes to (0-based).</summary>
    public const int LinkedPage = 24;

    /// <summary>The page the named destination <c>ten</c> shows (0-based).</summary>
    public const int NamedPage = 9;

    /// <summary>The pages the outline items go to (0-based).</summary>
    public static readonly int[] OutlinePages = [9, 29];

    /// <summary>The pages of the structure tree's paragraphs (0-based).</summary>
    public static readonly int[] StructurePages = [0, 19];

    private const int PagePadding = 40 * 1024;

    /// <summary>Builds the file.</summary>
    /// <param name="layout">The cross-reference layout of the linearized file.</param>
    /// <returns>The linearized file.</returns>
    public static byte[] Create(PdfCrossReferenceLayout layout = PdfCrossReferenceLayout.Table)
    {
        // Object i + 1 of the list: 1 catalog, 2 page tree, 3 font, 4 + 2k page k and 5 + 2k its content, then the rest.
        static int Page(int index) => 4 + (2 * index);
        int after = 4 + (2 * PageCount);
        int outlines = after;
        int structRoot = after + 3;
        int parentTree = after + 6;
        int dests = after + 7;
        int explicitLink = after + 8;
        int namedLink = after + 9;
        var objects = new List<string>
        {
            Format($"<< /Type /Catalog /Pages 2 0 R /Outlines {outlines} 0 R /StructTreeRoot {structRoot} 0 R /MarkInfo << /Marked true >> /Names << /Dests {dests} 0 R >> >>"),
            "<< /Type /Pages /Kids [" + string.Join(' ', Enumerable.Range(0, PageCount).Select(page => Format($"{Page(page)} 0 R"))) + Format($"] /Count {PageCount} >>"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        for (int page = 0; page < PageCount; page++)
        {
            int structure = Array.IndexOf(StructurePages, page);
            string extra = (page == 0 ? Format($" /Annots [{explicitLink} 0 R {namedLink} 0 R]") : string.Empty)
                + (structure >= 0 ? Format($" /StructParents {structure}") : string.Empty);
            objects.Add(Format($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {Page(page) + 1} 0 R{extra} >>"));
            string text = Format($"BT /F1 24 Tf 72 700 Td (Page {page + 1}) Tj ET");
            var content = new StringBuilder(structure >= 0 ? "/P << /MCID 0 >> BDC " + text + " EMC\n" : text + "\n");
            while (content.Length < PagePadding)
            {
                content.Append('%').Append('x', 78).Append('\n');
            }

            objects.Add(Format($"<< /Length {content.Length} >>\nstream\n{content}\nendstream"));
        }

        objects.Add(Format($"<< /Type /Outlines /First {outlines + 1} 0 R /Last {outlines + 2} 0 R /Count 2 >>"));
        objects.Add(Format($"<< /Title (Ten) /Parent {outlines} 0 R /Next {outlines + 2} 0 R /Dest [{Page(OutlinePages[0])} 0 R /Fit] >>"));
        objects.Add(Format($"<< /Title (Thirty) /Parent {outlines} 0 R /Prev {outlines + 1} 0 R /Dest [{Page(OutlinePages[1])} 0 R /Fit] >>"));
        objects.Add(Format($"<< /Type /StructTreeRoot /K [{structRoot + 1} 0 R {structRoot + 2} 0 R] /ParentTree {parentTree} 0 R /ParentTreeNextKey 2 >>"));
        objects.Add(Format($"<< /Type /StructElem /S /P /P {structRoot} 0 R /Pg {Page(StructurePages[0])} 0 R /K 0 >>"));
        objects.Add(Format($"<< /Type /StructElem /S /P /P {structRoot} 0 R /Pg {Page(StructurePages[1])} 0 R /K 0 >>"));
        objects.Add(Format($"<< /Nums [0 [{structRoot + 1} 0 R] 1 [{structRoot + 2} 0 R]] >>"));
        objects.Add(Format($"<< /Names [(ten) [{Page(NamedPage)} 0 R /Fit]] >>"));
        objects.Add(Format($"<< /Type /Annot /Subtype /Link /Rect [72 600 300 640] /Border [0 0 0] /Dest [{Page(LinkedPage)} 0 R /Fit] >>"));
        objects.Add("<< /Type /Annot /Subtype /Link /Rect [72 540 300 580] /Border [0 0 0] /Dest (ten) >>");

        using PdfDocument source = PdfDocument.Open(new TestPdf().Build([.. objects]));
        using var stream = new MemoryStream();
        source.Save(stream, new PdfSaveOptions().WithLinearize(true).WithCrossReferenceLayout(layout).WithStreamCompression(PdfStreamCompression.None));
        return stream.ToArray();
    }

    private static string Format(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
