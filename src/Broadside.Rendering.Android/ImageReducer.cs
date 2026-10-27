using System.Buffers;
using Broadside.Graphics;

namespace Broadside.Rendering.Android;

/// <summary>
/// Resamples an image to the device pixels it covers as the software rasterizer does: per axis of the sample grid, a box filter by
/// exact overlap over a pixel's footprint where it spans more than one sample (at most <see cref="MaxTaps"/> evenly spaced samples
/// beyond that), else the sample under the pixel's centre, or a tent between the two nearest when the image asks for interpolation.
/// Sample positions are computed with the rasterizer's arithmetic (its inverse transform; for an upright image stepped along each
/// span its scan converter emits), so that a pixel centre on a sample boundary takes the same sample. The caller clips the result
/// to the image's outline with anti-aliasing. The canvas's own sampling stays the path for enlargements to more pixels than
/// <see cref="Applies"/> allows.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.9.4 (image space to the unit square to device space), §8.9.5.3 (Interpolate). Works on premultiplied BGRA or
/// alpha-only pixels, so masks resample the same way. Runs once per image, transform and backend; the result is kept with the image.
/// </remarks>
internal static class ImageReducer
{
    /// <summary>The most samples one axis of a reduced pixel averages exactly, as the rasterizer's image painter.</summary>
    private const int MaxTaps = 16;

    /// <summary>The most device pixels a resampled image may have.</summary>
    private const long MaxPixels = 1L << 22;

    /// <summary>The device pixels an enlarged image may have per sample, else Android samples it.</summary>
    private const long MaxEnlargement = 256;

    /// <summary>The sample rows whose horizontal sums an upright resampling keeps at a time (unless one pixel row reads more).</summary>
    private const int BandRows = 64;

    /// <summary>
    /// Whether <paramref name="imageToDevice"/> (samples, y down from the first row, to device pixels) maps the image onto few enough
    /// pixels to resample here; then <paramref name="pixels"/> is the block of device pixels its outline touches and
    /// <paramref name="upright"/> tells whether the transform has neither rotation nor skew.
    /// </summary>
    public static bool Applies(int width, int height, in Matrix imageToDevice, out PixelRect pixels, out bool upright)
    {
        pixels = default;
        upright = imageToDevice.B == 0 && imageToDevice.C == 0;
        if (imageToDevice.Determinant == 0 || !double.IsFinite(imageToDevice.Determinant))
        {
            return false;
        }

        PathPoint p0 = imageToDevice.Transform(0, 0);
        PathPoint p1 = imageToDevice.Transform(width, 0);
        PathPoint p2 = imageToDevice.Transform(width, height);
        PathPoint p3 = imageToDevice.Transform(0, height);
        double x0 = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double x1 = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        double y0 = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        double y1 = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        if (!(x0 > -(1 << 24)) || !(x1 < 1 << 24) || !(y0 > -(1 << 24)) || !(y1 < 1 << 24))
        {
            return false;
        }

        int left = (int)Math.Floor(x0);
        int top = (int)Math.Floor(y0);
        int right = Math.Max((int)Math.Ceiling(x1), left + 1);
        int bottom = Math.Max((int)Math.Ceiling(y1), top + 1);
        long count = (long)(right - left) * (bottom - top);
        if (count > MaxPixels || count > Math.Max(MaxEnlargement * width * height, 1 << 20))
        {
            return false;
        }

        pixels = new PixelRect(left, top, right, bottom);
        return true;
    }

    /// <summary>Returns the image of the device pixels in <paramref name="pixels"/> (see <see cref="Applies"/>).</summary>
    public static PixelImage? Reduce(PixelImage source, in Matrix imageToDevice, PixelRect pixels, bool interpolate)
    {
        if (!imageToDevice.TryInvert(out Matrix inverse) || !double.IsFinite(inverse.A + inverse.B + inverse.C + inverse.D + inverse.E + inverse.F))
        {
            return null;
        }

        int channels = source.Channels;
        int outWidth = pixels.Width;
        int outHeight = pixels.Height;
        byte[] output = ArrayPool<byte>.Shared.Rent(outWidth * outHeight * channels);
        try
        {
            var grid = new Grid(source.Width, source.Height, source.Data, source.Width * channels, channels, inverse, interpolate);
            if (imageToDevice.B == 0 && imageToDevice.C == 0)
            {
                Upright(grid, imageToDevice, inverse, pixels, output);
            }
            else
            {
                Skewed(grid, imageToDevice, inverse, pixels, output);
            }

            return PixelImage.Copy(output.AsSpan(0, outWidth * outHeight * channels), outWidth, outHeight, channels);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(output);
        }
    }

