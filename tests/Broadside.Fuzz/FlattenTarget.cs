using Broadside.Forms;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>flatten</c> target (issue #170): opens the input as a whole file and flattens every annotation, widgets included, into
/// the page content (each page's content is interpreted and rewritten around the appearances), then saves and reopens. Invariants:
/// flattening throws only <see cref="InvalidOperationException"/> for a form with an XFA resource, leaving it in place; afterwards
/// the document has no form and no page has an annotation, no object reference of the structure tree names an annotation a page
/// listed and no catalog permission names a signature the form held (#281); the saved file opens with the same page count and no
/// form. An encrypted input stops after flattening.
/// </summary>
/// <remarks>ISO 32000-2 §12.5.5, §12.5.3, §7.8.2, §14.7.5.3, §12.8.4.</remarks>
internal static class FlattenTarget
{
    private static readonly PdfEngine Engine = new(new PdfOptions().UseSystemFontResolver(null));

    public static void Target(ReadOnlySpan<byte> data)
    {
        using PdfDocument? document = FuzzTargets.OpenOrNull(data, Engine);
        if (document is null)
        {
            return;
        }

        int pages = document.Pages.Count;
        if (document.AcroForm?.Xfa is not null)
        {
            try
            {
                document.Flatten();
            }
            catch (InvalidOperationException)
            {
                if (document.AcroForm is null)
                {
                    throw new InvalidOperationException("A refused flatten removed the form.");
                }

                return;
            }

            throw new InvalidOperationException("A form with an XFA resource was flattened.");
        }

        var listed = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PdfPage page in document.Pages)
        {
            listed.UnionWith(page.Annotations.Select(annotation => annotation.Dictionary));
        }

        var signatures = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PdfSignatureField field in document.AcroForm?.TerminalFields.OfType<PdfSignatureField>() ?? [])
        {
            if (field.SignatureDictionary is { } signature)
            {
                signatures.Add(signature);
            }
        }

        document.Flatten(new PdfFlattenOptions().WithAnnotations(true));
        CheckReferences(document, listed, signatures);
        if (document.AcroForm is not null)
        {
            throw new InvalidOperationException("The form is still there after flattening.");
        }

        foreach (PdfPage page in document.Pages)
        {
            if (page.Annotations.Count != 0)
            {
                throw new InvalidOperationException($"A page still has {page.Annotations.Count} annotations after flattening.");
            }
        }

        if (document.IsEncrypted)
        {
            return;
        }

        using var output = new MemoryStream();
        document.Save(output);

        using PdfDocument saved = PdfDocument.Open(output.ToArray());
        if (saved.Pages.Count != pages || saved.AcroForm is not null)
        {
            throw new InvalidOperationException($"The saved file has {saved.Pages.Count} pages (the input had {pages}) and a form: {saved.AcroForm is not null}.");
        }
    }

    /// <summary>Throws when the structure tree or the catalog's permissions still name an annotation or signature flattening removed.</summary>
    private static void CheckReferences(PdfDocument document, HashSet<CosDictionary> annotations, HashSet<CosDictionary> signatures)
    {
        if (document.StructureTree is { } tree)
        {
            foreach (PdfStructureElement element in tree.Elements)
            {
                foreach (PdfObjectReference reference in element.Children.OfType<PdfObjectReference>())
                {
                    if (reference.ReferencedObject is CosDictionary target && annotations.Contains(target))
                    {
                        throw new InvalidOperationException($"The structure element {element} still refers to a flattened annotation.");
                    }
                }
            }
        }

        if (document.Catalog.TryGetValue(new CosName("Perms"), out CosObject? value) && document.Resolve(value) is CosDictionary permissions)
        {
            foreach (KeyValuePair<CosName, CosObject> entry in permissions)
            {
                if (document.Resolve(entry.Value) is CosDictionary signature && signatures.Contains(signature))
                {
                    throw new InvalidOperationException($"The catalog permission {entry.Key} still names a removed signature.");
                }
            }
        }
    }
}
