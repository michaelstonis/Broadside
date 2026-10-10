using Broadside.Structure;
using Broadside.Tests.Document;

namespace Broadside.Tests.StructureTree;

/// <summary>
/// Builds a one-page tagged file in memory: object 1 the catalog (<c>MarkInfo</c> Marked true), 2 the page tree, 3 the page
/// (<c>StructParents 0</c>), 4 the structure tree root with <c>rootEntries</c>, then the given objects as 5, 6, and so on.
/// </summary>
internal static class TaggedPdf
{
    public static byte[] Build(string rootEntries, params string[] objects) => new TestPdf { Header = "%PDF-2.0", TrailerEntries = "/ID [<00> <00>]" }.Build(
        [
            "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /StructParents 0 >>",
            $"<< /Type /StructTreeRoot {rootEntries} >>",
            .. objects,
        ]);

    /// <summary>Reads everything the views offer, so every lazily reported diagnostic is reported.</summary>
    public static void ReadEverything(PdfDocument document)
    {
        if (document.StructureTree is not { } root)
        {
            return;
        }

        _ = (root.Children, root.Namespaces, document.MarkInfo.Marked, document.MarkInfo.UserProperties, document.MarkInfo.Suspects);
        foreach (PdfStructureElement element in root.Elements)
        {
            Visit(element);
        }

        foreach (PdfPage page in document.Pages)
        {
            _ = root.GetMarkedContentElements(page);
        }

        _ = root.FindElementById("id"u8);
    }

    private static void Visit(PdfStructureElement element)
    {
        _ = (element.StandardType, element.Namespace, element.Attributes, element.ClassAttributes, element.EffectiveLanguage, element.References);
        foreach (PdfStructureItem child in element.Children)
        {
            if (child is PdfStructureElement nested)
            {
                _ = nested.StandardType;
            }
        }
    }
}
