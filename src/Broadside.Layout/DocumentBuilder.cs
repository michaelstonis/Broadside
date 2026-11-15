using System.Diagnostics.CodeAnalysis;

namespace Broadside.Layout.Flow;

/// <summary>
/// The document scope of the document builder: document settings (title, author, language, theme, tagging) and the sections of content.
/// Reached through <c>PdfDocument.Create(doc =&gt; ...)</c> or <c>document.AppendPages(doc =&gt; ...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The builder is a front over flow layout: what the lambdas describe becomes flow blocks laid out by <see cref="FlowDocument"/> after the
/// outermost lambda returns. The lambdas run exactly once, in order, on the calling thread. The output is tagged by default (ISO 32000-2
/// §14.8): <c>Document</c>, then a <c>Sect</c> for each section after the first, then each element's structure type.
/// </para>
/// <para>
/// Builder handles are <see langword="ref"/> structs over one build: a nested lambda cannot capture an outer scope's builder, and a
/// handle cannot be stored or used across an <see langword="await"/>. A default handle, or one used after its build finished, throws
/// <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct DocumentBuilder
{
    private readonly BuildContext? _context;

    internal DocumentBuilder(BuildContext context) => _context = context;

    /// <summary>Gets the document being built, for setup that must happen before layout (such as embedding a font for an engine block).</summary>
    /// <exception cref="InvalidOperationException">The builder is not part of a build.</exception>
    public PdfDocument Document => Context.Document;

    /// <summary>Gets the theme in effect.</summary>
    /// <exception cref="InvalidOperationException">The builder is not part of a build.</exception>
    public Theme Theme => Context.Theme;

    private BuildContext Context => BuildContext.Check(_context);

    /// <summary>Sets the document title, written to the metadata with <c>DisplayDocTitle</c> set.</summary>
    /// <param name="title">The title.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="title"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.3.3 and §12.2, Table 147. Appending to a document that has a title keeps its title, with a diagnostic.</remarks>
    public DocumentBuilder WithTitle(string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        Context.Title = title;
        return this;
    }

    /// <summary>Sets the document's author.</summary>
    /// <param name="author">The author.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="author"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349 (<c>Author</c>).</remarks>
    public DocumentBuilder WithAuthor(string author)
    {
        ArgumentException.ThrowIfNullOrEmpty(author);
        Context.Author = author;
        return this;
    }

    /// <summary>Sets the document's subject.</summary>
    /// <param name="subject">The subject.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="subject"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349 (<c>Subject</c>).</remarks>
    public DocumentBuilder WithSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        Context.Subject = subject;
        return this;
    }

    /// <summary>Sets the document's keywords.</summary>
    /// <param name="keywords">The keywords.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="keywords"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349 (<c>Keywords</c>).</remarks>
    public DocumentBuilder WithKeywords(string keywords)
    {
        ArgumentException.ThrowIfNullOrEmpty(keywords);
        Context.Keywords = keywords;
        return this;
    }

    /// <summary>
    /// Sets the document's natural language (the catalog's <c>Lang</c>), which content inherits. The builder never infers it from the machine's
    /// culture.
    /// </summary>
    /// <param name="language">A BCP 47 language tag, such as <c>en-US</c>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="language"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.9.2. Minimum PDF version 1.4. Appending to a document that has a language keeps its language, with a diagnostic.</remarks>
    public DocumentBuilder WithLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        Context.Language = language;
        return this;
    }

    /// <summary>Sets the theme: fonts, sizes, colors, element styles and the default page setup.</summary>
    /// <param name="theme">The theme.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is null.</exception>
    public DocumentBuilder WithTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Context.Theme = theme;
        return this;
    }

    /// <summary>Sets whether the output is tagged; it is by default.</summary>
    /// <param name="tagged">False to write untagged content.</param>
    /// <returns>This builder.</returns>
    /// <remarks>ISO 32000-2 §14.8. Minimum PDF version 1.3 for the structure tree, 1.4 for <c>MarkInfo</c>.</remarks>
    public DocumentBuilder WithTagging(bool tagged)
    {
        Context.Tagging = tagged;
        return this;
    }

    /// <summary>Starts a section: content on new pages with its own page setup, header and footer.</summary>
    /// <param name="build">Describes the section.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    /// <remarks>ISO 32000-2 §14.8.4.4 (<c>Sect</c>): each section after the first is tagged as a <c>Sect</c>.</remarks>
    public DocumentBuilder Section(Action<SectionBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        BuildContext context = Context;
        var section = new SectionNode();
        context.Sections.Add(section);
        build(new SectionBuilder(context, section));
        return this;
    }

    /// <summary>Adds content to the last section (a section on the theme's page setup when there is none yet).</summary>
    /// <param name="build">Describes the content.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public DocumentBuilder Content(Action<ContainerBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        BuildContext context = Context;
        build(new ContainerBuilder(context, context.CurrentSection.Content));
        return this;
    }
}

