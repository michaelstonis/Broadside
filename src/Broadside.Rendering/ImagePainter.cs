using System.Runtime.InteropServices;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Paints converted image pixels (<see cref="ImageSource"/>), or one colour through a stencil mask, through the inverse of the
/// image-to-device transform: each device pixel's centre maps back into image space and takes the samples its footprint covers.
/// Enlarged axes take the nearest sample, or a bilinear blend of the four nearest when the image asks for interpolation; reduced axes
/// average every sample under the pixel's footprint (a box filter), so a reduced image neither aliases nor drops detail. A mask of
/// its own resolution (<see cref="MaskSource"/>) is resampled the same way on its own grid and scales the colour's opacity.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.4 (image space to the unit square to device space), §8.9.5.3 (Interpolate: without it, samples are shown as
/// square blocks; with it, the processor may smooth them) and §10.7.4 (device pixels). The footprint of a pixel in image space is
/// the bounding box of its preimage, the same for every pixel of an affine transform; axes where it spans more than one sample are
/// box-filtered by exact overlap, through at most <see cref="MaxTaps"/> evenly spaced samples per axis when it spans more. Masks
/// (§8.9.6.2, §8.9.6.3, §11.6.5.2) map onto the same unit square at their own width and height, so a mask sample position is the
/// image sample position scaled by the ratio of the sizes.
/// </para>
/// <para>
/// Hot path: allocates nothing; the per-pixel work is a few multiply-adds for enlargement and proportional to the samples averaged
/// for reduction. Compositing is premultiplied source-over (§11.3), the coverage of the image's outline scaling the sample.
/// It is a coverage sink of <see cref="ScanlineRasterizer.Paint"/>, so the runs it receives are already cut by the clipping path
/// (§8.5.4), like those of fills and strokes.
/// </para>
/// </remarks>
internal readonly ref struct ImagePainter : ICoverageSink
{
    /// <summary>The most samples one axis of a reduced pixel averages exactly; wider footprints are sampled at this many points.</summary>
    private const int MaxTaps = 16;

    private readonly uint[]? _pixels;
    private readonly uint _solid;
    private readonly Grid _grid;
    private readonly byte[]? _mask;
    private readonly Grid _maskGrid;
    private readonly Matrix _inverse;
    private readonly Span<byte> _target;
    private readonly int _stride;

    /// <summary>Creates a painter of image pixels.</summary>
    /// <param name="target">The device pixels painted into, premultiplied BGRA in memory order.</param>
    /// <param name="stride">The bytes from one row of <paramref name="target"/> to the next.</param>
    /// <param name="pixels">The converted pixels, <paramref name="width"/> × <paramref name="height"/>, premultiplied.</param>
    /// <param name="width">The image width in samples.</param>
    /// <param name="height">The image height in samples.</param>
    /// <param name="inverse">Device pixels to image space (samples, y down from the first row).</param>
    /// <param name="interpolate">Whether enlarged samples are blended bilinearly rather than shown as blocks.</param>
    /// <param name="mask">Opacities of a mask of its own resolution that scale the image's, <paramref name="maskWidth"/> × <paramref name="maskHeight"/>; <see langword="null"/> for none.</param>
    /// <param name="maskWidth">The mask width in samples.</param>
    /// <param name="maskHeight">The mask height in samples.</param>
    /// <param name="maskInterpolate">Whether the mask's enlarged samples are blended bilinearly.</param>
    public ImagePainter(
        Span<byte> target,
        int stride,
        uint[] pixels,
        int width,
        int height,
        in Matrix inverse,
        bool interpolate,
        byte[]? mask = null,
        int maskWidth = 1,
        int maskHeight = 1,
        bool maskInterpolate = false)
    {
        _target = target;
        _stride = stride;
        _pixels = pixels;
        _inverse = inverse;
        _grid = new Grid(width, height, inverse, 1, 1, interpolate);
        _mask = mask;
        _maskGrid = new Grid(maskWidth, maskHeight, inverse, (double)maskWidth / width, (double)maskHeight / height, maskInterpolate);
    }

    /// <summary>Creates a painter of one colour through a mask.</summary>
    /// <param name="target">The device pixels painted into, premultiplied BGRA in memory order.</param>
    /// <param name="stride">The bytes from one row of <paramref name="target"/> to the next.</param>
    /// <param name="color">The colour, opaque BGRA.</param>
    /// <param name="mask">The opacities, <paramref name="width"/> × <paramref name="height"/>.</param>
    /// <param name="width">The mask width in samples.</param>
    /// <param name="height">The mask height in samples.</param>
    /// <param name="inverse">Device pixels to mask space (samples, y down from the first row).</param>
    /// <param name="interpolate">Whether enlarged samples are blended bilinearly rather than shown as blocks.</param>
    public ImagePainter(Span<byte> target, int stride, uint color, byte[] mask, int width, int height, in Matrix inverse, bool interpolate)
    {
        _target = target;
        _stride = stride;
        _solid = color;
        _inverse = inverse;
        _grid = new Grid(width, height, inverse, 1, 1, interpolate);
        _mask = mask;
        _maskGrid = _grid;
    }

    /// <inheritdoc/>
    public void Span(int row, int x, int count, int coverage)
    {
        if (coverage <= 0)
        {
            return;
        }

        Span<uint> line = MemoryMarshal.Cast<byte, uint>(_target.Slice(row * _stride, (x + count) * 4));
        Matrix m = _inverse;
        double px = x + 0.5;
        double py = row + 0.5;
        double u = (m.A * px) + (m.C * py) + m.E;
        double v = (m.B * px) + (m.D * py) + m.F;
        for (int i = 0; i < count; i++, u += m.A, v += m.B)
        {
            uint sample = _pixels is { } pixels ? SampleColor(pixels, _grid, u, v) : _solid;
            if (_mask is { } mask)
            {
                int alpha = SampleMask(mask, _maskGrid, u * _maskGrid.ScaleU, v * _maskGrid.ScaleV);
                if (alpha == 0)
                {
                    continue;
                }

                sample = Scale(sample, alpha);
            }

            ref uint target = ref line[x + i];
            target = Composite(sample, target, coverage);
        }
    }

    /// <summary>Premultiplied source-over with the source scaled by <paramref name="coverage"/>: d = s·c + d·(1 − a·c).</summary>
    private static uint Composite(uint source, uint destination, int coverage)
    {
        uint alpha = source >> 24;
        if (coverage == 255 && alpha == 255)
        {
            return source;
        }

        int sourceAlpha = Divide255((int)alpha * coverage);
        int keep = 255 - sourceAlpha;
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int s = (int)((source >> shift) & 0xFF);
            int d = (int)((destination >> shift) & 0xFF);
            int value = Divide255((s * coverage) + (d * keep));
            result |= (uint)Math.Min(value, 255) << shift;
        }

        return result;
    }

    /// <summary>Scales every channel of a premultiplied pixel by <paramref name="alpha"/> / 255.</summary>
    private static uint Scale(uint pixel, int alpha)
    {
        if (alpha == 255)
        {
            return pixel;
        }

        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            result |= (uint)Divide255((int)((pixel >> shift) & 0xFF) * alpha) << shift;
        }

        return result;
    }

    /// <summary>Rounds <paramref name="value"/> / 255 for 0 ≤ value ≤ 2 × 255 × 255.</summary>
    private static int Divide255(int value)
    {
        value += 128;
        return (value + (value >> 8)) >> 8;
    }

    private static int Clamp(double value, int size) =>
        value >= size ? size - 1 : value > 0 ? (int)value : 0;

    /// <summary>The colour of the image at (u, v): the samples under the pixel's footprint, by the grid's filter.</summary>
    private static uint SampleColor(uint[] pixels, in Grid grid, double u, double v)
    {
        if (grid.Boxed)
        {
            return Average(pixels, grid, u, v);
        }

        return grid.Interpolate ? Bilinear(pixels, grid, u, v) : pixels[(Clamp(Math.Floor(v), grid.Height) * grid.Width) + Clamp(Math.Floor(u), grid.Width)];
    }

    /// <summary>The opacity of the mask at (u, v) of its own grid, 0 to 255, by the grid's filter.</summary>
    private static int SampleMask(byte[] mask, in Grid grid, double u, double v)
    {
        if (!grid.Boxed && !grid.Interpolate)
        {
            return mask[(Clamp(Math.Floor(v), grid.Height) * grid.Width) + Clamp(Math.Floor(u), grid.Width)];
        }

        Axis across = Axis.Create(u, grid.HalfU, grid.Width, grid.Interpolate);
        Axis down = Axis.Create(v, grid.HalfV, grid.Height, grid.Interpolate);
        double sum = 0;
        double total = 0;
        for (int j = 0; j < down.Count; j++)
        {
            double wy = down.Weight(j);
            if (wy <= 0)
            {
                continue;
            }

            int offset = down.Index(j) * grid.Width;
            for (int i = 0; i < across.Count; i++)
            {
                double w = across.Weight(i) * wy;
                if (w > 0)
                {
                    sum += w * mask[offset + across.Index(i)];
                    total += w;
                }
            }
        }

        return total <= 0
            ? mask[(Clamp(Math.Floor(v), grid.Height) * grid.Width) + Clamp(Math.Floor(u), grid.Width)]
            : (int)Channel(sum / total);
    }

    /// <summary>Blends the four samples whose centres surround (u, v), the image's edge samples extended outwards.</summary>
    private static uint Bilinear(uint[] pixels, in Grid grid, double u, double v)
    {
        double cu = u - 0.5;
        double cv = v - 0.5;
        double fu0 = Math.Floor(cu);
        double fv0 = Math.Floor(cv);
        double fu = Math.Clamp(cu - fu0, 0, 1);
        double fv = Math.Clamp(cv - fv0, 0, 1);
        int u0 = Clamp(fu0, grid.Width);
        int u1 = Clamp(fu0 + 1, grid.Width);
        int row0 = Clamp(fv0, grid.Height) * grid.Width;
        int row1 = Clamp(fv0 + 1, grid.Height) * grid.Width;
        uint p00 = pixels[row0 + u0];
        uint p10 = pixels[row0 + u1];
        uint p01 = pixels[row1 + u0];
        uint p11 = pixels[row1 + u1];
        if (p00 == p10 && p00 == p01 && p00 == p11)
        {
            return p00;
        }

        double w00 = (1 - fu) * (1 - fv);
        double w10 = fu * (1 - fv);
        double w01 = (1 - fu) * fv;
        double w11 = fu * fv;
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            double value = (w00 * ((p00 >> shift) & 0xFF)) + (w10 * ((p10 >> shift) & 0xFF)) + (w01 * ((p01 >> shift) & 0xFF)) + (w11 * ((p11 >> shift) & 0xFF));
            result |= (uint)Math.Clamp((int)(value + 0.5), 0, 255) << shift;
        }

        return result;
    }

    /// <summary>Averages the samples under the footprint of the pixel centred at (u, v); an enlarged axis takes one or two samples.</summary>
    private static uint Average(uint[] pixels, in Grid grid, double u, double v)
    {
        Axis across = Axis.Create(u, grid.HalfU, grid.Width, grid.Interpolate);
        Axis down = Axis.Create(v, grid.HalfV, grid.Height, grid.Interpolate);
        double b = 0;
        double g = 0;
        double r = 0;
        double a = 0;
        double total = 0;
        for (int j = 0; j < down.Count; j++)
        {
            double wy = down.Weight(j);
            if (wy <= 0)
            {
                continue;
            }

            int offset = down.Index(j) * grid.Width;
            for (int i = 0; i < across.Count; i++)
            {
                double w = across.Weight(i) * wy;
                if (w <= 0)
                {
                    continue;
                }

                uint pixel = pixels[offset + across.Index(i)];
                b += w * (pixel & 0xFF);
                g += w * ((pixel >> 8) & 0xFF);
                r += w * ((pixel >> 16) & 0xFF);
                a += w * (pixel >> 24);
                total += w;
            }
        }

        if (total <= 0)
        {
            return pixels[(Clamp(Math.Floor(v), grid.Height) * grid.Width) + Clamp(Math.Floor(u), grid.Width)];
        }

        double scale = 1 / total;
        return Channel(b * scale) | (Channel(g * scale) << 8) | (Channel(r * scale) << 16) | (Channel(a * scale) << 24);
    }

    private static uint Channel(double value) => (uint)Math.Clamp((int)(value + 0.5), 0, 255);

    /// <summary>A grid of samples that maps onto the unit square: its size, the half-extents of a pixel's footprint on it, its filter.</summary>
    private readonly struct Grid
    {
        public Grid(int width, int height, in Matrix inverse, double scaleU, double scaleV, bool interpolate)
        {
            Width = width;
            Height = height;
            ScaleU = scaleU;
            ScaleV = scaleV;
            Interpolate = interpolate;
            HalfU = 0.5 * (Math.Abs(inverse.A) + Math.Abs(inverse.C)) * scaleU;
            HalfV = 0.5 * (Math.Abs(inverse.B) + Math.Abs(inverse.D)) * scaleV;
            Boxed = HalfU > 0.5 || HalfV > 0.5;
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Gets the factor from the painter's image-space u to this grid's.</summary>
        public double ScaleU { get; }

        /// <summary>Gets the factor from the painter's image-space v to this grid's.</summary>
        public double ScaleV { get; }

        public bool Interpolate { get; }

        public double HalfU { get; }

        public double HalfV { get; }

        /// <summary>Gets a value indicating whether a pixel's footprint spans more than one sample on some axis.</summary>
        public bool Boxed { get; }
    }

    /// <summary>The samples one axis of a pixel's footprint takes, and their weights.</summary>
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

        /// <summary>The samples at <paramref name="center"/> ± <paramref name="half"/> along an axis of <paramref name="size"/> samples.</summary>
        public static Axis Create(double center, double half, int size, bool interpolate)
        {
            if (half > 0.5)
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

        public double Weight(int k)
        {
            switch (_kind)
            {
                case Kind.Box:
                    int index = _first + k;
                    return Math.Min(_high, index + 1) - Math.Max(_low, index);
                case Kind.Tent:
                    // _low holds the fraction towards the second sample.
                    return k == 0 ? 1 - _low : _low;
                default:
                    return 1;
            }
        }
    }
}
