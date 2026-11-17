using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// The paginator one <see cref="FlowDocument.Render"/> call runs: the current page, its canvas and content area, and a cursor moving
/// down from the top margin. Blocks ask it for room (<see cref="EnsureRoom"/>) before drawing a line and move the cursor
/// (<see cref="Advance"/>); it starts a page when a line would cross the bottom margin. Each page follows the current section's
/// <see cref="PageTemplate"/> (<see cref="StartSection"/>): <see cref="StartPage"/> paints its background and sets the body area,
/// and headers and footers are drawn at <see cref="Dispose"/> once the page count is known; tables (#177) use the same room and cursor calls per row.
/// Blocks nest (a list item holds paragraphs and lists): open blocks and parts form a stack that a page break ends innermost first
/// and begins again outermost first, and <see cref="PushIndent"/> narrows the content area for nested content on every page.
/// </summary>
internal sealed class FlowContext : IDisposable
{
    /// <summary>Slack for rounding when a line ends exactly at the bottom margin.</summary>
    private const double Tolerance = 1e-9;

    /// <summary>The height of a virtual page that never breaks.</summary>
    private const double Unbounded = 1e9;

    private readonly PdfDocument _document;
    private readonly FlowDocument _flow;
    private readonly List<PdfPage> _pages = [];
    private readonly List<object> _open = [];
    private readonly Stack<double> _indents = new();
    private readonly List<ListMarker> _markers = [];
    private readonly FlowContext? _parent;
    private readonly double[]? _heights;
    private readonly int _drawPage;
    private readonly double _regionLeft;
    private readonly List<DecoratedPage> _templated = [];
    private readonly List<Diagnostic> _diagnostics;
    private readonly bool _measuring;
    private readonly FlowReferences? _references;
    private PageTemplate _template;
    private bool _sectionStart = true;
    private bool _sectionHasContent;
    private int _section;
    private int _advancePage = -1;
    private int _measuredPages;
    private bool _measuredPageOpen;
    private PdfPage? _page;
    private PdfCanvas? _canvas;
    private int _virtualPage;
    private double _wordSpacing;

    public FlowContext(PdfDocument document, FlowDocument flow)
        : this(document, flow, flow.Services, [])
    {
    }

