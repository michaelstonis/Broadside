using System.Diagnostics.CodeAnalysis;
using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// A scope of content in the document builder (a section, a group, a list item, a table cell): headings, paragraphs, lists, tables,
/// images, groups and engine blocks, laid out top to bottom. Each method adds an element and returns its builder, whose modifiers chain:
/// <c>c.Paragraph(text).Justify().SpaceAfter(6)</c>.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.4 (standard structure types): each element names the structure element it becomes when tagged.</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct ContainerBuilder
{
    private readonly BuildContext? _context;
    private readonly ContainerNode? _container;

    internal ContainerBuilder(BuildContext context, ContainerNode container)
    {
        _context = context;
        _container = container;
    }

    /// <summary>Gets the document being built.</summary>
    /// <exception cref="InvalidOperationException">The builder is not part of a build.</exception>
    public PdfDocument Document => Context.Document;

    /// <summary>Gets the theme in effect, for components that pick its colors and styles.</summary>
    /// <exception cref="InvalidOperationException">The builder is not part of a build.</exception>
    public Theme Theme => Context.Theme;

    private BuildContext Context => BuildContext.Check(_context);

    /// <summary>Adds a heading of <paramref name="level"/> (<c>H1</c> to <c>H6</c>) in the theme's heading style.</summary>
    /// <param name="level">The level, 1 to 6.</param>
    /// <param name="text">The text.</param>
    /// <returns>The heading's builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is outside 1 to 6.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <remarks>ISO 32000-2 §14.8.4.3.6. A heading more than one level below the previous one (an H3 after an H1) is a diagnostic.</remarks>
    public ParagraphElement Heading(int level, string text)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 6);
        ArgumentNullException.ThrowIfNull(text);
        return Add(new ParagraphNode { Text = text, Level = level });
    }

    /// <summary>Adds a paragraph of plain text (<c>P</c>) in the body style.</summary>
    /// <param name="text">The text; a line feed breaks the line.</param>
    /// <returns>The paragraph's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <remarks>ISO 32000-2 §14.8.4.3.2.</remarks>
    public ParagraphElement Paragraph(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Add(new ParagraphNode { Text = text });
    }

    /// <summary>Adds a paragraph of spans: text with its own styles, links and line breaks.</summary>
    /// <param name="build">Describes the spans.</param>
    /// <returns>The paragraph's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    /// <remarks>ISO 32000-2 §14.8.4.3.2 (<c>P</c>) and §14.8.4.4 (<c>Span</c>, <c>Link</c>).</remarks>
    public ParagraphElement Paragraph(Action<TextBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        BuildContext context = Context;
        var paragraph = new ParagraphNode { Spans = [] };
        _container!.Blocks.Add(paragraph);
        build(new TextBuilder(context, paragraph));
        return new ParagraphElement(context, paragraph);
    }

    /// <summary>Adds a list (<c>L</c>), bulleted unless its builder says otherwise.</summary>
    /// <param name="build">Describes the items.</param>
    /// <returns>The list's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    /// <remarks>ISO 32000-2 §14.8.4.7.2 (<c>L</c>, <c>LI</c>, <c>Lbl</c>, <c>LBody</c>) and §14.8.5.5 (<c>ListNumbering</c>).</remarks>
    public ListElement List(Action<ListBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        BuildContext context = Context;
        var list = new ListNode();
        _container!.Blocks.Add(list);
        build(new ListBuilder(context, list));
        return new ListElement(context, list);
    }

    /// <summary>Adds a table (<c>Table</c>).</summary>
    /// <param name="build">Describes the columns and rows.</param>
    /// <returns>The table's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    /// <remarks>ISO 32000-2 §14.8.4.8.3 and §14.8.5.7.</remarks>
    public TableElement Table(Action<TableBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        BuildContext context = Context;
        var table = new TableNode();
        _container!.Blocks.Add(table);
        build(new TableBuilder(context, table));
        return new TableElement(context, table);
    }

    /// <summary>Adds an image (<c>Figure</c>) with its alternate text.</summary>
    /// <param name="source">The image.</param>
    /// <param name="alternateText">What the image shows, for readers that cannot see it.</param>
    /// <returns>The image's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="alternateText"/> is null, empty or white space.</exception>
    /// <remarks>
    /// ISO 32000-2 §14.8.4.6 (<c>Figure</c>) and §14.9.3 (<c>Alt</c>). Alternate text that looks like a file name is a diagnostic. Use
    /// <see cref="DecorativeImage"/> for an image that carries no meaning.
    /// </remarks>
    public ImageElement Image(ImageSource source, string alternateText)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(alternateText);
        return AddImage(new ImageNode { Source = source, AlternateText = alternateText });
    }

    /// <summary>Adds an image that carries no meaning, drawn as an artifact and skipped by assistive technology.</summary>
    /// <param name="source">The image.</param>
    /// <returns>The image's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <remarks>ISO 32000-2 §14.8.2.2 (artifacts).</remarks>
    public ImageElement DecorativeImage(ImageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return AddImage(new ImageNode { Source = source });
    }

    /// <summary>
    /// Adds a vector drawing (<c>Figure</c>) with its alternate text: <paramref name="draw"/> paints a box of <paramref name="width"/> by
    /// <paramref name="height"/> through the page's <see cref="PdfCanvas"/>, whose origin is the box's bottom-left corner and whose unit is the point.
    /// </summary>
    /// <param name="width">The width of the box.</param>
    /// <param name="height">The height of the box.</param>
    /// <param name="alternateText">What the drawing shows, for readers that cannot see it.</param>
    /// <param name="draw">Paints the drawing: the page's canvas, and the box <c>[0 0 width height]</c> in its current coordinates.</param>
    /// <returns>The drawing's builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="alternateText"/> is null, empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="draw"/> is null.</exception>
    /// <remarks>
    /// ISO 32000-2 §14.8.4.8.5 (<c>Figure</c>, with a <c>BBox</c> layout attribute, §14.8.5.4.3), §14.9.3 (<c>Alt</c>) and §8.4.2. The
    /// callback runs once, when the drawing is placed, inside a saved graphics state and the figure's marked content; what it leaves
    /// unbalanced is balanced again and recorded as a <c>DrawingStateUnbalanced</c> diagnostic. A drawing that does not fit below the
    /// content before it starts a page; one wider than the space it is in, or taller than a page, is scaled down.
    /// </remarks>
    public DrawingElement Drawing(Length width, Length height, string alternateText, Action<PdfCanvas, PdfRectangle> draw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alternateText);
        return AddDrawing(width, height, alternateText, draw);
    }

    /// <summary>Adds a vector drawing that carries no meaning (see <see cref="Drawing"/>), drawn as an artifact and skipped by assistive technology.</summary>
    /// <param name="width">The width of the box.</param>
    /// <param name="height">The height of the box.</param>
    /// <param name="draw">Paints the drawing.</param>
    /// <returns>The drawing's builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="draw"/> is null.</exception>
    /// <remarks>ISO 32000-2 §14.8.2.2 (artifacts).</remarks>
    public DrawingElement DecorativeDrawing(Length width, Length height, Action<PdfCanvas, PdfRectangle> draw) => AddDrawing(width, height, null, draw);

    /// <summary>
    /// Adds columns side by side, such as an address beside an invoice number: each column flows its own content from the same top, and
    /// what follows starts below the longest. Each column is tagged as one <c>Div</c>, read in column order, never as a table.
    /// </summary>
    /// <param name="build">Describes the columns.</param>
    /// <returns>The columns' builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="build"/> describes no column.</exception>
    /// <remarks>ISO 32000-2 §14.8.4.4 (grouping elements). This is not newspaper-style flow from one column into the next.</remarks>
    public ColumnsElement Columns(Action<ColumnsBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        BuildContext context = Context;
        var columns = new ColumnsNode();
        _container!.Blocks.Add(columns);
        build(new ColumnsBuilder(context, columns));
        if (columns.Columns.Count == 0)
        {
            throw new InvalidOperationException("Columns need at least one column.");
        }

        return new ColumnsElement(context, columns);
    }

    /// <summary>Adds a group: content that shares a style and adds no structure element of its own unless given a <see cref="GroupElement.Role"/>.</summary>
    /// <param name="build">Describes the content.</param>
    /// <returns>The group's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public GroupElement Group(Action<ContainerBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        BuildContext context = Context;
        var group = new GroupNode();
        _container!.Blocks.Add(group);
        build(new ContainerBuilder(context, group.Content));
        return new GroupElement(context, group);
    }

    /// <summary>Adds vertical space; space that would fall at the top of a page is dropped.</summary>
    /// <param name="height">The space.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is negative.</exception>
    public void Spacer(Length height)
    {
        _ = Context;
        _container!.Blocks.Add(new SpacerNode(height.ToPoints() >= 0 ? height : throw new ArgumentOutOfRangeException(nameof(height), height, "The space shall not be negative.")));
    }

    /// <summary>Ends the page: what follows starts on a new one. A break before any content, or inside a table cell, does nothing.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 (page objects).</remarks>
    public void PageBreak()
    {
        _ = Context;
        _container!.Blocks.Add(new PageBreakNode());
    }

    /// <summary>Adds a horizontal rule across the content width in the theme's border color, drawn as an artifact.</summary>
    /// <returns>The rule's builder.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.2 (artifacts).</remarks>
    public RuleElement Rule()
    {
        BuildContext context = Context;
        var rule = new RuleNode();
        _container!.Blocks.Add(rule);
        return new RuleElement(context, rule);
    }

    /// <summary>Adds a flow block built with the engine directly; it is laid out as it is, with the styles it carries.</summary>
    /// <param name="block">The block. Its fonts and images shall come from <see cref="Document"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> is null.</exception>
    public void Add(FlowBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        _ = Context;
        _container!.Blocks.Add(new RawNode(block));
    }

    private ParagraphElement Add(ParagraphNode paragraph)
    {
        BuildContext context = Context;
        _container!.Blocks.Add(paragraph);
        return new ParagraphElement(context, paragraph);
    }

    private ImageElement AddImage(ImageNode image)
    {
        BuildContext context = Context;
        _container!.Blocks.Add(image);
        return new ImageElement(context, image);
    }

    private DrawingElement AddDrawing(Length width, Length height, string? alternateText, Action<PdfCanvas, PdfRectangle> draw)
    {
        if (!(width.ToPoints() > 0 && double.IsFinite(width.ToPoints())))
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width shall be positive and finite.");
        }

        if (!(height.ToPoints() > 0 && double.IsFinite(height.ToPoints())))
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height shall be positive and finite.");
        }

        ArgumentNullException.ThrowIfNull(draw);
        BuildContext context = Context;
        var drawing = new DrawingNode { Width = width, Height = height, Draw = draw, AlternateText = alternateText };
        _container!.Blocks.Add(drawing);
        return new DrawingElement(context, drawing);
    }
}

