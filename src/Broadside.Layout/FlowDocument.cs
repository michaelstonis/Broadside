using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Layout.Flow;

/// <summary>
/// A flow-layout document: blocks laid out top to bottom in the body area of pages of a <see cref="PageTemplate"/> (one per
/// section, <see cref="StartSection"/>), starting a new page whenever the next line would cross the bottom of the body area. <see cref="Render"/> appends the pages to a
/// <see cref="PdfDocument"/> and draws through its canvas, so the result saves like any other page content.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.7.3.3 (page objects), §8.2 and §9.4 (page content and text operators): each page is a new page object with the
/// setup's media box and one content stream of text objects. The same blocks, fonts and setup produce the same bytes on every run
/// and platform.
/// </para>
/// <para>Not thread-safe: build and render from one thread, as for any document mutation.</para>
/// </remarks>
public sealed class FlowDocument
{
    private readonly List<FlowBlock> _blocks = [];
    private readonly List<(int Block, PageTemplate Template)> _sections = [];

    /// <summary>Creates an empty flow with pages of <paramref name="page"/> and no header, footer or background.</summary>
    /// <param name="page">The page size and margins.</param>
    /// <exception cref="ArgumentException">A dimension or margin is not finite or negative, or the margins leave no content area.</exception>
    public FlowDocument(PageSetup page)
    {
        page.Validate(nameof(page));
        Template = new PageTemplate(page);
    }

    /// <summary>Creates an empty flow whose pages follow <paramref name="template"/> until a <see cref="StartSection"/>.</summary>
    /// <param name="template">The template of the first section.</param>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is null.</exception>
    public FlowDocument(PageTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        Template = template;
    }

    /// <summary>The size and margins of the pages of the first section.</summary>
    public PageSetup Page => Template.Setup;

    /// <summary>The template of the first section.</summary>
    public PageTemplate Template { get; }

    /// <summary>
    /// Starts a section: the blocks added from now on begin on a new page and flow on pages of <paramref name="template"/>, whose
    /// first-page header and footer apply to that page. Page numbers continue across sections.
    /// </summary>
    /// <param name="template">The template of the new section.</param>
    /// <returns>This flow, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is null.</exception>
    public FlowDocument StartSection(PageTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        _sections.Add((_blocks.Count, template));
        return this;
    }

    /// <summary>The blocks, in the order they flow.</summary>
    public IReadOnlyList<FlowBlock> Blocks => _blocks;

    /// <summary>The replaceable layout steps (line breaking, shaping, bidi, structure).</summary>
    internal FlowServices Services { get; } = FlowServices.Default;

    /// <summary>
    /// Gets or sets how <see cref="Render"/> tags the content it lays out, or <see langword="null"/> (the default) for untagged content. Page
    /// backgrounds, watermarks, headers and footers are marked as artifacts either way.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.8. See <see cref="FlowTagging"/> for the structure written.</remarks>
    public FlowTagging? Tagging { get; set; }

    /// <summary>
    /// Gets or sets the deepest heading level <see cref="Render"/> adds to the document outline: 0 (the default) adds none; 1 to 6 add
    /// an item for every paragraph tagged <c>H1</c> down to that level, nested by level, in flow order, after the outline's existing items.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 0 to 6.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.3 (document outline): each item's title is the heading's text and its destination the heading's named destination
    /// (§12.3.2.4) when it has an <see cref="FlowBlock.Id"/>, else an explicit <c>XYZ</c> destination at the heading (§12.3.2.2).
    /// </remarks>
    public int OutlineDepth
    {
        get;
        set => field = value is >= 0 and <= 6 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The outline depth is 0 (none) or a heading level from 1 to 6.");
    }

