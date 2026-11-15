using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer.Wpf;

/// <summary>Turns tiles into WPF images and draws a <see cref="ViewerFrame"/> into a <see cref="DrawingContext"/>.</summary>
internal static class FrameRenderer
{
    internal static readonly Color Background = Color.FromRgb(0xE8, 0xE8, 0xEC);
    internal static readonly Color DarkBackground = Color.FromRgb(0x2B, 0x2B, 0x2B);

    private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Background));
    private static readonly Brush DarkBackgroundBrush = Freeze(new SolidColorBrush(DarkBackground));
    private static readonly Brush PaperBrush = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush DarkPaperBrush = Freeze(new SolidColorBrush(Colors.Black));
    private static readonly Brush ShadowBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x28, 0, 0, 0)));
    private static readonly Brush SelectionBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0x33, 0x7A, 0xFF)));
    private static readonly Brush SearchBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xD4, 0x00)));
    private static readonly Brush CurrentSearchBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0x8A, 0x00)));
    private static readonly Brush HandleBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x7A, 0xFF)));
    private static readonly Brush ChromeBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xC0, 0x33, 0x7A, 0xFF)));

    /// <summary>Copies a presented tile's pixels (premultiplied BGRA) into a frozen bitmap, which any thread may hand to the UI thread.</summary>
    /// <remarks>ISO 32000-2 §8.3.2.2 (device space): one tile pixel is one device pixel, so the bitmap claims 96 DPI and is drawn at its pixel size divided by the view's pixel ratio.</remarks>
    public static unsafe object CreateImage(Bitmap pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        BitmapSource image;
        Span<byte> span = pixels.Pixels;
        fixed (byte* source = span)
        {
            image = BitmapSource.Create(
                pixels.Width, pixels.Height, 96, 96, PixelFormats.Pbgra32, null, (IntPtr)source, pixels.Stride * pixels.Height, pixels.Stride);
        }

        image.Freeze();
        return image;
    }

    /// <summary>The view's background before a frame (and around the pages).</summary>
    public static Brush BackgroundFor(bool dark) => dark ? DarkBackgroundBrush : BackgroundBrush;

    /// <summary>Draws <paramref name="frame"/> over a <paramref name="width"/> × <paramref name="height"/> (DIPs) area at <paramref name="ratio"/> pixels per DIP.</summary>
    public static void Draw(DrawingContext context, ViewerFrame? frame, double width, double height, double ratio)
    {
        bool dark = frame?.IsDark ?? false;
        context.DrawRectangle(BackgroundFor(dark), null, new Rect(0, 0, width, height));
        if (frame is null || frame.Count == 0 || ratio <= 0)
        {
            return;
        }

        // Device pixels from here on: whole-pixel rectangles share their edges exactly, so no seam shows between tiles at any zoom.
        context.PushTransform(new ScaleTransform(1 / ratio, 1 / ratio));
        foreach (ref readonly ViewerFrame.Item item in frame.Items)
        {
            switch (item.Kind)
            {
                case ViewerFrame.ItemKind.Paper:
                    Rect paper = Snap(item.Destination);
                    context.DrawRectangle(ShadowBrush, null, new Rect(paper.X + 1, paper.Y + 2, paper.Width, paper.Height));
                    context.DrawRectangle(dark ? DarkPaperBrush : PaperBrush, null, paper);
                    break;
                case ViewerFrame.ItemKind.Tile when item.Image is ImageSource image:
                    context.DrawImage(image, Snap(item.Destination));
                    break;
                case ViewerFrame.ItemKind.SearchMatch:
                    context.DrawGeometry(SearchBrush, null, Quad(item));
                    break;
                case ViewerFrame.ItemKind.CurrentSearchMatch:
                    context.DrawGeometry(CurrentSearchBrush, null, Quad(item));
                    break;
                case ViewerFrame.ItemKind.Selection:
                    context.DrawGeometry(SelectionBrush, null, Quad(item));
                    break;
                case ViewerFrame.ItemKind.Chrome:
                    var chrome = new Pen(ChromeBrush, item.A.X);
                    chrome.Freeze();
                    context.DrawRectangle(null, chrome, ToRect(item.Destination));
                    break;
                case ViewerFrame.ItemKind.Handle:
                    var stem = new Pen(HandleBrush, item.A.Y);
                    stem.Freeze();
                    LayoutRectangle d = item.Destination;
                    context.DrawLine(stem, new Point(d.X, d.Y), new Point(d.X, d.Bottom));
                    context.DrawEllipse(HandleBrush, null, new Point(d.X, item.B.Y), item.A.X, item.A.X);
                    break;
            }
        }

        context.Pop();
    }

    private static StreamGeometry Quad(in ViewerFrame.Item item)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(ToPoint(item.A), isFilled: true, isClosed: true);
            path.LineTo(ToPoint(item.B), isStroked: false, isSmoothJoin: false);
            path.LineTo(ToPoint(item.C), isStroked: false, isSmoothJoin: false);
            path.LineTo(ToPoint(item.D), isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point ToPoint(PathPoint point) => new(point.X, point.Y);

    private static Rect ToRect(in LayoutRectangle r) => new(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height));

    private static Rect Snap(in LayoutRectangle r)
    {
        double left = Math.Round(r.X);
        double top = Math.Round(r.Y);
        return new Rect(left, top, Math.Max(0, Math.Round(r.Right) - left), Math.Max(0, Math.Round(r.Bottom) - top));
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
