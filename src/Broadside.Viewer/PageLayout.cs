using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// Where every page of a document sits in a viewer: pages grouped into spreads, spreads in a strip with gaps and padding, each page turned
/// by its own <c>Rotate</c> plus the user's rotation; and the exact mapping between viewport pixels, layout points and each page's default
/// user space, the zooms that fit a viewport, and the pages and tiles a viewport shows.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.7.2 (Table 29, <c>PageLayout</c>), §7.7.3.3 (<c>Rotate</c>), §8.3.2.3 (default user space) and §14.11.2 (the crop box
/// is the region displayed; <c>UserUnit</c>). Spec #33 story 54 (viewer capability 14).
/// </para>
/// <para>
/// Layout space is in points with y down, the same at every zoom: a page's box is its crop box turned and scaled by its user unit. A
/// <see cref="Viewport"/> shows it at <see cref="Viewport.Zoom"/> pixels per point; a page's pixels are those of a render of the whole
/// page at that scale and the layout's <see cref="Rotation"/> (<see cref="Tile.Region"/>), so the mapping here agrees with the tiles a
/// <see cref="DocumentSession"/> renders. A layout is immutable and safe to use from any thread; the per-frame queries
/// (<see cref="GetVisiblePages"/>, <see cref="GetVisibleTiles"/>, the mappings) allocate nothing and search the spreads in logarithmic time.
/// </para>
/// </remarks>
public sealed class PageLayout
{
    private const double Tolerance = 1e-6;

    private readonly PageMetrics[] _pages;
    private readonly int[] _rotations;
    private readonly LayoutRectangle[] _bounds;
    private readonly int[] _spreadOf;
    private readonly int[] _spreadFirst;
    private readonly LayoutRectangle[] _spreads;
    private readonly bool _horizontal;
    private readonly bool _paged;

    private PageLayout(ReadOnlySpan<PageMetrics> pages, PageLayoutOptions options)
    {
        Mode = options.Mode;
        ShowCover = options.ShowCover && Mode is PageLayoutMode.TwoUp or PageLayoutMode.TwoUpContinuous;
        Gap = options.Gap;
        Padding = options.Padding;
        Rotation = options.Rotation;
        _horizontal = Mode == PageLayoutMode.Horizontal;
        _paged = Mode is PageLayoutMode.SinglePage or PageLayoutMode.TwoUp;

        int count = pages.Length;
        _pages = pages.ToArray();
        _rotations = new int[count];
        _bounds = new LayoutRectangle[count];
        _spreadOf = new int[count];
        var widths = new double[count];
        var heights = new double[count];
        for (int i = 0; i < count; i++)
        {
            PageMetrics page = _pages[i];
            if (page.Index != i)
            {
                throw new ArgumentException($"The metrics at position {i} are of page {page.Index}; pass every page in order.", nameof(pages));
            }

            int rotation = (((page.Rotation + Rotation) % 360) + 360) % 360;
            bool quarter = rotation is 90 or 270;
            _rotations[i] = rotation;
            widths[i] = (quarter ? page.CropBox.Height : page.CropBox.Width) * page.UserUnit;
            heights[i] = (quarter ? page.CropBox.Width : page.CropBox.Height) * page.UserUnit;
        }

        bool twoUp = Mode is PageLayoutMode.TwoUp or PageLayoutMode.TwoUpContinuous;
        int spreadCount = !twoUp ? count : ShowCover ? 1 + (count / 2) : (count + 1) / 2;
        if (count == 0)
        {
            spreadCount = 0;
        }

        _spreadFirst = new int[spreadCount + 1];
        _spreads = new LayoutRectangle[spreadCount];
        for (int s = 0, page = 0; s < spreadCount; s++)
        {
            _spreadFirst[s] = page;
            page += twoUp && !(ShowCover && s == 0) ? 2 : 1;
            _spreadFirst[s + 1] = Math.Min(page, count);
        }

        // Across the strip: pages centred on one axis, or two-up pages meeting at a spine (a lone cover on its right, a lone last page on its left).
        double leftSlot = 0;
        double rightSlot = 0;
        double across = 0;
        for (int i = 0; i < count; i++)
        {
            across = Math.Max(across, _horizontal ? heights[i] : widths[i]);
            if (twoUp)
            {
                if (IsRightSlot(i))
                {
                    rightSlot = Math.Max(rightSlot, widths[i]);
                }
                else
                {
                    leftSlot = Math.Max(leftSlot, widths[i]);
                }
            }
        }

        double halfGap = leftSlot > 0 && rightSlot > 0 ? Gap / 2 : 0;
        if (twoUp)
        {
            across = leftSlot + rightSlot + (2 * halfGap);
        }

        double spine = Padding + leftSlot + halfGap;
        double along = Padding;
        for (int s = 0; s < spreadCount; s++)
        {
            double extent = 0;
            for (int i = _spreadFirst[s]; i < _spreadFirst[s + 1]; i++)
            {
                extent = Math.Max(extent, _horizontal ? widths[i] : heights[i]);
            }

            for (int i = _spreadFirst[s]; i < _spreadFirst[s + 1]; i++)
            {
                _spreadOf[i] = s;
                double w = widths[i];
                double h = heights[i];
                _bounds[i] = _horizontal
                    ? new LayoutRectangle(along, Padding + ((across - h) / 2), w, h)
                    : new LayoutRectangle(
                        twoUp ? (IsRightSlot(i) ? spine + halfGap : spine - halfGap - w) : Padding + ((across - w) / 2),
                        along + ((extent - h) / 2),
                        w,
                        h);
            }

            _spreads[s] = _horizontal
                ? new LayoutRectangle(along, Padding, extent, across)
                : new LayoutRectangle(Padding, along, across, extent);
            along += extent + Gap;
        }

        double length = spreadCount == 0 ? 2 * Padding : along - Gap + Padding;
        double breadth = across + (2 * Padding);
        ContentWidth = _horizontal ? length : breadth;
        ContentHeight = _horizontal ? breadth : length;
    }

