using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// The paginator one <see cref="FlowDocument.Render"/> call runs: the current page, its canvas and content area, and a cursor moving
/// down from the top margin. Blocks ask it for room (<see cref="EnsureRoom"/>) before drawing a line and move the cursor
/// (<see cref="Advance"/>); it starts a page when a line would cross the bottom margin. Page templates, headers and footers (#178)
/// hook <see cref="StartPage"/> and <see cref="FinishPage"/>; tables (#177) use the same room and cursor calls per row.
/// </summary>
internal sealed class FlowContext : IDisposable
{
    /// <summary>Slack for rounding when a line ends exactly at the bottom margin.</summary>
    private const double Tolerance = 1e-9;

    private readonly PdfDocument _document;
    private readonly FlowDocument _flow;
    private readonly List<PdfPage> _pages = [];
    private FlowBlock? _block;
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

    /// <summary>Starts a block: its portions on each page are reported to the structure sink.</summary>
    public void BeginBlock(FlowBlock block)
    {
        _block = block;
        if (_page is not null)
        {
            Services.Structure?.BeginBlock(block, _page, _canvas!);
        }
    }

    /// <summary>Ends the current block.</summary>
    public void EndBlock()
    {
        if (_block is not null && _page is not null)
        {
            Services.Structure?.EndBlock(_block, _page, _canvas!);
        }

        _block = null;
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
        FlowBlock? block = _block;
        EndBlock();
        FinishPage();
        StartPage();
        if (block is not null)
        {
            BeginBlock(block);
        }
    }

    /// <summary>Finishes the last page.</summary>
    public void Dispose()
    {
        EndBlock();
        FinishPage();
    }

    private void EnsurePage()
    {
        if (_page is null)
        {
            StartPage();
            if (_block is not null)
            {
                Services.Structure?.BeginBlock(_block, _page!, _canvas!);
            }
        }
    }

    /// <summary>Appends a page with the setup for its index and opens its canvas. Page templates (#178) draw backgrounds and headers here.</summary>
    private void StartPage()
    {
        PageSetup setup = _flow.PageSetupFor(_pages.Count);
        _page = _document.Pages.Add(new PdfRectangle(0, 0, setup.Width, setup.Height));
        _pages.Add(_page);
        _canvas = _page.CreateCanvas();
        Left = setup.Margins.Left;
        Right = setup.Width - setup.Margins.Right;
        Bottom = setup.Margins.Bottom;
        CursorY = setup.Height - setup.Margins.Top;
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
