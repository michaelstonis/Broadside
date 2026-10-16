using System.Runtime.CompilerServices;
using Broadside.Diagnostics;
using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

public sealed partial class SkiaBackend
{
    /// <summary>
    /// Tiling patterns (spec D23): the pattern cell's display list recorded once into an <see cref="SKPicture"/> and repeated by a
    /// picture shader over the filled path or the stroke's outline.
    /// </summary>
    private sealed partial class Replayer
    {
        /// <summary>The deepest nesting of tiling patterns drawn; a deeper cell is left out and reported.</summary>
        private const int MaxPatternDepth = 8;

        /// <summary>The most copies of the cell one tile holds where cells overlap their neighbours (a BBox larger than the steps).</summary>
        private const int MaxCellCopies = 256;

        private static readonly Diagnostic TilingApproximated = new(
            "BackendTilingPatternApproximated",
            DiagnosticSeverity.Warning,
            "The Skia backend drew a tiling pattern only in part (ISO 32000-2 §8.7.3.1): its cells nest more deeply than it draws, or " +
            "overlap their neighbours more often than it repeats them; the parts left out were not drawn.");

        private readonly SKPaint _tile = new() { IsAntialias = true };
        private readonly Replayer? _owner;
        private readonly int _patternDepth;
        private ConditionalWeakTable<DisplayList, Tile> _tiles = [];
        private Replayer? _cellReplayer;

        /// <summary>Gets how many pattern cells this replayer recorded into pictures.</summary>
        public int CellsRecorded { get; private set; }

        /// <summary>Fills the path with the tiling pattern (ISO 32000-2 §8.7.3.1): the cell repeated at its steps from the pattern's origin.</summary>
        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern)
        {
            if (path.IsEmpty || _compositing.FillAlpha == 0 || GetTile(pattern, out Matrix toDevice) is not { } tile)
            {
                return;
            }

            SKCanvas canvas = _canvas!;
            canvas.Save();
            ClipToPath(path, rule);
            Prepare(_tile, _compositing.FillAlpha, _compositing.BlendMode);
            PaintTile(tile, toDevice);
            canvas.Restore();
        }

        /// <summary>Paints the area the stroke covers with the tiling pattern (ISO 32000-2 §8.7.3.1, §8.5.3.2).</summary>
        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
        {
            if (path.IsEmpty || _compositing.StrokeAlpha == 0 || GetTile(pattern, out Matrix toDevice) is not { } tile)
            {
                return;
            }

            if (BeginStrokeArea(path, style))
            {
                Prepare(_tile, 255, SKBlendMode.SrcOver);
                PaintTile(tile, toDevice);
                EndStrokeArea();
            }
        }

        private void PaintTile(Tile tile, in Matrix toDevice)
        {
            SKCanvas canvas = _canvas!;
            canvas.SetMatrix(in _base);
            SKMatrix matrix = ToSkia(toDevice);
            canvas.Concat(in matrix);
            _tile.Shader = tile.Shader;
            canvas.DrawPaint(_tile);
            _tile.Shader = null;
        }