/// <summary>The spans of a paragraph in the document builder: text, links and line breaks, each with its own style.</summary>
/// <remarks>ISO 32000-2 §14.8.4.4 (<c>Span</c>, <c>Link</c>).</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct TextBuilder
{
    private readonly BuildContext? _context;
    private readonly ParagraphNode? _paragraph;

    internal TextBuilder(BuildContext context, ParagraphNode paragraph)
    {
        _context = context;
        _paragraph = paragraph;
    }

    /// <summary>Adds text in the paragraph's style.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The span's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public SpanElement Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Add(new SpanNode { Text = text });
    }

    /// <summary>Adds a link to a URI, in the theme's link style; it is tagged <c>Link</c> with its annotation.</summary>
    /// <param name="text">The link's text.</param>
    /// <param name="uri">The target.</param>
    /// <returns>The span's builder.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.5 (link annotations) and §12.6.4.8 (URI actions). Minimum PDF version 1.1.</remarks>
    public SpanElement Link(string text, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(uri);
        return Add(new SpanNode { Text = text, Link = uri });
    }

    /// <summary>Ends the line: what follows starts on the next one.</summary>
    public void LineBreak() => Add(new SpanNode { Text = "\n", LineBreak = true });

    private SpanElement Add(SpanNode span)
    {
        BuildContext context = BuildContext.Check(_context);
        _paragraph!.Spans!.Add(span);
        return new SpanElement(context, span);
    }
}

