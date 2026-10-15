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

        using var context = new FlowContext(document, this);
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
            }

            _blocks[index].LayOut(context);
        }

        return new FlowLayoutResult([.. context.Pages]);
    }
}

/// <summary>What <see cref="FlowDocument.Render"/> produced.</summary>
public sealed class FlowLayoutResult
{
    internal FlowLayoutResult(IReadOnlyList<PdfPage> pages)
    {
        Pages = pages;
    }

    /// <summary>The pages added to the document, in order.</summary>
    public IReadOnlyList<PdfPage> Pages { get; }
}
