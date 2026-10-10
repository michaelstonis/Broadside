using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Tests.StructureTree;

/// <summary>
/// Reads a document's whole structure tree through the public API: every element reached through <see cref="PdfStructureElement.Children"/>
/// (iteratively) and through <see cref="PdfStructureTreeRoot.Elements"/>, its role mapping, namespace, attributes, language and
/// content items, and every lookup (MCID per page, ID, object content item).
/// </summary>
internal static class StructureWalker
{
    private static readonly CosName Layout = new("Layout");
    private static readonly CosName TextAlign = new("TextAlign");

    /// <summary>Walks the tree; returns the number of elements reached through the children, or -1 when the document has no tree.</summary>
    public static int Walk(PdfDocument document)
    {
        if (document.StructureTree is not { } root)
        {
            return -1;
        }

        _ = (document.MarkInfo.Marked, root.Namespaces, root.ParentTreeNextKey, root.RoleMap, root.ClassMap);
        int count = 0;
        var stack = new Stack<PdfStructureElement>(root.Children.Reverse());
        var seen = new HashSet<PdfStructureElement>();
        while (stack.TryPop(out PdfStructureElement? element))
        {
            if (!seen.Add(element))
            {
                continue;
            }

            count++;
            _ = (element.StandardType, element.RoleMapping, element.Namespace, element.Title, element.Id, element.EffectiveLanguage, element.PhoneticAlphabet);
            _ = (element.Attributes, element.ClassAttributes, element.GetAttributeValue(Layout, TextAlign), element.References, element.Page);
            IReadOnlyList<PdfStructureItem> children = element.Children;
            for (int index = children.Count - 1; index >= 0; index--)
            {
                switch (children[index])
                {
                    case PdfStructureElement child:
                        stack.Push(child);
                        break;
                    case PdfObjectReference reference:
                        _ = root.FindElementForObject(reference.ReferencedObject);
                        break;
                }
            }
        }

        foreach (PdfStructureElement element in root.Elements)
        {
            if (element.Id is { } id)
            {
                _ = root.FindElementById(id.Bytes);
            }
        }

        foreach (PdfPage page in document.Pages)
        {
            _ = root.GetMarkedContentElements(page);
        }

        return count;
    }
}