    /// <summary>
    /// Gets or sets how many times at most <see cref="Render"/> lays the flow out again to settle its forward references (page references and
    /// tables of contents) before drawing it; 3 by default. When the page numbers still change after the last time, the last numbers stand
    /// and <c>ForwardReferencesNotConverged</c> is reported. A flow without forward references is laid out once.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxLayoutPasses
    {
        get;
        set => field = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "At least one layout pass is needed.");
    } = 3;

    /// <summary>
    /// The existing structure element the tagged blocks go under instead of a new <c>Document</c> element, when pages are appended to a
    /// tagged document; null for a new root.
    /// </summary>
    internal PdfStructureElement? StructureParent { get; set; }

    /// <summary>Appends a block.</summary>
    /// <param name="block">The block.</param>
    /// <returns>This flow, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> is null.</exception>
    public FlowDocument Add(FlowBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        _blocks.Add(block);
        return this;
    }

    /// <summary>
    /// Lays every block out and appends the pages to <paramref name="document"/> after its existing pages. The fonts of the blocks'
    /// styles shall come from <paramref name="document"/>. A flow with no blocks adds no page.
    /// </summary>
    /// <param name="document">The document to append pages to.</param>
    /// <returns>The pages added, and what the layout found worth reporting.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A block cannot be laid out, such as a paragraph with a character its font cannot encode (the message names the block), or a form
    /// field's name is not unique. Nothing is added.
    /// </exception>
    /// <exception cref="DiagnosticException"><paramref name="document"/> is in strict mode (<see cref="PdfOptions.UseStrict"/>) and the layout
    /// found a problem of severity <see cref="DiagnosticSeverity.Warning"/> or <see cref="DiagnosticSeverity.Error"/>: the first one is thrown
    /// once every page is laid out, so the pages stay added.</exception>
    /// <remarks>
    /// <para>
    /// What the layout finds (<see cref="FlowLayoutResult.Diagnostics"/>) is also recorded on <paramref name="document"/> with
    /// <see cref="PdfDocument.ReportDiagnostic"/>, in the same order, so <see cref="PdfDocument.Diagnostics"/> is the one list to look at.
    /// </para>
    /// <para>
    /// Anchors (<see cref="FlowBlock.Id"/>) become named destinations, links to them get their destinations once every page is drawn, and a
    /// flow with forward references is first laid out without drawing until its page numbers settle (<see cref="MaxLayoutPasses"/>).
    /// </para>
    /// </remarks>
    public FlowLayoutResult Render(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        for (int index = 0; index < _blocks.Count; index++)
        {
            _blocks[index].Validate(index);
        }

        Template.Validate();
        foreach ((_, PageTemplate template) in _sections)
        {
            template.Validate();
        }

        var diagnostics = new List<Diagnostic>();
        FlowReferences? references = FlowReferences.Scan(_blocks, document, OutlineDepth, diagnostics);
        if (references is { NeedsPageNumbers: true })
        {
            Settle(document, references, diagnostics);
        }

        FlowStructure? structure = Tagging is { } tagging ? BeginTagging(document, tagging, diagnostics) : null;
        references?.BeginPass();
        PdfPage[] pages;
        using (var context = new FlowContext(document, this, structure is null ? Services : Services with { Structure = structure }, diagnostics, references, measuring: false))
        {
            LayOutBlocks(context, structure);
            pages = [.. context.Pages];
        }

        IReadOnlyDictionary<string, FlowAnchor>? anchors = references?.Finish(OutlineDepth);
        foreach (Diagnostic diagnostic in diagnostics)
        {
            document.ReportDiagnostic(diagnostic);
        }

        return new FlowLayoutResult(pages, diagnostics, anchors);
    }

    /// <summary>
    /// Lays the flow out without drawing, each time with the page numbers the time before found, until they no longer change or
    /// <see cref="MaxLayoutPasses"/> is reached.
    /// </summary>
    private void Settle(PdfDocument document, FlowReferences references, List<Diagnostic> diagnostics)
    {
        for (int pass = 0; pass < MaxLayoutPasses; pass++)
        {
            references.BeginPass();
            using (var context = new FlowContext(document, this, Services, [], references, measuring: true))
            {
                LayOutBlocks(context, null);
            }

            if (references.EndMeasuringPass())
            {
                return;
            }
        }

        diagnostics.Add(new Diagnostic(
            LayoutDiagnostics.ForwardReferencesNotConverged.Name,
            DiagnosticSeverity.Warning,
            $"The page numbers the flow shows still changed after {MaxLayoutPasses} layout passes; the last pass's numbers are shown, and some may be wrong."));
    }

    /// <summary>Lays every block out through <paramref name="context"/>, starting each section where it begins.</summary>
    private void LayOutBlocks(FlowContext context, FlowStructure? structure)
    {
        int section = 0;
        int kept = 0;
        for (int index = 0; index < _blocks.Count; index++)
        {
            PageTemplate? next = null;
            while (section < _sections.Count && _sections[section].Block == index)
            {
                next = _sections[section++].Template;
            }

            if (next is not null)
            {
                context.StartSection(next);
                structure?.BeginSection(next);
            }

            if (index >= kept && _blocks[index].KeepWithNext)
            {
                kept = context.KeepWithNext(_blocks, index, section < _sections.Count ? _sections[section].Block : _blocks.Count);
            }

            _blocks[index].LayOut(context);
        }
    }

    /// <summary>Gets or creates the structure tree and writes the document-level tagging entries: <c>Lang</c>, the title and <c>MarkInfo</c>.</summary>
    private FlowStructure BeginTagging(PdfDocument document, FlowTagging tagging, List<Diagnostic> diagnostics)
    {
        PdfStructureTreeRoot root = document.StructureTree ?? document.CreateStructureTree();
        document.MarkInfo.Marked = true;
        if (tagging.Language is { } language)
        {
            document.Catalog[new CosName("Lang")] = TextString(language);
        }

        if (tagging.Title is { } title)
        {
            document.Properties.Title = title;
            CosDictionary preferences = document.ViewerPreferences?.Dictionary ?? new CosDictionary();
            preferences[new CosName("DisplayDocTitle")] = CosBoolean.True;
            if (document.ViewerPreferences is null)
            {
                document.Catalog[new CosName("ViewerPreferences")] = preferences;
            }
        }

        return new FlowStructure(root, tagging, diagnostics, StructureParent);
    }

    /// <summary>A text string (§7.9.2.2): PDFDocEncoding for printable ASCII, else UTF-16BE with a byte order mark.</summary>
    internal static CosString TextString(string text) =>
        new(text.All(static c => c is >= ' ' and <= '~') ? Encoding.ASCII.GetBytes(text) : [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)]);
}

