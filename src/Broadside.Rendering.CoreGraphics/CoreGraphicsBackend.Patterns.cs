using System.Runtime.CompilerServices;
using Broadside.Diagnostics;
using Broadside.Graphics;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics;

/// <summary>
/// Tiling patterns (ISO 32000-2 §8.7.3, spec D23): the pattern cell's display list rendered once per scale into a tile image, which is
/// repeated over the lattice within the filled path or the stroke's outline.
/// </summary>
/// <remarks>
/// The lattice is laid on the device pixels as the software rasterizer lays it and every tile drawn at its place in device space, rather
/// than through a <see cref="CGPattern"/>: a pattern's matrix is relative to the context's default user space, which CoreGraphics does
/// not expose (a view's context has a backing scale in it, a pattern cell's context another), so its tiles could not be put on the
/// pixels the rasterizer puts them on.
/// </remarks>
internal sealed partial class CoreGraphicsBackend
{
    /// <summary>The deepest nesting of tiling patterns drawn; a deeper cell is left out and reported.</summary>
    private const int MaxPatternDepth = 8;

    /// <summary>The most copies of the cell one tile holds where cells overlap their neighbours (a BBox larger than the steps).</summary>
    private const int MaxCellCopies = 256;

    /// <summary>The most pixels a tile image may have.</summary>
    private const long MaxTilePixels = 1L << 24;

    /// <summary>The most tiles one paint draws.</summary>
    private const long MaxTiles = 1L << 20;

    private static readonly Diagnostic TilingApproximated = new(
        "BackendTilingPatternApproximated",
        DiagnosticSeverity.Warning,
        "The CoreGraphics backend drew a tiling pattern only in part (ISO 32000-2 §8.7.3.1): its cells nest more deeply than it draws, " +
        "overlap their neighbours more often than it repeats them, or are too large or too many to draw; the parts left out were not drawn.");

    private readonly CoreGraphicsBackend? _owner;
    private readonly int _patternDepth;
    private ConditionalWeakTable<DisplayList, Tile> _tiles = [];
    private CoreGraphicsBackend? _cellBackend;

    /// <summary>Initializes a backend that renders pattern cells for <paramref name="owner"/>, sharing its image and shading caches.</summary>
    private CoreGraphicsBackend(CoreGraphicsBackend owner, int patternDepth)
    {
        _owner = owner;
        _patternDepth = patternDepth;
        _images = owner._images;
        _shadings = owner._shadings;
    }

    /// <summary>Gets how many pattern cells were rendered into tile images.</summary>
    internal int CellsRecorded { get; private set; }

    /// <summary>Fills the path with the tiling pattern (ISO 32000-2 §8.7.3.1): the cell repeated at its steps from the pattern's origin.</summary>
    private void FillPathWithTilingPatternCore(PathView path, FillRule rule, in DisplayTilingPattern pattern)
    {
        if (path.IsEmpty || _compositing.FillAlpha == 0 || GetTile(pattern, out Matrix toPixels) is not { } tile)
        {
            return;
        }

        CGContext context = _context!;
        context.SaveState();
        ClipToPath(context, path, rule);
        context.SetAlpha((nfloat)(_compositing.FillAlpha / 255.0));
        PaintTiles(tile, toPixels);
        context.RestoreState();
    }

