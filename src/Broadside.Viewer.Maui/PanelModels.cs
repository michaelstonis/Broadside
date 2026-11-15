using System.Globalization;
using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Signatures;
using Broadside.Structure;

namespace Broadside.Viewer.Maui;

// The panels' logic, free of MAUI types so the net10.0 tests drive it (#358). The public panels only lay it out.

/// <summary>A name and a value the document information panel lists.</summary>
internal readonly record struct DocumentFact(string Name, string Value);

/// <summary>What the document information panel lists about a document (ISO 32000-2 §14.3 metadata, §14.7 structure, §7.5.8 linearization).</summary>
internal static class DocumentFacts
{
    /// <summary>Reads the facts; a remote document whose parts are not loaded yet lists what it can and reports <paramref name="incomplete"/>.</summary>
    public static IReadOnlyList<DocumentFact> Read(PdfDocument document, string? fileName, out bool incomplete)
    {
        ArgumentNullException.ThrowIfNull(document);
        var facts = new List<DocumentFact>();
        incomplete = false;
        Add(facts, "File", fileName);
        Add(facts, "Pages", document.Pages.Count.ToString(CultureInfo.InvariantCulture));
        try
        {
            Add(facts, "PDF version", document.Version.ToString());
            Add(facts, "Linearized", document.IsLinearized ? "yes" : "no");
            Add(facts, "Encrypted", document.IsEncrypted ? "yes" : "no");
            Add(facts, "Tagged", document.StructureTree is null ? "no" : "yes");
            Add(facts, "Language", document.Language);
            if (document.Information is { } info)
            {
                Add(facts, "Title", info.Title);
                Add(facts, "Author", info.Author);
                Add(facts, "Subject", info.Subject);
                Add(facts, "Keywords", info.Keywords);
                Add(facts, "Creator", info.Creator);
                Add(facts, "Producer", info.Producer);
                Add(facts, "Created", info.CreationDate?.Value.ToString("u", CultureInfo.InvariantCulture));
                Add(facts, "Modified", info.ModificationDate?.Value.ToString("u", CultureInfo.InvariantCulture));
            }

            if (document.Metadata?.Packet is { } xmp)
            {
                Add(facts, "XMP title", xmp.Title);
                Add(facts, "XMP producer", xmp.Producer);
                Add(facts, "PDF/A", xmp.PdfAPart is int part ? string.Create(CultureInfo.InvariantCulture, $"{part}{xmp.PdfAConformance?.ToLowerInvariant()}") : null);
                Add(facts, "PDF/UA", xmp.PdfUAPart is int ua ? ua.ToString(CultureInfo.InvariantCulture) : null);
            }
        }
        catch (PdfRangeNotLoadedException)
        {
            incomplete = true;
        }

        return facts;
    }

    private static void Add(List<DocumentFact> facts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            facts.Add(new DocumentFact(name, value));
        }
    }
}

/// <summary>One visible row of the tag tree: an element, how deep it is, and whether it has children and is expanded.</summary>
internal sealed record TagRow(PdfStructureElement Element, int Depth, string Type, string? Detail, bool HasChildren, bool IsExpanded)
{
    /// <summary>The row's text: the structure type, its standard type when role-mapped, and the alternate text, actual text or title.</summary>
    public string Text =>
        Type
        + (Element.StandardType?.Name is { } standard && standard != Type ? " → " + standard : string.Empty)
        + (Detail is null ? string.Empty : " “" + Detail + "”");
}

/// <summary>The structure tree (ISO 32000-2 §14.7) as rows that expand and collapse, as assistive technology reads it.</summary>
internal sealed class TagTree
{
    private const int MaxChildren = 500;
    private readonly IReadOnlyList<PdfStructureElement> _roots;
    private readonly HashSet<PdfStructureElement> _open = new(ReferenceEqualityComparer.Instance);

    public TagTree(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _roots = document.StructureTree?.Children ?? [];
    }

    public bool IsEmpty => _roots.Count == 0;

    /// <summary>Expands a collapsed element or collapses an expanded one.</summary>
    public void Toggle(PdfStructureElement element)
    {
        if (!_open.Remove(element))
        {
            _open.Add(element);
        }
    }

    /// <summary>The visible rows: the roots, and the children of every expanded element, depth first.</summary>
    public IReadOnlyList<TagRow> Rows()
    {
        var rows = new List<TagRow>();
        foreach (PdfStructureElement root in _roots)
        {
            AddRow(rows, root, 0);
        }

        return rows;
    }

