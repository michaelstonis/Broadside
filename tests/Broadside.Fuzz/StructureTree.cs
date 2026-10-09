using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Structure;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>structure-tree</c> target: a random COS graph read as a structure tree. An input that starts with <c>%PDF-</c> is opened as
/// a file (the corpus seeds, chiefly <c>tagged-structure.pdf</c>, mutated); any other input is split at line feeds into object bodies
/// of a synthetic tagged file: the first line is the inside of the structure tree root dictionary (object 4), the following lines are
/// objects 5, 6, and so on, so mutations wire elements, K arrays, role maps, namespaces, class maps, attributes and the parent and ID
/// trees to each other at random.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.7, §14.8. The walk reads every element through <see cref="PdfStructureTreeRoot.Elements"/> and, iteratively,
/// through <see cref="PdfStructureElement.Children"/>, resolves every role mapping, attribute and language, and runs every lookup.
/// Invariants: lenient reading never throws; every walk ends (the harness times out otherwise); <see cref="PdfStructureTreeRoot.Elements"/>
/// lists each element once; every element's parent chain ends within the depth limit.
/// </remarks>
internal static class StructureTree
{
    private const int MaxObjects = 256;
    private static readonly CosName Layout = new("Layout");
    private static readonly CosName TextAlign = new("TextAlign");

    public static void Target(ReadOnlySpan<byte> data)
    {
        byte[] file = data.StartsWith("%PDF-"u8) ? data.ToArray() : Synthesize(data);
        using PdfDocument? document = OpenOrNull(file);
        if (document is not null)
        {
            Walk(document);
        }
    }

    private static PdfDocument? OpenOrNull(byte[] file)
    {
        try
        {
            return PdfDocument.Open(file);
        }
        catch (Exception exception) when (exception is DiagnosticException or PdfPasswordException or PdfEncryptionNotSupportedException or PdfCertificateException)
        {
            return null;
        }
    }

    private static void Walk(PdfDocument document)
    {
        _ = (document.MarkInfo.Marked, document.MarkInfo.UserProperties, document.MarkInfo.Suspects);
        if (document.StructureTree is not { } root)
        {
            return;
        }

        _ = (root.Namespaces, root.ParentTreeNextKey, root.RoleMap, root.ClassMap, root.PronunciationLexicon, root.AssociatedFiles);
        var listed = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PdfStructureElement element in root.Elements)
        {
            if (!listed.Add(element.Dictionary))
            {
                throw new InvalidOperationException("Elements lists an element twice.");
            }

            int steps = 0;
            for (PdfStructureElement? ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (++steps > 256)
                {
                    throw new InvalidOperationException("An element's parent chain does not end within the depth limit.");
                }
            }

            if (element.Id is { } id)
            {
                _ = root.FindElementById(id.Bytes);
            }
        }

        var seen = new HashSet<PdfStructureElement>();
        var stack = new Stack<PdfStructureElement>(root.Children);
        while (stack.TryPop(out PdfStructureElement? element))
        {
            if (!seen.Add(element))
            {
                continue;
            }

            _ = (element.StandardType, element.RoleMapping, element.Namespace.Kind, element.EffectiveLanguage, element.PhoneticAlphabet, element.Revision);
            _ = (element.Attributes, element.ClassAttributes, element.GetAttributeValue(Layout, TextAlign), element.References, element.Page);
            foreach (PdfAttributeObject attributes in element.Attributes)
            {
                _ = (attributes.Owner, attributes.Namespace, (attributes as PdfUserPropertiesAttributes)?.Properties, (attributes as PdfTableAttributes)?.Headers);
            }

            foreach (PdfStructureItem item in element.Children)
            {
                switch (item)
                {
                    case PdfStructureElement child:
                        stack.Push(child);
                        break;
                    case PdfObjectReference reference:
                        _ = root.FindElementForObject(reference.ReferencedObject);
                        break;
                    case PdfMarkedContentReference content when content.ContentStream is { } stream:
                        _ = root.FindElement(stream, content.Mcid);
                        break;
                }
            }
        }

        foreach (PdfPage page in document.Pages)
        {
            _ = root.GetMarkedContentElements(page);
        }
    }

    /// <summary>Builds a one-page tagged file whose structure tree root and objects 5 onward are the input's lines.</summary>
    private static byte[] Synthesize(ReadOnlySpan<byte> data)
    {
        string[] lines = Encoding.Latin1.GetString(data).Split('\n', MaxObjects);
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 4 0 R /Lang (en) >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /StructParents 0 >>",
            $"<< /Type /StructTreeRoot {lines[0]} >>",
        };
        objects.AddRange(lines.Skip(1));

        var text = new StringBuilder("%PDF-2.0\n");
        var offsets = new List<int>();
        for (int index = 0; index < objects.Count; index++)
        {
            offsets.Add(text.Length);
            text.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        int xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /ID [<00> <00>] >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }
}
