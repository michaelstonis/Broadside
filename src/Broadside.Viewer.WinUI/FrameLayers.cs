using Broadside.Graphics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Broadside.Viewer.WinUI;

/// <summary>
/// Shows a <see cref="ViewerFrame"/> with XAML elements, reused from frame to frame: paper rectangles, tile images, search and
/// selection quadrilaterals, focus rings and selection handles, back to front. The XAML compositor draws them (on the GPU).
/// </summary>
/// <remarks>
/// Positions arrive in device pixels and are set in device-independent units (pixels divided by the rasterization scale). Brushes
/// are per instance: XAML objects belong to the thread that made them, and each window may have its own.
/// </remarks>
internal sealed class FrameLayers
{
    private readonly SolidColorBrush _lightPaper = new(Colors.White);
    private readonly SolidColorBrush _darkPaper = new(Colors.Black);
    private readonly SolidColorBrush _paperEdge = new(ColorHelper.FromArgb(0x28, 0, 0, 0));
    private readonly SolidColorBrush _selectionFill = new(ColorHelper.FromArgb(0x4D, 0x1A, 0x73, 0xE8));
    private readonly SolidColorBrush _matchFill = new(ColorHelper.FromArgb(0x66, 0xFF, 0xC8, 0x00));
    private readonly SolidColorBrush _currentMatchFill = new(ColorHelper.FromArgb(0x8C, 0xFF, 0x78, 0x00));
    private readonly SolidColorBrush _chromeStroke = new(ColorHelper.FromArgb(0xC0, 0x33, 0x7A, 0xFF));
    private readonly SolidColorBrush _handleFill = new(ColorHelper.FromArgb(0xFF, 0x33, 0x7A, 0xFF));

    private readonly Pool<Rectangle> _papers;
    private readonly Pool<Image> _tiles;
    private readonly Pool<Polygon> _quads;
    private readonly Pool<Rectangle> _rings;
    private readonly Pool<Rectangle> _bars;
    private readonly Pool<Ellipse> _knobs;

    public FrameLayers(Panel host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _papers = new Pool<Rectangle>(host, () => new Rectangle { StrokeThickness = 1, Stroke = _paperEdge });
        _tiles = new Pool<Image>(host, () => new Image { Stretch = Stretch.Fill });
        _quads = new Pool<Polygon>(host, () => new Polygon { Points = { default, default, default, default } });
        _rings = new Pool<Rectangle>(host, () => new Rectangle { Stroke = _chromeStroke });
        _bars = new Pool<Rectangle>(host, () => new Rectangle { Fill = _handleFill });
        _knobs = new Pool<Ellipse>(host, () => new Ellipse { Fill = _handleFill });
    }

    /// <summary>How many tile images the last frame showed (diagnostics and tests).</summary>
    public int TileCount { get; private set; }

    public void Apply(ViewerFrame frame, double ratio)
    {
        double scale = ratio > 0 ? 1 / ratio : 1;
        _papers.Begin();
        _tiles.Begin();
        _quads.Begin();
        _rings.Begin();
        _bars.Begin();
        _knobs.Begin();
        SolidColorBrush paper = frame.IsDark ? _darkPaper : _lightPaper;
        foreach (ref readonly ViewerFrame.Item item in frame.Items)
        {
            LayoutRectangle d = item.Destination;
            switch (item.Kind)
            {
                case ViewerFrame.ItemKind.Paper:
                    Rectangle sheet = _papers.Next();
                    sheet.Fill = paper;
                    Place(sheet, d, scale);
                    break;
                case ViewerFrame.ItemKind.Tile when item.Image is WriteableTileImage image:
                    Image tile = _tiles.Next();
                    if (!ReferenceEquals(tile.Source, image.Bitmap))
                    {
                        tile.Source = image.Bitmap;
                    }

                    Place(tile, d, scale);
                    break;
                case ViewerFrame.ItemKind.SearchMatch or ViewerFrame.ItemKind.CurrentMatch or ViewerFrame.ItemKind.Selection:
                    Polygon quad = _quads.Next();
                    quad.Fill = item.Kind switch
                    {
                        ViewerFrame.ItemKind.SearchMatch => _matchFill,
                        ViewerFrame.ItemKind.CurrentMatch => _currentMatchFill,
                        _ => _selectionFill,
                    };
                    PointCollection points = quad.Points;
                    points[0] = ToPoint(item.A, scale);
                    points[1] = ToPoint(item.B, scale);
                    points[2] = ToPoint(item.C, scale);
                    points[3] = ToPoint(item.D, scale);
                    break;
                case ViewerFrame.ItemKind.Chrome:
                    Rectangle ring = _rings.Next();
                    ring.StrokeThickness = Math.Max(item.Width * scale, 1);
                    Place(ring, d, scale);
                    break;
                case ViewerFrame.ItemKind.Handle:
                    double width = 2 * ratio;
                    Place(_bars.Next(), new LayoutRectangle(d.X - (width / 2), d.Y, width, d.Height), scale);
                    double radius = item.Width;
                    Place(_knobs.Next(), new LayoutRectangle(d.X - radius, d.Bottom, 2 * radius, 2 * radius), scale);
                    break;
            }
        }

        TileCount = _tiles.Used;
        _papers.End();
        _tiles.End();
        _quads.End();
        _rings.End();
        _bars.End();
        _knobs.End();
    }

    private static void Place(FrameworkElement element, in LayoutRectangle r, double scale)
    {
        Canvas.SetLeft(element, r.X * scale);
        Canvas.SetTop(element, r.Y * scale);
        element.Width = Math.Max(r.Width * scale, 0);
        element.Height = Math.Max(r.Height * scale, 0);
    }

    private static Point ToPoint(PathPoint p, double scale) => new(p.X * scale, p.Y * scale);

    /// <summary>
    /// Elements of one kind in their own canvas, stacked in the order the kinds were created (paper under tiles under marks), reused
    /// from frame to frame: the unused rest is collapsed.
    /// </summary>
    private sealed class Pool<T>
        where T : FrameworkElement
    {
        private readonly Canvas _canvas = new() { IsHitTestVisible = false };
        private readonly List<T> _elements = [];
        private readonly Func<T> _create;

        public Pool(Panel host, Func<T> create)
        {
            _create = create;
            host.Children.Add(_canvas);
        }

        public int Used { get; private set; }

        public void Begin() => Used = 0;

        public T Next()
        {
            T element;
            if (Used < _elements.Count)
            {
                element = _elements[Used];
                element.Visibility = Visibility.Visible;
            }
            else
            {
                element = _create();
                _elements.Add(element);
                _canvas.Children.Add(element);
            }

            Used++;
            return element;
        }

        public void End()
        {
            for (int i = Used; i < _elements.Count; i++)
            {
                if (_elements[i].Visibility != Visibility.Collapsed)
                {
                    _elements[i].Visibility = Visibility.Collapsed;
                    if (_elements[i] is Image image)
                    {
                        image.Source = null; // the tile's bitmap can go once nothing shows it
                    }
                }
            }
        }
    }
}
