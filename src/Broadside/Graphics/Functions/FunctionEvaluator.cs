namespace Broadside.Graphics.Functions;

/// <summary>
/// A function compiled once from its dictionary or stream: immutable, safe to evaluate from any number of threads at once, never
/// allocating, never throwing for any input.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.10.1. The base class owns the rules every function type shares: inputs are clipped to the domain before the
/// type's own evaluation runs, and outputs are clipped to the range (when there is one) after it. A non-finite value never leaves:
/// it becomes 0 before range clipping, and values beyond the float range saturate when converted.
/// </para>
/// <para>
/// Arithmetic runs in double precision. Inputs are read from the first <see cref="InputCount"/> elements of the input span and the
/// first <see cref="OutputCount"/> elements of the output span are written; callers size the spans (the public
/// <see cref="PdfFunction"/> checks them).
/// </para>
/// </remarks>
internal abstract class FunctionEvaluator
{
    /// <summary>The most inputs or outputs a function may have (an implementation limit, matching the 32 colourants of DeviceN).</summary>
    public const int MaxArity = 32;

    private readonly double[] _domain;
    private readonly double[]? _range;

    /// <summary>Initializes the shared part of an evaluator.</summary>
    /// <param name="domain">The domain, 2 × m numbers. A pair given in decreasing order clips to the interval between its ends.</param>
    /// <param name="range">The range, 2 × n numbers, or <see langword="null"/> for no output clipping.</param>
    /// <param name="outputCount">n.</param>
    protected FunctionEvaluator(double[] domain, double[]? range, int outputCount)
    {
        _domain = domain;
        _range = range;
        InputCount = domain.Length / 2;
        OutputCount = outputCount;
    }

    /// <summary>Gets m, the number of inputs.</summary>
    public int InputCount { get; }

    /// <summary>Gets n, the number of outputs.</summary>
    public int OutputCount { get; }

    /// <summary>Gets a value indicating whether the function could be compiled; an invalid one fails every evaluation.</summary>
    public virtual bool IsValid => true;

    /// <summary>Evaluates the function at one point.</summary>
    /// <param name="input">At least <see cref="InputCount"/> values.</param>
    /// <param name="output">At least <see cref="OutputCount"/> values; receives the outputs.</param>
    /// <returns>How the evaluation went.</returns>
    public FunctionStatus Evaluate(ReadOnlySpan<double> input, Span<double> output)
    {
        int m = InputCount;
        Span<double> clipped = stackalloc double[m];
        for (int i = 0; i < m; i++)
        {
            clipped[i] = Clip(input[i], _domain[2 * i], _domain[(2 * i) + 1]);
        }

        output = output[..OutputCount];
        FunctionStatus status = EvaluateCore(clipped, output);
        for (int j = 0; j < output.Length; j++)
        {
            double value = output[j];
            if (!double.IsFinite(value))
            {
                value = 0;
                status = Worse(status, FunctionStatus.Repaired);
            }

            output[j] = _range is null ? value : Clip(value, _range[2 * j], _range[(2 * j) + 1]);
        }

        return status;
    }

    /// <summary>Evaluates the function at one point, in single precision.</summary>
    /// <param name="input">At least <see cref="InputCount"/> values.</param>
    /// <param name="output">At least <see cref="OutputCount"/> values; receives the outputs.</param>
    /// <returns>How the evaluation went.</returns>
    public FunctionStatus Evaluate(ReadOnlySpan<float> input, Span<float> output)
    {
        Span<double> x = stackalloc double[InputCount];
        Span<double> y = stackalloc double[OutputCount];
        for (int i = 0; i < x.Length; i++)
        {
            x[i] = input[i];
        }

        FunctionStatus status = Evaluate(x, y);
        for (int j = 0; j < y.Length; j++)
        {
            output[j] = (float)Math.Clamp(y[j], float.MinValue, float.MaxValue);
        }

        return status;
    }

    /// <summary>Evaluates the function at <paramref name="count"/> points stored one after the other.</summary>
    /// <param name="inputs">At least <paramref name="count"/> × <see cref="InputCount"/> values, point after point.</param>
    /// <param name="outputs">At least <paramref name="count"/> × <see cref="OutputCount"/> values; receives the outputs, point after point.</param>
    /// <param name="count">The number of points.</param>
    /// <returns>The worst status of the evaluations.</returns>
    public FunctionStatus Evaluate(ReadOnlySpan<float> inputs, Span<float> outputs, int count)
    {
        int m = InputCount;
        int n = OutputCount;
        FunctionStatus status = FunctionStatus.Ok;
        for (int k = 0; k < count; k++)
        {
            status = Worse(status, Evaluate(inputs.Slice(k * m, m), outputs.Slice(k * n, n)));
        }

        return status;
    }

    /// <summary>Clips <paramref name="value"/> to the interval between <paramref name="first"/> and <paramref name="second"/>; NaN becomes the lower end.</summary>
    /// <param name="value">The value.</param>
    /// <param name="first">One end, normally the lower.</param>
    /// <param name="second">The other end, normally the upper.</param>
    /// <returns>The clipped value.</returns>
    internal static double Clip(double value, double first, double second)
    {
        double low = Math.Min(first, second);
        double high = Math.Max(first, second);
        return value >= low ? (value <= high ? value : high) : low;
    }

    /// <summary>The Interpolate function of §7.10.1: the line through (xmin, ymin) and (xmax, ymax) at x; ymin when xmin = xmax.</summary>
    /// <param name="x">x.</param>
    /// <param name="xMin">xmin.</param>
    /// <param name="xMax">xmax.</param>
    /// <param name="yMin">ymin.</param>
    /// <param name="yMax">ymax.</param>
    /// <returns>The interpolated value.</returns>
    internal static double Interpolate(double x, double xMin, double xMax, double yMin, double yMax) =>
        xMax == xMin ? yMin : yMin + ((x - xMin) * (yMax - yMin) / (xMax - xMin));

    /// <summary>The worse of two statuses.</summary>
    /// <param name="left">One status.</param>
    /// <param name="right">The other.</param>
    /// <returns>The worse.</returns>
    internal static FunctionStatus Worse(FunctionStatus left, FunctionStatus right) => left >= right ? left : right;

    /// <summary>The type's own evaluation, over inputs already clipped to the domain; the base clips the outputs.</summary>
    /// <param name="input">Exactly <see cref="InputCount"/> clipped inputs.</param>
    /// <param name="output">Exactly <see cref="OutputCount"/> outputs to write.</param>
    /// <returns>How the evaluation went.</returns>
    protected abstract FunctionStatus EvaluateCore(ReadOnlySpan<double> input, Span<double> output);
}
