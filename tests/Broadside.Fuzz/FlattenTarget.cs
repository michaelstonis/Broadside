namespace Broadside.Fuzz;

/// <summary>
/// The <c>flatten</c> target (issue #170): opens the input as a whole file and flattens every annotation, widgets included, into
/// the page content (each page's content is interpreted and rewritten around the appearances), then saves and reopens. Invariants:
/// flattening throws only <see cref="InvalidOperationException"/> for a form with an XFA resource, leaving it in place; afterwards
/// the document has no form and no page has an annotation; the saved file opens with the same page count and no form. An encrypted
/// input stops after flattening, as does one whose damaged encryption the save refuses.
/// </summary>
/// <remarks>ISO 32000-2 §12.5.5, §12.5.3, §7.8.2.</remarks>
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

        document.Flatten(new PdfFlattenOptions().WithAnnotations(true));
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
        try
        {
            document.Save(output);
        }
        catch (NotSupportedException)
        {
            // A damaged encrypted input whose encryption is not recognized can still not be saved (the `save` target's outcome).
            return;
        }

        using PdfDocument saved = PdfDocument.Open(output.ToArray());
        if (saved.Pages.Count != pages || saved.AcroForm is not null)
        {
            throw new InvalidOperationException($"The saved file has {saved.Pages.Count} pages (the input had {pages}) and a form: {saved.AcroForm is not null}.");
        }
    }
}
