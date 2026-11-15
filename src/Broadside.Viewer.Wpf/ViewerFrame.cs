using Broadside.Graphics;

namespace Broadside.Viewer.Wpf;

/// <summary>
/// One composed frame: what the view shows, in device pixels, built by the <see cref="ViewerController"/> on the UI thread and drawn by
/// the view's <c>OnRender</c>. It holds no WPF type, so the controller and its tests stay free of the framework.
/// </summary>
internal sealed class ViewerFrame
{
    private Item[] _items = new Item[64];

    public enum ItemKind
    {
        Paper,
        Tile,
        SearchMatch,
        CurrentSearchMatch,
        Selection,
        Chrome,
        Handle,
    }

    public int Count { get; private set; }

    /// <summary>Gets or sets the frame's width in device pixels.</summary>
    public double Width { get; set; }

    /// <summary>Gets or sets the frame's height in device pixels.</summary>
    public double Height { get; set; }

    /// <summary>Gets or sets a value indicating whether the paper and surroundings are dark (smart dark mode).</summary>
    public bool IsDark { get; set; }

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
        IsDark = false;
        return this;
    }

    /// <summary>
    /// One thing to draw, in device pixels. Tiles carry their image; selection and search quads use the four points; chrome carries
    /// its stroke width in <see cref="Item.A"/>.X; handles their radius in A.X, stroke in A.Y and knob centre in <see cref="Item.B"/>.Y.
    /// </summary>
    public readonly record struct Item(
        ItemKind Kind,
        LayoutRectangle Destination,
        object? Image = null,
        PathPoint A = default,
        PathPoint B = default,
        PathPoint C = default,
        PathPoint D = default);
}
