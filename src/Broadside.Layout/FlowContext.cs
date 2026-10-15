using Broadside.Annotations;
using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// The paginator one <see cref="FlowDocument.Render"/> call runs: the current page, its canvas and content area, and a cursor moving
/// down from the top margin. Blocks ask it for room (<see cref="EnsureRoom"/>) before drawing a line and move the cursor
/// (<see cref="Advance"/>); it starts a page when a line would cross the bottom margin. Page templates, headers and footers (#178)
/// hook <see cref="StartPage"/> and <see cref="FinishPage"/>; tables (#177) use the same room and cursor calls per row.
/// Blocks nest (a list item holds paragraphs and lists): open blocks and parts form a stack that a page break ends innermost first
/// and begins again outermost first, and <see cref="PushIndent"/> narrows the content area for nested content on every page.
/// </summary>
internal sealed class FlowContext : IDisposable
{
    /// <summary>Slack for rounding when a line ends exactly at the bottom margin.</summary>
    private const double Tolerance = 1e-9;

    private readonly PdfDocument _document;
    private readonly FlowDocument _flow;
    private readonly List<PdfPage> _pages = [];
    private readonly List<object> _open = [];
    private readonly Stack<double> _indents = new();
    private readonly List<ListMarker> _markers = [];
    private PdfPage? _page;
    private PdfCanvas? _canvas;

    public FlowContext(PdfDocument document, FlowDocument flow)
    {
        _document = document;
        _flow = flow;
        Services = flow.Services;
    }

    /// <summary>The replaceable layout steps.</summary>
    public FlowServices Services { get; }

    /// <summary>The pages created so far, in order.</summary>
    public IReadOnlyList<PdfPage> Pages => _pages;

    /// <summary>The canvas of the current page; starts the first page when none is open.</summary>
    public PdfCanvas Canvas
    {
        get
        {
            EnsurePage();
            return _canvas!;
        }
    }

    /// <summary>The current page; starts the first page when none is open.</summary>
    public PdfPage Page
    {
        get
        {
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
    public double WordSpacing { get; set; }

    /// <summary>Starts a block: its portions on each page are reported to the structure sink. Blocks nest inside open blocks and parts.</summary>
    public void BeginBlock(FlowBlock block)
    {
        _open.Add(block);
        if (_page is not null)
        {
            Begin(block);
        }
    }

    /// <summary>Ends the innermost open block.</summary>
    public void EndBlock()
    {
        if (_open.Count > 0 && _open[^1] is FlowBlock)
        {
            if (_page is not null)
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
        if (_page is not null)
        {
            Begin(part);
        }
    }

    /// <summary>Ends the innermost open part.</summary>
    public void EndPart()
    {
        if (_open.Count > 0 && _open[^1] is FlowPart)
        {
            if (_page is not null)
            {
                End(_open[^1]);
            }

            _open.RemoveAt(_open.Count - 1);
        }
    }

    /// <summary>Reports an annotation added for <paramref name="part"/> to the structure sink.</summary>
    public void AddAnnotation(FlowPart part, PdfAnnotation annotation) => Services.Structure?.AddAnnotation(part, annotation);

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
    }

    /// <summary>Finishes the current page and starts the next, carrying the open block over.</summary>
    public void BreakPage()
    {
        if (_page is not null)
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

    /// <summary>Finishes the last page.</summary>
    public void Dispose()
    {
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
    }

    private void EnsurePage()
    {
        if (_page is null)
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
            Services.Structure?.BeginBlock(block, _page!, _canvas!);
        }
        else
        {
            Services.Structure?.BeginPart((FlowPart)open, _page!, _canvas!);
        }
    }

    private void End(object open)
    {
        if (open is FlowBlock block)
        {
            Services.Structure?.EndBlock(block, _page!, _canvas!);
        }
        else
        {
            Services.Structure?.EndPart((FlowPart)open, _page!, _canvas!);
        }
    }

    /// <summary>Appends a page with the setup for its index and opens its canvas. Page templates (#178) draw backgrounds and headers here.</summary>
    private void StartPage()
    {
        PageSetup setup = _flow.PageSetupFor(_pages.Count);
        _page = _document.Pages.Add(new PdfRectangle(0, 0, setup.Width, setup.Height));
        _pages.Add(_page);
        _canvas = _page.CreateCanvas();
        Left = setup.Margins.Left + Indent;
        Right = setup.Width - setup.Margins.Right;
        Bottom = setup.Margins.Bottom;
        Top = setup.Height - setup.Margins.Top;
        CursorY = Top;
        AtPageTop = true;
        WordSpacing = 0;
    }

    /// <summary>Closes the current page's canvas. Footers and "page n of N" fields (#178) are completed here or after the last page.</summary>
    private void FinishPage()
    {
        _canvas?.Dispose();
        _canvas = null;
        _page = null;
    }
}

/// <summary>
/// A list item's marker waiting for the item's first line: its text and style, the indent of the item's content from the left
/// margin, and the gap between the marker's right edge and that content edge.
/// </summary>
internal readonly record struct ListMarker(ListItem Item, string Text, TextStyle Style, double Indent, double Gap);
