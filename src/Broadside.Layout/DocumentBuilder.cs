using System.Diagnostics.CodeAnalysis;
using Broadside.Graphics;

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

    /// <summary>
    /// Sets the deepest heading level the document outline (bookmarks) lists; 3 by default, 0 for no outline. Each entry goes to its
    /// heading, nested by level.
    /// </summary>
    /// <param name="depth">The level, 0 to 6.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is outside 0 to 6.</exception>
    /// <remarks>ISO 32000-2 §12.3.3 (document outline). Appending to a document adds the entries after its existing ones.</remarks>
    public DocumentBuilder WithOutlineDepth(int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(depth, 6);
        Context.OutlineDepth = depth;
        return this;
    }

    /// <summary>
    /// Declares the PDF/UA conformance the document is built for. Its preconditions are checked when the build ends (a language, a title,
    /// tagged output, no skipped heading level under PDF/UA-1, no PDF/A level it cannot combine with), and an unmet one throws there,
    /// naming every one; it chooses the structure namespace (PDF 1.7 for PDF/UA-1, PDF 2.0 for PDF/UA-2); and it becomes the default for
    /// the document's saves (<see cref="PdfDocument.DefaultSaveOptions"/>), whose own PDF/UA check still runs.
    /// </summary>
    /// <param name="conformance">The conformance, or <see cref="PdfUAConformance.None"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="conformance"/> is not a defined conformance.</exception>
    /// <remarks>ISO 14289-1 (PDF/UA-1) and ISO 14289-2 (PDF/UA-2); ISO 32000-2 §14.8 (tagged PDF) and §14.8.6 (structure namespaces).</remarks>
    public DocumentBuilder WithPdfUAConformance(PdfUAConformance conformance)
    {
        Context.PdfUA = Enum.IsDefined(conformance) ? conformance : throw new ArgumentOutOfRangeException(nameof(conformance), conformance, "Not a defined PDF/UA conformance.");
        return this;
    }

    /// <summary>
    /// Declares the PDF/A conformance level the document is built for. Its preconditions are checked when the build ends (attached files
    /// only at levels that allow them, no PDF/UA conformance it cannot combine with), and an unmet one throws there, naming every one; it
    /// chooses the structure namespace (PDF 1.7 for PDF/A-2 and PDF/A-3, PDF 2.0 for PDF/A-4); and it becomes the default for the
    /// document's saves (<see cref="PdfDocument.DefaultSaveOptions"/>), whose own PDF/A check still runs. Fonts are always embedded.
    /// </summary>
    /// <param name="conformance">The level, or <see cref="PdfAConformance.None"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="conformance"/> is not a defined level.</exception>
    /// <remarks>ISO 19005-2, ISO 19005-3 and ISO 19005-4; ISO 32000-2 §14.8.6 (structure namespaces).</remarks>
    public DocumentBuilder WithPdfAConformance(PdfAConformance conformance)
    {
        Context.PdfA = Enum.IsDefined(conformance) ? conformance : throw new ArgumentOutOfRangeException(nameof(conformance), conformance, "Not a defined PDF/A conformance level.");
        return this;
    }

    /// <summary>
    /// Attaches a file to the document: embedded, listed among its attachments and associated with the document with
    /// <paramref name="relationship"/>, such as the XML of an electronic invoice (<see cref="PdfFileRelationship.Alternative"/>). The MIME
    /// type follows the file name's extension (<c>text/xml</c>, <c>application/json</c>, <c>text/csv</c>, ...; <c>application/octet-stream</c>
    /// when unknown).
    /// </summary>
    /// <param name="fileName">The file's name.</param>
    /// <param name="contents">The file's bytes; copied.</param>
    /// <param name="relationship">How the file relates to the document.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is null or empty.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.11.4 (embedded file streams), §7.7.4 (<c>EmbeddedFiles</c>) and §14.13.3 (associated files of the catalog). The
    /// file's modification date is the document's creation date, so the same input gives the same bytes. Associated files need PDF 2.0,
    /// or PDF/A-3 on PDF 1.7; under PDF/A they are allowed only at PDF/A-3 and PDF/A-4f.
    /// </remarks>
    public DocumentBuilder AttachFile(string fileName, ReadOnlySpan<byte> contents, PdfFileRelationship relationship)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        return AttachFile(fileName, contents, relationship, MediaTypeOf(fileName));
    }

    /// <summary>Attaches a file of the MIME type <paramref name="mediaType"/> (see <see cref="AttachFile(string, ReadOnlySpan{byte}, PdfFileRelationship)"/>).</summary>
    /// <param name="fileName">The file's name.</param>
    /// <param name="contents">The file's bytes; copied.</param>
    /// <param name="relationship">How the file relates to the document.</param>
    /// <param name="mediaType">The MIME type, such as <c>application/xml</c>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> or <paramref name="mediaType"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §7.11.4 and §14.13.3.</remarks>
    public DocumentBuilder AttachFile(string fileName, ReadOnlySpan<byte> contents, PdfFileRelationship relationship, string mediaType)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentException.ThrowIfNullOrEmpty(mediaType);
        Context.Attachments.Add(new AttachmentNode(fileName, contents.ToArray(), relationship, mediaType));
        return this;
    }

    /// <summary>Attaches a text file, written in UTF-8 (see <see cref="AttachFile(string, ReadOnlySpan{byte}, PdfFileRelationship)"/>).</summary>
    /// <param name="fileName">The file's name.</param>
    /// <param name="text">The file's text.</param>
    /// <param name="relationship">How the file relates to the document.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <remarks>ISO 32000-2 §7.11.4 and §14.13.3.</remarks>
    public DocumentBuilder AttachFile(string fileName, string text, PdfFileRelationship relationship)
    {
        ArgumentNullException.ThrowIfNull(text);
        return AttachFile(fileName, System.Text.Encoding.UTF8.GetBytes(text), relationship);
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

    private static string MediaTypeOf(string fileName) => Path.GetExtension(fileName).ToUpperInvariant() switch
    {
        ".XML" => "text/xml",
        ".JSON" => "application/json",
        ".CSV" => "text/csv",
        ".TXT" => "text/plain",
        ".HTML" or ".HTM" => "text/html",
        ".PDF" => "application/pdf",
        ".PNG" => "image/png",
        ".JPG" or ".JPEG" => "image/jpeg",
        ".ZIP" => "application/zip",
        _ => "application/octet-stream",
    };
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

    /// <summary>
    /// Draws on every page of the section once its content is laid out, over the body and under the header and footer: crop marks, a
    /// page border, letterhead art. The callback receives the page's canvas in default user space (points, origin at the bottom-left
    /// corner of the page) and the page: its number, the page count, its section and its setup. Calling it again adds another drawing.
    /// </summary>
    /// <param name="draw">Draws on one page.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="draw"/> is null.</exception>
    /// <remarks>
    /// ISO 32000-2 §14.8.2.2.2, Table 363: what it draws is a pagination artifact, in a saved graphics state (§8.4.2). It runs once per
    /// page, in page order; what it leaves unbalanced on the canvas is balanced and recorded as a <c>DrawingStateUnbalanced</c>
    /// diagnostic.
    /// </remarks>
    public SectionBuilder Decorate(Action<PdfCanvas, PageInfo> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        SectionNode section = Section;
        section.Decoration = section.Decoration is null ? draw : section.Decoration + draw;
        return this;
    }

    /// <summary>Fills every page of the section with <paramref name="color"/>, under everything else.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This builder.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363: a <c>Background</c> pagination artifact.</remarks>
    public SectionBuilder Background(PdfColor color)
    {
        Section.BackgroundColor = color;
        return this;
    }

    /// <summary>Stretches <paramref name="image"/> over every page of the section, above the background color and under everything else.</summary>
    /// <param name="image">The image; embedded once per document however many sections use it.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is null.</exception>
    /// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363 (a <c>Background</c> pagination artifact) and §8.9.5 (image XObjects).</remarks>
    public SectionBuilder Background(ImageSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        Section.BackgroundImage = image;
        return this;
    }

    /// <summary>
    /// Draws <paramref name="text"/> large, rotated 45 degrees and translucent across the middle of every page of the section, under the body:
    /// in the theme's heading font, bold, 60 points, in its muted color, at 20% opacity.
    /// </summary>
    /// <param name="text">The text, on one line.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is null or empty.</exception>
    /// <remarks>
    /// ISO 32000-2 §14.8.2.2.2, Table 363: a pagination artifact with <c>Subtype</c> <c>Watermark</c>; the opacity is the <c>ca</c> constant
    /// alpha of an ExtGState (§11.6.4.4), minimum PDF version 1.4.
    /// </remarks>
    public SectionBuilder Watermark(string text) => Watermark(text, Flow.Style.Empty);

    /// <summary>Draws <paramref name="text"/> as a watermark (see <see cref="Watermark(string)"/>) with <paramref name="style"/> over its default look.</summary>
    /// <param name="text">The text, on one line.</param>
    /// <param name="style">The font, size, weight and color over the default look.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    /// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363, and §11.6.4.4. Minimum PDF version 1.4.</remarks>
    public SectionBuilder Watermark(string text, Style style)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentNullException.ThrowIfNull(style);
        SectionNode section = Section;
        section.Watermark = text;
        section.WatermarkStyle = style;
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
