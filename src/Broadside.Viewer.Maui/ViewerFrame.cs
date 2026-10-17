using SkiaSharp;

namespace Broadside.Viewer.Maui;

/// <summary>One composed frame: what the canvas shows, in device pixels, built on the UI thread and drawn by the view's paint.</summary>
/// <remarks>
/// The paint of a GPU view may run on its own thread (Android's GL thread), so a frame is an immutable record once published: the
/// controller never mutates the view model while the canvas reads it (<see cref="FramePresenter"/>).
/// </remarks>
internal sealed class ViewerFrame
{
    internal static readonly SKColor Background = new(0xE8, 0xE8, 0xEC);
    internal static readonly SKColor SelectionColor = new(0x33, 0x7A, 0xFF, 0x55);
    internal static readonly SKColor HandleColor = new(0x33, 0x7A, 0xFF);
    internal static readonly SKColor ChromeColor = new(0x33, 0x7A, 0xFF, 0xC0);

    private Item[] _items = new Item[64];

    public enum ItemKind
    {
        Paper,
        Tile,
        Selection,
        Chrome,
        Handle,
    }

    public long Generation { get; set; }

    public int Count { get; private set; }

    public double Width { get; set; }

    public double Height { get; set; }

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

    /// <summary>Draws the frame over the whole canvas (cleared to the viewer's background first, never left black).</summary>
    public void Draw(SKCanvas canvas, SKPaint paint, SKPathBuilder path)
    {
        canvas.Clear(Background);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        foreach (ref readonly Item item in Items)
        {
            switch (item.Kind)
            {
                case ItemKind.Paper:
                    paint.Reset();
                    paint.Color = new SKColor(0, 0, 0, 0x28);
                    canvas.DrawRect(Snap(item.Destination with { Left = item.Destination.Left + 1, Top = item.Destination.Top + 2, Right = item.Destination.Right + 1, Bottom = item.Destination.Bottom + 2 }), paint);
                    paint.Color = SKColors.White;
                    canvas.DrawRect(Snap(item.Destination), paint);
                    break;
                case ItemKind.Tile when item.Image is { } image:
                    paint.Reset();
                    canvas.DrawImage(image, Snap(item.Destination), sampling, paint);
                    break;
                case ItemKind.Selection:
                    paint.Reset();
                    paint.Color = SelectionColor;
                    paint.IsAntialias = true;
                    path.Reset();
                    path.MoveTo(item.A);
                    path.LineTo(item.B);
                    path.LineTo(item.C);
                    path.LineTo(item.D);
                    path.Close();
                    using (SKPath quad = path.Detach())
                    {
                        canvas.DrawPath(quad, paint);
                    }

                    break;
                case ItemKind.Chrome:
                    paint.Reset();
                    paint.Color = ChromeColor;
                    paint.IsAntialias = true;
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = item.A.X;
                    canvas.DrawRect(item.Destination, paint);
                    break;
                case ItemKind.Handle:
                    paint.Reset();
                    paint.Color = HandleColor;
                    paint.IsAntialias = true;
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = item.A.Y;
                    canvas.DrawLine(item.Destination.Left, item.Destination.Top, item.Destination.Left, item.Destination.Bottom, paint);
                    paint.Style = SKPaintStyle.Fill;
                    canvas.DrawCircle(item.Destination.Left, item.B.Y, item.A.X, paint);
                    break;
            }
        }
    }

    // Whole device pixels: neighbouring tiles share their edges exactly, so no seam shows between them at any zoom.
    private static SKRect Snap(SKRect rect) =>
        new(MathF.Round(rect.Left), MathF.Round(rect.Top), MathF.Round(rect.Right), MathF.Round(rect.Bottom));

    /// <summary>One thing to draw. Selection quads use the four points; chrome carries its stroke width in A.X; handles their radius in A.X, stroke in A.Y and knob centre in B.Y.</summary>
    public readonly record struct Item(ItemKind Kind, SKRect Destination, SKImage? Image = null, SKPoint A = default, SKPoint B = default, SKPoint C = default, SKPoint D = default);
}