/// <summary>A span's modifiers in the document builder: they apply to the span only.</summary>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct SpanElement
{
    private readonly BuildContext? _context;
    private readonly SpanNode? _span;

    internal SpanElement(BuildContext context, SpanNode span)
    {
        _context = context;
        _span = span;
    }

    private Style Local
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _span!.Local ?? Flow.Style.Empty;
        }

        set => _span!.Local = value;
    }

    /// <summary>Uses the bold face.</summary>
    /// <returns>This builder.</returns>
    public SpanElement Bold()
    {
        Local = Local with { Bold = true };
        return this;
    }

    /// <summary>Uses the italic face.</summary>
    /// <returns>This builder.</returns>
    public SpanElement Italic()
    {
        Local = Local with { Italic = true };
        return this;
    }

    /// <summary>Underlines the text.</summary>
    /// <returns>This builder.</returns>
    public SpanElement Underline()
    {
        Local = Local with { Underline = true };
        return this;
    }

    /// <summary>Strikes the text through.</summary>
    /// <returns>This builder.</returns>
    public SpanElement StrikeThrough()
    {
        Local = Local with { StrikeThrough = true };
        return this;
    }

    /// <summary>Sets the font size.</summary>
    /// <param name="size">The size in points.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive and finite.</exception>
    public SpanElement FontSize(double size)
    {
        Local = Local with { FontSize = size };
        return this;
    }

    /// <summary>Sets the font family.</summary>
    /// <param name="family">The family.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="family"/> is null.</exception>
    public SpanElement Font(FontFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        Local = Local with { FontFamily = family };
        return this;
    }

    /// <summary>Sets the text color.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="color"/> is a pattern.</exception>
    public SpanElement Color(PdfColor color)
    {
        Local = Local with { Color = color };
        return this;
    }

    /// <summary>Sets the span's natural language, written as <c>Lang</c> on a <c>Span</c> when it differs from the paragraph's.</summary>
    /// <param name="language">A BCP 47 language tag.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="language"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.9.2.</remarks>
    public SpanElement Language(string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        Local = Local with { Language = language };
        return this;
    }

    /// <summary>Applies a partial style to the span; its set properties win over earlier modifiers.</summary>
    /// <param name="style">The style.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public SpanElement Style(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Local = Local.Merge(style);
        return this;
    }
}

