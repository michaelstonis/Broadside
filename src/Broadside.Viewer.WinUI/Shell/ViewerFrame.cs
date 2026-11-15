using Broadside.Graphics;

namespace Broadside.Viewer.WinUI;

/// <summary>
/// What one frame shows, in device pixels from the view's top-left, back to front: paper, content tiles, overlay tiles, search
/// matches, the selection, focus chrome and selection handles. The host turns it into its own elements.
/// </summary>
internal sealed class ViewerFrame
{
    private Item[] _items = new Item[64];

    /// <summary>What an item draws.</summary>
    public enum ItemKind
    {
        /// <summary>A page's paper (white, black in dark mode) under its tiles: <see cref="Item.Destination"/>.</summary>
        Paper,

        /// <summary>A tile image stretched over <see cref="Item.Destination"/>.</summary>
        Tile,

        /// <summary>A search match's quadrilateral (<see cref="Item.A"/> to <see cref="Item.D"/>).</summary>
        SearchMatch,

        /// <summary>The current search match's quadrilateral.</summary>
        CurrentMatch,

        /// <summary>A quadrilateral of selected text.</summary>
        Selection,

        /// <summary>A focus or hover ring around <see cref="Item.Destination"/>, <see cref="Item.Width"/> pixels wide.</summary>
        Chrome,

        /// <summary>A selection handle: a bar from the top to the bottom of <see cref="Item.Destination"/> (its left edge) and a knob of radius <see cref="Item.Width"/> under it.</summary>
        Handle,
    }

    /// <summary>Gets or sets the frame's width in device pixels.</summary>
    public double Width { get; set; }

    /// <summary>Gets or sets the frame's height in device pixels.</summary>
    public double Height { get; set; }

    /// <summary>Gets or sets whether the pages are drawn in dark mode (black paper, dark surroundings).</summary>
    public bool IsDark { get; set; }

    /// <summary>Gets how many items the frame holds.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the items, back to front.</summary>
    public ReadOnlySpan<Item> Items => _items.AsSpan(0, Count);

    public void Add(in Item item)
    {
        if (Count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
        }

        _items[Count++] = item;
    }

    public ViewerFrame Reset()
    {
        Array.Clear(_items, 0, Count);
        Count = 0;
        return this;
    }

    /// <summary>A rectangle snapped to whole device pixels, so neighboring tiles share their edges exactly at any zoom.</summary>
    public static LayoutRectangle Snap(in LayoutRectangle r)
    {
        double left = Math.Round(r.X);
        double top = Math.Round(r.Y);
        return new LayoutRectangle(left, top, Math.Round(r.Right) - left, Math.Round(r.Bottom) - top);
    }

    /// <summary>One thing a frame draws.</summary>
    /// <param name="Kind">What it is.</param>
    /// <param name="Destination">Where it goes, in device pixels.</param>
    /// <param name="Image">The tile image (tiles only).</param>
    /// <param name="Width">A ring's stroke width or a handle's knob radius, in device pixels.</param>
    public readonly record struct Item(ItemKind Kind, LayoutRectangle Destination, ITileImage? Image = null, double Width = 0)
    {
        /// <summary>Gets a quadrilateral's first corner (bottom-left of the text).</summary>
        public PathPoint A { get; init; }

        /// <summary>Gets a quadrilateral's second corner (bottom-right).</summary>
        public PathPoint B { get; init; }

        /// <summary>Gets a quadrilateral's third corner (top-right).</summary>
        public PathPoint C { get; init; }

        /// <summary>Gets a quadrilateral's fourth corner (top-left).</summary>
        public PathPoint D { get; init; }
    }
}