        /// <summary>
        /// Returns the tile of the pattern's cell and the transform from the tile's space to the device. The cell is recorded in
        /// pattern space scaled by the pattern's device scale, so that what the cell decides per device pixel (the thinnest line, a
        /// degenerate subpath) comes out at about a pixel; a later paint at another scale records it again.
        /// </summary>
        private Tile? GetTile(in DisplayTilingPattern pattern, out Matrix toDevice)
        {
            toDevice = default;
            PdfTilingPattern model = pattern.Pattern;
            Matrix patternToDevice = Layout(model, pattern.Matrix * _device);
            double scale = Math.Sqrt(Math.Abs(patternToDevice.Determinant));
            double xStep = Math.Abs(model.XStep);
            double yStep = Math.Abs(model.YStep);
            PdfRectangle box = model.BoundingBox;
            double width = Math.Abs(box.Right - box.Left);
            double height = Math.Abs(box.Top - box.Bottom);
            if (!(scale > 0) || !double.IsFinite(scale) || !(xStep > 0) || !(yStep > 0) || !double.IsFinite(xStep * yStep * scale * scale) || !(width > 0) || !(height > 0))
            {
                return null;
            }

            // The space the cell is recorded in: pattern space under the lattice's own scale when the lattice is upright, so that the
            // tiles are drawn by whole-pixel translation; otherwise under its average scale, the rest left to the shader's transform.
            Matrix recording = patternToDevice.B == 0 && patternToDevice.C == 0
                ? new Matrix(patternToDevice.A, 0, 0, patternToDevice.D, 0, 0)
                : Matrix.CreateScale(scale, scale);
            toDevice = patternToDevice.B == 0 && patternToDevice.C == 0
                ? Matrix.CreateTranslation(patternToDevice.E, patternToDevice.F)
                : Matrix.CreateScale(1 / scale, 1 / scale) * patternToDevice;
            if (!_tiles.TryGetValue(pattern.Cell, out Tile? tile))
            {
                tile = new Tile();
                _tiles.Add(pattern.Cell, tile);
            }

            if (tile.Shader is not null && tile.Recording == recording)
            {
                return tile;
            }

            if (_patternDepth >= MaxPatternDepth)
            {
                Report(512, TilingApproximated);
                return null;
            }

            tile.Release();
            Record(tile, pattern, recording, xStep, yStep, width, height);
            return tile.Shader is null ? null : tile;
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
        /// Records one tile: the rectangle of the steps from the BBox's corner, holding the cell and every neighbour whose BBox reaches
        /// into it (each clipped to its BBox), so the picture shader's repetition paints the lattice of §8.7.3.1 with overlaps.
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
                Report(512, TilingApproximated);
                (across, up) = (Math.Min(across, 16), Math.Min(up, 16));
            }

            SKRect area = Rectangle(recording, left, bottom, left + xStep, bottom + yStep);
            Replayer cell = _cellReplayer ??= new Replayer(_owner ?? this, _patternDepth + 1) { ForceOutlines = ForceOutlines };
            cell.ForceOutlines = ForceOutlines;
            using var recorder = new SKPictureRecorder();
            SKCanvas canvas = recorder.BeginRecording(area);
            for (int j = 1 - up; j <= 0; j++)
            {
                for (int i = 1 - across; i <= 0; i++)
                {
                    double dx = i * xStep;
                    double dy = j * yStep;
                    canvas.Save();
                    canvas.ClipRect(Rectangle(recording, left + dx, bottom + dy, left + dx + width, bottom + dy + height), SKClipOperation.Intersect, antialias: true);
                    cell.Begin(_document!, canvas, Matrix.CreateTranslation(dx, dy) * recording);
                    try
                    {
                        pattern.Cell.Replay(cell);
                    }
                    finally
                    {
                        cell.End();
                    }

                    canvas.Restore();
                }
            }

            tile.Picture = recorder.EndRecording();
            tile.Shader = tile.Picture.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, SKFilterMode.Linear, SKMatrix.Identity, area);
            tile.Recording = recording;
            (_owner ?? this).CellsRecorded++;
        }

        /// <summary>The rectangle from (x0, y0) to (x1, y1) under a scale (an upright transform without translation).</summary>
        private static SKRect Rectangle(in Matrix scale, double x0, double y0, double x1, double y1) =>
            new SKRect((float)(x0 * scale.A), (float)(y0 * scale.D), (float)(x1 * scale.A), (float)(y1 * scale.D)).Standardized;

        private void DisposeTiling()
        {
            _tile.Dispose();
            _cellReplayer?.Dispose();
            foreach (KeyValuePair<DisplayList, Tile> entry in _tiles)
            {
                entry.Value.Release();
            }

            _tiles = [];
        }

        /// <summary>A pattern cell recorded under one scale, and its repeating shader.</summary>
        private sealed class Tile
        {
            public SKPicture? Picture { get; set; }

            public SKShader? Shader { get; set; }

            public Matrix Recording { get; set; }

            public void Release()
            {
                Shader?.Dispose();
                Picture?.Dispose();
                Shader = null;
                Picture = null;
            }
        }
    }
}