/// <summary>A paragraph's or heading's modifiers in the document builder: they apply to the element and its spans.</summary>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct ParagraphElement
{
    private readonly BuildContext? _context;
    private readonly ParagraphNode? _paragraph;

    internal ParagraphElement(BuildContext context, ParagraphNode paragraph)
    {
        _context = context;
        _paragraph = paragraph;
    }

    private ParagraphNode Node
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _paragraph!;
        }
    }

    private Style Local
    {
        get => Node.Local ?? Flow.Style.Empty;
        set => _paragraph!.Local = value;
    }

    /// <summary>Uses the bold face.</summary>
    /// <returns>This builder.</returns>
    public ParagraphElement Bold()
    {
        Local = Local with { Bold = true };
        return this;
    }

    /// <summary>Uses the italic face.</summary>
    /// <returns>This builder.</returns>
    public ParagraphElement Italic()
    {
        Local = Local with { Italic = true };
        return this;
    }

    /// <summary>Underlines the text.</summary>
    /// <returns>This builder.</returns>
    public ParagraphElement Underline()
    {
        Local = Local with { Underline = true };
        return this;
    }

    /// <summary>Strikes the text through.</summary>
    /// <returns>This builder.</returns>
    public ParagraphElement StrikeThrough()
    {
        Local = Local with { StrikeThrough = true };
        return this;
    }

    /// <summary>Sets the font size.</summary>
    /// <param name="size">The size in points.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive and finite.</exception>
    public ParagraphElement FontSize(double size)
    {
        Local = Local with { FontSize = size };
        return this;
    }

    /// <summary>Sets the font family.</summary>
    /// <param name="family">The family.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="family"/> is null.</exception>
    public ParagraphElement Font(FontFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        Local = Local with { FontFamily = family };
        return this;
    }

    /// <summary>Sets the text color.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="color"/> is a pattern.</exception>
    public ParagraphElement Color(PdfColor color)
    {
        Local = Local with { Color = color };
        return this;
    }

    /// <summary>Aligns the lines to the left edge.</summary>
    /// <returns>This builder.</returns>
    public ParagraphElement AlignLeft() => Align(TextAlignment.Left);

    /// <summary>Centers the lines.</summary>
    /// <returns>This builder.</returns>
    public ParagraphElement AlignCenter() => Align(TextAlignment.Center);

    /// <summary>Aligns the lines to the right edge.</summary>
    /// <returns>This builder.</returns>
    public ParagraphElement AlignRight() => Align(TextAlignment.Right);

    /// <summary>Justifies every line but the last.</summary>
    /// <returns>This builder.</returns>
    public ParagraphElement Justify() => Align(TextAlignment.Justify);

    /// <summary>Sets the distance between baselines as a multiple of the font size.</summary>
    /// <param name="multiple">The multiple.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="multiple"/> is not positive and finite.</exception>
    public ParagraphElement LineHeight(double multiple)
    {
        Local = Local with { LineHeight = multiple };
        return this;
    }

    /// <summary>Indents the first line.</summary>
    /// <param name="indent">The indent.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="indent"/> is negative.</exception>
    public ParagraphElement FirstLineIndent(Length indent)
    {
        Local = Local with { FirstLineIndent = indent };
        return this;
    }

    /// <summary>Sets the space above the element (dropped at the top of a page).</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public ParagraphElement SpaceBefore(Length space)
    {
        Local = Local with { SpaceBefore = space };
        return this;
    }

    /// <summary>Sets the space below the element.</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public ParagraphElement SpaceAfter(Length space)
    {
        Local = Local with { SpaceAfter = space };
        return this;
    }

    /// <summary>Sets the element's natural language, written as <c>Lang</c> when it differs from its parent's.</summary>
    /// <param name="language">A BCP 47 language tag.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="language"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.9.2.</remarks>
    public ParagraphElement Language(string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        Local = Local with { Language = language };
        return this;
    }

    /// <summary>Applies a partial style to the element; its set properties win over earlier modifiers.</summary>
    /// <param name="style">The style.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public ParagraphElement Style(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Local = Local.Merge(style);
        return this;
    }

    /// <summary>Keeps the element on the same page as the element after it (a heading with its first paragraph).</summary>
    /// <returns>This builder.</returns>
    public ParagraphElement KeepWithNext()
    {
        Node.KeepWithNext = true;
        return this;
    }

    /// <summary>Keeps the element's lines on one page when they fit on one.</summary>
    /// <returns>This builder.</returns>
    public ParagraphElement KeepTogether()
    {
        Node.KeepTogether = true;
        return this;
    }

    /// <summary>Names the element, so links and page references can point at it.</summary>
    /// <param name="id">The name, unique in the document.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §12.3.2.4 (named destinations).</remarks>
    public ParagraphElement Id(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Node.Id = id;
        return this;
    }

    private ParagraphElement Align(TextAlignment alignment)
    {
        Local = Local with { Alignment = alignment };
        return this;
    }
}

