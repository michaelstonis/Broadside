using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics.Functions;

/// <summary>Compiles Type 0 (sampled) functions: reads the entries and decodes the whole sample table once.</summary>
/// <remarks>
/// ISO 32000-2 §7.10.2, Table 39. The samples are one bit stream, most significant bit first, with no padding anywhere (not even at
/// the end of a row, unlike image data), the first dimension varying fastest and the n values of one point adjacent. They are mapped
/// through Decode at compile time (Decode is linear, so decoding before interpolating gives the same result) and kept in single
/// precision.
/// </remarks>
internal static class SampledCompiler
{
    /// <summary>The most sample values (n × the product of Size) a function may have: a guard against allocation bombs.</summary>
    public const long MaxSampleValues = 1 << 24;

    /// <summary>Compiles a Type 0 function.</summary>
    /// <param name="stream">The function stream.</param>
    /// <param name="compilation">The compilation.</param>
    /// <param name="expectedOutputs">The consumer's number of outputs, for a missing Range; 0 when unknown.</param>
    /// <returns>The evaluator.</returns>
    public static FunctionEvaluator Compile(CosStream stream, FunctionCompilation compilation, int expectedOutputs)
    {
        CosDictionary dictionary = stream.Dictionary;
        int[]? size = ReadSize(dictionary, compilation);
        if (size is null)
        {
            return FunctionCompiler.Invalid(0, expectedOutputs);
        }

        int m = size.Length;
        double[]? domain = compilation.ReadDomain(dictionary, inferredInputs: m);
        if (domain is null)
        {
            return FunctionCompiler.Invalid(m, expectedOutputs);
        }

        domain = Fit(domain, m, FunctionCompilation.UnitPairs(m), compilation, "Domain");
        double[]? range = compilation.ReadRange(dictionary);
        int n;
        if (range is not null)
        {
            n = range.Length / 2;
        }
        else if (expectedOutputs > 0)
        {
            compilation.Repaired("Range is missing; the number of outputs comes from where the function is used and outputs are not clipped.");
            n = expectedOutputs;
        }
        else
        {
            compilation.Invalid("Range is missing, so the number of outputs is unknown.");
            return FunctionCompiler.Invalid(m, 0);
        }

        if (n > FunctionEvaluator.MaxArity)
        {
            compilation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Range has {n} outputs; at most {FunctionEvaluator.MaxArity} are supported."));
            return FunctionCompiler.Invalid(m, 0);
        }

        if (ReadBitsPerSample(dictionary, compilation) is not { } bitsPerSample)
        {
            return FunctionCompiler.Invalid(m, n, range);
        }

        ReadOrder(dictionary, size, compilation);
        double[] defaultEncode = new double[2 * m];
        for (int i = 0; i < m; i++)
        {
            defaultEncode[(2 * i) + 1] = size[i] - 1;
        }

        double[] encode = Fit(compilation.ReadNumbers(dictionary, FunctionNames.Encode) ?? defaultEncode, m, defaultEncode, compilation, "Encode");
        double[] defaultDecode = range ?? FunctionCompilation.UnitPairs(n);
        double[] decode = Fit(compilation.ReadNumbers(dictionary, FunctionNames.Decode) ?? defaultDecode, n, defaultDecode, compilation, "Decode");

        long values = n;
        foreach (int dimension in size)
        {
            values *= dimension;
            if (values > MaxSampleValues)
            {
                compilation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Size calls for more than {MaxSampleValues} sample values; the function is not used."));
                return FunctionCompiler.Invalid(m, n, range);
            }
        }

        float[] samples = ReadSamples(compilation.Decode(stream).Span, (int)values, n, bitsPerSample, decode, compilation);
        return new SampledEvaluator(domain, range, size, encode, samples, n);
    }

    /// <summary>Reads Size: m positive integers; reals are truncated with a diagnostic.</summary>
    private static int[]? ReadSize(CosDictionary dictionary, FunctionCompilation compilation)
    {
        double[]? numbers = compilation.ReadNumbers(dictionary, FunctionNames.Size);
        if (numbers is not { Length: > 0 } || numbers.Length > FunctionEvaluator.MaxArity)
        {
            compilation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Size is missing, empty, or has more than {FunctionEvaluator.MaxArity} dimensions."));
            return null;
        }

        int[] size = new int[numbers.Length];
        bool truncated = false;
        for (int i = 0; i < size.Length; i++)
        {
            double value = Math.Truncate(numbers[i]);
            truncated |= value != numbers[i];
            if (!(value >= 1 && value <= MaxSampleValues))
            {
                compilation.Invalid("Size holds a number of samples that is not a positive integer, or is too large.");
                return null;
            }

            size[i] = (int)value;
        }

        if (truncated)
        {
            compilation.Repaired("Size holds numbers that are not integers; they are truncated.");
        }

        return size;
    }