    private void AddRow(List<TagRow> rows, PdfStructureElement element, int depth)
    {
        List<PdfStructureElement> children = [.. element.Children.OfType<PdfStructureElement>().Take(MaxChildren)];
        bool open = _open.Contains(element);
        string type = element.StructureType?.Value ?? "?";
        rows.Add(new TagRow(element, depth, type, element.AlternateDescription ?? element.ActualText ?? element.Title, children.Count > 0, open));
        if (open)
        {
            foreach (PdfStructureElement child in children)
            {
                AddRow(rows, child, depth + 1);
            }
        }
    }
}

/// <summary>An annotation of the page in view, as the annotation panel lists it.</summary>
internal sealed record AnnotationItem(AnnotationTarget Target, string Kind, string? Text, bool Removable, PdfRectangle Rectangle);

/// <summary>The annotation tools: what the annotation panel adds to a page (ISO 32000-2 §12.5.6), through the viewer's annotation layer.</summary>
internal enum AnnotationTool
{
    Note,
    Box,
    Text,
    Stamp,
}

/// <summary>Lists, adds and removes annotations through the <see cref="AnnotationLayer"/>, so only the overlay tiles they touch redraw.</summary>
internal static class AnnotationEditing
{
    /// <summary>The annotations and form fields of a page.</summary>
    public static async Task<IReadOnlyList<AnnotationItem>> ListAsync(AnnotationLayer layer, int pageIndex, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layer);
        int count = await layer.LoadPageAsync(pageIndex, cancellationToken).ConfigureAwait(true);
        var items = new List<AnnotationItem>(count);
        for (int i = 0; i < count; i++)
        {
            var target = new AnnotationTarget(pageIndex, i);
            PdfAnnotation annotation = layer.GetAnnotation(target);
            AnnotationRole role = layer.GetRole(target);
            string kind = role == AnnotationRole.Field ? "Form field" : annotation.Subtype?.Value ?? "Annotation";
            items.Add(new AnnotationItem(target, kind, annotation.Contents ?? layer.GetAccessibleName(target), role != AnnotationRole.Field, layer.GetRectangle(target)));
        }

        return items;
    }

    /// <summary>
    /// Adds an annotation near the top left of a page, each one (<paramref name="step"/>) a little lower than the last, and selects it.
    /// Returns where it went, or null when the page could not take it.
    /// </summary>
    public static AnnotationTarget? Add(AnnotationLayer layer, int pageIndex, AnnotationTool tool, int step)
    {
        ArgumentNullException.ThrowIfNull(layer);
        step %= 8;
        AnnotationTarget? added = layer.Add(pageIndex, page =>
        {
            PdfRectangle crop = page.CropBox;
            double x = crop.Left + 48 + (step * 12);
            double y = crop.Top - 72 - (step * 64);
            return tool switch
            {
                AnnotationTool.Note => page.AddTextAnnotation(
                    new PdfRectangle(x, y - 24, x + 24, y), "A note added in the Broadside viewer.",
                    new PdfAnnotationOptions().WithColor(new PdfDeviceColor(1, 0.85, 0.2)).WithTitle("Broadside")),
                AnnotationTool.Box => page.AddSquareAnnotation(
                    new PdfRectangle(x, y - 60, x + 120, y),
                    new PdfAnnotationOptions().WithColor(new PdfDeviceColor(0.85, 0.1, 0.1)).WithInteriorColor(new PdfDeviceColor(1, 0.9, 0.9)).WithOpacity(0.6).WithContents("Box")),
                AnnotationTool.Text => page.AddFreeTextAnnotation(
                    new PdfRectangle(x, y - 40, x + 180, y), "Typed with Broadside", new PdfAnnotationOptions().WithColor(new PdfDeviceColor(0.1, 0.3, 0.8))),
                _ => page.AddStampAnnotation(
                    new PdfRectangle(x, y - 50, x + 160, y), "Approved", new PdfAnnotationOptions().WithColor(new PdfDeviceColor(0.1, 0.55, 0.2))),
            };
        });
        if (added is { } target)
        {
            layer.Select(target);
        }

        return added;
    }

    /// <summary>Highlights the selected text of its first page (ISO 32000-2 §12.5.6.10) and clears the selection; null when nothing is selected.</summary>
    public static AnnotationTarget? HighlightSelection(AnnotationLayer layer, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.IsEmpty)
        {
            return null;
        }

        int page = selection.Start.PageIndex;
        Quad[] quads = new Quad[Math.Max(selection.GetQuads(page, []), 1)];
        int count = Math.Min(selection.GetQuads(page, quads), quads.Length);
        if (count == 0)
        {
            return null;
        }

        var marks = new List<PdfQuadrilateral>(count);
        foreach (Quad quad in quads.AsSpan(0, count))
        {
            marks.Add(new PdfQuadrilateral(quad.TopLeft, quad.TopRight, quad.BottomLeft, quad.BottomRight));
        }

        AnnotationTarget? added = layer.Add(page, p => p.AddHighlightAnnotation(marks, new PdfAnnotationOptions().WithColor(new PdfDeviceColor(1, 0.95, 0.2))));
        if (added is not null)
        {
            selection.Clear();
        }

        return added;
    }
}