    /// <summary>Gets the arrangement.</summary>
    public PageLayoutMode Mode { get; }

    /// <summary>Gets whether the first page stands alone on the right (two-up modes only).</summary>
    public bool ShowCover { get; }

    /// <summary>Gets the space between spreads and between the pages of a spread, in points.</summary>
    public double Gap { get; }

    /// <summary>Gets the space around the content, in points.</summary>
    public double Padding { get; }

    /// <summary>Gets the user's rotation added to every page's own, in degrees clockwise (<see cref="Tile.Rotation"/> of the tiles).</summary>
    public int Rotation { get; }

    /// <summary>Gets the number of pages.</summary>
    public int PageCount => _pages.Length;

    /// <summary>Gets the number of spreads: groups of one or two pages side by side.</summary>
    public int SpreadCount => _spreads.Length;

    /// <summary>Gets the width of the content, padding included, in points: the scroll width is this times the zoom.</summary>
    public double ContentWidth { get; }

    /// <summary>Gets the height of the content, padding included, in points: the scroll height is this times the zoom.</summary>
    public double ContentHeight { get; }

    /// <summary>Lays out pages from their metrics.</summary>
    /// <param name="pages">The metrics of every page, in order (<see cref="PageMetrics.Index"/> equal to the position).</param>
    /// <param name="options">The arrangement; <see langword="null"/> means defaults (continuous).</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentException">A page's metrics are out of order.</exception>
    public static PageLayout Create(ReadOnlySpan<PageMetrics> pages, PageLayoutOptions? options = null) =>
        new(pages, options ?? new PageLayoutOptions());

