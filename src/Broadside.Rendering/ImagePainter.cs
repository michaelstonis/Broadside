using System.Runtime.CompilerServices;
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
/// for reduction, where each pixel's footprint (columns, rows and their weights) is worked out once and averaged by
/// <see cref="BoxFilter"/>. When the transform keeps u the same down the image (an upright image), the columns are worked out once
/// per image rather than per pixel: <see cref="ImageFootprints"/> keeps them for every row whose span starts where an earlier one did
/// (issue #509). An upright image enlarged on both axes reads the samples of a run of pixels from the rows it worked out once, and
/// both paths composite each run with one call (issue #517), so the loops do not depend on the JIT inlining per-pixel helpers.
/// Compositing goes through a <see cref="Compositor"/> (§11.3: constant alpha and blend mode), the coverage of the image's outline
/// scaling the sample. It is a coverage sink of <see cref="ScanlineRasterizer.Paint"/>, so the runs it receives are already cut by the clipping path
/// (§8.5.4), like those of fills and strokes.
/// </para>
/// </remarks>
internal readonly ref struct ImagePainter : ICoverageSink
{
    /// <summary>The most samples one axis of a reduced pixel averages exactly; wider footprints are sampled at this many points.</summary>
    private const int MaxTaps = 16;

    /// <summary>The most pixels of a span <see cref="SpanKept"/> averages before it composites them (1 KB of stack).</summary>
    private const int RunPixels = 256;

    private readonly uint[]? _pixels;
    private readonly uint _solid;
    private readonly Grid _grid;
    private readonly byte[]? _mask;
    private readonly Grid _maskGrid;
    private readonly Matrix _inverse;
    private readonly Span<byte> _target;
    private readonly int _stride;
    private readonly Compositor _compositor;
    private readonly ImageFootprints? _footprints;

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
    /// <param name="compositor">The constant alpha and blend mode the pixels composite with (§11.3); the default is opaque Normal.</param>
    /// <param name="average">Whether a reduced axis averages the samples under the pixel; otherwise the sample at its centre is taken (§10.7.4).</param>
    /// <param name="maskScaleU">
    /// The factor from image sample positions to mask sample positions across, or 0 for <paramref name="maskWidth"/> /
    /// <paramref name="width"/>; given when the image's samples are a reduction whose grid overhangs the unit square.
    /// </param>
    /// <param name="maskScaleV">The factor down, or 0 for <paramref name="maskHeight"/> / <paramref name="height"/>.</param>
    /// <param name="footprints">
    /// Where the footprints' columns are kept from one row to the next when u does not change down the image, or
    /// <see langword="null"/> to work them out for every pixel; emptied here, as they belong to this painter's grid.
    /// </param>
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
        bool maskInterpolate = false,
        Compositor compositor = default,
        bool average = true,
        double maskScaleU = 0,
        double maskScaleV = 0,
        ImageFootprints? footprints = null)
    {
        _compositor = compositor;
        _footprints = footprints;
        footprints?.Clear();
        _target = target;
        _stride = stride;
        _pixels = pixels;
        _inverse = inverse;
        _grid = new Grid(width, height, inverse, 1, 1, interpolate, average);
        _mask = mask;
        _maskGrid = new Grid(
            maskWidth,
            maskHeight,
            inverse,
            maskScaleU > 0 ? maskScaleU : (double)maskWidth / width,
            maskScaleV > 0 ? maskScaleV : (double)maskHeight / height,
            maskInterpolate,
            average);
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
    /// <param name="compositor">The constant alpha and blend mode the colour composites with (§11.3); the default is opaque Normal.</param>
    /// <param name="average">Whether a reduced axis averages the samples under the pixel; otherwise the sample at its centre is taken (§10.7.4).</param>
    public ImagePainter(Span<byte> target, int stride, uint color, byte[] mask, int width, int height, in Matrix inverse, bool interpolate, Compositor compositor = default, bool average = true)
    {
        _compositor = compositor;
        _target = target;
        _stride = stride;
        _solid = color;
        _inverse = inverse;
        _grid = new Grid(width, height, inverse, 1, 1, interpolate, average);
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
        if (_pixels is { } reduced && _grid.Boxed)
        {
            SpanBoxed(reduced, line, x, count, coverage, u, v);
            return;
        }

        if (_pixels is { } enlarged && _mask is null && m.B == 0)
        {
            SpanLevel(enlarged, line.Slice(x, count), coverage, u, v);
            return;
        }

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
            target = _compositor.Composite(sample, target, coverage);
        }
    }

    /// <summary>
    /// <see cref="Span"/> for the image pixels of an upright image enlarged on both axes, without a mask of its own: every pixel of
    /// the span takes the same rows, so they are worked out once; the samples of up to <see cref="RunPixels"/> pixels are read into
    /// a run, then composited together. The same samples as the per-pixel loop, u stepped pixel by pixel as it steps it.
    /// </summary>
    [SkipLocalsInit] // Only the samples written to the run are read.
    private void SpanLevel(uint[] pixels, Span<uint> targets, int coverage, double u, double v)
    {
        double step = _inverse.A;
        int width = _grid.Width;
        int height = _grid.Height;
        Compositor compositor = _compositor;
        Span<uint> run = stackalloc uint[RunPixels];
        int i = 0;
        if (!_grid.Interpolate)
        {
            // The nearest sample: the row under v, the column under each pixel's u (SampleColor).
            ReadOnlySpan<uint> row = pixels.AsSpan(Clamp(Math.Floor(v), height) * width, width);
            while (i < targets.Length)
            {
                Span<uint> samples = run[..Math.Min(targets.Length - i, RunPixels)];
                for (int k = 0; k < samples.Length; k++, u += step)
                {
                    samples[k] = row[Clamp(Math.Floor(u), width)];
                }

                compositor.CompositeRow(samples, targets.Slice(i, samples.Length), coverage);
                i += samples.Length;
            }

            return;
        }

        // The bilinear blend (Bilinear), its two rows and the fraction between them worked out once.
        double cv = v - 0.5;
        double fv0 = Math.Floor(cv);
        double fv = Math.Clamp(cv - fv0, 0, 1);
        ReadOnlySpan<uint> row0 = pixels.AsSpan(Clamp(fv0, height) * width, width);
        ReadOnlySpan<uint> row1 = pixels.AsSpan(Clamp(fv0 + 1, height) * width, width);
        while (i < targets.Length)
        {
            Span<uint> samples = run[..Math.Min(targets.Length - i, RunPixels)];
            for (int k = 0; k < samples.Length; k++, u += step)
            {
                samples[k] = Bilinear(row0, row1, width, u, fv);
            }

            compositor.CompositeRow(samples, targets.Slice(i, samples.Length), coverage);
            i += samples.Length;
        }
    }

    /// <summary><see cref="Span"/> for image pixels reduced on some axis: each pixel's footprint averaged by <see cref="BoxFilter"/>.</summary>
    [SkipLocalsInit] // The footprint scratch is written (Axis.Fill) before it is read, over the lengths read.
    private void SpanBoxed(uint[] pixels, Span<uint> line, int x, int count, int coverage, double u, double v)
    {
        Matrix m = _inverse;
        Taps taps = new(stackalloc int[BoxFilter.MaxTaps], stackalloc double[BoxFilter.MaxTaps], stackalloc int[BoxFilter.MaxTaps], stackalloc double[BoxFilter.MaxTaps]);

        // Where v does not change along the span (an upright image), every pixel of it takes the same rows: work them out once.
        bool level = m.B == 0;
        int rows = level ? Rows(_grid, v, taps) : 0;

        // Where u does not change down the image (C = 0), a span that starts at the same pixel as the last one, at the same u, takes
        // the same columns pixel for pixel: they are worked out for the first such span and read back for the others.
        ImageFootprints? footprints = m.C == 0 ? _footprints : null;
        int first = footprints is not null ? Columns(footprints, x, count, u) : 0;
        if (level && footprints is not null && _mask is null)
        {
            SpanKept(pixels, line.Slice(x, count), coverage, u, v, taps, rows, footprints, first);
            return;
        }

        for (int i = 0; i < count; i++, u += m.A, v += m.B)
        {
            if (!level)
            {
                rows = Rows(_grid, v, taps);
            }

            uint sample = footprints is not null
                ? Average(pixels, _grid, u, v, taps, rows, footprints.Columns(first + i), footprints.Weights(first + i))
                : Average(pixels, _grid, u, v, taps, rows);
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
            target = _compositor.Composite(sample, target, coverage);
        }
    }

    /// <summary>
    /// <see cref="SpanBoxed"/> for the common case: an upright image without a mask of its own, whose rows are worked out once for
    /// the span and whose columns <paramref name="footprints"/> holds from pixel <paramref name="first"/>; the same averages in the
    /// same order, with the per-pixel work reduced to reading them back.
    /// </summary>
    [SkipLocalsInit] // Only the averages AverageRun writes are read.
    private void SpanKept(uint[] pixels, Span<uint> targets, int coverage, double u, double v, in Taps taps, int rows, ImageFootprints footprints, int first)
    {
        double step = _inverse.A;
        ReadOnlySpan<int> rowIndices = taps.Rows[..rows];
        ReadOnlySpan<double> rowWeights = taps.RowWeights[..rows];
        ReadOnlySpan<int> starts = footprints.Starts[first..(first + targets.Length + 1)];
        ReadOnlySpan<int> columns = footprints.AllColumns;
        ReadOnlySpan<double> weights = footprints.AllWeights;
        Compositor compositor = _compositor;

        // The averages of up to RunPixels pixels at a time, then composited. The weights (Axis.Fill) are finite and never negative,
        // as BoxFilter.AverageRun requires. u is only needed for the pixels it falls back on: it is stepped pixel by pixel up to such
        // a pixel (it is at pixel stepped), as the per-pixel loop steps it.
        Span<uint> averages = stackalloc uint[RunPixels];
        int stepped = 0;
        int i = 0;
        while (i < targets.Length)
        {
            int count = Math.Min(targets.Length - i, RunPixels);
            int averaged = BoxFilter.AverageRun(pixels, rowIndices, rowWeights, starts.Slice(i, count + 1), columns, weights, averages[..count]);
            compositor.CompositeRow(averages[..averaged], targets.Slice(i, averaged), coverage);
            i += averaged;
            if (averaged < count)
            {
                // Weights that sum to 0: the sample under the pixel's centre.
                for (; stepped < i; stepped++)
                {
                    u += step;
                }

                uint sample = pixels[(Clamp(Math.Floor(v), _grid.Height) * _grid.Width) + Clamp(Math.Floor(u), _grid.Width)];
                targets[i] = compositor.Composite(sample, targets[i], coverage);
                i++;
            }
        }
    }

    /// <summary>
    /// Returns where <paramref name="footprints"/> holds the columns of the <paramref name="count"/> pixels of a span from pixel
    /// <paramref name="x"/>, the first centred at <paramref name="u"/>: a span kept from an earlier row that started there, else this
    /// one, worked out with the u of each pixel stepped as <see cref="SpanBoxed"/> steps it, so they are the columns it would work out.
    /// </summary>
    private int Columns(ImageFootprints footprints, int x, int count, double u)
    {
        int first = footprints.Find(x, u, count);
        if (first >= 0)
        {
            return first;
        }

        first = footprints.Start(x, u, count);
        double step = _inverse.A;
        for (int i = 0; i < count; i++, u += step)
        {
            Axis across = Axis.Create(u, _grid.HalfU, _grid.Width, _grid.Interpolate);
            footprints.Add(across.Fill(footprints.NextColumns, footprints.NextWeights, 1));
        }

        return first;
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

    /// <summary>The colour of the image at (u, v) of a grid enlarged on both axes: the nearest sample, or a bilinear blend.</summary>
    private static uint SampleColor(uint[] pixels, in Grid grid, double u, double v)
    {
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

    /// <summary>
    /// <see cref="Bilinear(uint[], in Grid, double, double)"/> at u, its rows (<paramref name="row0"/> and <paramref name="row1"/>,
    /// each <paramref name="width"/> samples) and the fraction <paramref name="fv"/> between them given: the same expressions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Bilinear(ReadOnlySpan<uint> row0, ReadOnlySpan<uint> row1, int width, double u, double fv)
    {
        double cu = u - 0.5;
        double fu0 = Math.Floor(cu);
        double fu = Math.Clamp(cu - fu0, 0, 1);
        int u0 = Clamp(fu0, width);
        int u1 = Clamp(fu0 + 1, width);
        uint p00 = row0[u0];
        uint p10 = row0[u1];
        uint p01 = row1[u0];
        uint p11 = row1[u1];
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

    /// <summary>Writes the rows of the footprint of a pixel centred at v (the index of each row's first sample) and their weights; returns how many.</summary>
    private static int Rows(in Grid grid, double v, in Taps taps) =>
        Axis.Create(v, grid.HalfV, grid.Height, grid.Interpolate).Fill(taps.Rows, taps.RowWeights, grid.Width);

    /// <summary>
    /// Averages the samples under the footprint of the pixel centred at (u, v), whose <paramref name="rows"/> rows are in
    /// <paramref name="taps"/> already; an enlarged axis takes one or two samples.
    /// </summary>
    private static uint Average(uint[] pixels, in Grid grid, double u, double v, in Taps taps, int rows)
    {
        int columns = Axis.Create(u, grid.HalfU, grid.Width, grid.Interpolate).Fill(taps.Columns, taps.ColumnWeights, 1);
        return Average(pixels, grid, u, v, taps, rows, taps.Columns[..columns], taps.ColumnWeights);
    }

    /// <summary>Averages the samples under the footprint of the pixel centred at (u, v), its columns and their weights given.</summary>
    private static uint Average(uint[] pixels, in Grid grid, double u, double v, in Taps taps, int rows, ReadOnlySpan<int> columns, ReadOnlySpan<double> columnWeights)
    {
        return BoxFilter.Average(pixels, taps.Rows[..rows], taps.RowWeights, columns, columnWeights, out uint result)
            ? result
            : pixels[(Clamp(Math.Floor(v), grid.Height) * grid.Width) + Clamp(Math.Floor(u), grid.Width)];
    }

    private static uint Channel(double value) => (uint)Math.Clamp((int)(value + 0.5), 0, 255);

    /// <summary>Scratch for one footprint of the box filter: its columns and rows and their weights, <see cref="BoxFilter.MaxTaps"/> each.</summary>
    private readonly ref struct Taps(Span<int> columns, Span<double> columnWeights, Span<int> rows, Span<double> rowWeights)
    {
        public Span<int> Columns { get; } = columns;

        public Span<double> ColumnWeights { get; } = columnWeights;

        public Span<int> Rows { get; } = rows;

        public Span<double> RowWeights { get; } = rowWeights;
    }

    /// <summary>A grid of samples that maps onto the unit square: its size, the half-extents of a pixel's footprint on it, its filter.</summary>
    private readonly struct Grid
    {
        public Grid(int width, int height, in Matrix inverse, double scaleU, double scaleV, bool interpolate, bool average)
        {
            Width = width;
            Height = height;
            ScaleU = scaleU;
            ScaleV = scaleV;
            Interpolate = interpolate;
            HalfU = 0.5 * (Math.Abs(inverse.A) + Math.Abs(inverse.C)) * scaleU;
            HalfV = 0.5 * (Math.Abs(inverse.B) + Math.Abs(inverse.D)) * scaleV;
            if (!average)
            {
                // Without averaging, a reduced axis takes the sample under the pixel's centre, as an enlarged one does.
                HalfU = Math.Min(HalfU, 0.5);
                HalfV = Math.Min(HalfV, 0.5);
            }

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

        /// <summary>Writes the axis's samples, each times <paramref name="stride"/>, and their weights (<see cref="Index"/> and <see cref="Weight"/> of every tap); returns how many.</summary>
        public int Fill(Span<int> indices, Span<double> weights, int stride)
        {
            // The kind is switched on once rather than per tap, as Index and Weight do; the values are the same expressions.
            indices = indices[..Count];
            weights = weights[..indices.Length];
            switch (_kind)
            {
                case Kind.Box:
                    for (int k = 0; k < indices.Length; k++)
                    {
                        int index = _first + k;
                        indices[k] = index * stride;
                        weights[k] = Math.Min(_high, index + 1) - Math.Max(_low, index);
                    }

                    break;
                case Kind.Points:
                    for (int k = 0; k < indices.Length; k++)
                    {
                        indices[k] = Clamp(Math.Floor(_low + ((k + 0.5) * _step)), _size) * stride;
                        weights[k] = 1;
                    }

                    break;
                default:
                    for (int k = 0; k < indices.Length; k++)
                    {
                        indices[k] = Index(k) * stride;
                        weights[k] = Weight(k);
                    }

                    break;
            }

            return Count;
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