    /// <summary>Paints the area the stroke covers with the tiling pattern (ISO 32000-2 §8.7.3.1, §8.5.3.2).</summary>
    private void StrokePathWithTilingPatternCore(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
    {
        if (path.IsEmpty || _compositing.StrokeAlpha == 0 || GetTile(pattern, out Matrix toPixels) is not { } tile)
        {
            return;
        }

        CGContext context = _context!;
        context.SaveState();
        if (ClipToStroke(context, path, style))
        {
            context.SetAlpha((nfloat)(_compositing.StrokeAlpha / 255.0));
            PaintTiles(tile, toPixels);
        }

        context.RestoreState();
    }

    /// <summary>Draws every tile of the lattice that meets the current clip; <paramref name="toPixels"/> maps the tile's space to the device.</summary>
    private void PaintTiles(Tile tile, in Matrix toPixels)
    {
        CGContext context = _context!;
        if (!toPixels.TryInvert(out Matrix fromPixels))
        {
            return;
        }

        // The clip's device bounds, back in the tile's space, give the lattice's visible rows and columns.
        CGRect clip = context.GetClipBoundingBox();
        if (clip.IsNull() || clip.IsEmpty)
        {
            return;
        }

        Matrix clipToTile = _pixels * fromPixels;
        PathPoint a = clipToTile.Transform(clip.Left, clip.Top);
        PathPoint b = clipToTile.Transform(clip.Right, clip.Top);
        PathPoint c = clipToTile.Transform(clip.Right, clip.Bottom);
        PathPoint d = clipToTile.Transform(clip.Left, clip.Bottom);
        CGRect area = tile.Area;
        double width = area.Width;
        double height = area.Height;
        double i0 = Math.Floor((Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) - area.X) / width) - 1;
        double i1 = Math.Ceiling((Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) - area.X) / width);
        double j0 = Math.Floor((Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) - area.Y) / height) - 1;
        double j1 = Math.Ceiling((Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) - area.Y) / height);
        if (!double.IsFinite(i0 + i1 + j0 + j1) || (i1 - i0 + 1) * (j1 - j0 + 1) > MaxTiles)
        {
            Report(ReportedKinds.TilingApproximated, TilingApproximated);
            return;
        }

        Concat(context, _fromPixels);
        Concat(context, toPixels);
        context.InterpolationQuality = toPixels.B == 0 && toPixels.C == 0 ? CGInterpolationQuality.None : CGInterpolationQuality.Low;
        CGImage image = tile.Image!;
        for (double j = j0; j <= j1; j++)
        {
            for (double i = i0; i <= i1; i++)
            {
                context.DrawImage(new CGRect(area.X + (i * width), area.Y + (j * height), tile.PixelWidth, tile.PixelHeight), image);
            }
        }
    }

    /// <summary>
    /// Returns the tile of the pattern's cell and the transform from the tile's space to the device pixels. The cell is rendered in
    /// pattern space scaled by the pattern's device scale, so that what the cell decides per device pixel (the thinnest line, a
    /// degenerate subpath) comes out at about a pixel; a later paint at another scale renders it again.
    /// </summary>
    private Tile? GetTile(in DisplayTilingPattern pattern, out Matrix toPixels)
    {
        toPixels = default;
        PdfTilingPattern model = pattern.Pattern;
        if (!_baseInvertible)
        {
            return null;
        }

        Matrix patternToPixels = Layout(model, pattern.Matrix * _device * _pixels);
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

        // The space the cell is rendered in: pattern space under the lattice's own scale when the lattice is upright, so that the
        // tiles are drawn by whole-pixel translation; otherwise under its average scale, the rest left to the tile's transform.
        bool upright = patternToPixels.B == 0 && patternToPixels.C == 0;
        Matrix recording = upright ? new Matrix(patternToPixels.A, 0, 0, patternToPixels.D, 0, 0) : Matrix.CreateScale(scale, scale);
        toPixels = upright
            ? Matrix.CreateTranslation(patternToPixels.E, patternToPixels.F)
            : Matrix.CreateScale(1 / scale, 1 / scale) * patternToPixels;
        if (!_tiles.TryGetValue(pattern.Cell, out Tile? tile))
        {
            tile = new Tile();
            _tiles.Add(pattern.Cell, tile);
        }

        if (tile.Image is not null && tile.Recording == recording)
        {
            return tile;
        }

        if (_patternDepth >= MaxPatternDepth)
        {
            Report(ReportedKinds.TilingApproximated, TilingApproximated);
            return null;
        }

        tile.Release();
        Record(tile, pattern, recording, xStep, yStep, width, height);
        return tile.Image is null ? null : tile;
    }

    /// <summary>
    /// Lays the lattice on the device as the software rasterizer does (Table 74, TilingType): constant spacing (types 1 and 3), and
    /// any step under a pixel, rounds the steps to whole device pixels, a distortion below one pixel; then the BBox's corner moves
    /// onto the pixel grid (less than half a pixel), so that tiles abut on whole pixels. No distortion (type 2) keeps the exact
    /// steps, every tile at its own sub-pixel position.
    /// </summary>
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
    /// Renders one tile: the rectangle of the steps from the BBox's corner, holding the cell and every neighbour whose BBox reaches
    /// into it (each clipped to its BBox), so that repeating the tile paints the lattice of §8.7.3.1 with overlaps.
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
            Report(ReportedKinds.TilingApproximated, TilingApproximated);
            (across, up) = (Math.Min(across, 16), Math.Min(up, 16));
        }

        CGRect area = Rectangle(recording, left, bottom, left + xStep, bottom + yStep);
        double pixelWidth = Math.Ceiling(area.Width - 1e-6);
        double pixelHeight = Math.Ceiling(area.Height - 1e-6);
        if (!(pixelWidth >= 1) || !(pixelHeight >= 1) || pixelWidth * pixelHeight > MaxTilePixels)
        {
            Report(ReportedKinds.TilingApproximated, TilingApproximated);
            return;
        }

        int w = (int)pixelWidth;
        int h = (int)pixelHeight;
        byte[] pixels = new byte[w * h * 4];
        using CGColorSpace space = CGColorSpace.CreateSrgb() ?? throw new PlatformNotSupportedException("CoreGraphics has no sRGB colour space.");
        using var bitmap = new CGBitmapContext(pixels, w, h, 8, w * 4, space, CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little);
        CoreGraphicsBackend cell = _cellBackend ??= new CoreGraphicsBackend(_owner ?? this, _patternDepth + 1);
        cell.ForceOutlines = ForceOutlines;

        // The bitmap's pixels are the tile's space moved to the area's corner (both y up).
        Matrix toBitmap = Matrix.CreateTranslation(-area.X, -area.Y);
        for (int j = 1 - up; j <= 0; j++)
        {
            for (int i = 1 - across; i <= 0; i++)
            {
                double dx = i * xStep;
                double dy = j * yStep;
                bitmap.SaveState();
                CGRect copy = Rectangle(recording, left + dx, bottom + dy, left + dx + width, bottom + dy + height);
                bitmap.ClipToRect(new CGRect(copy.X - area.X, copy.Y - area.Y, copy.Width, copy.Height));
                cell.Replay(pattern.Cell, bitmap, Matrix.CreateTranslation(dx, dy) * recording * toBitmap);
                MergeDiagnostics(cell);
                bitmap.RestoreState();
            }
        }

        tile.Image = bitmap.ToImage();
        tile.Recording = recording;
        tile.Area = area;
        tile.PixelWidth = w;
        tile.PixelHeight = h;
        (_owner ?? this).CellsRecorded++;
    }

    /// <summary>Takes the diagnostics a cell's replay reported that this replay has not.</summary>
    private void MergeDiagnostics(CoreGraphicsBackend cell)
    {
        foreach (Diagnostic diagnostic in cell.Diagnostics)
        {
            bool known = false;
            foreach (Diagnostic existing in Diagnostics)
            {
                known |= existing.Code == diagnostic.Code;
            }

            if (!known)
            {
                Diagnostics.Add(diagnostic);
            }
        }
    }

    /// <summary>The rectangle from (x0, y0) to (x1, y1) under a scale (an upright transform without translation).</summary>
    private static CGRect Rectangle(in Matrix scale, double x0, double y0, double x1, double y1)
    {
        double a = x0 * scale.A;
        double b = x1 * scale.A;
        double c = y0 * scale.D;
        double d = y1 * scale.D;
        return new CGRect(Math.Min(a, b), Math.Min(c, d), Math.Abs(b - a), Math.Abs(d - c));
    }

    /// <summary>Releases the tiles and the cell backend's caches.</summary>
    private void ClearTiles()
    {
        foreach (KeyValuePair<DisplayList, Tile> entry in _tiles)
        {
            entry.Value.Release();
        }

        _tiles = [];
    }

    /// <summary>A pattern cell rendered under one scale into a tile image.</summary>
    private sealed class Tile
    {
        public CGImage? Image { get; set; }

        public Matrix Recording { get; set; }

        /// <summary>Gets or sets the rectangle of the steps in the tile's space; the image's lower-left corner is its corner.</summary>
        public CGRect Area { get; set; }

        public int PixelWidth { get; set; }

        public int PixelHeight { get; set; }

        public void Release()
        {
            Image?.Dispose();
            Image = null;
        }
    }
}