    /// <summary>Lays out every page of a session's document, loading the metrics of pages not yet loaded (a remote document fetches them).</summary>
    /// <param name="session">The session.</param>
    /// <param name="options">The arrangement; <see langword="null"/> means defaults (continuous).</param>
    /// <param name="cancellationToken">Cancels the loading.</param>
    /// <returns>The layout.</returns>
    public static async ValueTask<PageLayout> CreateAsync(DocumentSession session, PageLayoutOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var pages = new PageMetrics[session.PageCount];
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i] = session.TryGetPageMetrics(i, out PageMetrics metrics)
                ? metrics
                : await session.GetPageMetricsAsync(i, cancellationToken).ConfigureAwait(false);
        }

        return Create(pages, options);
    }

    /// <summary>Returns the metrics a page was laid out from.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>The metrics.</returns>
    public PageMetrics GetPageMetrics(int pageIndex) => _pages[CheckPage(pageIndex)];

    /// <summary>Returns a page's box in layout points: its crop box turned by its rotation (<see cref="GetPageRotation"/>) and scaled by its user unit.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>The box, y down.</returns>
    public LayoutRectangle GetPageBounds(int pageIndex) => _bounds[CheckPage(pageIndex)];

    /// <summary>Returns the rotation a page is shown at: its own <c>Rotate</c> plus <see cref="Rotation"/>, as 0, 90, 180 or 270.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>The rotation in degrees clockwise.</returns>
    public int GetPageRotation(int pageIndex) => _rotations[CheckPage(pageIndex)];

    /// <summary>Returns the spread a page belongs to.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>The zero-based spread.</returns>
    public int GetSpread(int pageIndex) => _spreadOf[CheckPage(pageIndex)];

    /// <summary>Returns the pages of a spread.</summary>
    /// <param name="spreadIndex">The zero-based spread.</param>
    /// <returns>The first page and the number of pages (one or two).</returns>
    public (int FirstPage, int Count) GetSpreadPages(int spreadIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spreadIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(spreadIndex, SpreadCount);
        return (_spreadFirst[spreadIndex], _spreadFirst[spreadIndex + 1] - _spreadFirst[spreadIndex]);
    }

    /// <summary>
    /// Returns the box a spread occupies in layout points: its pages' extent along the strip, and across it the widest page (or, two-up,
    /// both slots beside the spine).
    /// </summary>
    /// <param name="spreadIndex">The zero-based spread.</param>
    /// <returns>The box, y down.</returns>
    public LayoutRectangle GetSpreadBounds(int spreadIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spreadIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(spreadIndex, SpreadCount);
        return _spreads[spreadIndex];
    }

    /// <summary>Returns the pixels of a whole-page render at a zoom: the rectangle every <see cref="Tile.Region"/> of the page lies in.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="zoom">Pixels per point.</param>
    /// <returns>The rectangle at the origin, rounded up as <see cref="RenderOptions.GetPixelRectangle"/> does.</returns>
    public PixelRectangle GetPagePixels(int pageIndex, double zoom)
    {
        CheckZoom(zoom);
        LayoutRectangle box = _bounds[CheckPage(pageIndex)];
        return new PixelRectangle(0, 0, Pixels(box.Width * zoom), Pixels(box.Height * zoom));
    }

    /// <summary>Computes a zoom for a viewport size: actual size, or the zoom at which a page's spread and its padding fit.</summary>
    /// <param name="mode">The zoom to compute.</param>
    /// <param name="viewportWidth">The viewport's width in pixels.</param>
    /// <param name="viewportHeight">The viewport's height in pixels.</param>
    /// <param name="pageIndex">The page whose spread is fitted.</param>
    /// <param name="actualSize">The pixels per point at actual size: 1 for 72 DPI pixels, 96 / 72 for CSS pixels, times the pixel ratio for device pixels.</param>
    /// <returns>Pixels per point.</returns>
    public double GetZoom(ZoomMode mode, double viewportWidth, double viewportHeight, int pageIndex, double actualSize = 1)
    {
        CheckZoom(actualSize);
        CheckPage(pageIndex);
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0 || !double.IsFinite(viewportHeight) || viewportHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth), "A viewport has a positive, finite size.");
        }

        LayoutRectangle spread = _spreads[_spreadOf[pageIndex]];
        double width = spread.Width + (2 * Padding);
        double height = spread.Height + (2 * Padding);
        double fitWidth = width > 0 ? viewportWidth / width : actualSize;
        return mode switch
        {
            ZoomMode.FitWidth => fitWidth,
            ZoomMode.FitPage => height > 0 ? Math.Min(fitWidth, viewportHeight / height) : fitWidth,
            _ => actualSize,
        };
    }

    /// <summary>
    /// Returns the transform from a page's default user space to viewport pixels: the device transform of <see cref="RenderOptions"/> at
    /// the viewport's zoom and the layout's rotation, moved to where the page shows. Its inverse maps viewport pixels to the page.
    /// </summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>The transform (ISO 32000-2 §8.3.2.3, §8.3.4).</returns>
    public Matrix GetPageTransform(in Viewport viewport, int pageIndex)
    {
        CheckPage(pageIndex);
        (double originX, double originY) = Origin(viewport);
        LayoutRectangle box = _bounds[pageIndex];
        PageMetrics page = _pages[pageIndex];
        double k = page.UserUnit * viewport.Zoom;
        PdfRectangle crop = page.CropBox;
        (double left, double bottom, double right, double top) = (crop.Left * k, crop.Bottom * k, crop.Right * k, crop.Top * k);
        double dx = originX + (box.X * viewport.Zoom);
        double dy = originY + (box.Y * viewport.Zoom);

        // As RenderOptions: X = (x - L)k, Y = (T - y)k, then turned clockwise inside the page's own bounds.
        return _rotations[pageIndex] switch
        {
            90 => new Matrix(0, k, k, 0, dx - bottom, dy - left),
            180 => new Matrix(-k, 0, 0, k, dx + right, dy - bottom),
            270 => new Matrix(0, -k, -k, 0, dx + top, dy + right),
            _ => new Matrix(k, 0, 0, -k, dx - left, dy + top),
        };
    }

    /// <summary>Maps a point in a page's default user space to viewport pixels.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="x">The x coordinate in default user space.</param>
    /// <param name="y">The y coordinate in default user space.</param>
    /// <returns>The viewport point.</returns>
    public PathPoint PageToViewport(in Viewport viewport, int pageIndex, double x, double y) => GetPageTransform(viewport, pageIndex).Transform(x, y);

    /// <summary>Maps a rectangle in a page's default user space (an annotation's or a link's <c>Rect</c>) to its bounds in viewport pixels.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="rectangle">The rectangle in default user space.</param>
    /// <returns>The viewport rectangle (the transform only turns by quarters, so it is exact).</returns>
    public LayoutRectangle PageToViewport(in Viewport viewport, int pageIndex, PdfRectangle rectangle)
    {
        Matrix transform = GetPageTransform(viewport, pageIndex);
        PathPoint a = transform.Transform(rectangle.Left, rectangle.Bottom);
        PathPoint b = transform.Transform(rectangle.Right, rectangle.Top);
        return new LayoutRectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }

    /// <summary>Maps viewport pixels to a page's default user space, whether or not the point is on the page.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="x">The viewport x.</param>
    /// <param name="y">The viewport y.</param>
    /// <returns>The point on the page.</returns>
    public PagePoint ViewportToPage(in Viewport viewport, int pageIndex, double x, double y)
    {
        CheckViewport(viewport);
        GetPageTransform(viewport, pageIndex).TryInvert(out Matrix inverse);
        PathPoint point = inverse.Transform(x, y);
        return new PagePoint(pageIndex, point.X, point.Y);
    }

    /// <summary>Finds the page under a viewport pixel (hit testing) and the point on it in default user space.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="x">The viewport x.</param>
    /// <param name="y">The viewport y.</param>
    /// <param name="point">The page and the point on it.</param>
    /// <returns><see langword="false"/> when the pixel is in a gap or the padding, or on a spread a paged mode hides.</returns>
    public bool TryGetPageAt(in Viewport viewport, double x, double y, out PagePoint point)
    {
        CheckViewport(viewport);
        point = default;
        if (SpreadCount == 0)
        {
            return false;
        }

        (double originX, double originY) = Origin(viewport);
        double layoutX = (x - originX) / viewport.Zoom;
        double layoutY = (y - originY) / viewport.Zoom;
        int spread = _paged ? CentreSpread(viewport, originX, originY) : SpreadAt(_horizontal ? layoutX : layoutY);
        for (int i = _spreadFirst[spread]; i < _spreadFirst[spread + 1]; i++)
        {
            if (_bounds[i].Contains(layoutX, layoutY))
            {
                point = ViewportToPage(viewport, i, x, y);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lists the pages a viewport shows, in order, and the pixels of each that show. A paged mode shows only the spread at the viewport's
    /// centre.
    /// </summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="destination">Receives the pages, as many as fit.</param>
    /// <param name="margin">Pixels added on every side of the viewport, for render-ahead.</param>
    /// <returns>The number of visible pages; when it is more than <paramref name="destination"/> holds, call again with more room.</returns>
    public int GetVisiblePages(in Viewport viewport, Span<VisiblePage> destination, double margin = 0)
    {
        var walker = new VisibleWalker(this, viewport, margin);
        int count = 0;
        while (walker.MoveNext())
        {
            if (count < destination.Length)
            {
                destination[count] = walker.Current;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Lists the tiles a viewport shows: each visible page cut into a grid of <paramref name="tileSize"/> pixel squares from its pixel
    /// origin (the last row and column clipped to the page), at the viewport's zoom and the layout's rotation, page by page in order.
    /// </summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="tileSize">The side of a tile in pixels.</param>
    /// <param name="destination">Receives the tiles, as many as fit.</param>
    /// <param name="margin">Pixels added on every side of the viewport, for render-ahead.</param>
    /// <returns>The number of tiles; when it is more than <paramref name="destination"/> holds, call again with more room.</returns>
    public int GetVisibleTiles(in Viewport viewport, int tileSize, Span<Tile> destination, double margin = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tileSize, 1);
        var walker = new VisibleWalker(this, viewport, margin);
        int count = 0;
        while (walker.MoveNext())
        {
            VisiblePage page = walker.Current;
            PixelRectangle whole = GetPagePixels(page.PageIndex, viewport.Zoom);
            PixelRectangle region = page.Region;
            int lastRow = (region.Y + region.Height - 1) / tileSize;
            int lastColumn = (region.X + region.Width - 1) / tileSize;
            for (int row = region.Y / tileSize; row <= lastRow; row++)
            {
                for (int column = region.X / tileSize; column <= lastColumn; column++)
                {
                    if (count < destination.Length)
                    {
                        int x = column * tileSize;
                        int y = row * tileSize;
                        var pixels = new PixelRectangle(x, y, Math.Min(tileSize, whole.Width - x), Math.Min(tileSize, whole.Height - y));
                        destination[count] = new Tile(page.PageIndex, viewport.Zoom, pixels) { Rotation = Rotation };
                    }

                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Returns the page a viewer reports as current: in the spread at the viewport's centre, the page nearest the centre across the strip.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <returns>The zero-based page, or -1 when the layout has no pages.</returns>
    public int GetCurrentPage(in Viewport viewport)
    {
        CheckViewport(viewport);
        if (SpreadCount == 0)
        {
            return -1;
        }

        (double originX, double originY) = Origin(viewport);
        int spread = CentreSpread(viewport, originX, originY);
        double centre = _horizontal ? ((viewport.Height / 2) - originY) / viewport.Zoom : ((viewport.Width / 2) - originX) / viewport.Zoom;
        int best = _spreadFirst[spread];
        double bestDistance = double.MaxValue;
        for (int i = _spreadFirst[spread]; i < _spreadFirst[spread + 1]; i++)
        {
            LayoutRectangle box = _bounds[i];
            (double low, double high) = _horizontal ? (box.Y, box.Bottom) : (box.X, box.Right);
            double distance = centre < low ? low - centre : centre > high ? centre - high : 0;
            if (distance < bestDistance)
            {
                (best, bestDistance) = (i, distance);
            }
        }

        return best;
    }

    /// <summary>Scrolls a viewport to a page: its spread's leading edge (less the padding) at the viewport's leading edge (in a paged mode a spread shorter than the viewport is centred instead), the page centred across the strip.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>The viewport scrolled, kept inside the content (<see cref="Clamp"/>).</returns>
    public Viewport ScrollTo(in Viewport viewport, int pageIndex)
    {
        CheckViewport(viewport);
        CheckPage(pageIndex);
        LayoutRectangle spread = _spreads[_spreadOf[pageIndex]];
        LayoutRectangle box = _bounds[pageIndex];
        double z = viewport.Zoom;
        double extent = _horizontal ? viewport.Width : viewport.Height;
        double start = (spread.Y - Padding) * z;
        double end = (spread.Bottom + Padding) * z;
        if (_horizontal)
        {
            start = (spread.X - Padding) * z;
            end = (spread.Right + Padding) * z;
        }

        // A paged spread shorter than the viewport is centred (the leading edge would show the spreads after it as well).
        double along = _paged && end - start < extent ? (start + end - extent) / 2 : start;
        return Clamp(_horizontal
            ? viewport with { X = along, Y = ((box.Y + (box.Height / 2)) * z) - (viewport.Height / 2) }
            : viewport with { X = ((box.X + (box.Width / 2)) * z) - (viewport.Width / 2), Y = along });
    }

    /// <summary>Snaps a viewport along the strip to the spread whose leading edge (less the padding) is nearest its leading edge.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <returns>The viewport snapped and kept inside the content, or unchanged when the layout has no pages.</returns>
    public Viewport Snap(in Viewport viewport)
    {
        CheckViewport(viewport);
        if (SpreadCount == 0)
        {
            return Clamp(viewport);
        }

        double offset = ((_horizontal ? viewport.X : viewport.Y) / viewport.Zoom) + Padding;
        int spread = LastStartingAtOrBefore(offset);
        if (spread < 0 || (spread + 1 < SpreadCount && Start(spread + 1) - offset < offset - Start(spread)))
        {
            spread++;
        }

        double snapped = (Start(spread) - Padding) * viewport.Zoom;
        return Clamp(_horizontal ? viewport with { X = snapped } : viewport with { Y = snapped });
    }

    /// <summary>Changes a viewport's zoom keeping the content under one viewport pixel (a pinch's or a wheel's focus) in place.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="zoom">The new zoom in pixels per point.</param>
    /// <param name="x">The viewport x of the focus.</param>
    /// <param name="y">The viewport y of the focus.</param>
    /// <returns>The viewport zoomed and kept inside the content (content smaller than the viewport is centred instead).</returns>
    public Viewport ZoomAt(in Viewport viewport, double zoom, double x, double y)
    {
        CheckViewport(viewport);
        CheckZoom(zoom);
        (double originX, double originY) = Origin(viewport);
        double layoutX = (x - originX) / viewport.Zoom;
        double layoutY = (y - originY) / viewport.Zoom;
        return Clamp(viewport with { X = (layoutX * zoom) - x, Y = (layoutY * zoom) - y, Zoom = zoom });
    }

    /// <summary>
    /// Keeps a viewport's scroll offsets inside the content: from 0 to the content's size at the zoom less the viewport's. In a paged mode
    /// (<see cref="PageLayoutMode.SinglePage"/>, <see cref="PageLayoutMode.TwoUp"/>) the range along the strip widens, when the first or last
    /// spread is shorter than the viewport, to the offsets that centre it, so the first and last spreads can be shown on their own.
    /// </summary>
    /// <param name="viewport">The viewport.</param>
    /// <returns>The viewport clamped.</returns>
    public Viewport Clamp(in Viewport viewport)
    {
        CheckViewport(viewport);
        (double minAlong, double maxAlong) = AlongRange(viewport);
        double maxX = Math.Max(0, (ContentWidth * viewport.Zoom) - viewport.Width);
        double maxY = Math.Max(0, (ContentHeight * viewport.Zoom) - viewport.Height);
        return _horizontal
            ? viewport with { X = Math.Clamp(viewport.X, minAlong, maxAlong), Y = Math.Clamp(viewport.Y, 0, maxY) }
            : viewport with { X = Math.Clamp(viewport.X, 0, maxX), Y = Math.Clamp(viewport.Y, minAlong, maxAlong) };
    }

    /// <summary>
    /// Moves a viewport from an earlier layout of the same pages to this one (real page sizes replacing placeholders, other options)
    /// so that it keeps showing what it showed: the current page of <paramref name="previous"/> keeps its top-left corner where it was
    /// in the viewport, at the same zoom.
    /// </summary>
    /// <param name="previous">The layout the viewport was in.</param>
    /// <param name="viewport">The viewport.</param>
    /// <returns>The viewport in this layout, kept inside the content (<see cref="Clamp"/>).</returns>
    /// <remarks>
    /// Spec #33 viewer capability 3. Without it a page that grows the column (a wider page arriving after page 1's placeholder size)
    /// would shift every page across the strip under fixed scroll offsets, and the page in view would slide out of it.
    /// </remarks>
    public Viewport Anchor(PageLayout previous, in Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(previous);
        CheckViewport(viewport);
        int page = previous.GetCurrentPage(viewport);
        if (page < 0 || page >= PageCount)
        {
            return Clamp(viewport);
        }

        (double originX, double originY) = previous.Origin(viewport);
        LayoutRectangle was = previous._bounds[page];
        LayoutRectangle box = _bounds[page];
        double zoom = viewport.Zoom;
        double x = (was.X * zoom) + originX; // the page's top-left corner in viewport pixels
        double y = (was.Y * zoom) + originY;
        return Clamp(viewport with { X = (box.X * zoom) - x, Y = (box.Y * zoom) - y });
    }

    /// <summary>The scroll offsets along the strip a viewport may take (see <see cref="Clamp"/>).</summary>
    internal (double Min, double Max) AlongRange(in Viewport viewport)
    {
        double extent = _horizontal ? viewport.Width : viewport.Height;
        double max = Math.Max(0, ((_horizontal ? ContentWidth : ContentHeight) * viewport.Zoom) - extent);
        if (!_paged || SpreadCount == 0)
        {
            return (0, max);
        }

        double zoom = viewport.Zoom;
        int last = SpreadCount - 1;
        double first = (((Start(0) - Padding) + (End(0) + Padding)) * zoom - extent) / 2;
        double trailing = (((Start(last) - Padding) + (End(last) + Padding)) * zoom - extent) / 2;
        return (Math.Min(0, first), Math.Max(max, trailing));
    }

    private static int Pixels(double extent) => Math.Max(1, (int)Math.Min(int.MaxValue / 4, Math.Ceiling(extent - Tolerance)));

    private static void CheckZoom(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "A zoom is a positive, finite number of pixels per point.");
        }
    }

    private static void CheckViewport(in Viewport viewport)
    {
        CheckZoom(viewport.Zoom);
        if (!double.IsFinite(viewport.X) || !double.IsFinite(viewport.Y) || !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height)
            || viewport.Width < 0 || viewport.Height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), viewport, "A viewport has finite offsets and a finite size, zero or more.");
        }
    }

    private int CheckPage(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
        return pageIndex;
    }

    private bool IsRightSlot(int pageIndex) => (pageIndex % 2 == 1) != ShowCover;

    private double Start(int spread) => _horizontal ? _spreads[spread].X : _spreads[spread].Y;

    private double End(int spread) => _horizontal ? _spreads[spread].Right : _spreads[spread].Bottom;

    /// <summary>The content's origin in viewport pixels: centred along an axis where it is smaller than the viewport (but not along a paged mode's strip).</summary>
    private (double X, double Y) Origin(in Viewport viewport)
    {
        double width = ContentWidth * viewport.Zoom;
        double height = ContentHeight * viewport.Zoom;
        // A paged mode never centres the content along the strip: its offsets reach the centred first and last spread instead (AlongRange).
        return (width < viewport.Width && !(_paged && _horizontal) ? (viewport.Width - width) / 2 : -viewport.X,
            height < viewport.Height && !(_paged && !_horizontal) ? (viewport.Height - height) / 2 : -viewport.Y);
    }

    private int CentreSpread(in Viewport viewport, double originX, double originY) =>
        SpreadAt(_horizontal ? ((viewport.Width / 2) - originX) / viewport.Zoom : ((viewport.Height / 2) - originY) / viewport.Zoom);

    /// <summary>The spread at a coordinate along the strip, or the nearer one when it falls in a gap.</summary>
    private int SpreadAt(double along)
    {
        int spread = LastStartingAtOrBefore(along);
        if (spread < 0)
        {
            return 0;
        }

        return spread + 1 < SpreadCount && along - End(spread) > Start(spread + 1) - along ? spread + 1 : spread;
    }

    private int LastStartingAtOrBefore(double along)
    {
        int low = 0;
        int high = SpreadCount - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (Start(middle) <= along)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return high;
    }

    private int FirstEndingAtOrAfter(double along)
    {
        int low = 0;
        int high = SpreadCount;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (End(middle) < along)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>Walks the visible pages without allocating.</summary>
    private ref struct VisibleWalker
    {
        private readonly PageLayout _layout;
        private readonly Viewport _viewport;
        private readonly double _originX;
        private readonly double _originY;
        private readonly double _margin;
        private readonly double _alongEnd;
        private readonly int _lastSpread;
        private int _page;
        private int _spread;

        public VisibleWalker(PageLayout layout, in Viewport viewport, double margin)
        {
            CheckViewport(viewport);
            if (!double.IsFinite(margin) || margin < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(margin), margin, "A margin is a finite number of pixels, zero or more.");
            }

            _layout = layout;
            _viewport = viewport;
            _margin = margin;
            (_originX, _originY) = layout.Origin(viewport);
            Current = default;
            double zoom = viewport.Zoom;
            double alongStart = layout._horizontal ? (-margin - _originX) / zoom : (-margin - _originY) / zoom;
            _alongEnd = layout._horizontal ? (viewport.Width + margin - _originX) / zoom : (viewport.Height + margin - _originY) / zoom;
            if (layout.SpreadCount == 0)
            {
                (_spread, _lastSpread) = (0, -1);
            }
            else if (layout._paged)
            {
                _spread = _lastSpread = layout.CentreSpread(viewport, _originX, _originY);
            }
            else
            {
                (_spread, _lastSpread) = (layout.FirstEndingAtOrAfter(alongStart), layout.SpreadCount - 1);
            }

            _page = _spread <= _lastSpread ? layout._spreadFirst[_spread] : 0;
        }

        public VisiblePage Current { get; private set; }

        public bool MoveNext()
        {
            PageLayout layout = _layout;
            double zoom = _viewport.Zoom;
            while (_spread <= _lastSpread)
            {
                if (_page >= layout._spreadFirst[_spread + 1])
                {
                    _spread++;
                    if (_spread > _lastSpread || layout.Start(_spread) > _alongEnd)
                    {
                        _spread = _lastSpread + 1;
                        return false;
                    }

                    continue;
                }

                int page = _page++;
                LayoutRectangle box = layout._bounds[page];
                double x = _originX + (box.X * zoom);
                double y = _originY + (box.Y * zoom);
                int width = Pixels(box.Width * zoom);
                int height = Pixels(box.Height * zoom);
                int left = Clip(Math.Floor(-_margin - x), width);
                int top = Clip(Math.Floor(-_margin - y), height);
                int right = Clip(Math.Ceiling(_viewport.Width + _margin - x), width);
                int bottom = Clip(Math.Ceiling(_viewport.Height + _margin - y), height);
                if (right > left && bottom > top)
                {
                    Current = new VisiblePage(page, new PixelRectangle(left, top, right - left, bottom - top), x, y);
                    return true;
                }
            }

            return false;
        }

        private static int Clip(double value, int limit) => value <= 0 ? 0 : value >= limit ? limit : (int)value;
    }
}