    /// <summary>Reads BitsPerSample: one of the eight values, or (with a diagnostic) any other width from 1 to 32.</summary>
    private static int? ReadBitsPerSample(CosDictionary dictionary, FunctionCompilation compilation)
    {
        double? value = compilation.ReadNumber(dictionary, FunctionNames.BitsPerSample);
        if (value is not { } bits || bits != Math.Floor(bits) || bits < 1 || bits > 32)
        {
            compilation.Invalid("BitsPerSample is missing or is not an integer from 1 to 32.");
            return null;
        }

        int result = (int)bits;
        if (result is not (1 or 2 or 4 or 8 or 12 or 16 or 24 or 32))
        {
            compilation.Repaired(string.Create(CultureInfo.InvariantCulture, $"BitsPerSample {result} is not 1, 2, 4, 8, 12, 16, 24 or 32; samples are read with {result} bits."));
        }

        return result;
    }

    /// <summary>Reads Order: 3 is evaluated linearly (noted when a cubic spline was possible); other values are repaired to 1.</summary>
    private static void ReadOrder(CosDictionary dictionary, int[] size, FunctionCompilation compilation)
    {
        double? order = compilation.ReadNumber(dictionary, FunctionNames.Order);
        if (order is null or 1)
        {
            return;
        }

        if (order != 3)
        {
            compilation.Repaired("Order is neither 1 nor 3; linear interpolation is used.");
        }
        else if (Array.TrueForAll(size, static dimension => dimension >= 4))
        {
            // §7.10.2: with fewer than 4 samples in a dimension Order 3 "shall be ignored", so only a possible spline is noted.
            compilation.Report(
                DiagnosticCodes.FunctionOrderUnsupported,
                DiagnosticSeverity.Information,
                "Order 3 (cubic spline interpolation) is not implemented; linear interpolation is used.");
        }
    }

    /// <summary>Fits an array of pairs to <paramref name="pairs"/> pairs, taking missing ones from <paramref name="defaults"/>.</summary>
    private static double[] Fit(double[] values, int pairs, double[] defaults, FunctionCompilation compilation, string key)
    {
        if (values.Length == 2 * pairs)
        {
            return values;
        }

        compilation.Repaired(string.Create(CultureInfo.InvariantCulture, $"{key} has {values.Length} numbers where {2 * pairs} are needed; missing pairs take their defaults and extra numbers are ignored."));
        double[] fitted = (double[])defaults.Clone();
        values.AsSpan(0, Math.Min(values.Length & ~1, fitted.Length)).CopyTo(fitted);
        return fitted;
    }

    /// <summary>Reads <paramref name="count"/> samples of <paramref name="bits"/> bits each and decodes them.</summary>
    private static float[] ReadSamples(ReadOnlySpan<byte> data, int count, int n, int bits, double[] decode, FunctionCompilation compilation)
    {
        long required = (((long)count * bits) + 7) / 8;
        if (data.Length < required)
        {
            compilation.Report(
                DiagnosticCodes.FunctionSampleDataTruncated,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The sample data has {data.Length} bytes where {required} are needed; the missing samples are 0."));
        }

        double maximum = (1UL << bits) - 1;
        ulong mask = (1UL << bits) - 1;
        double[] scale = new double[n];
        for (int j = 0; j < n; j++)
        {
            scale[j] = (decode[(2 * j) + 1] - decode[2 * j]) / maximum;
        }

        float[] samples = new float[count];
        ulong buffer = 0;
        int buffered = 0;
        int position = 0;
        for (int k = 0; k < count; k++)
        {
            while (buffered < bits)
            {
                buffer = (buffer << 8) | (position < data.Length ? data[position] : 0UL);
                position++;
                buffered += 8;
            }

            buffered -= bits;
            ulong raw = (buffer >> buffered) & mask;
            int j = k % n;
            samples[k] = (float)(decode[2 * j] + (raw * scale[j]));
        }

        return samples;
    }
}
