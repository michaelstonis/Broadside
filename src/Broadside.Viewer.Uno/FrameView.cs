using System.Runtime.InteropServices.WindowsRuntime;
using Broadside.Graphics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Broadside.Viewer.Uno;

/// <summary>
/// Shows a <see cref="ViewerFrame"/> as Uno elements: paper rectangles, one <see cref="Image"/> per tile (its pixels uploaded once into
/// a <see cref="WriteableBitmap"/>, premultiplied BGRA like the rasterizer's), and polygons for search matches and the selection.
/// Elements are pooled and reused frame to frame, so scrolling only moves them.
/// </summary>
#pragma warning disable CA1001 // the geometry is an element of the visual tree, released with it
internal sealed class FrameView
#pragma warning restore CA1001
{
    private static readonly SolidColorBrush Shadow = Brush(ViewerColors.Shadow);
    private static readonly SolidColorBrush SelectionBrush = Brush(ViewerColors.Selection);
    private static readonly SolidColorBrush MatchBrush = Brush(ViewerColors.SearchMatch);
    private static readonly SolidColorBrush CurrentMatchBrush = Brush(ViewerColors.CurrentMatch);
    private static readonly SolidColorBrush ChromeBrush = Brush(ViewerColors.Chrome);
    private static readonly SolidColorBrush HandleBrush = Brush(ViewerColors.Handle);
    private readonly SolidColorBrush _paper = Brush(ViewerColors.Paper);
    private readonly Canvas _papers = new() { IsHitTestVisible = false };
    private readonly Canvas _tiles = new() { IsHitTestVisible = false };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly List<Rectangle> _paperPool = [];
    private readonly List<Image> _tilePool = [];
    private readonly List<Polygon> _quadPool = [];
    private readonly List<Rectangle> _chromePool = [];
    private readonly List<Ellipse> _knobPool = [];
    private readonly RectangleGeometry _clip = new();

    public FrameView()
    {
        Root = new Canvas { IsHitTestVisible = false, Clip = _clip };
        Root.Children.Add(_papers);
        Root.Children.Add(_tiles);
        Root.Children.Add(_overlay);
        AutomationHidden(Root);
    }

    public Canvas Root { get; }

    public void SetClip(double width, double height) => _clip.Rect = new Rect(0, 0, Math.Max(width, 0), Math.Max(height, 0));

    public void Show(ViewerFrame frame)
    {
        double scale = 1 / frame.PixelRatio;
        _paper.Color = ToColor(frame.IsDark ? ViewerColors.DarkPaper : ViewerColors.Paper);
        int papers = 0;
        int tiles = 0;
        int quads = 0;
        int chrome = 0;
        int knobs = 0;
        foreach (ref readonly FrameItem item in frame.Items)
        {
            switch (item.Kind)
            {
                case FrameItemKind.Paper:
                    LayoutRectangle b = item.Bounds;
                    Place(Take(_paperPool, _papers, ref papers, () => new Rectangle { Fill = Shadow }), new LayoutRectangle(b.X + 1, b.Y + 2, b.Width, b.Height), scale);
                    Rectangle paper = Take(_paperPool, _papers, ref papers, () => new Rectangle());
                    paper.Fill = _paper;
                    Place(paper, b, scale);
                    break;
                case FrameItemKind.Tile when item.Pixels is { IsReleased: false } pixels:
                    Image image = Take(_tilePool, _tiles, ref tiles, () => new Image { Stretch = Stretch.Fill });
                    var source = (WriteableBitmap)(pixels.Image ??= Upload(pixels));
                    if (!ReferenceEquals(image.Source, source))
                    {
                        image.Source = source;
                    }

                    Place(image, item.Bounds, scale);
                    break;
                case FrameItemKind.SearchMatch or FrameItemKind.CurrentMatch or FrameItemKind.Selection:
                    Polygon quad = Take(_quadPool, _overlay, ref quads, () => new Polygon());
                    quad.Fill = item.Kind switch
                    {
                        FrameItemKind.SearchMatch => MatchBrush,
                        FrameItemKind.CurrentMatch => CurrentMatchBrush,
                        _ => SelectionBrush,
                    };
                    SetPoints(quad, item, scale);
                    break;
                case FrameItemKind.Chrome:
                    Rectangle ring = Take(_chromePool, _overlay, ref chrome, () => new Rectangle { Stroke = ChromeBrush });
                    ring.StrokeThickness = item.Stroke * scale;
                    Place(ring, item.Bounds, scale);
                    break;
                case FrameItemKind.Handle:
                    double radius = item.Stroke * scale;
                    Rectangle stem = Take(_chromePool, _overlay, ref chrome, () => new Rectangle());
                    stem.Stroke = null;
                    stem.Fill = HandleBrush;
                    Place(stem, new LayoutRectangle(item.A.X - frame.PixelRatio, item.A.Y, 2 * frame.PixelRatio, item.B.Y - item.A.Y), scale);
                    Ellipse knob = Take(_knobPool, _overlay, ref knobs, () => new Ellipse { Fill = HandleBrush });
                    Canvas.SetLeft(knob, (item.B.X * scale) - radius);
                    Canvas.SetTop(knob, item.B.Y * scale);
                    knob.Width = 2 * radius;
                    knob.Height = 2 * radius;
                    break;
            }
        }

        Hide(_paperPool, papers);
        Hide(_tilePool, tiles);
        Hide(_quadPool, quads);
        Hide(_chromePool, chrome);
        Hide(_knobPool, knobs);
    }

    /// <summary>Copies a tile's pixels into a bitmap image; the pixel buffer goes back to the pool once uploaded.</summary>
    private static WriteableBitmap Upload(TilePixels pixels)
    {
        var bitmap = new WriteableBitmap(pixels.Width, pixels.Height);
        using (Stream stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels.Pixels);
        }

        bitmap.Invalidate();
        pixels.DetachPixels();
        return bitmap;
    }

    private static T Take<T>(List<T> pool, Canvas parent, ref int used, Func<T> create)
        where T : FrameworkElement
    {
        if (used == pool.Count)
        {
            T created = create();
            pool.Add(created);
            parent.Children.Add(created);
        }

        T element = pool[used++];
        element.Visibility = Visibility.Visible;
        return element;
    }

    private static void Hide<T>(List<T> pool, int used)
        where T : FrameworkElement
    {
        for (int i = used; i < pool.Count; i++)
        {
            T element = pool[i];
            if (element.Visibility == Visibility.Visible)
            {
                element.Visibility = Visibility.Collapsed;
            }

            // A hidden image lets its tile's bitmap go.
            if (element is Image { Source: not null } image)
            {
                image.Source = null;
            }
        }
    }

    private static void Place(FrameworkElement element, in LayoutRectangle r, double scale)
    {
        Canvas.SetLeft(element, r.X * scale);
        Canvas.SetTop(element, r.Y * scale);
        element.Width = Math.Max(r.Width * scale, 0);
        element.Height = Math.Max(r.Height * scale, 0);
    }

    private static void SetPoints(Polygon polygon, in FrameItem item, double scale)
    {
        PointCollection points = polygon.Points;
        Point a = new(item.A.X * scale, item.A.Y * scale);
        Point b = new(item.B.X * scale, item.B.Y * scale);
        Point c = new(item.C.X * scale, item.C.Y * scale);
        Point d = new(item.D.X * scale, item.D.Y * scale);
        if (points.Count == 4 && points[0] == a && points[1] == b && points[2] == c && points[3] == d)
        {
            return;
        }

        points.Clear();
        points.Add(a);
        points.Add(b);
        points.Add(c);
        points.Add(d);
    }

    private static void AutomationHidden(UIElement element) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(element, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);

    internal static Color ToColor(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static SolidColorBrush Brush(uint argb) => new(ToColor(argb));
}
