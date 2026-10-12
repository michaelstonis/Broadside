using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace Broadside.Rendering.Icc;

/// <summary>A one-dimensional tone reproduction curve of an ICC profile: a <c>curv</c> or <c>para</c> tag, domain and range [0, 1].</summary>
/// <remarks>
/// ICC.1:2022 §10.6 (curveType: no entries is the identity, one entry a u8Fixed8Number gamma, more a uniformly spaced table of
/// uInt16Number values with linear interpolation between them) and §10.18 (parametricCurveType, the five functions of Table 68,
/// clipped to [0, 1]). Immutable.
/// </remarks>
internal sealed class IccCurve
{
    /// <summary>The number of parameters of each parametric function type (Table 68).</summary>
    private static readonly int[] ParameterCounts = [1, 3, 4, 5, 7];

    private readonly int _function;
    private readonly double[] _parameters;
    private readonly ushort[] _samples;

    private IccCurve(int function, double[] parameters, ushort[] samples)
    {
        _function = function;
        _parameters = parameters;
        _samples = samples;
    }

    /// <summary>Reads a curve tag.</summary>
    /// <param name="tag">The tag data element, from its type signature on.</param>
    /// <param name="curve">The curve.</param>
    /// <param name="problem">Why the tag is not a curve.</param>
    /// <returns>Whether the tag is a curve.</returns>
    public static bool TryRead(ReadOnlySpan<byte> tag, [NotNullWhen(true)] out IccCurve? curve, [NotNullWhen(false)] out string? problem) =>
        TryRead(tag, out curve, out _, out problem);

    /// <summary>Reads a curve embedded in a lookup-table tag, where curves follow one another (ICC.1:2022 §10.12.2).</summary>
    /// <param name="tag">The data from the curve's type signature on; it may run past the curve.</param>
    /// <param name="curve">The curve.</param>
    /// <param name="length">The number of bytes the curve takes, before any padding.</param>
    /// <param name="problem">Why the data is not a curve.</param>
    /// <returns>Whether the data starts with a curve.</returns>
    public static bool TryRead(ReadOnlySpan<byte> tag, [NotNullWhen(true)] out IccCurve? curve, out int length, [NotNullWhen(false)] out string? problem)
    {
        curve = null;
        length = 0;
        problem = null;
        if (tag.Length >= 12 && tag.StartsWith("curv"u8))
        {
            uint count = BinaryPrimitives.ReadUInt32BigEndian(tag[8..]);
            if (count > (uint)(tag.Length - 12) / 2)
            {
                problem = $"it holds {count} entries but has room for {(tag.Length - 12) / 2}";
                return false;
            }

            ushort[] samples = new ushort[count];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = BinaryPrimitives.ReadUInt16BigEndian(tag[(12 + (2 * i))..]);
            }

            curve = new IccCurve(-1, [], samples);
            length = 12 + (2 * samples.Length);
            return true;
        }

        if (tag.Length >= 12 && tag.StartsWith("para"u8))
        {
            int function = BinaryPrimitives.ReadUInt16BigEndian(tag[8..]);
            if (function >= ParameterCounts.Length)
            {
                problem = $"its function type {function} is not one of Table 68";
                return false;
            }

            int parameters = ParameterCounts[function];
            if (tag.Length < 12 + (4 * parameters))
            {
                problem = $"function type {function} needs {parameters} parameters";
                return false;
            }

            double[] values = new double[parameters];
            for (int i = 0; i < parameters; i++)
            {
                values[i] = BinaryPrimitives.ReadInt32BigEndian(tag[(12 + (4 * i))..]) / 65536.0;
            }

            curve = new IccCurve(function, values, []);
            length = 12 + (4 * parameters);
            return true;
        }

        problem = "it is neither a curveType nor a parametricCurveType";
        return false;
    }

    /// <summary>Evaluates the curve.</summary>
    /// <param name="x">The input, clipped to [0, 1].</param>
    /// <returns>The output in [0, 1]; an undefined value (Table 68 NOTE 1) is 0.</returns>
    public double Evaluate(double x)
    {
        x = Clip(x);
        return _function < 0 ? Sampled(x) : Clip(Parametric(x));
    }

    /// <summary>Samples the curve at <paramref name="size"/> evenly spaced inputs from 0 to 1, for interpolation.</summary>
    /// <param name="size">The number of samples, at least 2.</param>
    /// <returns>The outputs.</returns>
    public float[] ToTable(int size)
    {
        float[] table = new float[size];
        for (int i = 0; i < size; i++)
        {
            table[i] = (float)Evaluate(i / (double)(size - 1));
        }

        return table;
    }

    /// <summary>Gets whether the curve is the identity: a curveType with no entries or a gamma of 1 (§10.6), or a type 0 parametric gamma of 1 (§10.18).</summary>
    public bool IsIdentity => _function < 0
        ? _samples.Length == 0 || (_samples.Length == 1 && _samples[0] == 256)
        : _function == 0 && _parameters[0] == 1;

    /// <summary>
    /// The curve as a table for linear interpolation: a sampled curve's own entries, which is exact (§10.6), or
    /// <paramref name="size"/> samples of any other curve.
    /// </summary>
    /// <param name="size">The number of samples of a gamma or parametric curve.</param>
    /// <returns>The table, at least two entries, values in [0, 1].</returns>
    public float[] ToLookupTable(int size)
    {
        if (_function >= 0 || _samples.Length < 2)
        {
            return ToTable(size);
        }

        float[] table = new float[_samples.Length];
        for (int i = 0; i < table.Length; i++)
        {
            table[i] = _samples[i] / 65535f;
        }

        return table;
    }

    /// <summary>Clips to [0, 1]; NaN becomes 0.</summary>
    private static double Clip(double value) => value > 0 ? Math.Min(value, 1) : 0;

    private double Sampled(double x)
    {
        ushort[] samples = _samples;
        switch (samples.Length)
        {
            case 0:
                return x;
            case 1:
                // A u8Fixed8Number gamma: Y = X^gamma, not the inverse (§10.6).
                return Math.Pow(x, samples[0] / 256.0);
            default:
                double position = x * (samples.Length - 1);
                int index = Math.Min((int)position, samples.Length - 2);
                double low = samples[index] / 65535.0;
                double high = samples[index + 1] / 65535.0;
                return low + ((position - index) * (high - low));
        }
    }

    private double Parametric(double x)
    {
        double[] p = _parameters;
        double g = p[0];
        switch (_function)
        {
            case 0:
                return Math.Pow(x, g);
            case 1:
                return x >= -p[2] / p[1] ? Math.Pow((p[1] * x) + p[2], g) : 0;
            case 2:
                return x >= -p[2] / p[1] ? Math.Pow((p[1] * x) + p[2], g) + p[3] : p[3];
            case 3:
                return x >= p[4] ? Math.Pow((p[1] * x) + p[2], g) : p[3] * x;
            default:
                return x >= p[4] ? Math.Pow((p[1] * x) + p[2], g) + p[5] : (p[3] * x) + p[6];
        }
    }
}
