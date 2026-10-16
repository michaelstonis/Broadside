using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// Tiling patterns (ISO 32000-2 §8.7.3.1): the cell is drawn once per scale into a bitmap tile on a second device context of the same
/// device (a nested backend sharing the caches, at most <see cref="MaxPatternDepth"/> deep), its neighbours' overlap merged in, and the
/// lattice is laid out like the software rasterizer's. A lattice Direct2D can repeat exactly (whole-pixel steps, or rotated) is a
/// wrapping bitmap brush; an upright lattice with fractional steps draws each tile at its rounded pixel position under the clip.
/// </summary>
internal sealed unsafe partial class Direct2DBackend
{
    private const int MaxPatternDepth = 8;

    private const int MaxCellCopies = 256;

    private const long MaxTilePixels = 1L << 24;

    private const long MaxTiles = 1L << 20;

    private static readonly Diagnostic TilingApproximated = new(
        "BackendTilingPatternApproximated",
        DiagnosticSeverity.Warning,
        "The Direct2D backend drew a tiling pattern only in part (ISO 32000-2 §8.7.3.1): its cells nest more deeply than it draws, " +
        "overlap their neighbours more often than it repeats them, or are too large or too many to draw; the parts left out were not drawn.");

    private readonly Direct2DBackend? _owner;
    private readonly int _patternDepth;
    private ConditionalWeakTable<DisplayList, Tile> _tiles = [];
    private Direct2DBackend? _cellBackend;

    public Direct2DBackend()
    {
        Diagnostics = new ReadOnlyCollection<Diagnostic>(_diagnostics);
        _images = new ImageCache();
        _shadings = new ShadingCache();
    }

    private Direct2DBackend(Direct2DBackend owner, int patternDepth)
    {
        Diagnostics = new ReadOnlyCollection<Diagnostic>(_diagnostics);
        _owner = owner;
        _patternDepth = patternDepth;
        _images = owner._images;
        _shadings = owner._shadings;
    }

    /// <summary>Gets how many tiling-pattern cells were drawn into tiles.</summary>
    internal int CellsRecorded { get; private set; }

    /// <summary>Fills the path with a tiling pattern (ISO 32000-2 §8.7.3.1).</summary>
    private void FillPathWithTilingPatternCore(PathView path, FillRule rule, in DisplayTilingPattern pattern)
    {
        if (path.IsEmpty || _compositing.FillAlpha == 0 || GetTile(pattern, out Matrix toPixels, out bool upright) is not { } tile)
        {
            return;
        }

        int mark = _clipCount;
        try
        {
            float opacity = _compositing.FillAlpha / 255f;
            if (upright && !tile.Whole)
            {
                IntersectClip(path, rule);
                DrawTiles(tile, toPixels, opacity);
            }
            else
            {
                UseTile(tile, toPixels, upright, opacity);
                FillWith(path, rule);
            }
        }
        finally
        {
            EndPaint(mark);
        }
    }