    /// <summary>
    /// An upright image, separably: v per row (B is 0, so it does not step along a span); u per column stepped along the spans the
    /// rasterizer's scan converter emits (the pixel of each edge cell alone, the pixels between them).
    /// </summary>
    private static void Upright(in Grid grid, in Matrix imageToDevice, in Matrix inverse, PixelRect pixels, byte[] output)
    {
        int channels = grid.Channels;
        int outWidth = pixels.Width;
        int outHeight = pixels.Height;
        double x1 = Math.Max(imageToDevice.E, (imageToDevice.A * grid.Width) + imageToDevice.E);
        int end = (int)Math.Floor(x1) - pixels.Left;
        var columns = new Axis[outWidth];
        double u = 0;
        for (int i = 0; i < outWidth; i++)
        {
            u = i == 0 || i == 1 || i == end ? (inverse.A * (pixels.Left + i + 0.5)) + (inverse.C * 0.5) + inverse.E : u + inverse.A;
            columns[i] = grid.Across(u);
        }

        var rows = new Axis[outHeight];
        for (int j = 0; j < outHeight; j++)
        {
            double v = (inverse.B * (pixels.Left + 0.5)) + (inverse.D * (pixels.Top + j + 0.5)) + inverse.F;
            rows[j] = grid.Down(v);
        }

        // Horizontal pass: per sample row, each column's weighted sums and its total weight (the last of channels + 1 values), for the
        // sample rows of a band of output rows at a time (issue #432): one buffer for every sample row of the image was 150 MB for a
        // 2550 x 3150 photograph on a phone, per backend converting it. A row's sums do not depend on the band, so the pixels are the same.
        int stride = outWidth * (channels + 1);
        int span = 0;
        for (int y = 0; y < outHeight;)
        {
            y = NextBand(rows, y, out int low, out int high);
            span = Math.Max(span, high - low + 1);
        }

        double[] across = ArrayPool<double>.Shared.Rent(stride * span);
        try
        {
            Span<double> total = stackalloc double[5];
            for (int start = 0; start < outHeight;)
            {
                int stop = NextBand(rows, start, out int low, out int high);
                for (int y = low; y <= high; y++)
                {
                    ReadOnlySpan<byte> row = grid.Row(y);
                    int offset = (y - low) * stride;
                    for (int x = 0; x < outWidth; x++)
                    {
                        Axis axis = columns[x];
                        Span<double> sums = across.AsSpan(offset + (x * (channels + 1)), channels + 1);
                        sums.Clear();
                        for (int k = 0; k < axis.Count; k++)
                        {
                            double w = axis.Weight(k);
                            if (w > 0)
                            {
                                int index = axis.Index(k) * channels;
                                for (int c = 0; c < channels; c++)
                                {
                                    sums[c] += w * row[index + c];
                                }

                                sums[channels] += w;
                            }
                        }
                    }
                }

                for (int y = start; y < stop; y++)
                {
                    Axis axis = rows[y];
                    for (int x = 0; x < outWidth; x++)
                    {
                        total.Clear();
                        for (int k = 0; k < axis.Count; k++)
                        {
                            double w = axis.Weight(k);
                            if (w > 0)
                            {
                                int from = ((axis.Index(k) - low) * stride) + (x * (channels + 1));
                                for (int c = 0; c <= channels; c++)
                                {
                                    total[c] += w * across[from + c];
                                }
                            }
                        }

                        Store(total, channels, output.AsSpan(((y * outWidth) + x) * channels, channels));
                    }
                }

                start = stop;
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(across);
        }
    }

    /// <summary>
    /// Returns the end of the band of output rows that starts at <paramref name="start"/>: rows are added while the sample rows they
    /// read (<paramref name="low"/> to <paramref name="high"/>) stay within <see cref="BandRows"/>, or within the first row's own when
    /// it reads more.
    /// </summary>
    private static int NextBand(Axis[] rows, int start, out int low, out int high)
    {
        RowsOf(rows[start], out low, out high);
        int limit = Math.Max(BandRows, high - low + 1);
        int end = start + 1;
        while (end < rows.Length)
        {
            RowsOf(rows[end], out int first, out int last);
            int bandLow = Math.Min(low, first);
            int bandHigh = Math.Max(high, last);
            if (bandHigh - bandLow + 1 > limit)
            {
                break;
            }

            (low, high) = (bandLow, bandHigh);
            end++;
        }

        return end;
    }

    /// <summary>Gets the first and last sample rows <paramref name="axis"/> reads.</summary>
    private static void RowsOf(in Axis axis, out int low, out int high)
    {
        low = int.MaxValue;
        high = int.MinValue;
        for (int k = 0; k < axis.Count; k++)
        {
            int index = axis.Index(k);
            low = Math.Min(low, index);
            high = Math.Max(high, index);
        }
    }

    /// <summary>
    /// A rotated or skewed image, pixel by pixel: the footprint's samples on both axes around (u, v), stepped along each row's spans
    /// as the rasterizer's scan converter emits them (each pixel an edge of the outline crosses alone, the pixels between them).
    /// </summary>
    private static void Skewed(in Grid grid, in Matrix imageToDevice, in Matrix inverse, PixelRect pixels, byte[] output)
    {
        int channels = grid.Channels;
        int outWidth = pixels.Width;
        Span<double> total = stackalloc double[5];
        Span<PathPoint> corners = stackalloc PathPoint[4];
        corners[0] = imageToDevice.Transform(0, 0);
        corners[1] = imageToDevice.Transform(grid.Width, 0);
        corners[2] = imageToDevice.Transform(grid.Width, grid.Height);
        corners[3] = imageToDevice.Transform(0, grid.Height);
        bool[] cells = new bool[outWidth];
        for (int y = 0; y < pixels.Height; y++)
        {
            double py = pixels.Top + y + 0.5;
            MarkCells(corners, pixels.Top + y, pixels.Left, cells);
            double u = 0;
            double v = 0;
            for (int x = 0; x < outWidth; x++)
            {
                double px = pixels.Left + x + 0.5;
                if (x == 0 || cells[x] || cells[x - 1])
                {
                    u = (inverse.A * px) + (inverse.C * py) + inverse.E;
                    v = (inverse.B * px) + (inverse.D * py) + inverse.F;
                }
                else
                {
                    u += inverse.A;
                    v += inverse.B;
                }

                Axis across = grid.Across(u);
                Axis down = grid.Down(v);
                total.Clear();
                for (int j = 0; j < down.Count; j++)
                {
                    double wy = down.Weight(j);
                    if (wy <= 0)
                    {
                        continue;
                    }

                    ReadOnlySpan<byte> row = grid.Row(down.Index(j));
                    for (int i = 0; i < across.Count; i++)
                    {
                        double w = across.Weight(i) * wy;
                        if (w > 0)
                        {
                            int index = across.Index(i) * channels;
                            for (int c = 0; c < channels; c++)
                            {
                                total[c] += w * row[index + c];
                            }

                            total[channels] += w;
                        }
                    }
                }

                if (total[channels] <= 0)
                {
                    // A footprint outside the grid: the sample under the centre, clamped (the outline clip removes it anyway).
                    ReadOnlySpan<byte> row = grid.Row(Clamp(Math.Floor(v), grid.Height));
                    int index = Clamp(Math.Floor(u), grid.Width) * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        total[c] = row[index + c];
                    }

                    total[channels] = 1;
                }

                Store(total, channels, output.AsSpan(((y * outWidth) + x) * channels, channels));
            }
        }
    }