/// <summary>
/// A section of the document builder: its page setup, its header and footer variants, a scope style and its content. Sections after the
/// first start on a new page; page numbers continue across sections.
/// </summary>
/// <remarks>ISO 32000-2 §14.11.2 (page boundaries) and §14.8.2.2.2 (headers and footers are pagination artifacts).</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct SectionBuilder
{
    private readonly BuildContext? _context;
    private readonly SectionNode? _section;

    internal SectionBuilder(BuildContext context, SectionNode section)
    {
        _context = context;
        _section = section;
    }

    private SectionNode Section
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _section!;
        }
    }

    /// <summary>Sets the page size; the theme's page size by default.</summary>
    /// <param name="size">The size.</param>
    /// <returns>This builder.</returns>
    public SectionBuilder Size(PageSize size)
    {
        Section.Size = size;
        return this;
    }

    /// <summary>Sets the page size.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A side is not positive.</exception>
    public SectionBuilder Size(Length width, Length height) => Size(new PageSize(width, height));

    /// <summary>Turns the pages so the long side runs across.</summary>
    /// <returns>This builder.</returns>
    public SectionBuilder Landscape()
    {
        Section.Landscape = true;
        return this;
    }

    /// <summary>Turns the pages so the long side runs down (the default).</summary>
    /// <returns>This builder.</returns>
    public SectionBuilder Portrait()
    {
        Section.Landscape = false;
        return this;
    }

    /// <summary>Sets every margin to <paramref name="all"/>.</summary>
    /// <param name="all">The margin.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="all"/> is negative.</exception>
    public SectionBuilder Margins(Length all) => Margins(all, all, all, all);

    /// <summary>Sets the margins.</summary>
    /// <param name="top">The top margin.</param>
    /// <param name="right">The right margin.</param>
    /// <param name="bottom">The bottom margin.</param>
    /// <param name="left">The left margin.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A margin is negative.</exception>
    public SectionBuilder Margins(Length top, Length right, Length bottom, Length left)
    {
        foreach ((Length value, string name) in (ReadOnlySpan<(Length, string)>)[(top, nameof(top)), (right, nameof(right)), (bottom, nameof(bottom)), (left, nameof(left))])
        {
            if (value.ToPoints() < 0)
            {
                throw new ArgumentOutOfRangeException(name, value, "A margin shall not be negative.");
            }
        }

        Section.Margins = new PageMargins(left.ToPoints(), top.ToPoints(), right.ToPoints(), bottom.ToPoints());
        return this;
    }

    /// <summary>Sets the style everything in the section inherits.</summary>
    /// <param name="style">The style.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public SectionBuilder Style(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Section.Style = style;
        return this;
    }

    /// <summary>Sets the header of every page (unless a first-page or even-page header applies).</summary>
    /// <param name="build">Describes the header; describing nothing draws nothing.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public SectionBuilder Header(Action<HeaderFooterBuilder> build)
    {
        Section.Header = Region(build);
        return this;
    }

    /// <summary>Sets the footer of every page (unless a first-page or even-page footer applies).</summary>
    /// <param name="build">Describes the footer; describing nothing draws nothing.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public SectionBuilder Footer(Action<HeaderFooterBuilder> build)
    {
        Section.Footer = Region(build);
        return this;
    }

    /// <summary>Sets the header of the section's first page; <c>h =&gt; { }</c> leaves that page without one.</summary>
    /// <param name="build">Describes the header.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public SectionBuilder FirstPageHeader(Action<HeaderFooterBuilder> build)
    {
        Section.FirstPageHeader = Region(build);
        return this;
    }

    /// <summary>Sets the footer of the section's first page; <c>f =&gt; { }</c> leaves that page without one.</summary>
    /// <param name="build">Describes the footer.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public SectionBuilder FirstPageFooter(Action<HeaderFooterBuilder> build)
    {
        Section.FirstPageFooter = Region(build);
        return this;
    }

    /// <summary>Sets the header of even-numbered pages.</summary>
    /// <param name="build">Describes the header.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public SectionBuilder EvenPageHeader(Action<HeaderFooterBuilder> build)
    {
        Section.EvenPageHeader = Region(build);
        return this;
    }

    /// <summary>Sets the footer of even-numbered pages.</summary>
    /// <param name="build">Describes the footer.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public SectionBuilder EvenPageFooter(Action<HeaderFooterBuilder> build)
    {
        Section.EvenPageFooter = Region(build);
        return this;
    }

    /// <summary>Adds content to the section.</summary>
    /// <param name="build">Describes the content.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public SectionBuilder Content(Action<ContainerBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        SectionNode section = Section;
        build(new ContainerBuilder(_context!, section.Content));
        return this;
    }

    private HeaderFooterNode Region(Action<HeaderFooterBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _ = Section;
        var region = new HeaderFooterNode();
        build(new HeaderFooterBuilder(_context!, region));
        return region;
    }
}

