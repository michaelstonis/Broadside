using System.Runtime.CompilerServices;
using Broadside.Diagnostics;
using Broadside.Graphics;
using AndroidBitmap = Android.Graphics.Bitmap;
using AndroidCanvas = Android.Graphics.Canvas;
using BitmapShader = Android.Graphics.BitmapShader;
using Matrix = Broadside.Graphics.Matrix;
using Paint = Android.Graphics.Paint;
using PaintFlags = Android.Graphics.PaintFlags;
using Shader = Android.Graphics.Shader;

namespace Broadside.Rendering.Android;

public sealed partial class AndroidCanvasBackend
{
    /// <summary>
    /// Tiling patterns (ISO 32000-2 §8.7.3, spec D23): the pattern cell's display list rendered once per scale into a bitmap tile, which
    /// a repeating bitmap shader lays over the lattice within the filled path or the stroke's outline.
    /// </summary>
    /// <remarks>
    /// The lattice is laid on the device pixels as the software rasterizer lays it (types 1 and 3 round the steps to whole pixels, so
    /// the tile repeats by whole pixels and the shader repeats it exactly); a lattice with sub-pixel steps (type 2) or a rotated one
    /// draws each tile at its own place, as the rasterizer does.
    /// </remarks>
    private sealed partial class Backend
    {
        /// <summary>The deepest nesting of tiling patterns drawn; a deeper cell is left out and reported.</summary>
        private const int MaxPatternDepth = 8;

        /// <summary>The most copies of the cell one tile holds where cells overlap their neighbours (a BBox larger than the steps).</summary>
        private const int MaxCellCopies = 256;

        /// <summary>The most pixels a tile bitmap may have.</summary>
        private const long MaxTilePixels = 1L << 24;

        /// <summary>The most tiles one paint draws one by one.</summary>
        private const long MaxTiles = 1L << 20;

        private static readonly Shader.TileMode Repeat = Shader.TileMode.Repeat!;
        private static readonly AndroidBitmap.Config TileConfig = AndroidBitmap.Config.Argb8888!;

        private static readonly Diagnostic TilingApproximated = new(
            "BackendTilingPatternApproximated",
            DiagnosticSeverity.Warning,
            "The Android backend drew a tiling pattern only in part (ISO 32000-2 §8.7.3.1): its cells nest more deeply than it draws, " +
            "overlap their neighbours more often than it repeats them, or are too large or too many to draw; the parts left out were not drawn.");

        private readonly Paint _tilePaint = new(PaintFlags.AntiAlias);
        private BlendMode _tileBlendMode;
        private ConditionalWeakTable<DisplayList, Tile> _tiles = [];
        private Backend? _cellBackend;

        /// <summary>Gets how many pattern cells were rendered into tiles.</summary>
        public int CellsRecorded { get; private set; }

        /// <summary>Fills the path with the tiling pattern (ISO 32000-2 §8.7.3.1): the cell repeated at its steps from the pattern's origin.</summary>
        private void FillPathWithTilingPatternCore(PathView path, FillRule rule, in DisplayTilingPattern pattern)
        {
            if (path.IsEmpty || _compositing.FillAlpha == 0 || GetTile(pattern, out Matrix toPixels) is not { } tile)
            {
                return;
            }

            AndroidCanvas canvas = _canvas!;
            int count = canvas.Save();
            BuildDevicePath(path, rule);
            canvas.ClipPath(_path);
            Prepare(_tilePaint, ref _tileBlendMode, _compositing.FillAlpha, _compositing.BlendMode);
            PaintTiles(tile, toPixels);
            canvas.RestoreToCount(count);
        }

