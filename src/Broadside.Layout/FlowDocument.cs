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
    /// <returns>The pages added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentException">A block cannot be laid out, such as a paragraph with a character its font cannot encode (the message names the block). Nothing is added.</exception>
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
        FlowStructure? structure = Tagging is { } tagging ? BeginTagging(document, tagging, diagnostics) : null;
        using var context = new FlowContext(document, this, structure is null ? Services : Services with { Structure = structure });
        int section = 0;
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

            _blocks[index].LayOut(context);
        }

        return new FlowLayoutResult([.. context.Pages], diagnostics);
    }

    /// <summary>Gets or creates the structure tree and writes the document-level tagging entries: <c>Lang</c>, the title and <c>MarkInfo</c>.</summary>
    private static FlowStructure BeginTagging(PdfDocument document, FlowTagging tagging, List<Diagnostic> diagnostics)
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

        return new FlowStructure(root, tagging, diagnostics);
    }

    /// <summary>A text string (§7.9.2.2): PDFDocEncoding for printable ASCII, else UTF-16BE with a byte order mark.</summary>
    private static CosString TextString(string text) =>
        new(text.All(static c => c is >= ' ' and <= '~') ? Encoding.ASCII.GetBytes(text) : [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)]);
}

/// <summary>What <see cref="FlowDocument.Render"/> produced.</summary>
public sealed class FlowLayoutResult
{
    internal FlowLayoutResult(IReadOnlyList<PdfPage> pages, IReadOnlyList<Diagnostic> diagnostics)
    {
        Pages = pages;
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// Gets what the layout found worth reporting. With <see cref="FlowDocument.Tagging"/> on, a figure (an <see cref="ImageBlock"/> or an
    /// <see cref="InlineImage"/>) without alternate text records <c>FigureWithoutAlternateText</c> (warning, with the element's reference).
    /// </summary>
    /// <remarks>ISO 32000-2 §14.9.3.</remarks>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>The pages added to the document, in order.</summary>
    public IReadOnlyList<PdfPage> Pages { get; }
}