    /// <summary>Strokes the path with a tiling pattern (ISO 32000-2 §8.7.3.1).</summary>
    private void StrokePathWithTilingPatternCore(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
    {
        if (path.IsEmpty || _compositing.StrokeAlpha == 0 || GetTile(pattern, out Matrix toPixels, out bool upright) is not { } tile || !ConfigurePen(style))
        {
            return;
        }

        int mark = _clipCount;
        try
        {
            UseTile(tile, toPixels, upright, _compositing.StrokeAlpha / 255f);
            StrokeWith(path, style);
        }
        finally
        {
            EndPaint(mark);
        }
    }

    /// <summary>Makes the tile, repeated by Direct2D, the current paint: tile pixels map to the lattice's space, then to the device.</summary>
    private void UseTile(Tile tile, in Matrix toPixels, bool upright, float opacity)
    {
        Matrix placement = new Matrix(tile.Area.Width / tile.PixelWidth, 0, 0, tile.Area.Height / tile.PixelHeight, tile.Area.X, tile.Area.Y);
        UseBitmap(
            (ID2D1Bitmap*)tile.Bitmap,
            placement * toPixels,
            upright ? D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_NEAREST_NEIGHBOR : D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR,
            D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_WRAP,
            opacity);
    }

    /// <summary>Draws the tiles of the lattice over the clip's bounds, each at its rounded pixel position (an upright lattice).</summary>
    private void DrawTiles(Tile tile, in Matrix toPixels, float opacity)
    {
        D2D_RECT_F clip = ClipBounds;
        if (!(clip.right > clip.left && clip.bottom > clip.top))
        {
            return;
        }

        TileArea area = tile.Area;
        double offsetX = toPixels.E;
        double offsetY = toPixels.F;
        double i0 = Math.Floor((clip.left - offsetX - area.X) / area.Width) - 1;
        double i1 = Math.Ceiling((clip.right - offsetX - area.X) / area.Width);
        double j0 = Math.Floor((clip.top - offsetY - area.Y) / area.Height) - 1;
        double j1 = Math.Ceiling((clip.bottom - offsetY - area.Y) / area.Height);
        if (!double.IsFinite(i0 + i1 + j0 + j1) || (i1 - i0 + 1) * (j1 - j0 + 1) > MaxTiles)
        {
            Skip(SkippedOperations.TilingApproximated, TilingApproximated);
            return;
        }

        SetWorldTransform(Matrix.Identity);
        ID2D1Bitmap* bitmap = (ID2D1Bitmap*)tile.Bitmap;
        for (double j = j0; j <= j1; j++)
        {
            float top = (float)Math.Floor(area.Y + (j * area.Height) + offsetY + 0.5);
            for (double i = i0; i <= i1; i++)
            {
                float left = (float)Math.Floor(area.X + (i * area.Width) + offsetX + 0.5);
                var destination = new D2D_RECT_F { left = left, top = top, right = left + tile.PixelWidth, bottom = top + tile.PixelHeight };
                _context->DrawBitmap(bitmap, &destination, opacity, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_NEAREST_NEIGHBOR, null, null);
            }
        }
    }

    private Tile? GetTile(in DisplayTilingPattern pattern, out Matrix toPixels, out bool upright)
    {
        toPixels = default;
        upright = false;
        PdfTilingPattern model = pattern.Pattern;
        Matrix patternToPixels = Layout(model, pattern.Matrix * _device);
        double scale = Math.Sqrt(Math.Abs(patternToPixels.Determinant));
        double xStep = Math.Abs(model.XStep);
        double yStep = Math.Abs(model.YStep);
        PdfRectangle box = model.BoundingBox;
        double width = Math.Abs(box.Right - box.Left);
        double height = Math.Abs(box.Top - box.Bottom);
        if (!(scale > 0) || !double.IsFinite(scale) || !(xStep > 0) || !(yStep > 0) || !double.IsFinite(xStep * yStep * scale * scale) || !(width > 0) || !(height > 0))
        {
            return null;
        }

        // The space the cell is drawn in: pattern space under the lattice's own scale when the lattice is upright, so that the tiles
        // land on the device by translation; otherwise under its average scale, the rest left to the brush's transform.
        upright = patternToPixels.B == 0 && patternToPixels.C == 0;
        Matrix recording = upright ? new Matrix(patternToPixels.A, 0, 0, patternToPixels.D, 0, 0) : Matrix.CreateScale(scale, scale);
        toPixels = upright
            ? Matrix.CreateTranslation(patternToPixels.E, patternToPixels.F)
            : Matrix.CreateScale(1 / scale, 1 / scale) * patternToPixels;
        if (!_tiles.TryGetValue(pattern.Cell, out Tile? tile))
        {
            tile = new Tile();
            _tiles.Add(pattern.Cell, tile);
        }

        if (tile.Bitmap is not null && tile.Recording == recording)
        {
            return tile;
        }

        if (_patternDepth >= MaxPatternDepth)
        {
            Skip(SkippedOperations.TilingApproximated, TilingApproximated);
            return null;
        }

        tile.Release();
        Record(tile, pattern, recording, xStep, yStep, width, height);
        return tile.Bitmap is null ? null : tile;
    }

    /// <summary>The software rasterizer's lattice (its <c>TilingLayout</c>): steps rounded to whole pixels unless the pattern asks for no distortion, the BBox corner on a pixel.</summary>
    private static Matrix Layout(PdfTilingPattern pattern, in Matrix toDevice)
    {
        Matrix m = toDevice;
        double xStep = pattern.XStep;
        double yStep = pattern.YStep;
        double ux = m.A * xStep;
        double uy = m.B * xStep;
        double vx = m.C * yStep;
        double vy = m.D * yStep;
        if (!double.IsFinite(ux) || !double.IsFinite(uy) || !double.IsFinite(vx) || !double.IsFinite(vy) || xStep == 0 || yStep == 0)
        {
            return m;
        }

        bool tiny = Math.Sqrt((ux * ux) + (uy * uy)) < 1 || Math.Sqrt((vx * vx) + (vy * vy)) < 1;
        if (pattern.TilingType != PdfTilingType.NoDistortion || tiny)
        {
            (double sux, double suy) = RoundStep(ux, uy);
            (double svx, double svy) = RoundStep(vx, vy);
            if ((sux * svy) - (suy * svx) != 0)
            {
                m = new Matrix(sux / xStep, suy / xStep, svx / yStep, svy / yStep, m.E, m.F);
            }
        }

        PdfRectangle box = pattern.BoundingBox;
        PathPoint corner = m.Transform(box.Left, box.Bottom);
        return new Matrix(m.A, m.B, m.C, m.D, m.E + (RoundHalfUp(corner.X) - corner.X), m.F + (RoundHalfUp(corner.Y) - corner.Y));
    }

    private static (double X, double Y) RoundStep(double x, double y)
    {
        double rx = RoundHalfUp(x);
        double ry = RoundHalfUp(y);
        if (rx == 0 && ry == 0)
        {
            return Math.Abs(x) >= Math.Abs(y) ? (x < 0 ? -1 : 1, 0) : (0, y < 0 ? -1 : 1);
        }

        return (rx, ry);
    }

    private static double RoundHalfUp(double value) => Math.Floor(value + 0.5);

    /// <summary>
    /// Draws the cell into a new tile: the copies of the cell one step left and down overlapping the tile's area (its BBox larger than
    /// the steps) are drawn too, each clipped to its BBox, so that the tile repeats seamlessly.
    /// </summary>
    private void Record(Tile tile, in DisplayTilingPattern pattern, in Matrix recording, double xStep, double yStep, double width, double height)
    {
        PdfRectangle box = pattern.Pattern.BoundingBox;
        double left = Math.Min(box.Left, box.Right);
        double bottom = Math.Min(box.Bottom, box.Top);
        int across = (int)Math.Min(Math.Ceiling(width / xStep), MaxCellCopies);
        int up = (int)Math.Min(Math.Ceiling(height / yStep), MaxCellCopies);
        if ((long)across * up > MaxCellCopies)
        {
            Skip(SkippedOperations.TilingApproximated, TilingApproximated);
            (across, up) = (Math.Min(across, 16), Math.Min(up, 16));
        }

        TileArea area = Scaled(recording, left, bottom, left + xStep, bottom + yStep);
        double pixelWidth = Math.Ceiling(area.Width - 1e-6);
        double pixelHeight = Math.Ceiling(area.Height - 1e-6);
        Direct2DSurface surface = _surface!;
        if (!(pixelWidth >= 1) || !(pixelHeight >= 1) || pixelWidth * pixelHeight > MaxTilePixels || pixelWidth > surface.MaximumSize || pixelHeight > surface.MaximumSize)
        {
            Skip(SkippedOperations.TilingApproximated, TilingApproximated);
            return;
        }

        int w = (int)pixelWidth;
        int h = (int)pixelHeight;
        ID2D1DeviceContext* context = surface.CellContext(_patternDepth + 1);
        var properties = new D2D1_BITMAP_PROPERTIES1
        {
            pixelFormat = new D2D1_PIXEL_FORMAT { format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED },
            dpiX = 96,
            dpiY = 96,
            bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET,
        };
        ID2D1Bitmap1* bitmap;
        context->CreateBitmap(new D2D_SIZE_U { width = (uint)w, height = (uint)h }, null, 0, &properties, &bitmap);
        Direct2DBackend cell = _cellBackend ??= new Direct2DBackend(_owner ?? this, _patternDepth + 1);
        cell.ForceOutlines = ForceOutlines;
        HRESULT result;
        context->SetTarget((ID2D1Image*)bitmap);
        context->BeginDraw();
        try
        {
            var clear = default(D2D1_COLOR_F);
            context->Clear(&clear);

            // The tile's pixels are the recording space moved to the area's corner.
            Matrix toBitmap = Matrix.CreateTranslation(-area.X, -area.Y);
            for (int j = 1 - up; j <= 0; j++)
            {
                for (int i = 1 - across; i <= 0; i++)
                {
                    double dx = i * xStep;
                    double dy = j * yStep;
                    TileArea copy = Scaled(recording, left + dx, bottom + dy, left + dx + width, bottom + dy + height);
                    cell.Begin(surface, context, w, h, _fonts, _document!, Matrix.CreateTranslation(dx, dy) * recording * toBitmap);
                    try
                    {
                        cell.PushRectangle(new D2D_RECT_F
                        {
                            left = (float)(copy.X - area.X),
                            top = (float)(copy.Y - area.Y),
                            right = (float)(copy.X - area.X + copy.Width),
                            bottom = (float)(copy.Y - area.Y + copy.Height),
                        });
                        pattern.Cell.Replay(cell);
                    }
                    finally
                    {
                        cell.End();
                    }

                    foreach (Diagnostic diagnostic in cell._diagnostics)
                    {
                        Add(diagnostic);
                    }
                }
            }
        }
        finally
        {
            result = context->EndDraw(null, null);
            context->SetTarget(null);
        }

        if (result.Failed || Root._rasterizePage)
        {
            // The device was lost while drawing the cell (the page's own EndDraw reports it and the render is drawn again), or the
            // cell blends with its backdrop and the software rasterizer draws the page: the tile is not kept.
            bitmap->Release();
            return;
        }

        tile.Bitmap = bitmap;
        tile.Recording = recording;
        tile.Area = area;
        tile.PixelWidth = w;
        tile.PixelHeight = h;
        tile.Whole = Math.Abs(area.Width - w) < 1e-6 && Math.Abs(area.Height - h) < 1e-6;
        (_owner ?? this).CellsRecorded++;
    }

    private static TileArea Scaled(in Matrix scale, double x0, double y0, double x1, double y1)
    {
        double a = x0 * scale.A;
        double b = x1 * scale.A;
        double c = y0 * scale.D;
        double d = y1 * scale.D;
        return new TileArea(Math.Min(a, b), Math.Min(c, d), Math.Abs(b - a), Math.Abs(d - c));
    }

    /// <summary>Releases the tiles of this backend and of the backends drawing nested cells.</summary>
    private void ClearTiles()
    {
        foreach (KeyValuePair<DisplayList, Tile> entry in _tiles)
        {
            entry.Value.Release();
        }

        _tiles = [];
        _cellBackend?.ClearTiles();
    }

    /// <summary>A rectangle of the recording space: its smaller corner and its size.</summary>
    private readonly record struct TileArea(double X, double Y, double Width, double Height);

    private sealed class Tile
    {
        public ID2D1Bitmap1* Bitmap { get; set; }

        public Matrix Recording { get; set; }

        public TileArea Area { get; set; }

        public int PixelWidth { get; set; }

        public int PixelHeight { get; set; }

        /// <summary>Gets or sets a value indicating whether the area is whole pixels, so that the tile repeats exactly at its own size.</summary>
        public bool Whole { get; set; }

        public void Release()
        {
            if (Bitmap is not null)
            {
                Bitmap->Release();
                Bitmap = null;
            }
        }
    }
}