        /// <summary>Paints the area the stroke covers with the tiling pattern (ISO 32000-2 §8.7.3.1, §8.5.3.2).</summary>
        private void StrokePathWithTilingPatternCore(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
        {
            if (path.IsEmpty || _compositing.StrokeAlpha == 0 || GetTile(pattern, out Matrix toPixels) is not { } tile)
            {
                return;
            }

            if (BeginStrokeArea(path, style, out int count))
            {
                Prepare(_tilePaint, ref _tileBlendMode, 255, BlendMode.Normal);
                PaintTiles(tile, toPixels);
                _canvas!.RestoreToCount(count);
            }
        }

        /// <summary>Paints the lattice of tiles over the current clip; <paramref name="toPixels"/> maps the tile's space to the device pixels.</summary>
        private void PaintTiles(Tile tile, in Matrix toPixels)
        {
            AndroidCanvas canvas = _canvas!;
            if (!toPixels.TryInvert(out Matrix fromPixels) || !canvas.GetClipBounds(_clip))
            {
                return;
            }

            int count = canvas.Save();
            ConcatFromPixels();
            if (!Concat(toPixels))
            {
                canvas.RestoreToCount(count);
                return;
            }

            AndroidBitmap bitmap = tile.Bitmap!;
            if (tile.Shader is { } shader && SetMatrix(Matrix.CreateTranslation(tile.X, tile.Y)))
            {
                // Whole-pixel steps: the shader repeats the tile from its place, exactly where the lattice puts each copy.
                shader.SetLocalMatrix(_matrix);
                _tilePaint.SetShader(shader);
                _tilePaint.FilterBitmap = false;
                canvas.DrawPaint(_tilePaint);
                _tilePaint.SetShader(null);
                canvas.RestoreToCount(count);
                return;
            }

            // The clip's bounds, back in the tile's space, give the lattice's visible rows and columns.
            Matrix clipToTile = _pixels * fromPixels;
            PathPoint a = clipToTile.Transform(_clip.Left, _clip.Top);
            PathPoint b = clipToTile.Transform(_clip.Right, _clip.Top);
            PathPoint c = clipToTile.Transform(_clip.Right, _clip.Bottom);
            PathPoint d = clipToTile.Transform(_clip.Left, _clip.Bottom);
            double width = tile.Width;
            double height = tile.Height;
            double i0 = Math.Floor((Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) - tile.X) / width) - 1;
            double i1 = Math.Ceiling((Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) - tile.X) / width);
            double j0 = Math.Floor((Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) - tile.Y) / height) - 1;
            double j1 = Math.Ceiling((Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) - tile.Y) / height);
            if (!double.IsFinite(i0 + i1 + j0 + j1) || (i1 - i0 + 1) * (j1 - j0 + 1) > MaxTiles)
            {
                Report(Reported.TilingApproximated, TilingApproximated);
                canvas.RestoreToCount(count);
                return;
            }

            _tilePaint.FilterBitmap = toPixels.B != 0 || toPixels.C != 0;
            for (double j = j0; j <= j1; j++)
            {
                for (double i = i0; i <= i1; i++)
                {
                    canvas.DrawBitmap(bitmap, (float)(tile.X + (i * width)), (float)(tile.Y + (j * height)), _tilePaint);
                }
            }

            canvas.RestoreToCount(count);
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
            // tiles are placed by translation; otherwise under its average scale, the rest left to the tile's transform.
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

            if (tile.Bitmap is not null && tile.Recording == recording)
            {
                return tile;
            }

            if (_patternDepth >= MaxPatternDepth)
            {
                Report(Reported.TilingApproximated, TilingApproximated);
                return null;
            }

            tile.Release();
            Record(tile, pattern, recording, upright, xStep, yStep, width, height);
            return tile.Bitmap is null ? null : tile;
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
        private void Record(Tile tile, in DisplayTilingPattern pattern, in Matrix recording, bool upright, double xStep, double yStep, double width, double height)
        {
            PdfRectangle box = pattern.Pattern.BoundingBox;
            double left = Math.Min(box.Left, box.Right);
            double bottom = Math.Min(box.Bottom, box.Top);
            int across = (int)Math.Min(Math.Ceiling(width / xStep), MaxCellCopies);
            int up = (int)Math.Min(Math.Ceiling(height / yStep), MaxCellCopies);
            if ((long)across * up > MaxCellCopies)
            {
                Report(Reported.TilingApproximated, TilingApproximated);
                (across, up) = (Math.Min(across, 16), Math.Min(up, 16));
            }

            (double x, double y, double w, double h) = Rectangle(recording, left, bottom, left + xStep, bottom + yStep);
            double pixelWidth = Math.Ceiling(w - 1e-6);
            double pixelHeight = Math.Ceiling(h - 1e-6);
            if (!(pixelWidth >= 1) || !(pixelHeight >= 1) || pixelWidth * pixelHeight > MaxTilePixels)
            {
                Report(Reported.TilingApproximated, TilingApproximated);
                return;
            }

            AndroidBitmap bitmap = AndroidBitmap.CreateBitmap((int)pixelWidth, (int)pixelHeight, TileConfig);
            using (var canvas = new AndroidCanvas(bitmap))
            {
                Backend cell = _cellBackend ??= new Backend(_owner ?? this, _patternDepth + 1);
                cell.ForceOutlines = ForceOutlines;

                // The bitmap's pixels are the tile's space moved to the area's corner (both y down).
                Matrix toBitmap = Matrix.CreateTranslation(-x, -y);
                for (int j = 1 - up; j <= 0; j++)
                {
                    for (int i = 1 - across; i <= 0; i++)
                    {
                        double dx = i * xStep;
                        double dy = j * yStep;
                        int count = canvas.Save();
                        (double cx, double cy, double cw, double ch) = Rectangle(recording, left + dx, bottom + dy, left + dx + width, bottom + dy + height);
                        canvas.ClipRect((float)(cx - x), (float)(cy - y), (float)(cx - x + cw), (float)(cy - y + ch));
                        cell.Begin(_document!, canvas, Matrix.CreateTranslation(dx, dy) * recording * toBitmap);
                        try
                        {
                            pattern.Cell.Replay(cell);
                        }
                        finally
                        {
                            cell.End();
                        }

                        MergeDiagnostics(cell);
                        canvas.RestoreToCount(count);
                    }
                }
            }

            tile.Bitmap = bitmap;
            tile.Recording = recording;
            (tile.X, tile.Y, tile.Width, tile.Height) = (x, y, w, h);

            // Whole-pixel steps (always for types 1 and 3 on an upright lattice): the tile's size is the period, so a shader repeats it.
            if (upright && Math.Abs(w - pixelWidth) < 1e-6 && Math.Abs(h - pixelHeight) < 1e-6)
            {
                tile.Shader = new BitmapShader(bitmap, Repeat, Repeat);
            }

            (_owner ?? this).CellsRecorded++;
        }

        /// <summary>Takes the diagnostics a cell's replay reported that this replay has not.</summary>
        private void MergeDiagnostics(Backend cell)
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

        /// <summary>The rectangle from (x0, y0) to (x1, y1) under a scale (an upright transform without translation): its smaller corner and size.</summary>
        private static (double X, double Y, double Width, double Height) Rectangle(in Matrix scale, double x0, double y0, double x1, double y1)
        {
            double a = x0 * scale.A;
            double b = x1 * scale.A;
            double c = y0 * scale.D;
            double d = y1 * scale.D;
            return (Math.Min(a, b), Math.Min(c, d), Math.Abs(b - a), Math.Abs(d - c));
        }

        /// <summary>Releases the tiles.</summary>
        private void ClearTiles()
        {
            foreach (KeyValuePair<DisplayList, Tile> entry in _tiles)
            {
                entry.Value.Release();
            }

            _tiles = [];
        }

        /// <summary>A pattern cell rendered under one scale into a bitmap tile.</summary>
        private sealed class Tile
        {
            public AndroidBitmap? Bitmap { get; set; }

            /// <summary>Gets or sets the repeating shader over <see cref="Bitmap"/>, when the steps are whole pixels.</summary>
            public BitmapShader? Shader { get; set; }

            public Matrix Recording { get; set; }

            /// <summary>Gets or sets the rectangle of the steps in the tile's space; the bitmap's top-left corner is its corner.</summary>
            public double X { get; set; }

            public double Y { get; set; }

            public double Width { get; set; }

            public double Height { get; set; }

            public void Release()
            {
                Shader?.Dispose();
                Shader = null;
                Bitmap?.Recycle();
                Bitmap?.Dispose();
                Bitmap = null;
            }
        }
    }
}
