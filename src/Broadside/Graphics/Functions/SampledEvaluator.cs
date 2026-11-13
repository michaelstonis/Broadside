namespace Broadside.Graphics.Functions;

/// <summary>A Type 0 (sampled) function: inputs encoded into a sample table and interpolated between the surrounding samples.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.10.2. For each input, e_i = Interpolate(x′_i, Domain, Encode) clipped to [0, Size_i − 1]; the outputs interpolate
/// the (already decoded) samples around e. The clause asks only for "interpolation" (Order 1, linear) or "cubic spline interpolation"
/// (Order 3) "from the nearest surrounding values in the sample table", and says that with Size below 4 a spline "is not possible and
/// Order 3 shall be ignored". It defines neither the spline nor how several inputs combine, so this is the documented choice.
/// </para>
/// <para>
/// Order 1 is multilinear (the weighted sum of the 2^a corners of the cell, a being the number of inputs that fall between samples;
/// inputs on a sample, or in a dimension of Size 1, add no corners). Beyond eight such inputs the 256-corner cell gives way to
/// simplex interpolation over a + 1 corners (Kuhn's triangulation of the cell), which agrees with multilinear interpolation on the
/// samples themselves and on every function that is linear in each cell.
/// </para>
/// <para>
/// Order 3 is a local cubic spline along each input whose Size is at least 4: the cubic Hermite spline through the samples with
/// the Catmull-Rom tangent (half the difference of the two neighbors) at an interior sample and, at the first and last sample, the
/// difference to the one neighbor. It passes through every sample, has a continuous first derivative, and reads the four nearest
/// samples of an input (three in the end cells). With several inputs the spline is applied along each in turn (the tensor product:
/// the weights of the 4^c × 2^l taps multiply), and an input whose Size is below 4 is interpolated linearly (§7.10.2 ignores Order 3
/// there). The tap count is bounded: at most four inputs per evaluation are cubic (the first four that fall between samples), and
/// when more than eight fall between samples the whole evaluation is linear, as for Order 1. Only multiplications and additions are
/// used, so the result is the same on every platform.
/// </para>
/// <para>Nothing is allocated per evaluation: the cell lives in stack buffers of m entries and the taps in buffers of 4 per input.</para>
/// </remarks>
internal sealed class SampledEvaluator : FunctionEvaluator
{
    /// <summary>The most inputs between samples that are interpolated multilinearly (2^8 corners).</summary>
    private const int MaxMultilinearAxes = 8;

    /// <summary>The most inputs per evaluation that use a cubic spline (4^4 taps); the rest of the inputs between samples are linear.</summary>
    private const int MaxCubicAxes = 4;

    private readonly int[] _last;
    private readonly bool[] _cubic;
    private readonly bool _anyCubic;
    private readonly int[] _strides;
    private readonly double[] _domainStart;
    private readonly double[] _encodeStart;
    private readonly double[] _encodeScale;
    private readonly float[] _samples;
    private readonly int _n;

    /// <summary>Initializes a new instance of the <see cref="SampledEvaluator"/> class.</summary>
    /// <param name="domain">m pairs.</param>
    /// <param name="range">n pairs, or <see langword="null"/> when missing (repaired).</param>
    /// <param name="size">m positive sizes.</param>
    /// <param name="encode">m pairs.</param>
    /// <param name="samples">n × the product of the sizes decoded samples, first dimension fastest, a point's n values adjacent.</param>
    /// <param name="outputCount">n.</param>
    /// <param name="cubic">Whether Order is 3; the spline then applies to each input of Size 4 or more.</param>
    public SampledEvaluator(double[] domain, double[]? range, int[] size, double[] encode, float[] samples, int outputCount, bool cubic = false)
        : base(domain, range, outputCount)
    {
        int m = size.Length;
        _n = outputCount;
        _samples = samples;
        _last = new int[m];
        _cubic = new bool[m];
        _strides = new int[m];
        _domainStart = new double[m];
        _encodeStart = new double[m];
        _encodeScale = new double[m];
        int stride = 1;
        for (int i = 0; i < m; i++)
        {
            _last[i] = size[i] - 1;
            _cubic[i] = cubic && size[i] >= 4;
            _anyCubic |= _cubic[i];
            _strides[i] = stride;
            stride *= size[i];
            _domainStart[i] = domain[2 * i];
            _encodeStart[i] = encode[2 * i];
            double width = domain[(2 * i) + 1] - domain[2 * i];
            _encodeScale[i] = width == 0 ? 0 : (encode[(2 * i) + 1] - encode[2 * i]) / width;
        }
    }

    /// <inheritdoc/>
    protected override FunctionStatus EvaluateCore(ReadOnlySpan<double> input, Span<double> output)
    {
        int m = input.Length;
        Span<int> axes = stackalloc int[m];
        Span<double> fractions = stackalloc double[m];
        Span<int> cells = stackalloc int[m];
        int origin = 0;
        int active = 0;
        for (int i = 0; i < m; i++)
        {
            double e = _encodeStart[i] + ((input[i] - _domainStart[i]) * _encodeScale[i]);
            int last = _last[i];
            e = e >= 0 ? (e <= last ? e : last) : 0;
            if (last == 0)
            {
                continue;
            }

            int cell = Math.Min((int)e, last - 1);
            double fraction = e - cell;
            origin += cell * _strides[i];
            cells[i] = cell;
            if (fraction > 0)
            {
                fractions[i] = fraction;
                axes[active++] = i;
            }
        }

        output.Clear();
        axes = axes[..active];
        if (_anyCubic && active <= MaxMultilinearAxes)
        {
            Cubic(origin, axes, cells, fractions, output);
        }
        else if (active <= MaxMultilinearAxes)
        {
            Multilinear(origin, axes, fractions, output);
        }
        else
        {
            Simplex(origin, axes, fractions, output);
        }

        return FunctionStatus.Ok;
    }

