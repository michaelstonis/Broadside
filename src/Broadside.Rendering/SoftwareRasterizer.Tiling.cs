using System.Runtime.InteropServices;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Tiling patterns (ISO 32000-2 §8.7.3): fills and strokes whose paint is a pattern cell replicated over the painted area.</summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The most tiles one paint visits; more paints nothing (only steps far below a pixel with a huge bounding box reach it).</summary>
    private const long MaxTiles = 1L << 22;

    /// <summary>The most pixels a cell is rendered into; a larger part of a cell paints nothing.</summary>
    private const long MaxCellPixels = 1L << 26;

    /// <summary>The backend's tiling half.</summary>
    /// <remarks>
    /// <para>
    /// A paint renders the cell's display list once, into a scratch bitmap at device resolution, through a child backend (one per
    /// nesting level, so a cell painting with another pattern renders that one through its own child). The tiles are then copied
    /// into a layer covering the painted area at whole-pixel offsets, later tiles over earlier ones where they overlap, and the layer
    /// composites over the page through the shape's coverage and the clip with the paint's compositor (§11.6.7: one object).
    /// </para>
    /// <para>
    /// Whole-pixel offsets come from Table 74's TilingType: for constant spacing (1 and 3) XStep and YStep are rounded to whole
    /// device pixels, distorting the cell by less than one pixel; for no distortion (2) each tile's position is rounded instead, so
    /// the spacing varies by less than one pixel. Either way the cell is shifted by less than half a pixel so that the corner of its
    /// bounding box falls on the pixel grid, which keeps the edges of abutting tiles from blending into seams when the pattern is
    /// axis aligned. Steps shorter than a pixel are rounded for every TilingType (the cell cannot be resolved at that size).
    /// </para>
    /// <para>
    /// Allocates nothing once warm: the cell bitmap, the layer and the child backends are kept for the next paint.
    /// </para>
    /// </remarks>
    private sealed partial class Backend
    {
        private Backend? _cellBackend;
        private Bitmap? _cellBitmap;
        private byte[] _cellPixels = [];
        private uint[] _tileLayer = [];

        /// <summary>Fills the path with the tiling pattern (§8.7.3.1).</summary>
        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern)
        {
            if (path.IsEmpty || _clip.IsEmpty || _fillCompositor.PaintsNothing)
            {
                return;
            }

            _scan.Reset(_width, _height);
            AddPath(path);
            PaintTiling(pattern, rule, _fillCompositor);
        }

        /// <summary>Tiles the area the stroke covers (§8.5.3.2, outlined like <see cref="StrokePath"/>) with the pattern.</summary>
        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
        {
            if (path.IsEmpty || _clip.IsEmpty || _strokeCompositor.PaintsNothing)
            {
                return;
            }

            _scan.Reset(_width, _height);
            _stroker.Stroke(path, style, _toDevice);
            PaintTiling(pattern, FillRule.NonZero, _strokeCompositor);
        }

        /// <summary>Paints the shape in the scan converter with the pattern's tiles.</summary>
        private void PaintTiling(in DisplayTilingPattern pattern, FillRule rule, Compositor compositor)
        {
            if (!_scan.TryGetBounds(out int left, out int top, out int right, out int bottom))
            {
                return;
            }

            _clip.GetBounds(out int clipLeft, out int clipTop, out int clipRight, out int clipBottom);
            var area = new PixelBox(
                Math.Max(Math.Max(left, clipLeft), 0),
                Math.Max(Math.Max(top, clipTop), 0),
                Math.Min(Math.Min(right, clipRight), _width),
                Math.Min(Math.Min(bottom, clipBottom), _height));
            if (area.IsEmpty
                || !TilingLayout.TryCreate(pattern.Pattern, pattern.Matrix * _device, area, out TilingLayout layout)
                || !layout.TryGetCellBox(area, out PixelBox cell))
            {
                return;
            }

            int layerLength = area.Width * area.Height;
            Pooled.Ensure(ref _tileLayer, layerLength);
            Span<uint> layer = _tileLayer.AsSpan(0, layerLength);
            if (_probe.Inspect(pattern.Cell).Blends)
            {
                // §11.6.7: the cell is a non-isolated group, so its blend modes see the backdrop; only a cell in the Normal mode alone
                // may be rendered once on a clear surface (NOTE 1).
                PaintNonIsolatedTiles(pattern.Cell, layout, area, layer);
                Bitmap ground = _target!;
                var blended = new TilingPainter(ground.Pixels, ground.Stride, layer, area.Left, area.Top, area.Width, area.Height, compositor);
                _scan.Paint(rule, ref blended);
                return;
            }

            Bitmap cellBitmap = RenderCell(pattern.Cell, layout.CellMatrix, cell);
            layer.Clear();
            ReadOnlySpan<uint> cellPixels = MemoryMarshal.Cast<byte, uint>(cellBitmap.Pixels[..(cell.Width * cell.Height * 4)]);
            for (long j = layout.JFirst; j <= layout.JLast; j++)
            {
                for (long i = layout.IFirst; i <= layout.ILast; i++)
                {
                    layout.GetOffset(i, j, out long offsetX, out long offsetY);
                    CopyTile(cellPixels, cell, offsetX, offsetY, layer, area);
                }
            }

            Bitmap target = _target!;
            var painter = new TilingPainter(target.Pixels, target.Stride, layer, area.Left, area.Top, area.Width, area.Height, compositor);
            _scan.Paint(rule, ref painter);
        }

        /// <summary>Renders the cell list into the cell bitmap, which covers <paramref name="box"/> of the device, on a clear surface.</summary>
        private Bitmap RenderCell(DisplayList cell, in Matrix cellMatrix, PixelBox box)
        {
            int length = box.Width * box.Height * 4;
            Pooled.Ensure(ref _cellPixels, length);
            Memory<byte> pixels = _cellPixels.AsMemory(0, length);
            pixels.Span.Clear();
            if (_cellBitmap is null)
            {
                _cellBitmap = new Bitmap(pixels, box.Width, box.Height, box.Width * 4);
            }
            else
            {
                _cellBitmap.Attach(pixels, box.Width, box.Height, box.Width * 4);
            }

            Backend child = _cellBackend ??= new Backend();
            child.Begin(_document!, _cellBitmap, cellMatrix * Matrix.CreateTranslation(-box.Left, -box.Top), box.Width, box.Height);
            child._pixelModel = _pixelModel;
            try
            {
                cell.Replay(child);
            }
            finally
            {
                child.End();
            }

            return _cellBitmap;
        }

        /// <summary>Lays the cell bitmap, moved by the tile's offset, over the layer where they meet (premultiplied source-over).</summary>
        private static void CopyTile(ReadOnlySpan<uint> cell, PixelBox cellBox, long offsetX, long offsetY, Span<uint> layer, PixelBox area)
        {
            long left = Math.Max(cellBox.Left + offsetX, area.Left);
            long right = Math.Min(cellBox.Right + offsetX, area.Right);
            long top = Math.Max(cellBox.Top + offsetY, area.Top);
            long bottom = Math.Min(cellBox.Bottom + offsetY, area.Bottom);
            if (left >= right || top >= bottom)
            {
                return;
            }

            int count = (int)(right - left);
            for (long y = top; y < bottom; y++)
            {
                ReadOnlySpan<uint> source = cell.Slice((int)(((y - offsetY - cellBox.Top) * cellBox.Width) + (left - offsetX - cellBox.Left)), count);
                Span<uint> destination = layer.Slice((int)(((y - area.Top) * area.Width) + (left - area.Left)), count);
                for (int x = 0; x < count; x++)
                {
                    uint pixel = source[x];
                    if (pixel == 0)
                    {
                        continue;
                    }

                    destination[x] = pixel >= 0xFF000000u || destination[x] == 0 ? pixel : Compositor.Over(pixel, destination[x], 255);
                }
            }
        }

        /// <summary>Returns the tiling buffers to their pools and disposes the child backends.</summary>
        private void DisposeTiling()
        {
            _cellBackend?.Dispose();
            _cellBitmap?.Dispose();
            Pooled.Return(ref _cellPixels);
            Pooled.Return(ref _tileLayer);
        }
    }

    /// <summary>A rectangle of device pixels, right and bottom exclusive.</summary>
    private readonly record struct PixelBox(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;

        public int Height => Bottom - Top;

        public bool IsEmpty => Right <= Left || Bottom <= Top;
    }

    /// <summary>
    /// Where the tiles of one paint go: the cell's transform to the device (adjusted to the pixel grid as TilingType allows), the
    /// device box of the cell at the origin of pattern space, the steps in device pixels and the range of tiles that can reach the
    /// painted area.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.7.3.1, Table 74 (BBox, XStep, YStep, TilingType).</remarks>
    private readonly struct TilingLayout
    {
        private readonly double _ux;
        private readonly double _uy;
        private readonly double _vx;
        private readonly double _vy;
        private readonly double _cellLeft;
        private readonly double _cellTop;
        private readonly double _cellRight;
        private readonly double _cellBottom;

        private TilingLayout(in Matrix cellMatrix, double ux, double uy, double vx, double vy, double cellLeft, double cellTop, double cellRight, double cellBottom, long iFirst, long iLast, long jFirst, long jLast)
        {
            CellMatrix = cellMatrix;
            _ux = ux;
            _uy = uy;
            _vx = vx;
            _vy = vy;
            _cellLeft = cellLeft;
            _cellTop = cellTop;
            _cellRight = cellRight;
            _cellBottom = cellBottom;
            IFirst = iFirst;
            ILast = iLast;
            JFirst = jFirst;
            JLast = jLast;
        }

        /// <summary>Gets the transform from pattern space to the device the cell at the origin is rendered with.</summary>
        public Matrix CellMatrix { get; }

        public long IFirst { get; }

        public long ILast { get; }

        public long JFirst { get; }

        public long JLast { get; }

        /// <summary>Lays out the pattern's tiles for a paint over <paramref name="area"/>; false when nothing can be painted.</summary>
        /// <param name="pattern">The pattern.</param>
        /// <param name="toDevice">Pattern space to device pixels.</param>
        /// <param name="area">The device pixels the paint can reach.</param>
        /// <param name="layout">The layout.</param>
        public static bool TryCreate(PdfTilingPattern pattern, in Matrix toDevice, PixelBox area, out TilingLayout layout)
        {
            layout = default;
            double xStep = pattern.XStep;
            double yStep = pattern.YStep;
            Matrix m = toDevice;
            double ux = m.A * xStep;
            double uy = m.B * xStep;
            double vx = m.C * yStep;
            double vy = m.D * yStep;
            if (!double.IsFinite(ux) || !double.IsFinite(uy) || !double.IsFinite(vx) || !double.IsFinite(vy) || !double.IsFinite(m.E) || !double.IsFinite(m.F))
            {
                return false;
            }

            // Table 74, TilingType: constant spacing rounds the steps to whole pixels (a distortion below one pixel).
            bool tiny = Math.Sqrt((ux * ux) + (uy * uy)) < 1 || Math.Sqrt((vx * vx) + (vy * vy)) < 1;
            bool snapped = false;
            if (pattern.TilingType != PdfTilingType.NoDistortion || tiny)
            {
                (double sux, double suy) = RoundStep(ux, uy);
                (double svx, double svy) = RoundStep(vx, vy);
                if ((sux * svy) - (suy * svx) != 0)
                {
                    (ux, uy, vx, vy) = (sux, suy, svx, svy);
                    m = new Matrix(ux / xStep, uy / xStep, vx / yStep, vy / yStep, m.E, m.F);
                    snapped = true;
                }
            }

            double determinant = (ux * vy) - (uy * vx);
            if (determinant == 0 || !double.IsFinite(determinant) || (!snapped && tiny))
            {
                return false;
            }

            // The bounding box's corner on the pixel grid: a shift of less than half a pixel.
            PdfRectangle box = pattern.BoundingBox;
            PathPoint corner = m.Transform(box.Left, box.Bottom);
            m = new Matrix(m.A, m.B, m.C, m.D, m.E + (Round(corner.X) - corner.X), m.F + (Round(corner.Y) - corner.Y));

            PathPoint p0 = m.Transform(box.Left, box.Bottom);
            PathPoint p1 = m.Transform(box.Right, box.Bottom);
            PathPoint p2 = m.Transform(box.Right, box.Top);
            PathPoint p3 = m.Transform(box.Left, box.Top);
            double cellLeft = Math.Floor(Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X)));
            double cellRight = Math.Ceiling(Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X)));
            double cellTop = Math.Floor(Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y)));
            double cellBottom = Math.Ceiling(Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y)));
            if (!double.IsFinite(cellLeft + cellRight + cellTop + cellBottom))
            {
                return false;
            }

            // A bounding box without area still paints one pixel (Table 74, NOTE 1; §10.7.4).
            cellRight = Math.Max(cellRight, cellLeft + 1);
            cellBottom = Math.Max(cellBottom, cellTop + 1);

            // The tile nearest the area becomes the one rendered, so that every box below stays within a few areas of the bitmap
            // even when pattern space puts the origin's cell far away. It moves by whole pixels: nothing changes phase.
            double nearX = ((area.Left + area.Right) / 2.0) - ((cellLeft + cellRight) / 2);
            double nearY = ((area.Top + area.Bottom) / 2.0) - ((cellTop + cellBottom) / 2);
            double nearI = Math.Round(((vy * nearX) - (vx * nearY)) / determinant);
            double nearJ = Math.Round(((ux * nearY) - (uy * nearX)) / determinant);
            double shiftX = Round((nearI * ux) + (nearJ * vx));
            double shiftY = Round((nearI * uy) + (nearJ * vy));
            if (!double.IsFinite(shiftX) || !double.IsFinite(shiftY))
            {
                return false;
            }

            m = new Matrix(m.A, m.B, m.C, m.D, m.E + shiftX, m.F + shiftY);
            cellLeft += shiftX;
            cellRight += shiftX;
            cellTop += shiftY;
            cellBottom += shiftY;

            // Offsets o with the tile (cell box + o) meeting the area, in lattice coordinates: a bound over the offset box's corners,
            // widened by one tile each way for the rounding of no-distortion offsets.
            double minX = area.Left - cellRight;
            double maxX = area.Right - cellLeft;
            double minY = area.Top - cellBottom;
            double maxY = area.Bottom - cellTop;
            double iMin = double.MaxValue;
            double iMax = double.MinValue;
            double jMin = double.MaxValue;
            double jMax = double.MinValue;
            foreach ((double x, double y) in (ReadOnlySpan<(double, double)>)[(minX, minY), (maxX, minY), (minX, maxY), (maxX, maxY)])
            {
                double i = ((vy * x) - (vx * y)) / determinant;
                double j = ((ux * y) - (uy * x)) / determinant;
                iMin = Math.Min(iMin, i);
                iMax = Math.Max(iMax, i);
                jMin = Math.Min(jMin, j);
                jMax = Math.Max(jMax, j);
            }

            iMin = Math.Floor(iMin) - 1;
            iMax = Math.Ceiling(iMax) + 1;
            jMin = Math.Floor(jMin) - 1;
            jMax = Math.Ceiling(jMax) + 1;
            if (!double.IsFinite(iMin + iMax + jMin + jMax) || (iMax - iMin + 1) * (jMax - jMin + 1) > MaxTiles)
            {
                return false;
            }

            layout = new TilingLayout(m, ux, uy, vx, vy, cellLeft, cellTop, cellRight, cellBottom, (long)iMin, (long)iMax, (long)jMin, (long)jMax);
            return true;
        }

        /// <summary>Returns the device offset of tile (i, j) from the cell at the origin, in whole pixels.</summary>
        public void GetOffset(long i, long j, out long x, out long y)
        {
            x = (long)Round((i * _ux) + (j * _vx));
            y = (long)Round((i * _uy) + (j * _vy));
        }

        /// <summary>
        /// Returns the part of the origin cell's device box that some tile shows inside <paramref name="area"/>: what the cell bitmap
        /// has to cover. False when no tile reaches the area or that part is too large to render.
        /// </summary>
        public bool TryGetCellBox(PixelBox area, out PixelBox box)
        {
            box = default;
            long minX = long.MaxValue;
            long maxX = long.MinValue;
            long minY = long.MaxValue;
            long maxY = long.MinValue;
            for (long j = JFirst; j <= JLast; j++)
            {
                for (long i = IFirst; i <= ILast; i++)
                {
                    GetOffset(i, j, out long x, out long y);
                    if (_cellLeft + x < area.Right && _cellRight + x > area.Left && _cellTop + y < area.Bottom && _cellBottom + y > area.Top)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            if (minX > maxX)
            {
                return false;
            }

            double left = Math.Max(_cellLeft, area.Left - (double)maxX);
            double right = Math.Min(_cellRight, area.Right - (double)minX);
            double top = Math.Max(_cellTop, area.Top - (double)maxY);
            double bottom = Math.Min(_cellBottom, area.Bottom - (double)minY);
            if (left >= right || top >= bottom || (right - left) * (bottom - top) > MaxCellPixels
                || left < int.MinValue / 2 || right > int.MaxValue / 2 || top < int.MinValue / 2 || bottom > int.MaxValue / 2)
            {
                return false;
            }

            box = new PixelBox((int)left, (int)top, (int)right, (int)bottom);
            return true;
        }

        /// <summary>Rounds a step to whole pixels, keeping at least one pixel of it along its larger component.</summary>
        private static (double X, double Y) RoundStep(double x, double y)
        {
            double rx = Round(x);
            double ry = Round(y);
            if (rx == 0 && ry == 0)
            {
                return Math.Abs(x) >= Math.Abs(y) ? (x < 0 ? -1 : 1, 0) : (0, y < 0 ? -1 : 1);
            }

            return (rx, ry);
        }

        private static double Round(double value) => Math.Floor(value + 0.5);
    }
}