    /// <summary>Marks the columns of row <paramref name="row"/> an edge of the outline passes through (the scan converter's cells).</summary>
    private static void MarkCells(ReadOnlySpan<PathPoint> corners, int row, int left, bool[] cells)
    {
        Array.Clear(cells);
        for (int i = 0; i < corners.Length; i++)
        {
            PathPoint a = corners[i];
            PathPoint b = corners[(i + 1) % corners.Length];
            double top = Math.Max(row, Math.Min(a.Y, b.Y));
            double bottom = Math.Min(row + 1, Math.Max(a.Y, b.Y));
            if (!(bottom >= top) || a.Y == b.Y)
            {
                continue;
            }

            double xa = a.X + ((b.X - a.X) * (top - a.Y) / (b.Y - a.Y));
            double xb = a.X + ((b.X - a.X) * (bottom - a.Y) / (b.Y - a.Y));
            int first = Math.Max((int)Math.Floor(Math.Min(xa, xb)) - left, 0);
            int last = Math.Min((int)Math.Floor(Math.Max(xa, xb)) - left, cells.Length - 1);
            for (int x = first; x <= last; x++)
            {
                cells[x] = true;
            }
        }
    }

    private static void Store(ReadOnlySpan<double> sums, int channels, Span<byte> pixel)
    {
        double scale = sums[channels] > 0 ? 1 / sums[channels] : 0;
        for (int c = 0; c < channels; c++)
        {
            pixel[c] = (byte)Math.Clamp((int)((sums[c] * scale) + 0.5), 0, 255);
        }
    }