    public FlowContext(PdfDocument document, FlowDocument flow, FlowServices services, List<Diagnostic> diagnostics)
    {
        _document = document;
        _flow = flow;
        _template = flow.Template;
        Services = services;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// A paginator that resolves <paramref name="references"/> (anchors, links, page references): when <paramref name="measuring"/>, it
    /// only counts pages and places blocks, adding no page and drawing nothing, so a layout pass finds where the anchors land.
    /// </summary>
    public FlowContext(PdfDocument document, FlowDocument flow, FlowServices services, List<Diagnostic> diagnostics, FlowReferences? references, bool measuring)
        : this(document, flow, services, diagnostics)
    {
        _references = references;
        _measuring = measuring;
    }

    /// <summary>
    /// A region of <paramref name="parent"/> (a table cell): content laid out between <paramref name="left"/> and
    /// <paramref name="right"/> from <paramref name="top"/> down, on virtual pages whose content heights are
    /// <paramref name="heights"/> (the last repeating). Only virtual page <paramref name="drawPage"/> draws, on the parent's canvas
    /// at <paramref name="top"/>, and only while the parent draws; -1 draws nothing (measuring). A break moves to the next virtual
    /// page, never the parent's.
    /// </summary>
    public FlowContext(FlowContext parent, double left, double right, double top, double[] heights, int drawPage)
    {
        _parent = parent;
        _document = parent._document;
        _flow = parent._flow;
        _template = parent._template;
        Services = parent.Services;
        _diagnostics = parent._diagnostics;
        _references = parent._references;
        _heights = heights;
        _drawPage = drawPage;
        _regionLeft = left;
        Left = left;
        Right = right;
        Top = top;
        Bottom = top - heights[0];
        CursorY = top;
        ContentBottom = top;
    }

    /// <summary>The replaceable layout steps.</summary>
    public FlowServices Services { get; }

    /// <summary>What the layout found worth reporting, shared by a context and its regions.</summary>
    public List<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>The lowest the cursor has been after placed content (<see cref="Advance"/>); <see cref="Top"/> before any.</summary>
    public double ContentBottom { get; private set; }

    /// <summary>The virtual page of the first <see cref="Advance"/> since <see cref="WatchAdvance"/>, or -1 when none happened yet.</summary>
    public int FirstAdvancePage => _advancePage;

    /// <summary>The pages created so far, in order.</summary>
    public IReadOnlyList<PdfPage> Pages => _parent?.Pages ?? _pages;

    /// <summary>The number of pages started so far (virtual pages in a region): changes exactly when a page breaks.</summary>
    public int PageCount => _parent is null ? (_measuring ? _measuredPages : _pages.Count) : _virtualPage + 1;

    /// <summary>True when what blocks draw now lands on a page: at the root unless measuring, on its drawn virtual page in a region.</summary>
    public bool Drawing => _parent is null ? !_measuring : _virtualPage == _drawPage && _parent.Drawing;

    /// <summary>True when what blocks place now is where it lands on the page (drawn or, in a measuring pass, would be drawn).</summary>
    public bool Placing => _parent is null || (_virtualPage == _drawPage && _parent.Placing);

    /// <summary>The anchors, links and page references of the flow, or null when it has none.</summary>
    public FlowReferences? References => _references;

    /// <summary>The number of the flow's page that content placed now lands on, from 1.</summary>
    public int PageNumber => _parent?.PageNumber ?? PageCount;

    /// <summary>The content height of the page a break would start next.</summary>
    public double NextPageHeight
    {
        get
        {
            if (_parent is not null)
            {
                return HeightOf(_virtualPage + 1);
            }

            (double bottom, double top) = _template.BodyFor(PageCount + 1, !PageOpen && _sectionStart);
            return top - bottom;
        }
    }

    /// <summary>True when content placed on the first page of a region crossed its bottom (it did not fit there).</summary>
    public bool OverflowedFirstPage { get; private set; }

    /// <summary>The canvas of the current page; starts the first page when none is open.</summary>
    public PdfCanvas Canvas
    {
        get
        {
            if (_parent is not null)
            {
                return _parent.Canvas;
            }

            EnsurePage();
            return _canvas!;
        }
    }

    /// <summary>The current page; starts the first page when none is open.</summary>
    public PdfPage Page
    {
        get
        {
            if (_parent is not null)
            {
                return _parent.Page;
            }

            EnsurePage();
            return _page!;
        }
    }

    /// <summary>The top edge of the content area.</summary>
    public double Top { get; private set; }

    /// <summary>The sum of the open indents (<see cref="PushIndent"/>): how far <see cref="Left"/> is inside the left margin.</summary>
    public double Indent { get; private set; }

    /// <summary>True while a list marker waits for the first line of its item.</summary>
    public bool HasPendingMarkers => _markers.Count > 0;

    /// <summary>The left edge of the content area.</summary>
    public double Left { get; private set; }

    /// <summary>The right edge of the content area.</summary>
    public double Right { get; private set; }

    /// <summary>The bottom edge of the content area.</summary>
    public double Bottom { get; private set; }

    /// <summary>The width of the content area.</summary>
    public double Width => Right - Left;

    /// <summary>The y coordinate of the cursor: the top of the next line.</summary>
    public double CursorY { get; private set; }

    /// <summary>True when nothing has been placed on the current page yet (or no page is open), so space above is dropped.</summary>
    public bool AtPageTop { get; private set; } = true;

    /// <summary>The word spacing (<c>Tw</c>) the current page's content stream has set; 0 at the start of each page.</summary>
    public double WordSpacing
    {
        get => _parent?.WordSpacing ?? _wordSpacing;
        set
        {
            if (_parent is not null)
            {
                _parent.WordSpacing = value;
            }
            else
            {
                _wordSpacing = value;
            }
        }
    }

    /// <summary>
    /// Starts a block: its portions on each page are reported to the structure sink, and where it starts is recorded when it is an
    /// anchor (<see cref="FlowReferences"/>). Blocks nest inside open blocks and parts.
    /// </summary>
    public void BeginBlock(FlowBlock block)
    {
        if (_references is not null && Placing && _references.Tracks(block))
        {
            // An anchor needs a page, even for a block that has drawn nothing yet (a list before its first line).
            EnsurePage();
            _references.Place(block, PageNumber, Drawing ? Page : null, Left, CursorY);
        }

        _open.Add(block);
        if (Live)
        {
            Begin(block);
        }
    }

    /// <summary>Ends the innermost open block.</summary>
    public void EndBlock()
    {
        if (_open.Count > 0 && _open[^1] is FlowBlock)
        {
            if (Live)
            {
                End(_open[^1]);
            }

            _open.RemoveAt(_open.Count - 1);
        }
    }

    /// <summary>Starts a part inside the innermost open block or part (a list item, label or body, or an inline element).</summary>
    public void BeginPart(FlowPart part)
    {
        _open.Add(part);
        if (Live)
        {
            Begin(part);
        }
    }

    /// <summary>Ends the innermost open part.</summary>
    public void EndPart()
    {
        if (_open.Count > 0 && _open[^1] is FlowPart)
        {
            if (Live)
            {
                End(_open[^1]);
            }

            _open.RemoveAt(_open.Count - 1);
        }
    }

    /// <summary>Reports an annotation added for <paramref name="part"/> to the structure sink.</summary>
    public void AddAnnotation(FlowPart part, PdfAnnotation annotation) => Services.Structure?.AddAnnotation(part, annotation, null);

    /// <summary>
    /// Reports an annotation on <paramref name="page"/> for <paramref name="part"/> to the structure sink: the open block or part itself
    /// (a form field's widget), or its child (a link over a run).
    /// </summary>
    public void AddAnnotation(FlowPart part, PdfAnnotation annotation, PdfPage page) => Services.Structure?.AddAnnotation(part, annotation, page);

    /// <summary>The document the flow renders into.</summary>
    public PdfDocument Document => _document;

    /// <summary>Narrows the content area by <paramref name="amount"/> on the left, on this page and the next, until <see cref="PopIndent"/>.</summary>
    public void PushIndent(double amount)
    {
        _indents.Push(amount);
        Indent += amount;
        Left += amount;
    }

    /// <summary>Undoes the last <see cref="PushIndent"/>.</summary>
    public void PopIndent()
    {
        double amount = _indents.Pop();
        Indent -= amount;
        Left -= amount;
    }

    /// <summary>Queues a list marker to be drawn on the first line placed from now on (<see cref="PlaceMarkers"/>).</summary>
    public void AddMarker(ListMarker marker) => _markers.Add(marker);

    /// <summary>
    /// Draws the queued list markers on the line whose baseline is <paramref name="baseline"/>: each right-aligned to its gap before
    /// the content edge of its item, as a <c>Lbl</c> part. Blocks call it before beginning themselves at their first line.
    /// </summary>
    public void PlaceMarkers(double baseline)
    {
        if (_markers.Count == 0)
        {
            return;
        }

        if (!Drawing)
        {
            _markers.Clear();
            return;
        }

        PdfCanvas canvas = Canvas;
        double margin = Left - Indent;
        foreach (ListMarker marker in _markers)
        {
            TextStyle style = marker.Style;
            ShapedRun shaped = Services.Shaper.Shape(marker.Text, style);
            double right = margin + marker.Indent - marker.Gap;
            BeginPart(new FlowPart("Lbl", marker.Item, null));
            canvas.BeginText();
            canvas.SetFont(style.Font, style.FontSize);
            if (WordSpacing != 0)
            {
                canvas.SetWordSpacing(0);
                WordSpacing = 0;
            }

            if (style.Color is { } color)
            {
                canvas.SetFillColor(color);
            }

            canvas.SetTextMatrix(new Matrix(1, 0, 0, 1, right - Services.Shaper.Measure(marker.Text, style), baseline));
            canvas.ShowText(shaped.Glyphs, shaped.Adjustments);
            canvas.EndText();
            if (style.Color is not null)
            {
                canvas.SetFillColor(PdfColor.FromGray(0));
            }

            EndPart();
        }

        _markers.Clear();
    }

    /// <summary>Draws the queued list markers beside content whose top is at the cursor (an image), baselines at their ascent below it.</summary>
    public void PlaceMarkersAtTop()
    {
        if (_markers.Count == 0)
        {
            return;
        }

        double ascent = 0;
        foreach (ListMarker marker in _markers)
        {
            ascent = Math.Max(ascent, marker.Style.Ascent);
        }

        PlaceMarkers(CursorY - ascent);
    }

    /// <summary>Moves the cursor down by <paramref name="space"/> unless at the top of a page, where space is dropped.</summary>
    public void AddSpace(double space)
    {
        if (!AtPageTop)
        {
            CursorY -= space;
        }
    }

    /// <summary>Starts a new page when <paramref name="height"/> below the cursor would cross the bottom margin (never on an empty page).</summary>
    public void EnsureRoom(double height)
    {
        EnsurePage();
        if (!AtPageTop && CursorY - height < Bottom - Tolerance)
        {
            BreakPage();
        }
    }

    /// <summary>Moves the cursor down past placed content.</summary>
    public void Advance(double height)
    {
        EnsurePage();
        CursorY -= height;
        AtPageTop = false;
        _sectionHasContent = true;
        ContentBottom = Math.Min(ContentBottom, CursorY);
        if (_advancePage == -2)
        {
            _advancePage = _virtualPage;
        }

        if (_parent is not null && _virtualPage == 0 && CursorY < Bottom - Tolerance)
        {
            OverflowedFirstPage = true;
        }
    }

    /// <summary>Finishes the current page and starts the next, carrying the open block over.</summary>
    public void BreakPage()
    {
        if (_parent is not null)
        {
            if (Drawing)
            {
                for (int index = _open.Count - 1; index >= 0; index--)
                {
                    End(_open[index]);
                }
            }

            _virtualPage++;
            Left = _regionLeft + Indent;
            Bottom = Top - HeightOf(_virtualPage);
            CursorY = Top;
            ContentBottom = Top;
            AtPageTop = true;
            if (Drawing)
            {
                BeginOpen();
            }

            return;
        }

        if (Live)
        {
            for (int index = _open.Count - 1; index >= 0; index--)
            {
                End(_open[index]);
            }
        }

        FinishPage();
        StartPage();
        BeginOpen();
    }

    /// <summary>Makes <paramref name="template"/> the template of the pages from here on, breaking to a new page when one is open.</summary>
    public void StartSection(PageTemplate template)
    {
        _template = template;
        _sectionStart = true;
        _sectionHasContent = false;
        _section++;
        if (PageOpen)
        {
            BreakPage();
        }
    }

    /// <summary>Finishes the last page, then draws every page's header and footer now that the page count is known.</summary>
    public void Dispose()
    {
        if (_parent is not null)
        {
            return;
        }

        while (_open.Count > 0)
        {
            if (_open[^1] is FlowBlock)
            {
                EndBlock();
            }
            else
            {
                EndPart();
            }
        }

        FinishPage();
        PageDecorations.DrawHeadersAndFooters(_templated, Services.Shaper, _diagnostics, _document);
    }

    /// <summary>
    /// A <see cref="PageBreak"/>: ends the page, so what follows starts on a new one. Nothing happens before the first content of the
    /// flow or of a section (which starts on a new page anyway) or inside a region (a table cell or a column); a break right after
    /// another leaves a blank page.
    /// </summary>
    public void BreakPageExplicitly()
    {
        if (_parent is not null || !_sectionHasContent)
        {
            return;
        }

        BreakPage();
    }

    /// <summary>Starts recording the virtual page of the next <see cref="Advance"/> in <see cref="FirstAdvancePage"/>.</summary>
    public void WatchAdvance() => _advancePage = -2;

    /// <summary>
    /// Lays <paramref name="blocks"/> out in order, honouring <see cref="FlowBlock.KeepWithNext"/>: before each chain of blocks kept with
    /// the next, the page breaks when the chain and the first line of the block after it do not fit below the cursor but fit on a new page.
    /// </summary>
    public void LayOutBlocks(IReadOnlyList<FlowBlock> blocks)
    {
        int kept = 0;
        for (int index = 0; index < blocks.Count; index++)
        {
            if (index >= kept && blocks[index].KeepWithNext)
            {
                kept = KeepWithNext(blocks, index, blocks.Count);
            }

            blocks[index].LayOut(this);
        }
    }

    /// <summary>
    /// Breaks the page before the chain of blocks kept with the next that starts at <paramref name="start"/> when the chain and the first
    /// line of the block after it do not fit below the cursor but fit on a new page. Returns the index after the chain.
    /// </summary>
    public int KeepWithNext(IReadOnlyList<FlowBlock> blocks, int start, int end)
    {
        int next = start;
        while (next < end && blocks[next].KeepWithNext)
        {
            next++;
        }

        if (next >= end || AtPageTop)
        {
            return next;
        }

        double fresh = NextPageHeight;
        if (!ChainFits(blocks, start, next, [CursorY - Bottom, fresh], continuing: true) && ChainFits(blocks, start, next, [fresh, fresh], continuing: false))
        {
            BreakPage();
        }

        return next;
    }

    /// <summary>
    /// Lays <paramref name="blocks"/> out without drawing and returns the height they take from the cursor on one page, whatever its
    /// size: from the top of a page (space before the first block dropped) when <paramref name="continuing"/> is false, else below content.
    /// </summary>
    public double MeasureHeight(IReadOnlyList<FlowBlock> blocks, bool continuing)
    {
        using var region = new FlowContext(this, Left, Right, 0, [Unbounded], -1);
        if (continuing)
        {
            region.Advance(0);
        }

        foreach (FlowBlock block in blocks)
        {
            block.LayOut(region);
        }

        return -region.ContentBottom;
    }

    /// <summary>
    /// Lays blocks out without drawing from the cursor on virtual pages of <paramref name="heights"/>: true when every block from
    /// <paramref name="start"/> to <paramref name="next"/> stays on the first page and the first line of the block at <paramref name="next"/>
    /// lands there too.
    /// </summary>
    private bool ChainFits(IReadOnlyList<FlowBlock> blocks, int start, int next, double[] heights, bool continuing)
    {
        using var region = new FlowContext(this, Left, Right, 0, heights, -1);
        if (continuing)
        {
            region.Advance(0);
        }

        for (int index = start; index < next; index++)
        {
            blocks[index].LayOut(region);
            if (region.PageCount > 1 || region.OverflowedFirstPage)
            {
                return false;
            }
        }

        region.WatchAdvance();
        blocks[next].LayOut(region);
        // A next block that places nothing (a spacer) cannot be separated from.
        return region.FirstAdvancePage <= 0 && !region.OverflowedFirstPage;
    }

    /// <summary>True when structure hooks fire now: a page is open at the root, or this region draws.</summary>
    private bool Live => _parent is null ? _page is not null : Drawing;

    /// <summary>True when a page (a counted one, when measuring) is open at the root.</summary>
    private bool PageOpen => _page is not null || _measuredPageOpen;

    private double HeightOf(int virtualPage) => _heights![Math.Min(virtualPage, _heights.Length - 1)];

    private void EnsurePage()
    {
        if (_parent is null && !PageOpen)
        {
            StartPage();
            BeginOpen();
        }
    }

    private void BeginOpen()
    {
        foreach (object open in _open)
        {
            Begin(open);
        }
    }

    private void Begin(object open)
    {
        if (open is FlowBlock block)
        {
            Services.Structure?.BeginBlock(block, Page, Canvas);
        }
        else
        {
            Services.Structure?.BeginPart((FlowPart)open, Page, Canvas);
        }
    }

    private void End(object open)
    {
        if (open is FlowBlock block)
        {
            Services.Structure?.EndBlock(block, Page, Canvas);
        }
        else
        {
            Services.Structure?.EndPart((FlowPart)open, Page, Canvas);
        }
    }

    /// <summary>
    /// Appends a page of the current template, opens its canvas and paints the template's background and watermark first; the body
    /// area is the margins moved in past the page's header and footer, which are drawn after the last page.
    /// </summary>
    private void StartPage()
    {
        PageSetup setup = _template.Setup;
        bool first = _sectionStart;
        _sectionStart = false;
        if (_measuring)
        {
            _measuredPages++;
            _measuredPageOpen = true;
        }
        else
        {
            _page = _document.Pages.Add(new PdfRectangle(0, 0, setup.Width, setup.Height));
            _pages.Add(_page);
            _templated.Add(new DecoratedPage(_page, _template, first, _section));
            _canvas = _page.CreateCanvas();
            PageDecorations.DrawBackground(_canvas, _template, Services.Shaper);
        }

        (double bottom, double top) = _template.BodyFor(PageCount, first);
        Left = setup.Margins.Left + Indent;
        Right = setup.Width - setup.Margins.Right;
        Bottom = bottom;
        Top = top;
        CursorY = Top;
        ContentBottom = Top;
        AtPageTop = true;
        WordSpacing = 0;
    }

    /// <summary>Closes the current page's canvas.</summary>
    private void FinishPage()
    {
        _canvas?.Dispose();
        _canvas = null;
        _page = null;
        _measuredPageOpen = false;
    }
}

/// <summary>
/// A list item's marker waiting for the item's first line: its text and style, the indent of the item's content from the left
/// margin, and the gap between the marker's right edge and that content edge.
/// </summary>
internal readonly record struct ListMarker(ListItem Item, string Text, TextStyle Style, double Indent, double Gap);

/// <summary>A page a template decorates once the page count is known: the template, whether it is the first page of its section, and the section's index.</summary>
internal readonly record struct DecoratedPage(PdfPage Page, PageTemplate Template, bool First, int Section);
