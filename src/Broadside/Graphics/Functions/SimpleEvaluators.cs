namespace Broadside.Graphics.Functions;

/// <summary>A function that could not be compiled: every evaluation fails and writes 0, clipped to the range when there is one.</summary>
/// <remarks>ISO 32000-2 §7.10; ADR 0005. Consumers apply their own fallback (a tint transform's default, skipping a shading).</remarks>
internal sealed class InvalidEvaluator(double[] domain, double[]? range, int outputCount) : FunctionEvaluator(domain, range, outputCount)
{
    /// <inheritdoc/>
    public override bool IsValid => false;

    /// <inheritdoc/>
    protected override FunctionStatus EvaluateCore(ReadOnlySpan<double> input, Span<double> output)
    {
        output.Clear();
        return FunctionStatus.Failed;
    }
}

/// <summary>The name <c>Identity</c> where an entry accepts it: m inputs returned unchanged as m outputs.</summary>
/// <remarks>ISO 32000-2 §8.4.5 Table 57 (<c>TR</c>), §11.6.5.1 Table 142 (soft mask <c>TR</c>).</remarks>
internal sealed class IdentityEvaluator(int count) : FunctionEvaluator(FunctionCompilation.UnboundedPairs(count), range: null, count)
{
    /// <inheritdoc/>
    protected override FunctionStatus EvaluateCore(ReadOnlySpan<double> input, Span<double> output)
    {
        input.CopyTo(output);
        return FunctionStatus.Ok;
    }
}

/// <summary>An array of n functions of m inputs and one output each, evaluated as one m-in n-out function.</summary>
/// <remarks>ISO 32000-2 §8.7.4.5.2-8.7.4.5.4 Tables 78-80 (shading <c>Function</c>: "an array of n 2-in, 1-out functions" for Type 1, 1-in for Types 2 and 3).</remarks>
internal sealed class ArrayEvaluator(double[] domain, FunctionEvaluator[] functions) : FunctionEvaluator(domain, range: null, functions.Length)
{
    /// <inheritdoc/>
    protected override FunctionStatus EvaluateCore(ReadOnlySpan<double> input, Span<double> output)
    {
        FunctionStatus status = FunctionStatus.Ok;
        for (int j = 0; j < functions.Length; j++)
        {
            status = Worse(status, functions[j].Evaluate(input, output.Slice(j, 1)));
        }

        return status;
    }
}

/// <summary>A Type 2 (exponential interpolation) function: y_j = C0_j + x^N × (C1_j − C0_j).</summary>
/// <remarks>
/// ISO 32000-2 §7.10.3, Table 40. N = 1 and N = 2 avoid <see cref="Math.Pow"/>. When x^N is not finite (x = 0 with a negative N, a
/// negative x with a non-integer N: the domain should have excluded both) the outputs are C0 and the status is
/// <see cref="FunctionStatus.Repaired"/>.
/// </remarks>
internal sealed class ExponentialEvaluator : FunctionEvaluator
{
    private readonly double[] _c0;
    private readonly double[] _difference;
    private readonly double _exponent;

    /// <summary>Initializes a new instance of the <see cref="ExponentialEvaluator"/> class.</summary>
    /// <param name="domain">One pair.</param>
    /// <param name="range">n pairs, or <see langword="null"/>.</param>
    /// <param name="c0">C0, n numbers.</param>
    /// <param name="c1">C1, n numbers.</param>
    /// <param name="exponent">N.</param>
    public ExponentialEvaluator(double[] domain, double[]? range, double[] c0, double[] c1, double exponent)
        : base(domain, range, c0.Length)
    {
        _c0 = c0;
        _difference = new double[c0.Length];
        for (int j = 0; j < c0.Length; j++)
        {
            _difference[j] = c1[j] - c0[j];
        }

        _exponent = exponent;
    }

    /// <inheritdoc/>
    protected override FunctionStatus EvaluateCore(ReadOnlySpan<double> input, Span<double> output)
    {
        double x = input[0];
        double power = _exponent switch
        {
            1 => x,
            2 => x * x,
            _ => Math.Pow(x, _exponent),
        };
        if (!double.IsFinite(power))
        {
            _c0.CopyTo(output);
            return FunctionStatus.Repaired;
        }

        for (int j = 0; j < output.Length; j++)
        {
            output[j] = _c0[j] + (power * _difference[j]);
        }

        return FunctionStatus.Ok;
    }
}

/// <summary>A Type 3 (stitching) function: one of k 1-input functions chosen by the subdomain x falls in, over an encoded input.</summary>
/// <remarks>
/// ISO 32000-2 §7.10.4, Table 41. Intervals are closed on the left and open on the right, except that the last is closed on the
/// right and, when Domain0 = Bounds0, the first is the single point Domain0 and the second is open on the left. A degenerate interval
/// maps to its Encode's first number, which gives the clause's rules for Bounds0 = Domain0 (x′ = Encode0) and Bounds(k−2) = Domain1
/// (x′ = Encode2(k−1)). Each sub-function clips to its own domain and range.
/// </remarks>
internal sealed class StitchingEvaluator : FunctionEvaluator
{
    private readonly FunctionEvaluator[] _functions;
    private readonly double[] _bounds;
    private readonly double[] _encode;
    private readonly double _domain0;
    private readonly double _domain1;

    /// <summary>Initializes a new instance of the <see cref="StitchingEvaluator"/> class.</summary>
    /// <param name="domain">One pair.</param>
    /// <param name="range">n pairs, or <see langword="null"/>.</param>
    /// <param name="functions">k 1-input functions with n outputs each.</param>
    /// <param name="bounds">k − 1 bounds.</param>
    /// <param name="encode">2 × k numbers.</param>
    public StitchingEvaluator(double[] domain, double[]? range, FunctionEvaluator[] functions, double[] bounds, double[] encode)
        : base(domain, range, functions[0].OutputCount)
    {
        _functions = functions;
        _bounds = bounds;
        _encode = encode;
        _domain0 = domain[0];
        _domain1 = domain[1];
    }

    /// <inheritdoc/>
    protected override FunctionStatus EvaluateCore(ReadOnlySpan<double> input, Span<double> output)
    {
        double x = input[0];
        int last = _functions.Length - 1;
        int i = 0;
        if (last == 0 || _bounds[0] != _domain0 || x != _domain0)
        {
            while (i < last && x >= _bounds[i])
            {
                i++;
            }
        }

        double low = i == 0 ? _domain0 : _bounds[i - 1];
        double high = i == last ? _domain1 : _bounds[i];
        Span<double> encoded = stackalloc double[1];
        encoded[0] = Interpolate(x, low, high, _encode[2 * i], _encode[(2 * i) + 1]);
        return _functions[i].Evaluate(encoded, output);
    }
}