    private static int Clamp(double value, int size) => value >= size ? size - 1 : value > 0 ? (int)value : 0;

    /// <summary>The sample grid and the half-extents of a pixel's footprint on it, as the rasterizer's image painter derives them.</summary>
    private readonly ref struct Grid
    {
        private readonly ReadOnlySpan<byte> _samples;
        private readonly int _rowBytes;
        private readonly double _halfU;
        private readonly double _halfV;
        private readonly bool _boxed;
        private readonly bool _interpolate;

        public Grid(int width, int height, ReadOnlySpan<byte> samples, int rowBytes, int channels, in Matrix inverse, bool interpolate)
        {
            Width = width;
            Height = height;
            Channels = channels;
            _samples = samples;
            _rowBytes = rowBytes;
            _interpolate = interpolate;
            _halfU = 0.5 * (Math.Abs(inverse.A) + Math.Abs(inverse.C));
            _halfV = 0.5 * (Math.Abs(inverse.B) + Math.Abs(inverse.D));
            _boxed = _halfU > 0.5 || _halfV > 0.5;
        }

        public int Width { get; }

        public int Height { get; }

        public int Channels { get; }

        public ReadOnlySpan<byte> Row(int y) => _samples.Slice(y * _rowBytes, Width * Channels);

        public Axis Across(double u) => Axis.Create(u, _halfU, Width, _interpolate, _boxed);

        public Axis Down(double v) => Axis.Create(v, _halfV, Height, _interpolate, _boxed);
    }

    /// <summary>The samples one axis of a pixel's footprint takes and their weights, as the rasterizer's image painter chooses them.</summary>
    private readonly struct Axis
    {
        private readonly int _first;
        private readonly int _second;
        private readonly double _low;
        private readonly double _high;
        private readonly double _step;
        private readonly int _size;
        private readonly Kind _kind;

        private Axis(Kind kind, int count, int first, int second, double low, double high, double step, int size)
        {
            _kind = kind;
            Count = count;
            _first = first;
            _second = second;
            _low = low;
            _high = high;
            _step = step;
            _size = size;
        }

        private enum Kind
        {
            Nearest,
            Tent,
            Box,
            Points,
        }

        public int Count { get; }

        /// <summary>
        /// The samples at <paramref name="center"/> ± <paramref name="half"/> along an axis of <paramref name="size"/> samples; when
        /// neither axis is boxed, the tent or nearest sample of the centre.
        /// </summary>
        public static Axis Create(double center, double half, int size, bool interpolate, bool boxed)
        {
            if (boxed && half > 0.5)
            {
                double low = Math.Max(center - half, 0);
                double high = Math.Min(center + half, size);
                if (high > low)
                {
                    if (high - low > MaxTaps)
                    {
                        return new Axis(Kind.Points, MaxTaps, 0, 0, low, high, (high - low) / MaxTaps, size);
                    }

                    int first = (int)Math.Floor(low);
                    int last = Math.Min((int)Math.Ceiling(high) - 1, size - 1);
                    return new Axis(Kind.Box, last - first + 1, first, 0, low, high, 0, size);
                }
            }

            if (interpolate)
            {
                double c = center - 0.5;
                double floor = Math.Floor(c);
                return new Axis(Kind.Tent, 2, Clamp(floor, size), Clamp(floor + 1, size), Math.Clamp(c - floor, 0, 1), 0, 0, size);
            }

            return new Axis(Kind.Nearest, 1, Clamp(Math.Floor(center), size), 0, 0, 0, 0, size);
        }

        public int Index(int k) => _kind switch
        {
            Kind.Box => _first + k,
            Kind.Tent => k == 0 ? _first : _second,
            Kind.Points => Clamp(Math.Floor(_low + ((k + 0.5) * _step)), _size),
            _ => _first,
        };

        public double Weight(int k) => _kind switch
        {
            Kind.Box => Math.Min(_high, _first + k + 1) - Math.Max(_low, _first + k),
            Kind.Tent => k == 0 ? 1 - _low : _low,
            _ => 1,
        };
    }
}
