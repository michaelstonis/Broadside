using Broadside.Annotations;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// What lies where on a page: the boxes of its shown character codes with their Unicode text in content order, the areas its images
/// and paths paint, and its link regions, queried by point and by rectangle in page space, so a viewer maps a click or a drag to
/// content (text selection, link activation). Built from a display list with <see cref="DisplayList.CreateHitTestIndex"/>, or with a
/// page's links by <see cref="PdfPage.CreateHitTestIndex()"/>.
/// </summary>
/// <remarks>
/// <para>
/// Viewer capability 4; ADR 0006 (the display list is what hit-testing reads). Page space is default user space (ISO 32000-2
/// §8.3.2.3), the space the display list and annotation rectangles are in, before the crop box, rotation and user unit: a viewer maps
/// a device point into it through the inverse of the transform it renders with (for the software rasterizer,
/// <c>RenderOptions.GetDeviceTransform</c>). Glyph boxes follow §9.4.4 and §9.8.1 (<see cref="HitTestGlyph"/>), image and path areas
/// §8.9.4, §8.5.3 and §8.5.4 (<see cref="HitTestShape"/>), link regions §12.5.6.5 (<see cref="HitTestLink"/>).
/// </para>
/// <para>
/// The items are bucketed on a uniform grid over the page when the index is built, a fixed number of arrays per page; a query by
/// point visits one cell, so it costs the few items there, and no query allocates. Matches come in order of kind (glyphs, images,
/// paths, links) and, within a kind, in content order: for glyphs the order they were shown, for images and paths the order they
/// were painted (the last one painted is on top), for links the order of the page's <c>Annots</c> array. Glyphs and links are not
/// cut by the clip.
/// </para>
/// <para>Immutable once built: any number of threads may query it at once.</para>
/// </remarks>
public sealed class HitTestIndex
{
    // An item covering more grid cells than this is kept apart and visited by every query, so large paints do not fill every cell.
    private const int MaxCellsPerItem = 16;
    private const int MaxCellsPerAxis = 256;

    private readonly DisplayTextGlyph[] _glyphs;
    private readonly char[] _text;
    private readonly HitTestShape[] _images;
    private readonly HitTestShape[] _paths;
    private readonly HitTestLink[] _links;

    // Items are numbered across kinds: glyphs, then images, paths and links.
    private readonly int _imageStart;
    private readonly int _pathStart;
    private readonly int _linkStart;
    private readonly int _count;

    // The grid: cell (column, row) holds _cellItems[_cellStart[cell].._cellStart[cell + 1]], ascending; _overflow holds the items
    // that are too large or not finite, ascending.
    private readonly double _left;
    private readonly double _bottom;
    private readonly double _right;
    private readonly double _top;
    private readonly double _columnScale;
    private readonly double _rowScale;
    private readonly int _columns;
    private readonly int _rows;
    private readonly int[] _cellStart;
    private readonly int[] _cellItems;
    private readonly int[] _overflow;