    private void Cubic(int origin, ReadOnlySpan<int> axes, ReadOnlySpan<int> cells, ReadOnlySpan<double> fractions, Span<double> output)
    {
        // One row of up to four taps per interpolated input: the offset from the cell's first sample and the weight.
        Span<int> offsets = stackalloc int[axes.Length * 4];
        Span<double> weights = stackalloc double[axes.Length * 4];
        Span<int> counts = stackalloc int[axes.Length];
        Span<int> choice = stackalloc int[axes.Length];
        choice.Clear();
        int cubicAxes = 0;
        long taps = 1;
        for (int t = 0; t < axes.Length; t++)
        {
            int axis = axes[t];
            int stride = _strides[axis];
            double f = fractions[axis];
            Span<int> o = offsets.Slice(t * 4, 4);
            Span<double> w = weights.Slice(t * 4, 4);
            if (!_cubic[axis] || cubicAxes == MaxCubicAxes)
            {
                counts[t] = 2;
                o[0] = 0;
                o[1] = stride;
                w[0] = 1 - f;
                w[1] = f;
            }
            else
            {
                cubicAxes++;
                // Catmull-Rom weights of the samples at cell - 1, cell, cell + 1 and cell + 2.
                double f2 = f * f;
                double f3 = f2 * f;
                double before = ((-f3) + (2 * f2) - f) / 2;
                double at = ((3 * f3) - (5 * f2) + 2) / 2;
                double next = ((-3 * f3) + (4 * f2) + f) / 2;
                double after = (f3 - f2) / 2;
                int cell = cells[axis];
                if (cell == 0)
                {
                    // The sample before the first is the first reflected through the second (a straight end tangent).
                    counts[t] = 3;
                    o[0] = 0;
                    o[1] = stride;
                    o[2] = 2 * stride;
                    w[0] = at + (2 * before);
                    w[1] = next - before;
                    w[2] = after;
                }
                else if (cell + 1 == _last[axis])
                {
                    // Likewise after the last sample.
                    counts[t] = 3;
                    o[0] = -stride;
                    o[1] = 0;
                    o[2] = stride;
                    w[0] = before;
                    w[1] = at - after;
                    w[2] = next + (2 * after);
                }
                else
                {
                    counts[t] = 4;
                    o[0] = -stride;
                    o[1] = 0;
                    o[2] = stride;
                    o[3] = 2 * stride;
                    w[0] = before;
                    w[1] = at;
                    w[2] = next;
                    w[3] = after;
                }
            }

            taps *= counts[t];
        }

        // Walk the product of the rows like an odometer, the first input fastest.
        for (long tap = 0; tap < taps; tap++)
        {
            double weight = 1;
            int index = origin;
            for (int t = 0; t < axes.Length; t++)
            {
                weight *= weights[(t * 4) + choice[t]];
                index += offsets[(t * 4) + choice[t]];
            }

            Accumulate(index, weight, output);
            for (int t = 0; t < axes.Length; t++)
            {
                if (++choice[t] < counts[t])
                {
                    break;
                }

                choice[t] = 0;
            }
        }
    }

    private void Multilinear(int origin, ReadOnlySpan<int> axes, ReadOnlySpan<double> fractions, Span<double> output)
    {
        int corners = 1 << axes.Length;
        for (int corner = 0; corner < corners; corner++)
        {
            double weight = 1;
            int index = origin;
            for (int t = 0; t < axes.Length; t++)
            {
                int axis = axes[t];
                if ((corner & (1 << t)) != 0)
                {
                    weight *= fractions[axis];
                    index += _strides[axis];
                }
                else
                {
                    weight *= 1 - fractions[axis];
                }
            }

            Accumulate(index, weight, output);
        }
    }

    private void Simplex(int origin, Span<int> axes, ReadOnlySpan<double> fractions, Span<double> output)
    {
        // Order the axes by decreasing fraction (insertion sort: m <= 32), then walk from the origin corner one axis at a time.
        for (int t = 1; t < axes.Length; t++)
        {
            int axis = axes[t];
            int s = t - 1;
            while (s >= 0 && fractions[axes[s]] < fractions[axis])
            {
                axes[s + 1] = axes[s];
                s--;
            }

            axes[s + 1] = axis;
        }

        int index = origin;
        Accumulate(index, 1 - fractions[axes[0]], output);
        for (int t = 0; t < axes.Length; t++)
        {
            index += _strides[axes[t]];
            double next = t + 1 < axes.Length ? fractions[axes[t + 1]] : 0;
            Accumulate(index, fractions[axes[t]] - next, output);
        }
    }

    private void Accumulate(int point, double weight, Span<double> output)
    {
        int first = point * _n;
        for (int j = 0; j < output.Length; j++)
        {
            output[j] += weight * _samples[first + j];
        }
    }
}
