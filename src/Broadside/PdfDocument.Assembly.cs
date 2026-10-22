namespace Broadside;

/// <content>Merging documents and splitting one into ranges (issue #160, spec story 30): ISO 32000-2 §7.7.3, §12.3, §12.4.2 and §12.7.</content>
public sealed partial class PdfDocument
{
    /// <summary>Gets the configuration of the engine that opened or created the document, for documents made from it (<see cref="ExtractPages"/>).</summary>
    internal EngineConfiguration Configuration { get; private init; } = null!;

    /// <summary>Creates a new document holding the pages of <paramref name="sources"/>, in order, with their outlines, named destinations, form fields and page labels.</summary>
    /// <param name="sources">The documents to merge, in the order their pages appear; the same document may appear more than once.</param>
    /// <returns>The merged document, not yet saved; dispose it when done. The sources are only read and may be disposed once this returns.</returns>
    /// <exception cref="ArgumentException"><paramref name="sources"/> is empty or holds a null document.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.7.3, §12.3.2.4, §12.3.3, §12.4.2, §12.7.3 and §14.7. A document made by <see cref="Create()"/> (PDF 2.0, on an engine with
    /// default options) with its one blank page removed, then <see cref="AppendPages(PdfDocument)"/> for each source in turn: see there
    /// for what each source contributes and how names that collide are renamed. To merge on an engine with other options, create the
    /// document with <see cref="PdfEngine.Create"/> and append the sources to it.
    /// </remarks>
    public static PdfDocument Merge(params IEnumerable<PdfDocument> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        PdfDocument[] documents = [.. sources];
        if (documents.Length == 0)
        {
            throw new ArgumentException("At least one document is merged.", nameof(sources));
        }

        if (Array.IndexOf(documents, null) >= 0)
        {
            throw new ArgumentException("A document to merge is null.", nameof(sources));
        }

        PdfDocument merged = CreateEmpty(PdfEngine.Default.Configuration);
        try
        {
            foreach (PdfDocument source in documents)
            {
                merged.AppendPages(source);
            }

            return merged;
        }
        catch
        {
            merged.Dispose();
            throw;
        }
    }

    /// <summary>Appends every page of <paramref name="source"/> to this document, with its outline, named destinations, form fields and page labels.</summary>
    /// <param name="source">Another document; it is only read.</param>
    /// <exception cref="ArgumentException"><paramref name="source"/> is this document.</exception>
    /// <exception cref="NotSupportedException">A stream of an encrypted source cannot be copied decrypted (see <see cref="PdfObjectCopier.Copy"/>).</exception>
    /// <exception cref="Diagnostics.DiagnosticException">The source is in strict mode and a page reads its <c>Annots</c> from a page tree node.</exception>
    /// <remarks>ISO 32000-2 §7.7.3, §12.3, §12.7 and §14.7. As <see cref="AppendPages(PdfDocument, Range)"/> with every page.</remarks>
    public void AppendPages(PdfDocument source) => AppendPages(source, Range.All);

