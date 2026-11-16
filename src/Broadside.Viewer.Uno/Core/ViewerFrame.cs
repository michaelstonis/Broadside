using Broadside.Graphics;

namespace Broadside.Viewer.Uno;

/// <summary>The kinds of thing a <see cref="ViewerFrame"/> draws, bottom to top in the order they were added.</summary>
internal enum FrameItemKind
{
    /// <summary>A page's paper (white, or black under the dark-mode color filter) with a soft shadow.</summary>
    Paper,

    /// <summary>A tile's pixels stretched over its destination.</summary>
    Tile,

    /// <summary>A search match (quad A, B, C, D).</summary>
    SearchMatch,

    /// <summary>The current search match (quad A, B, C, D).</summary>
    CurrentMatch,

    /// <summary>Selected text (quad A, B, C, D).</summary>
    Selection,

    /// <summary>A pressed, hovered or focused link or field: a rectangle outline <see cref="FrameItem.Stroke"/> wide.</summary>
    Chrome,

    /// <summary>A touch selection handle: a stem from A (top) to B (bottom) and a knob of radius <see cref="FrameItem.Stroke"/> below B.</summary>
    Handle,
}

/// <summary>One thing to draw, in device pixels from the view's top-left.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Bounds">Its destination (paper, tile, chrome).</param>
/// <param name="Pixels">The tile's pixels (<see cref="FrameItemKind.Tile"/>).</param>
/// <param name="A">The quad's first corner (bottom-left), or a handle's top.</param>
/// <param name="B">The second corner (bottom-right), or a handle's bottom.</param>
/// <param name="C">The third corner (top-right).</param>
/// <param name="D">The fourth corner (top-left).</param>
/// <param name="Stroke">An outline's width or a handle's knob radius.</param>
internal readonly record struct FrameItem(
    FrameItemKind Kind,
    LayoutRectangle Bounds,
    TilePixels? Pixels = null,
    PathPoint A = default,
    PathPoint B = default,
    PathPoint C = default,
    PathPoint D = default,
    double Stroke = 0);

/// <summary>One composed frame: what the view shows, in device pixels, built on the UI thread by <see cref="ViewerController.Frame"/>.</summary>
internal sealed class ViewerFrame
{
    private FrameItem[] _items = new FrameItem[64];

    public int Count { get; private set; }

    /// <summary>Gets or sets the frame's width in device pixels.</summary>
    public double Width { get; set; }

    /// <summary>Gets or sets the frame's height in device pixels.</summary>
    public double Height { get; set; }

    /// <summary>Gets or sets the device pixels per device-independent unit.</summary>
    public double PixelRatio { get; set; } = 1;

    /// <summary>Gets or sets whether the pages are drawn with the dark-mode color filter (black paper, dark surroundings).</summary>
    public bool IsDark { get; set; }

    public ReadOnlySpan<FrameItem> Items => _items.AsSpan(0, Count);

    public void Add(in FrameItem item)
    {
        if (Count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
        }

        _items[Count++] = item;
    }

    public void Reset()
    {
        Array.Clear(_items, 0, Count);
        Count = 0;
        IsDark = false;
    }

    /// <summary>Rounds a destination to whole device pixels, so neighboring tiles share their edges exactly and no seam shows.</summary>
    public static LayoutRectangle Snap(in LayoutRectangle r)
    {
        double left = Math.Round(r.X);
        double top = Math.Round(r.Y);
        return new LayoutRectangle(left, top, Math.Round(r.Right) - left, Math.Round(r.Bottom) - top);
    }
}