/// <summary>The items of a list in the document builder.</summary>
/// <remarks>ISO 32000-2 §14.8.4.7.2 (<c>LI</c>, <c>Lbl</c>, <c>LBody</c>).</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct ListBuilder
{
    private readonly BuildContext? _context;
    private readonly ListNode? _list;

    internal ListBuilder(BuildContext context, ListNode list)
    {
        _context = context;
        _list = list;
    }

    /// <summary>Adds an item of plain text.</summary>
    /// <param name="text">The text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public void Item(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _ = BuildContext.Check(_context);
        _list!.Items.Add(new ItemNode { Text = text });
    }

    /// <summary>Adds an item of any content: paragraphs, nested lists, tables, images.</summary>
    /// <param name="build">Describes the item's content.</param>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public void Item(Action<ContainerBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        BuildContext context = BuildContext.Check(_context);
        var item = new ItemNode { Content = new ContainerNode() };
        _list!.Items.Add(item);
        build(new ContainerBuilder(context, item.Content));
    }
}

/// <summary>A list's modifiers in the document builder: its numbering and the style its items inherit.</summary>
/// <remarks>ISO 32000-2 §14.8.5.5 (<c>ListNumbering</c>, written from the numbering).</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct ListElement
{
    private readonly BuildContext? _context;
    private readonly ListNode? _list;

    internal ListElement(BuildContext context, ListNode list)
    {
        _context = context;
        _list = list;
    }

    private ListNode Node
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _list!;
        }
    }

    /// <summary>Numbers the items 1., 2., 3. and so on.</summary>
    /// <returns>This builder.</returns>
    public ListElement Numbered() => Numbering(ListNumbering.Decimal);

    /// <summary>Sets how the items are numbered.</summary>
    /// <param name="numbering">The numbering.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="numbering"/> is not a defined value.</exception>
    public ListElement Numbering(ListNumbering numbering)
    {
        Node.Numbering = Enum.IsDefined(numbering) ? numbering : throw new ArgumentOutOfRangeException(nameof(numbering), numbering, "Unknown numbering.");
        return this;
    }

    /// <summary>Bullets the items with <paramref name="bullet"/>.</summary>
    /// <param name="bullet">The bullet text, such as <c>–</c>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="bullet"/> is null or empty.</exception>
    public ListElement Bullet(string bullet)
    {
        ArgumentException.ThrowIfNullOrEmpty(bullet);
        Node.Numbering = ListNumbering.Bullet;
        Node.Bullet = bullet;
        return this;
    }

    /// <summary>Sets the first number of a numbered list.</summary>
    /// <param name="first">The first number, at least 1.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="first"/> is less than 1.</exception>
    public ListElement StartAt(int first)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(first, 1);
        Node.StartAt = first;
        return this;
    }

    /// <summary>Sets the text before and after each number, such as <c>(</c> and <c>)</c>.</summary>
    /// <param name="prefix">The text before the number.</param>
    /// <param name="suffix">The text after the number.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ListElement Affixes(string prefix, string suffix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(suffix);
        Node.Prefix = prefix;
        Node.Suffix = suffix;
        return this;
    }

    /// <summary>Sets the indent of the items' content from the list's left edge; 18 points by default.</summary>
    /// <param name="indent">The indent.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="indent"/> is negative.</exception>
    public ListElement Indent(Length indent)
    {
        Node.Indent = indent.ToPoints() >= 0 ? indent : throw new ArgumentOutOfRangeException(nameof(indent), indent, "The indent shall not be negative.");
        return this;
    }

    /// <summary>Keeps the list on the same page as the element after it.</summary>
    /// <returns>This builder.</returns>
    public ListElement KeepWithNext()
    {
        Node.KeepWithNext = true;
        return this;
    }

    /// <summary>Keeps the whole list on one page when it fits on one.</summary>
    /// <returns>This builder.</returns>
    public ListElement KeepTogether()
    {
        Node.KeepTogether = true;
        return this;
    }

    /// <summary>Uses the bold face for the list's text.</summary>
    /// <returns>This builder.</returns>
    public ListElement Bold()
    {
        Node.Local = (_list!.Local ?? Flow.Style.Empty) with { Bold = true };
        return this;
    }

    /// <summary>Uses the italic face for the list's text.</summary>
    /// <returns>This builder.</returns>
    public ListElement Italic()
    {
        Node.Local = (_list!.Local ?? Flow.Style.Empty) with { Italic = true };
        return this;
    }

    /// <summary>Sets the font size of the list's text.</summary>
    /// <param name="size">The size in points.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive and finite.</exception>
    public ListElement FontSize(double size)
    {
        Node.Local = (_list!.Local ?? Flow.Style.Empty) with { FontSize = size };
        return this;
    }

    /// <summary>Sets the color of the list's text and markers.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="color"/> is a pattern.</exception>
    public ListElement Color(PdfColor color)
    {
        Node.Local = (_list!.Local ?? Flow.Style.Empty) with { Color = color };
        return this;
    }

    /// <summary>Sets the list's natural language, written as <c>Lang</c> when it differs from its parent's.</summary>
    /// <param name="language">A BCP 47 language tag.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="language"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.9.2.</remarks>
    public ListElement Language(string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        Node.Local = (_list!.Local ?? Flow.Style.Empty) with { Language = language };
        return this;
    }

    /// <summary>Applies a partial style that the list's items inherit.</summary>
    /// <param name="style">The style.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public ListElement Style(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Node.Local = (_list!.Local ?? Flow.Style.Empty).Merge(style);
        return this;
    }
}