    /// <summary>Appends the pages of <paramref name="source"/> that <paramref name="pages"/> selects to this document, with the outline items, named destinations, form fields and page labels that belong to them.</summary>
    /// <param name="source">Another document; it is only read.</param>
    /// <param name="pages">The 0-based page indices to copy, such as <c>2..5</c> for the third to fifth pages.</param>
    /// <exception cref="ArgumentException"><paramref name="source"/> is this document.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pages"/> is not within the source's pages.</exception>
    /// <exception cref="NotSupportedException">A stream of an encrypted source cannot be copied decrypted (see <see cref="PdfObjectCopier.Copy"/>).</exception>
    /// <exception cref="Diagnostics.DiagnosticException">The source is in strict mode and a page reads its <c>Annots</c> from a page tree node.</exception>
    /// <remarks>
    /// <para>
    /// <b>Pages</b> (§7.7.3) are copied in one <see cref="PdfObjectCopier"/> session, so what they share is copied once, and added
    /// after this document's last page; the page tree is rebuilt balanced, as <see cref="PdfPageCollection.Insert(int, Objects.CosReference)"/>
    /// rebuilds it.
    /// </para>
    /// <para>
    /// <b>Outline</b> (§12.3.3): the source's items are added after this document's top-level items, keeping their levels, titles,
    /// colours, styles, open states and destinations or actions. When every page is copied every item is; otherwise an item is kept
    /// when it shows a copied page or one of its descendants is kept, and a kept item whose own destination is a page not copied
    /// loses that destination (it stays as a heading for its descendants). An item's structure element (<c>SE</c>) names the element's
    /// copy when the element is copied, and is left out otherwise.
    /// </para>
    /// <para>
    /// <b>Named destinations</b> (§12.3.2.4): those of the source's <c>Dests</c> name tree and catalog <c>Dests</c> dictionary that
    /// show a copied page (all of them when every page is copied) are added to this document's. A name this document already has is
    /// renamed by appending <c>_1</c>, <c>_2</c>, … (the first that is free), and every reference to it among the copies (outline
    /// items, link annotations, go-to actions and their <c>Next</c> actions) is renamed with it. A destination whose page is given as a
    /// page number (a producer error the reader accepts) is given the copied page.
    /// </para>
    /// <para>
    /// <b>Form fields</b> (§12.7.3, §12.7.4): the source's fields with a widget on a copied page (all of them when every page is
    /// copied) are added to this document's interactive form, which is created when absent, with their values; a field's widgets on
    /// pages not copied are left out. A top-level field whose name a field of this document has is renamed by appending <c>_1</c>,
    /// <c>_2</c>, …, so both keep their values, and the field names in the copies' reset-form, submit-form and hide actions and
    /// signature field locks are renamed with it (names inside JavaScript are not). The form's <c>DA</c> and <c>Q</c> are given to
    /// the copied top-level fields that do not set their own when they differ from this document's, its <c>DR</c> entries this
    /// document lacks are added, <c>NeedAppearances</c> and <c>SigFlags</c> are combined, and its calculation order (<c>CO</c>) is
    /// appended. An XFA form is not copied.
    /// </para>
    /// <para>
    /// <b>Page labels</b> (§12.4.2): when either document has page labels, the copied pages keep the labels they had in the source
    /// (a source without labels numbers them in decimal, as a reader would) and this document's pages keep theirs.
    /// </para>
    /// <para>
    /// <b>Optional content</b> (§8.11.4): the source's optional content groups the copies use (all of them when every page is copied)
    /// are listed in this document's <c>OCProperties</c>, which is created when absent, after its own groups, each with its default
    /// state; the source's <c>Order</c>, <c>RBGroups</c>, <c>Locked</c> and <c>AS</c> are appended to the default configuration's
    /// and its alternate configurations to <c>Configs</c>, each keeping only those groups. This document's own entries are unchanged.
    /// </para>
    /// <para>
    /// <b>Structure</b> (§14.7): the structure elements with a content item on a copied page (all of them when every page is copied),
    /// and their ancestors, are added after this document's top-level elements, which gives this document a structure tree when it has
    /// none. An element whose content is partly on pages not copied keeps only its content on copied pages. The copied pages, forms
    /// and annotations get new parent-tree keys (§14.7.5.4), so their content finds the copied elements; content that is no element's
    /// in the copy loses its key. Namespaces are matched by name, and role map, namespace role map and class map entries this document
    /// lacks are added. A structure type this document maps differently, an attribute class it defines differently and an element ID
    /// it already has are renamed in the copies by appending <c>_1</c>, <c>_2</c>, … (a table cell's <c>Headers</c> follow the IDs),
    /// each recorded as an Information diagnostic (<c>StructureRoleConflict</c>, <c>StructureClassConflict</c>,
    /// <c>StructureIdConflict</c>).
    /// </para>
    /// <para>
    /// Copied objects may raise this document's version on save. A mutation of this document: no read of it may run
    /// concurrently; the source may be read by others.
    /// </para>
    /// </remarks>
    public void AppendPages(PdfDocument source, Range pages)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(source, this))
        {
            throw new ArgumentException("A document cannot append its own pages; move them with Pages.Move.", nameof(source));
        }

        (int first, int count) = pages.GetOffsetAndLength(source.Pages.Count);
        if (count > 0)
        {
            new DocumentAssembly(source, this, first, count).Run();
        }
    }

    /// <summary>Creates a new document holding the pages of this document that <paramref name="pages"/> selects.</summary>
    /// <param name="pages">The 0-based page indices, such as <c>0..3</c> for the first three pages; at least one page.</param>
    /// <returns>The new document, not yet saved, opened with this document's options; dispose it when done.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pages"/> is not within the document's pages.</exception>
    /// <exception cref="ArgumentException"><paramref name="pages"/> selects no page.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.7.3, §12.3, §12.4.2 and §14.7. A new PDF 2.0 document to which the pages are appended as
    /// <see cref="AppendPages(PdfDocument, Range)"/> appends them: it keeps the outline items that show those pages (and the items
    /// above them), the named destinations and form fields on them, their page labels, and the structure elements whose content is on
    /// them (pruned of content on other pages). This document is only read.
    /// </remarks>
    public PdfDocument ExtractPages(Range pages)
    {
        (int first, int count) = pages.GetOffsetAndLength(Pages.Count);
        if (count == 0)
        {
            throw new ArgumentException("The range shall select at least one page.", nameof(pages));
        }

        PdfDocument part = CreateEmpty(Configuration);
        try
        {
            part.AppendPages(this, new Range(first, first + count));
            return part;
        }
        catch
        {
            part.Dispose();
            throw;
        }
    }

    /// <summary>Splits the document into one new document per range of pages.</summary>
    /// <param name="ranges">The 0-based page ranges, such as <c>0..3</c>, <c>3..7</c> and <c>7..</c>; each selects at least one page. They may overlap.</param>
    /// <returns>The new documents, in the order of the ranges; dispose each when done.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A range is not within the document's pages.</exception>
    /// <exception cref="ArgumentException">A range selects no page, or there is none.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.7.3, §12.3, §12.4.2 and §14.7. Each document is made as <see cref="ExtractPages"/> makes it, so its outline
    /// holds only the items that show its own pages and its structure tree only the elements whose content is on them. Every range is checked before any document is made. This document is only read.
    /// </remarks>
    public IReadOnlyList<PdfDocument> Split(params IEnumerable<Range> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        Range[] list = [.. ranges];
        if (list.Length == 0)
        {
            throw new ArgumentException("At least one range is given.", nameof(ranges));
        }

        int pageCount = Pages.Count;
        foreach (Range range in list)
        {
            if (range.GetOffsetAndLength(pageCount).Length == 0)
            {
                throw new ArgumentException("Each range shall select at least one page.", nameof(ranges));
            }
        }

        var parts = new List<PdfDocument>(list.Length);
        try
        {
            foreach (Range range in list)
            {
                parts.Add(ExtractPages(range));
            }

            return parts;
        }
        catch
        {
            foreach (PdfDocument part in parts)
            {
                part.Dispose();
            }

            throw;
        }
    }

    /// <summary>A new document (as <see cref="CreateNew"/> makes it) without its blank page, for pages to be appended to.</summary>
    private static PdfDocument CreateEmpty(EngineConfiguration configuration)
    {
        PdfDocument document = CreateNew(configuration);
        document.Pages.RemoveAt(0);
        return document;
    }
}