/// <summary>
/// A header or footer of the document builder: one line with up to three slots (left, center and right), each literal text with
/// <see cref="PageField"/> values filled in per page: <c>f.Center($"Page {PageField.Number} of {PageField.Count}")</c>.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363: drawn as a pagination artifact (<c>Subtype</c> <c>Header</c> or <c>Footer</c>).</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct HeaderFooterBuilder
{
    private readonly BuildContext? _context;
    private readonly HeaderFooterNode? _region;

    internal HeaderFooterBuilder(BuildContext context, HeaderFooterNode region)
    {
        _context = context;
        _region = region;
    }

    private HeaderFooterNode Region
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _region!;
        }
    }

    /// <summary>Sets the text at the left margin.</summary>
    /// <param name="text">The text.</param>
    /// <returns>This builder.</returns>
    public HeaderFooterBuilder Left(PageText text)
    {
        Region.Left = text;
        return this;
    }

    /// <summary>Sets the text centered between the margins.</summary>
    /// <param name="text">The text.</param>
    /// <returns>This builder.</returns>
    public HeaderFooterBuilder Center(PageText text)
    {
        Region.Center = text;
        return this;
    }

    /// <summary>Sets the text ending at the right margin.</summary>
    /// <param name="text">The text.</param>
    /// <returns>This builder.</returns>
    public HeaderFooterBuilder Right(PageText text)
    {
        Region.Right = text;
        return this;
    }

    /// <summary>Sets the style of the line, over the theme's <see cref="Theme.HeaderFooter"/> style.</summary>
    /// <param name="style">The style.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public HeaderFooterBuilder Style(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Region.Style = style;
        return this;
    }

    /// <summary>Sets the distance from the page edge to the line's outer edge; 36 points by default.</summary>
    /// <param name="offset">The offset.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative.</exception>
    public HeaderFooterBuilder Offset(Length offset)
    {
        Region.Offset = offset.ToPoints() >= 0 ? offset : throw new ArgumentOutOfRangeException(nameof(offset), offset, "The offset shall not be negative.");
        return this;
    }
}