/// <summary>An image's modifiers in the document builder: its size, alignment, spacing and caption.</summary>
/// <remarks>
/// ISO 32000-2 §8.9.5 (image XObjects), §14.8.4.6 (<c>Figure</c>) and §14.8.4.4 (<c>Caption</c>). With no size set, an image is drawn at
/// 72 pixels per inch; one wider than the space it is in, or taller than a page, is scaled down to fit.
/// </remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct ImageElement
{
    private readonly BuildContext? _context;
    private readonly ImageNode? _image;

    internal ImageElement(BuildContext context, ImageNode image)
    {
        _context = context;
        _image = image;
    }

    private ImageNode Node
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _image!;
        }
    }

    /// <summary>Sets the width; the height follows the image's aspect ratio unless set too.</summary>
    /// <param name="width">The width.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is not positive.</exception>
    public ImageElement Width(Length width)
    {
        Node.Width = Positive(width, nameof(width));
        return this;
    }

    /// <summary>Sets the height; the width follows the image's aspect ratio unless set too.</summary>
    /// <param name="height">The height.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is not positive.</exception>
    public ImageElement Height(Length height)
    {
        Node.Height = Positive(height, nameof(height));
        return this;
    }

    /// <summary>Sets the width and height.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A side is not positive.</exception>
    public ImageElement Size(Length width, Length height) => Width(width).Height(height);

    /// <summary>Scales the image to the width of the space it is in.</summary>
    /// <returns>This builder.</returns>
    public ImageElement FillWidth()
    {
        Node.FillWidth = true;
        return this;
    }

    /// <summary>Places the image at the left edge (the default).</summary>
    /// <returns>This builder.</returns>
    public ImageElement AlignLeft() => Align(TextAlignment.Left);

    /// <summary>Centers the image.</summary>
    /// <returns>This builder.</returns>
    public ImageElement AlignCenter() => Align(TextAlignment.Center);

    /// <summary>Places the image at the right edge.</summary>
    /// <returns>This builder.</returns>
    public ImageElement AlignRight() => Align(TextAlignment.Right);

    /// <summary>Sets the space above the image.</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public ImageElement SpaceBefore(Length space)
    {
        Node.Local = (Node.Local ?? Style.Empty) with { SpaceBefore = space };
        return this;
    }

    /// <summary>Sets the space below the image.</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public ImageElement SpaceAfter(Length space)
    {
        Node.Local = (Node.Local ?? Style.Empty) with { SpaceAfter = space };
        return this;
    }

    /// <summary>Keeps the image (and its caption) on the same page as the element after it.</summary>
    /// <returns>This builder.</returns>
    public ImageElement KeepWithNext()
    {
        Node.KeepWithNext = true;
        return this;
    }

    /// <summary>Adds a caption below the image in the theme's caption style, tagged as the figure's <c>Caption</c>; the image stays on its caption's page.</summary>
    /// <param name="caption">The caption.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="caption"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.8.4.4, Table 368.</remarks>
    public ImageElement Caption(string caption)
    {
        ArgumentException.ThrowIfNullOrEmpty(caption);
        Node.Caption = caption;
        return this;
    }

    private ImageElement Align(TextAlignment alignment)
    {
        Node.Alignment = alignment;
        return this;
    }

    private static Length Positive(Length value, string name) =>
        value.ToPoints() > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "The size shall be positive.");
}