    private HitTestIndex(DisplayTextGlyph[] glyphs, char[] text, HitTestShape[] images, HitTestShape[] paths, HitTestLink[] links)
    {
        _glyphs = glyphs;
        _text = text;
        _images = images;
        _paths = paths;
        _links = links;
        _imageStart = glyphs.Length;
        _pathStart = _imageStart + images.Length;
        _linkStart = _pathStart + paths.Length;
        _count = _linkStart + links.Length;

        // The grid spans the finite items' bounds, its cells about the size of an average item (so an item covers a few cells and a
        // cell holds a few items), and at most about two cells per item.
        double left = double.PositiveInfinity;
        double bottom = double.PositiveInfinity;
        double right = double.NegativeInfinity;
        double top = double.NegativeInfinity;
        double widths = 0;
        double heights = 0;
        int finite = 0;
        for (int id = 0; id < _count; id++)
        {
            PdfRectangle bounds = GetBounds(id);
            if (IsFinite(bounds))
            {
                left = Math.Min(left, bounds.Left);
                bottom = Math.Min(bottom, bounds.Bottom);
                right = Math.Max(right, bounds.Right);
                top = Math.Max(top, bounds.Top);
                widths += bounds.Width;
                heights += bounds.Height;
                finite++;
            }
        }

        if (finite == 0)
        {
            // No finite item: everything is visited by every query.
            left = bottom = right = top = 0;
        }

        _left = left;
        _bottom = bottom;
        _right = right;
        _top = top;
        double width = right - left;
        double height = top - bottom;
        double cellWidth = Math.Max(finite == 0 ? 0 : widths / finite, width / MaxCellsPerAxis);
        double cellHeight = Math.Max(finite == 0 ? 0 : heights / finite, height / MaxCellsPerAxis);
        _columns = width > 0 && double.IsFinite(width) ? (int)Math.Clamp(Math.Ceiling(width / cellWidth), 1, MaxCellsPerAxis) : 1;
        _rows = height > 0 && double.IsFinite(height) ? (int)Math.Clamp(Math.Ceiling(height / cellHeight), 1, MaxCellsPerAxis) : 1;
        long limit = (2L * _count) + 16;
        if ((long)_columns * _rows > limit)
        {
            double shrink = Math.Sqrt(limit / ((double)_columns * _rows));
            _columns = Math.Max(1, (int)(_columns * shrink));
            _rows = Math.Max(1, (int)(_rows * shrink));
        }

        _columnScale = _columns > 1 ? _columns / width : 0;
        _rowScale = _rows > 1 ? _rows / height : 0;

        // Count, then place: _cellStart first holds counts, then starts, then (while placing) ends, shifted back into starts.
        int cellCount = _columns * _rows;
        _cellStart = new int[cellCount + 1];
        int overflow = 0;
        for (int id = 0; id < _count; id++)
        {
            if (!TryGetCells(GetBounds(id), out int column0, out int row0, out int column1, out int row1))
            {
                overflow++;
                continue;
            }

            for (int row = row0; row <= row1; row++)
            {
                for (int column = column0; column <= column1; column++)
                {
                    _cellStart[(row * _columns) + column]++;
                }
            }
        }

        int total = 0;
        for (int cell = 0; cell < cellCount; cell++)
        {
            int count = _cellStart[cell];
            _cellStart[cell] = total;
            total += count;
        }

        _cellStart[cellCount] = total;
        _cellItems = new int[total];
        _overflow = new int[overflow];
        overflow = 0;
        for (int id = 0; id < _count; id++)
        {
            if (!TryGetCells(GetBounds(id), out int column0, out int row0, out int column1, out int row1))
            {
                _overflow[overflow++] = id;
                continue;
            }

            for (int row = row0; row <= row1; row++)
            {
                for (int column = column0; column <= column1; column++)
                {
                    _cellItems[_cellStart[(row * _columns) + column]++] = id;
                }
            }
        }

        for (int cell = cellCount - 1; cell > 0; cell--)
        {
            _cellStart[cell] = _cellStart[cell - 1];
        }

        _cellStart[0] = 0;
    }

    /// <summary>Gets the number of glyphs: every character code shown, in content order.</summary>
    /// <remarks>ISO 32000-2 §9.4.3.</remarks>
    public int GlyphCount => _glyphs.Length;

    /// <summary>Gets the Unicode text of every glyph, in content order; each glyph owns the range <see cref="HitTestGlyph.TextStart"/>, <see cref="HitTestGlyph.TextLength"/>.</summary>
    /// <remarks>ISO 32000-2 §9.10.2: each code's own mapping; nothing is inserted between glyphs.</remarks>
    public ReadOnlySpan<char> Text => _text;

    /// <summary>Gets the image and stencil-mask paints, in paint order.</summary>
    /// <remarks>ISO 32000-2 §8.9.4 and §8.9.6.2.</remarks>
    public ReadOnlySpan<HitTestShape> Images => _images;

    /// <summary>Gets the path fills and strokes (colour or shading pattern), in paint order.</summary>
    /// <remarks>ISO 32000-2 §8.5.3.</remarks>
    public ReadOnlySpan<HitTestShape> Paths => _paths;

    /// <summary>Gets the page's visible link annotations, in <c>Annots</c> order; empty for an index built from a display list alone.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.5.</remarks>
    public IReadOnlyList<HitTestLink> Links => _links;

