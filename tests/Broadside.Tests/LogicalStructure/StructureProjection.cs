using System.Globalization;
using System.Text;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Tests.LogicalStructure;

/// <summary>
/// Writes a structure tree as indented text through the public API only: one line per element (type, namespace, the standard type
/// it resolves to, title, ID), one line per content item and per attribute object. The snapshot of <c>tagged-structure.pdf</c> is
/// written from the generator by hand, not from this code's output.
/// </summary>
internal static class StructureProjection
{
    public static string Of(PdfDocument document, PdfStructureTreeRoot root)
    {
        var text = new StringBuilder();
        foreach (PdfStructureElement element in root.Children)
        {
            Write(document, element, 0, text);
        }

        return text.ToString();
    }

    private static void Write(PdfDocument document, PdfStructureElement element, int depth, StringBuilder text)
    {
        string indent = new(' ', depth * 2);
        text.Append(indent).Append(element.StructureType?.Value ?? "?").Append(" (").Append(Name(element.Namespace)).Append(')');
        PdfStructureType? standard = element.StandardType;
        text.Append(" -> ").Append(standard is null ? "unresolved" : $"{standard.Name} ({Name(standard.Namespace)})");
        if (element.Title is { } title)
        {
            text.Append(" T=").Append(title);
        }

        if (element.Id is { } id)
        {
            text.Append(" ID=").Append(Encoding.ASCII.GetString(id.Bytes));
        }

        text.Append('\n');
        foreach (PdfAttributeObject attributes in element.Attributes)
        {
            text.Append(indent).Append("  A ").Append(Attributes(attributes)).Append('\n');
        }

        foreach (CosName className in element.ClassNames)
        {
            text.Append(indent).Append("  C ").Append(className.Value).Append('\n');
        }

        foreach (PdfStructureItem item in element.Children)
        {
            switch (item)
            {
                case PdfStructureElement child:
                    Write(document, child, depth + 1, text);
                    break;
                case PdfMarkedContentReference content:
                    text.Append(indent).Append("  ").Append(content.Dictionary is null ? "MCID " : "MCR MCID ")
                        .Append(content.Mcid.ToString(CultureInfo.InvariantCulture)).Append(" page ").Append(PageNumber(document, content.Page)).Append('\n');
                    break;
                case PdfObjectReference reference:
                    string subtype = reference.ReferencedObject is CosDictionary dictionary && dictionary.TryGetValue(new CosName("Subtype"), out CosObject? value) && value is CosName name
                        ? name.Value
                        : "?";
                    text.Append(indent).Append("  OBJR ").Append(subtype).Append(" page ").Append(PageNumber(document, reference.Page)).Append('\n');
                    break;
                default:
                    throw new InvalidOperationException(item.GetType().Name);
            }
        }
    }

    private static string Attributes(PdfAttributeObject attributes)
    {
        var text = new StringBuilder(attributes.Owner?.Value ?? "?");
        if (attributes.Revision != 0)
        {
            text.Append(" r").Append(attributes.Revision.ToString(CultureInfo.InvariantCulture));
        }

        foreach (KeyValuePair<CosName, CosObject> entry in attributes.Dictionary)
        {
            if (entry.Key.Value is not "O")
            {
                text.Append(' ').Append(entry.Key.Value).Append('=').Append(entry.Value.ToString());
            }
        }

        return text.ToString();
    }

    private static string PageNumber(PdfDocument document, PdfPage? page)
    {
        if (page is null)
        {
            return "?";
        }

        for (int index = 0; index < document.Pages.Count; index++)
        {
            if (ReferenceEquals(document.Pages[index], page))
            {
                return (index + 1).ToString(CultureInfo.InvariantCulture);
            }
        }

        return "?";
    }

    private static string Name(PdfStructureNamespace ns) => ns.Kind switch
    {
        PdfStructureNamespaceKind.Pdf17 => "1.7",
        PdfStructureNamespaceKind.Pdf20 => "2.0",
        PdfStructureNamespaceKind.MathML => "MathML",
        _ => ns.Name,
    };
}