/// <summary>A group's modifiers in the document builder: the style its content inherits.</summary>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct GroupElement
{
    private readonly BuildContext? _context;
    private readonly GroupNode? _group;

    internal GroupElement(BuildContext context, GroupNode group)
    {
        _context = context;
        _group = group;
    }

    private Style Scope
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _group!.Scope ?? Flow.Style.Empty;
        }

        set => _group!.Scope = value;
    }

    /// <summary>Keeps the group's content on one page when it fits on one.</summary>
    /// <returns>This builder.</returns>
    public GroupElement KeepTogether()
    {
        _ = Scope;
        _group!.KeepTogether = true;
        return this;
    }

    /// <summary>Keeps the group on the same page as the element after it.</summary>
    /// <returns>This builder.</returns>
    public GroupElement KeepWithNext()
    {
        _ = Scope;
        _group!.KeepWithNext = true;
        return this;
    }

    /// <summary>Tags the group as one structure element of <paramref name="structureType"/> (such as <c>Div</c>, <c>Sect</c> or <c>Aside</c>); without it the group adds no element.</summary>
    /// <param name="structureType">The structure type.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="structureType"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.8.4.4 (grouping elements).</remarks>
    public GroupElement Role(string structureType)
    {
        ArgumentException.ThrowIfNullOrEmpty(structureType);
        _ = Scope;
        _group!.Role = structureType;
        return this;
    }

    /// <summary>Uses the bold face for the group's text.</summary>
    /// <returns>This builder.</returns>
    public GroupElement Bold()
    {
        Scope = Scope with { Bold = true };
        return this;
    }

    /// <summary>Uses the italic face for the group's text.</summary>
    /// <returns>This builder.</returns>
    public GroupElement Italic()
    {
        Scope = Scope with { Italic = true };
        return this;
    }

    /// <summary>Sets the font size of the group's text.</summary>
    /// <param name="size">The size in points.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive and finite.</exception>
    public GroupElement FontSize(double size)
    {
        Scope = Scope with { FontSize = size };
        return this;
    }

    /// <summary>Sets the font family of the group's text.</summary>
    /// <param name="family">The family.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="family"/> is null.</exception>
    public GroupElement Font(FontFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        Scope = Scope with { FontFamily = family };
        return this;
    }

    /// <summary>Sets the color of the group's text.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="color"/> is a pattern.</exception>
    public GroupElement Color(PdfColor color)
    {
        Scope = Scope with { Color = color };
        return this;
    }

    /// <summary>Aligns the group's lines to the right edge.</summary>
    /// <returns>This builder.</returns>
    public GroupElement AlignRight()
    {
        Scope = Scope with { Alignment = TextAlignment.Right };
        return this;
    }

    /// <summary>Centers the group's lines.</summary>
    /// <returns>This builder.</returns>
    public GroupElement AlignCenter()
    {
        Scope = Scope with { Alignment = TextAlignment.Center };
        return this;
    }

    /// <summary>Sets the natural language of the group's content.</summary>
    /// <param name="language">A BCP 47 language tag.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="language"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.9.2.</remarks>
    public GroupElement Language(string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        Scope = Scope with { Language = language };
        return this;
    }

    /// <summary>Applies a partial style that the group's content inherits (its text properties; space before and after are not inherited).</summary>
    /// <param name="style">The style.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public GroupElement Style(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Scope = Scope.Merge(style);
        return this;
    }
}