/// <summary>What <see cref="FlowDocument.Render"/> produced.</summary>
public sealed class FlowLayoutResult
{
    internal FlowLayoutResult(IReadOnlyList<PdfPage> pages, IReadOnlyList<Diagnostic> diagnostics, IReadOnlyDictionary<string, FlowAnchor>? anchors = null)
    {
        Pages = pages;
        Diagnostics = diagnostics;
        Anchors = anchors ?? new Dictionary<string, FlowAnchor>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets where each block with an <see cref="FlowBlock.Id"/> landed, by id: its page and the top left of its first line, which is also
    /// the named destination written for it. A block that drew nothing has no entry.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.3.2.4.</remarks>
    public IReadOnlyDictionary<string, FlowAnchor> Anchors { get; }

    /// <summary>
    /// Gets what the layout found worth reporting, also recorded on the document in <see cref="PdfDocument.Diagnostics"/>. With
    /// <see cref="FlowDocument.Tagging"/> on, a figure (an <see cref="ImageBlock"/>, a <see cref="DrawingBlock"/> or an
    /// <see cref="InlineImage"/>) without alternate text records <c>FigureWithoutAlternateText</c> (warning, with the element's reference);
    /// a drawing or page decoration that leaves the canvas unbalanced records <c>DrawingStateUnbalanced</c>, and a group kept together
    /// that is taller than a page <c>KeepTogetherTooTall</c>; a link or page reference to an id nothing has <c>AnchorNotFound</c>, a second
    /// block with an id already used <c>AnchorDuplicate</c>, and page numbers that did not settle <c>ForwardReferencesNotConverged</c>
    /// (<see cref="LayoutDiagnostics"/>).
    /// </summary>
    /// <remarks>ISO 32000-2 §14.9.3.</remarks>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>The pages added to the document, in order.</summary>
    public IReadOnlyList<PdfPage> Pages { get; }
}