    /// <summary>Gets a glyph.</summary>
    /// <param name="index">The glyph's index, its position in content order: 0 to <see cref="GlyphCount"/> − 1.</param>
    /// <returns>The glyph.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the glyphs.</exception>
    /// <remarks>ISO 32000-2 §9.4.4.</remarks>
    public HitTestGlyph GetGlyph(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _glyphs.Length);
        ref readonly DisplayTextGlyph glyph = ref _glyphs[index];
        return new HitTestGlyph
        {
            Quad = glyph.Quad,
            TextStart = glyph.TextStart,
            TextLength = glyph.TextLength,
            CharacterCode = glyph.CharacterCode,
            CodeLength = glyph.CodeLength,
            RenderingMode = glyph.Mode,
            IsVertical = glyph.IsVertical,
        };
    }

    /// <summary>Gets the Unicode text of a glyph.</summary>
    /// <param name="glyph">A glyph of this index.</param>
    /// <returns>Its slice of <see cref="Text"/>.</returns>
    /// <remarks>ISO 32000-2 §9.10.2.</remarks>
    public ReadOnlySpan<char> GetGlyphText(in HitTestGlyph glyph) => _text.AsSpan(glyph.TextStart, glyph.TextLength);

    /// <summary>Finds the items of the given kinds that contain a point.</summary>
    /// <param name="point">The point, in page space (default user space).</param>
    /// <param name="kinds">The kinds to look at.</param>
    /// <returns>The matches, by kind and then in content order; enumerating them allocates nothing.</returns>
    /// <remarks>
    /// Viewer capability 4. A point on an edge is inside. Glyphs match inside their box (§9.4.4), images and paths inside their area
    /// (<see cref="HitTestShape"/>), links inside a quadrilateral of their region (§12.5.6.5).
    /// </remarks>
    public HitTestMatches Find(PathPoint point, HitTestKinds kinds)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || (kinds & HitTestKinds.All) == 0)
        {
            return default;
        }

        ReadOnlySpan<int> cell = default;
        if (point.X >= _left && point.X <= _right && point.Y >= _bottom && point.Y <= _top)
        {
            int index = (Row(point.Y) * _columns) + Column(point.X);
            cell = _cellItems.AsSpan(_cellStart[index], _cellStart[index + 1] - _cellStart[index]);
        }

        return new HitTestMatches(this, kinds, point, default, isArea: false, cell, _overflow, scan: false);
    }

    /// <summary>Finds the items of the given kinds that share a point with a rectangle.</summary>
    /// <param name="area">The rectangle, in page space (default user space).</param>
    /// <param name="kinds">The kinds to look at.</param>
    /// <returns>The matches, by kind and then in content order; enumerating them allocates nothing.</returns>
    /// <remarks>
    /// Viewer capability 4. Rotated glyph boxes, image and path areas and link quadrilaterals are tested exactly against the
    /// rectangle (by separating axes), not by their upright bounds; touching edges count as shared.
    /// </remarks>
    public HitTestMatches Find(PdfRectangle area, HitTestKinds kinds)
    {
        if (!IsFinite(area) || (kinds & HitTestKinds.All) == 0)
        {
            return default;
        }

        if (!TryGetCells(area, out int column0, out int row0, out int column1, out int row1, limit: false)
            || area.Right < _left || area.Left > _right || area.Top < _bottom || area.Bottom > _top)
        {
            return new HitTestMatches(this, kinds, default, area, isArea: true, default, _overflow, scan: false);
        }

        if (column0 == column1 && row0 == row1)
        {
            int index = (row0 * _columns) + column0;
            ReadOnlySpan<int> cell = _cellItems.AsSpan(_cellStart[index], _cellStart[index + 1] - _cellStart[index]);
            return new HitTestMatches(this, kinds, default, area, isArea: true, cell, _overflow, scan: false);
        }

        // A rectangle over several cells visits every item once, in order.
        return new HitTestMatches(this, kinds, default, area, isArea: true, default, default, scan: true);
    }

    /// <summary>Builds the index of a display list's glyphs, images and paths, with links collected from a page.</summary>
    internal static HitTestIndex Create(DisplayList list, HitTestLink[] links)
    {
        var collector = new HitTestCollector();
        list.Replay(collector);
        return new HitTestIndex(list.TextGlyphs, list.Text, collector.Images, collector.Paths, links);
    }

    /// <summary>The page's visible link annotations and their regions (§12.5.6.5, §12.5.3 Hidden and NoView, §8.11.3.3).</summary>
    internal static HitTestLink[] CollectLinks(PdfPage page, PdfDocument document, PdfOptionalContentState? state)
    {
        List<HitTestLink>? links = null;
        foreach (PdfAnnotation annotation in page.Annotations)
        {
            if (annotation is not PdfLinkAnnotation link || (link.Flags & (PdfAnnotationFlags.Hidden | PdfAnnotationFlags.NoView)) != 0)
            {
                continue;
            }

            if (document.OptionalContent is { } properties && link.OptionalContent is not null && !link.IsVisible(state ??= properties.GetDefaultStates()))
            {
                continue;
            }

            IReadOnlyList<PdfQuadrilateral> region = link.ActivationRegion;
            PdfRectangle bounds = region[0].Bounds;
            foreach (PdfQuadrilateral quad in region)
            {
                PdfRectangle next = quad.Bounds;
                bounds = new PdfRectangle(
                    Math.Min(bounds.Left, next.Left),
                    Math.Min(bounds.Bottom, next.Bottom),
                    Math.Max(bounds.Right, next.Right),
                    Math.Max(bounds.Top, next.Top));
            }

            (links ??= []).Add(new HitTestLink(link, region, bounds));
        }

        return links is null ? [] : [.. links];
    }

    /// <summary>The kind of an item number and its index within the kind.</summary>
    internal HitTestItem Identify(int id) =>
        id < _imageStart ? new HitTestItem(HitTestKinds.Glyphs, id)
        : id < _pathStart ? new HitTestItem(HitTestKinds.Images, id - _imageStart)
        : id < _linkStart ? new HitTestItem(HitTestKinds.Paths, id - _pathStart)
        : new HitTestItem(HitTestKinds.Links, id - _linkStart);

    /// <summary>The first item number after the kind that holds <paramref name="id"/>.</summary>
    internal int EndOfKind(int id) => id < _imageStart ? _imageStart : id < _pathStart ? _pathStart : id < _linkStart ? _linkStart : _count;

    /// <summary>The number of items of every kind.</summary>
    internal int Count => _count;

    /// <summary>Whether item <paramref name="id"/> contains the point.</summary>
    internal bool Contains(int id, PathPoint point)
    {
        if (id < _imageStart)
        {
            ref readonly DisplayTextGlyph glyph = ref _glyphs[id];
            return HitTestGeometry.Contains(glyph.BottomLeft, glyph.Baseline, glyph.Across, point);
        }

        if (id < _linkStart)
        {
            ref readonly HitTestShape shape = ref id < _pathStart ? ref _images[id - _imageStart] : ref _paths[id - _pathStart];
            Quad quad = shape.Quad;
            return HitTestGeometry.Contains(shape.Bounds, point)
                && HitTestGeometry.Contains(quad.BottomLeft, Subtract(quad.BottomRight, quad.BottomLeft), Subtract(quad.TopLeft, quad.BottomLeft), point);
        }

        HitTestLink link = _links[id - _linkStart];
        if (!HitTestGeometry.Contains(link.Bounds, point))
        {
            return false;
        }

        foreach (PdfQuadrilateral quad in link.Region)
        {
            if (HitTestGeometry.Contains(quad, point))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether item <paramref name="id"/> shares a point with the rectangle.</summary>
    internal bool Intersects(int id, in PdfRectangle area)
    {
        if (id < _imageStart)
        {
            ref readonly DisplayTextGlyph glyph = ref _glyphs[id];
            return HitTestGeometry.Intersects(glyph.BottomLeft, glyph.Baseline, glyph.Across, area);
        }

        if (id < _linkStart)
        {
            ref readonly HitTestShape shape = ref id < _pathStart ? ref _images[id - _imageStart] : ref _paths[id - _pathStart];
            Quad quad = shape.Quad;
            return HitTestGeometry.Overlaps(shape.Bounds, area)
                && HitTestGeometry.Intersects(quad.BottomLeft, Subtract(quad.BottomRight, quad.BottomLeft), Subtract(quad.TopLeft, quad.BottomLeft), area);
        }

        HitTestLink link = _links[id - _linkStart];
        if (!HitTestGeometry.Overlaps(link.Bounds, area))
        {
            return false;
        }

        foreach (PdfQuadrilateral quad in link.Region)
        {
            if (HitTestGeometry.Intersects(quad, area))
            {
                return true;
            }
        }

        return false;
    }

    private static PathPoint Subtract(PathPoint a, PathPoint b) => new(a.X - b.X, a.Y - b.Y);

    private static bool IsFinite(in PdfRectangle bounds) =>
        double.IsFinite(bounds.Left) && double.IsFinite(bounds.Bottom) && double.IsFinite(bounds.Right) && double.IsFinite(bounds.Top);

    private PdfRectangle GetBounds(int id)
    {
        if (id < _imageStart)
        {
            ref readonly DisplayTextGlyph glyph = ref _glyphs[id];
            Quad quad = glyph.Quad;
            return HitTestGeometry.Bounds(quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft);
        }

        return id < _pathStart ? _images[id - _imageStart].Bounds
            : id < _linkStart ? _paths[id - _pathStart].Bounds
            : _links[id - _linkStart].Bounds;
    }

    /// <summary>The cells a rectangle covers; false when it is not finite or (with <paramref name="limit"/>) covers too many to bucket.</summary>
    private bool TryGetCells(in PdfRectangle bounds, out int column0, out int row0, out int column1, out int row1, bool limit = true)
    {
        column0 = row0 = column1 = row1 = 0;
        if (!IsFinite(bounds))
        {
            return false;
        }

        column0 = Column(bounds.Left);
        column1 = Column(bounds.Right);
        row0 = Row(bounds.Bottom);
        row1 = Row(bounds.Top);
        return !limit || ((long)(column1 - column0 + 1) * (row1 - row0 + 1)) <= MaxCellsPerItem;
    }

    private int Column(double x) => (int)Math.Clamp((x - _left) * _columnScale, 0, _columns - 1);

    private int Row(double y) => (int)Math.Clamp((y - _bottom) * _rowScale, 0, _rows - 1);
}
