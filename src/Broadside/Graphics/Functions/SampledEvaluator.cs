namespace Broadside.Graphics.Functions;

/// <summary>A Type 0 (sampled) function: inputs encoded into a sample table and interpolated between the surrounding samples.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.10.2. For each input, e_i = Interpolate(x′_i, Domain, Encode) clipped to [0, Size_i − 1]; the outputs interpolate
/// the (already decoded) samples around e. The clause asks only for "interpolation"; this is the multilinear form (the weighted sum
/// of the 2^a corners of the cell, a being the number of inputs that fall between samples; inputs on a sample, or in a dimension of
/// Size 1, add no corners). Beyond eight such inputs the 256-corner cell gives way to simplex interpolation over a + 1 corners
/// (Kuhn's triangulation of the cell), which agrees with multilinear interpolation on the samples themselves and on every function
/// that is linear in each cell.
/// </para>
/// <para>Nothing is allocated per evaluation: the cell lives in stack buffers of m entries.</para>
/// </remarks>
internal sealed class SampledEvaluator : FunctionEvaluator
{
    /// <summary>The most inputs between samples that are interpolated multilinearly (2^8 corners).</summary>
    private const int MaxMultilinearAxes = 8;

    private readonly int[] _last;
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
    public SampledEvaluator(double[] domain, double[]? range, int[] size, double[] encode, float[] samples, int outputCount)
        : base(domain, range, outputCount)
    {
        int m = size.Length;
        _n = outputCount;
        _samples = samples;
        _last = new int[m];
        _strides = new int[m];
        _domainStart = new double[m];
        _encodeStart = new double[m];
        _encodeScale = new double[m];
        int stride = 1;
        for (int i = 0; i < m; i++)
        {
            _last[i] = size[i] - 1;
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
            if (fraction > 0)
            {
                fractions[i] = fraction;
                axes[active++] = i;
            }
        }

        output.Clear();
        axes = axes[..active];
        if (active <= MaxMultilinearAxes)
        {
            Multilinear(origin, axes, fractions, output);
        }
        else
        {
            Simplex(origin, axes, fractions, output);
        }

        return FunctionStatus.Ok;
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
