using System.Buffers;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The pixel difference of two pages rendered alike: an overlay of both, the changed pixels alone, how much changed and where.
/// For changes text comparison cannot show, such as moved images, color changes and redrawn figures.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.3.2.2 (device space), §8.3.2.3 (default user space), §7.7.3.3 Table 31 (<c>Rotate</c>), §14.11.2 (crop box) and
/// §10.7.4 (pixels painted). Both pages are rendered by the <see cref="SoftwareRasterizer"/> through display lists, at the same
/// resolution, each with its own rotation, on white, onto a canvas the size of the larger page; the area a smaller page does not
/// cover is white. A pixel changed when one of its channels differs by more than <see cref="VisualComparisonOptions.Tolerance"/>.
/// </para>
/// <para>
/// Rendering records its repairs in each document's diagnostics as usual; the comparison adds none. Comparing reads both
/// documents, so several pairs of pages may be compared at once on different threads. The bitmaps are pooled: dispose the
/// comparison to return them.
/// </para>
/// </remarks>
public sealed class VisualComparison : IDisposable
{
    /// <summary>The side of the square cells changed pixels are grouped by into regions.</summary>
    internal const int CellSize = 8;

    private const uint Opaque = 0xFF000000u;

    private VisualComparison(Bitmap overlay, Bitmap difference, double changedFraction, IReadOnlyList<PdfRectangle> changedRegions)
    {
        Overlay = overlay;
        Difference = difference;
        ChangedFraction = changedFraction;
        ChangedRegions = changedRegions;
    }

    /// <summary>
    /// Gets both pages in one bitmap: what only the old page shows in red, what only the new page shows in cyan (where both show
    /// something that changed, the darker of the two), and the pixels that did not change in light gray.
    /// </summary>
    public Bitmap Overlay { get; }

    /// <summary>Gets the changed pixels alone, colored as in <see cref="Overlay"/>; every other pixel is transparent.</summary>
    public Bitmap Difference { get; }

    /// <summary>Gets the fraction of the canvas's pixels that changed, from 0 (the pages look the same) to 1.</summary>
    public double ChangedFraction { get; }

    /// <summary>
    /// Gets the changed areas, in the new page's default user space: the bounds of each group of changed pixels that touch on a grid of
    /// 8 × 8 pixel cells (diagonally too), with at least <see cref="VisualComparisonOptions.MinRegionArea"/> changed pixels, top to
    /// bottom. An area outside the new page (where only a larger old page shows) extends its user space past the crop box.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.3.2.3; mapped back through <see cref="RenderOptions.GetDeviceTransform"/>.</remarks>
    public IReadOnlyList<PdfRectangle> ChangedRegions { get; }

    /// <summary>Renders two pages and compares their pixels.</summary>
    /// <param name="oldPage">The old version's page.</param>
    /// <param name="newPage">The new version's page; the regions are in its user space. It may be in the same document or another.</param>
    /// <param name="options">The resolution, tolerance, smallest region and alignment; <see langword="null"/> for the defaults.</param>
    /// <returns>The comparison; dispose it to return its bitmaps to the pool.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="oldPage"/> or <paramref name="newPage"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The canvas would need more pixels than a bitmap can hold.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.3.2.2, §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2, as <see cref="VisualComparison"/> describes. Pages of documents
    /// with different page counts are paired by the caller; a page with no counterpart is not compared.
    /// </remarks>
    public static VisualComparison Compare(PdfPage oldPage, PdfPage newPage, VisualComparisonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldPage);
        ArgumentNullException.ThrowIfNull(newPage);
        options ??= new VisualComparisonOptions();
        RenderOptions render = new RenderOptions().WithDpi(options.Dpi);
        PixelSize oldSize = render.GetPixelSize(oldPage), newSize = render.GetPixelSize(newPage);
        int width = Math.Max(oldSize.Width, newSize.Width), height = Math.Max(oldSize.Height, newSize.Height);
        long length = (long)width * height * 4;
        if (length > Array.MaxLength)
        {
            throw new ArgumentException("The pages need more pixels than a bitmap can hold.", nameof(oldPage));
        }