/// <summary>A rule's modifiers in the document builder.</summary>
/// <remarks>ISO 32000-2 §14.8.2.2: a rule is a layout artifact.</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct RuleElement
{
    private readonly BuildContext? _context;
    private readonly RuleNode? _rule;

    internal RuleElement(BuildContext context, RuleNode rule)
    {
        _context = context;
        _rule = rule;
    }

    private RuleNode Node
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _rule!;
        }
    }

    /// <summary>Sets the line width; 0.5 points by default.</summary>
    /// <param name="thickness">The width.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="thickness"/> is not positive.</exception>
    public RuleElement Thickness(Length thickness)
    {
        Node.Thickness = thickness.ToPoints() > 0 ? thickness : throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "The thickness shall be positive.");
        return this;
    }

    /// <summary>Sets the color; the theme's border color by default.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This builder.</returns>
    public RuleElement Color(PdfColor color)
    {
        Node.Color = color;
        return this;
    }

    /// <summary>Sets the space above the rule; 6 points by default.</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public RuleElement SpaceBefore(Length space)
    {
        Node.Local = (Node.Local ?? Style.Empty) with { SpaceBefore = space };
        return this;
    }

    /// <summary>Sets the space below the rule; 6 points by default.</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public RuleElement SpaceAfter(Length space)
    {
        Node.Local = (Node.Local ?? Style.Empty) with { SpaceAfter = space };
        return this;
    }
}