/// <summary>One signed field's validation as the signature panel shows it (ISO 32000-2 §12.8, ISO/TS 32002).</summary>
internal sealed record SignatureRow(string FieldName, string Status, IReadOnlyList<DocumentFact> Facts, IReadOnlyList<string> Problems);

/// <summary>Validates the document's signatures and turns each result into a row.</summary>
internal static class SignatureStatus
{
    public static async Task<IReadOnlyList<SignatureRow>> ValidateAsync(PdfDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        IReadOnlyList<PdfSignatureValidation> results = await document.ValidateSignaturesAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
        var rows = new List<SignatureRow>(results.Count);
        foreach (PdfSignatureValidation result in results)
        {
            var facts = new List<DocumentFact>();
            if (result.SignerCertificate is { } signer)
            {
                facts.Add(new DocumentFact("Signer", signer.Subject));
                facts.Add(new DocumentFact("Issuer", signer.Issuer));
            }

            facts.Add(new DocumentFact("Format", (result.SubFilter ?? "–") + (result.DigestAlgorithm is { } digest ? ", " + digest.Name : string.Empty)));
            facts.Add(new DocumentFact("Covers", result.CoversWholeFile ? "the whole file" : "an earlier revision (the file changed after signing)"));
            facts.Add(new DocumentFact("Chain", string.Create(CultureInfo.InvariantCulture, $"{result.CertificatePath.Count} certificate(s)")));
            rows.Add(new SignatureRow(result.FieldName, result.Status.ToString(), facts, [.. result.Problems.Select(p => p.Kind + ": " + p.Message)]));
        }

        return rows;
    }
}

/// <summary>Copies of page layout options with one value changed (the options are a mutable class; the view keeps its own copy).</summary>
internal static class LayoutChanges
{
    public static PageLayoutOptions Copy(PageLayoutOptions? options)
    {
        options ??= new PageLayoutOptions();
        return new PageLayoutOptions().WithMode(options.Mode).WithCover(options.ShowCover).WithGap(options.Gap).WithPadding(options.Padding).WithRotation(options.Rotation);
    }

    /// <summary>A view rotation turned by a quarter turn, kept in 0, 90, 180 and 270.</summary>
    public static PageLayoutOptions Rotate(PageLayoutOptions? options, int quarterTurns)
    {
        PageLayoutOptions copy = Copy(options);
        return copy.WithRotation((((copy.Rotation + (quarterTurns * 90)) % 360) + 360) % 360);
    }
}

/// <summary>A form field's widget as the form panel lists it: where it is, its field, its fully qualified name and what to call it.</summary>
internal sealed record FormFieldItem(AnnotationTarget Target, PdfTerminalField Field, string Name, string Label, PdfRectangle Rectangle);

/// <summary>Lists the form's widgets through the <see cref="AnnotationLayer"/> (ISO 32000-2 §12.7): values set there regenerate the field's appearance and redraw only its overlay tiles.</summary>
internal static class FormFields
{
    /// <summary>The widgets of the first <paramref name="maxPages"/> pages, in page order then annotation order.</summary>
    public static async Task<IReadOnlyList<FormFieldItem>> ListAsync(AnnotationLayer layer, int pageCount, int maxPages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var fields = new List<FormFieldItem>();
        for (int page = 0; page < Math.Min(pageCount, maxPages); page++)
        {
            int count = await layer.LoadPageAsync(page, cancellationToken).ConfigureAwait(true);
            for (int i = 0; i < count; i++)
            {
                var target = new AnnotationTarget(page, i);
                if (layer.GetRole(target) == AnnotationRole.Field && layer.GetAnnotation(target) is PdfWidgetAnnotation { Field: { } field })
                {
                    string name = field.FullyQualifiedName;
                    fields.Add(new FormFieldItem(target, field, name, field.AlternateName ?? name, layer.GetRectangle(target)));
                }
            }
        }

        return fields;
    }
}