        int stride = width * 4;
        (int oldX, int oldY) = Place(oldSize, width, height, options.Alignment);
        (int newX, int newY) = Place(newSize, width, height, options.Alignment);
        byte[] oldCanvas = ArrayPool<byte>.Shared.Rent((int)length);
        byte[] newCanvas = ArrayPool<byte>.Shared.Rent((int)length);
        byte[] row = ArrayPool<byte>.Shared.Rent(width);
        Bitmap? overlay = null, difference = null;
        try
        {
            using (var rasterizer = new SoftwareRasterizer())
            {
                Draw(rasterizer, oldPage, render, oldCanvas, (int)length, oldSize, oldX, oldY, stride);
                Draw(rasterizer, newPage, render, newCanvas, (int)length, newSize, newX, newY, stride);
            }

            overlay = new Bitmap(width, height);
            difference = new Bitmap(width, height);
            int cellsWide = (width + CellSize - 1) / CellSize, cellsHigh = (height + CellSize - 1) / CellSize;
            var cells = new int[cellsWide * cellsHigh];
            long changed = CompareRows(oldCanvas.AsSpan(0, (int)length), newCanvas.AsSpan(0, (int)length), width, height, options.Tolerance, overlay, difference, cells, row.AsSpan(0, width));
            Matrix toUser = render.GetDeviceTransform(newPage).TryInvert(out Matrix inverse) ? inverse : Matrix.Identity;
            IReadOnlyList<PdfRectangle> regions = FindRegions(cells, cellsWide, cellsHigh, difference, options.MinRegionArea, toUser, newX, newY);
            var comparison = new VisualComparison(overlay, difference, (double)changed / ((long)width * height), regions);
            overlay = null;
            difference = null;
            return comparison;
        }
        finally
        {
            overlay?.Dispose();
            difference?.Dispose();
            ArrayPool<byte>.Shared.Return(oldCanvas);
            ArrayPool<byte>.Shared.Return(newCanvas);
            ArrayPool<byte>.Shared.Return(row);
        }
    }

    /// <summary>Returns the bitmaps to the pool.</summary>
    public void Dispose()
    {
        Overlay.Dispose();
        Difference.Dispose();
    }

    /// <summary>
    /// Compares two canvases of premultiplied BGRA pixels row by row, writing the overlay and difference pixels and counting the
    /// changed pixels of each cell. Allocates nothing.
    /// </summary>
    /// <returns>The number of changed pixels.</returns>
    internal static long CompareRows(ReadOnlySpan<byte> oldPixels, ReadOnlySpan<byte> newPixels, int width, int height, int tolerance, Bitmap overlay, Bitmap difference, Span<int> cells, Span<byte> row)
    {
        int stride = width * 4;
        int cellsWide = (width + CellSize - 1) / CellSize;
        long changed = 0;
        row = row[..width];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> oldRow = oldPixels.Slice(y * stride, stride);
            ReadOnlySpan<byte> newRow = newPixels.Slice(y * stride, stride);
            PixelDifference.MaxChannelDifference(oldRow, newRow, row);
            ReadOnlySpan<uint> oldColors = MemoryMarshal.Cast<byte, uint>(oldRow);
            ReadOnlySpan<uint> newColors = MemoryMarshal.Cast<byte, uint>(newRow);
            Span<uint> overlayRow = MemoryMarshal.Cast<byte, uint>(overlay.GetRow(y))[..width];
            Span<uint> differenceRow = MemoryMarshal.Cast<byte, uint>(difference.GetRow(y))[..width];
            Span<int> cellRow = cells.Slice(y / CellSize * cellsWide, cellsWide);
            for (int x = 0; x < width; x++)
            {
                int newLuminance = Luminance(newColors[x]);
                if (row[x] > tolerance)
                {
                    // Red takes the new page's lightness, green and blue the old page's: old ink alone is red, new ink alone cyan.
                    uint oldLuminance = (uint)Luminance(oldColors[x]);
                    uint color = Opaque | ((uint)newLuminance << 16) | (oldLuminance << 8) | oldLuminance;
                    overlayRow[x] = color;
                    differenceRow[x] = color;
                    cellRow[x / CellSize]++;
                    changed++;
                }
                else
                {
                    uint gray = (uint)(192 + (newLuminance >> 2));
                    overlayRow[x] = Opaque | (gray * 0x010101u);
                    differenceRow[x] = 0;
                }
            }
        }

        return changed;
    }

    /// <summary>The lightness of an opaque pixel, 0 to 255 (Rec. 601 weights in 8-bit fixed point).</summary>
    private static int Luminance(uint bgra) =>
        ((int)((bgra >> 16) & 0xFF) * 77 + (int)((bgra >> 8) & 0xFF) * 150 + (int)(bgra & 0xFF) * 29 + 128) >> 8;

    private static (int X, int Y) Place(PixelSize size, int width, int height, VisualAlignment alignment) =>
        alignment == VisualAlignment.Center ? ((width - size.Width) / 2, (height - size.Height) / 2) : (0, 0);

    /// <summary>Renders a page onto its place on a canvas filled with white.</summary>
    private static void Draw(SoftwareRasterizer rasterizer, PdfPage page, RenderOptions render, byte[] canvas, int length, PixelSize size, int x, int y, int stride)
    {
        canvas.AsSpan(0, length).Fill(0xFF);
        int start = (y * stride) + (x * 4);
        using var target = new Bitmap(canvas.AsMemory(start, length - start), size.Width, size.Height, stride);
        rasterizer.Render(page, target, render);
    }

    /// <summary>Groups the cells with changed pixels that touch (diagonally too) and returns each group's bounds in user space.</summary>
    private static ReadOnlyCollection<PdfRectangle> FindRegions(int[] cells, int cellsWide, int cellsHigh, Bitmap difference, int minArea, Matrix toUser, int pageX, int pageY)
    {
        var regions = new List<PdfRectangle>();
        var labels = new int[cells.Length];
        var pending = new Stack<int>();
        var members = new List<int>();
        int label = 0;
        for (int start = 0; start < cells.Length; start++)
        {
            if (cells[start] == 0 || labels[start] != 0)
            {
                continue;
            }

            label++;
            long area = 0;
            members.Clear();
            labels[start] = label;
            pending.Push(start);
            while (pending.TryPop(out int cell))
            {
                members.Add(cell);
                area += cells[cell];
                int cx = cell % cellsWide, cy = cell / cellsWide;
                for (int ny = Math.Max(cy - 1, 0); ny <= Math.Min(cy + 1, cellsHigh - 1); ny++)
                {
                    for (int nx = Math.Max(cx - 1, 0); nx <= Math.Min(cx + 1, cellsWide - 1); nx++)
                    {
                        int neighbor = (ny * cellsWide) + nx;
                        if (cells[neighbor] != 0 && labels[neighbor] == 0)
                        {
                            labels[neighbor] = label;
                            pending.Push(neighbor);
                        }
                    }
                }
            }

            if (area < minArea)
            {
                continue;
            }

            // The exact bounds of the group's changed pixels, from its cells' pixels.
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            foreach (int cell in members)
            {
                int x0 = cell % cellsWide * CellSize, y0 = cell / cellsWide * CellSize;
                for (int y = y0; y < Math.Min(y0 + CellSize, difference.Height); y++)
                {
                    ReadOnlySpan<uint> pixels = MemoryMarshal.Cast<byte, uint>(difference.GetRow(y));
                    for (int x = x0; x < Math.Min(x0 + CellSize, difference.Width); x++)
                    {
                        if (pixels[x] != 0)
                        {
                            left = Math.Min(left, x);
                            right = Math.Max(right, x + 1);
                            top = Math.Min(top, y);
                            bottom = Math.Max(bottom, y + 1);
                        }
                    }
                }
            }

            regions.Add(ToUser(toUser, left - pageX, top - pageY, right - pageX, bottom - pageY));
        }

        return regions.AsReadOnly();
    }

    private static PdfRectangle ToUser(Matrix toUser, double left, double top, double right, double bottom)
    {
        PathPoint a = toUser.Transform(left, top), b = toUser.Transform(right, top), c = toUser.Transform(right, bottom), d = toUser.Transform(left, bottom);
        return new PdfRectangle(
            Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)),
            Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)),
            Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)),
            Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
    }
}
