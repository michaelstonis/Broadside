using System.Globalization;
using Broadside.Graphics;
using Broadside.TestSupport;
using static Broadside.Tests.Graphics.FunctionTesting;

namespace Broadside.Tests.Graphics;

/// <summary>Type 0 (sampled) functions, ISO 32000-2 §7.10.2, Table 39.</summary>
public sealed class SampledFunctionTests
{
    private static readonly int[] AllBitsPerSample = [1, 2, 4, 8, 12, 16, 24, 32];

    private static readonly int[][] Sizes = [[5], [3, 4], [3, 2, 3]];

    public static TheoryData<int, int, int> Shapes()
    {
        var data = new TheoryData<int, int, int>();
        foreach (int bits in AllBitsPerSample)
        {
            foreach (int inputs in (int[])[1, 2, 3])
            {
                foreach (int outputs in (int[])[1, 3, 4])
                {
                    data.Add(bits, inputs, outputs);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Every_sample_of_a_table_comes_back_at_its_grid_point(int bits, int inputs, int outputs)
    {
        int[] size = Sizes[inputs - 1];
        ulong[] raw = Table(size, outputs, bits);
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Sampled(size, outputs, bits, raw));
        double maximum = Math.Pow(2, bits) - 1;

        foreach (int[] point in GridPoints(size))
        {
            float[] x = [.. point.Select((g, i) => (float)g / (size[i] - 1))];
            int first = Index(point, size) * outputs;
            float[] expected = [.. Enumerable.Range(0, outputs).Select(j => (float)(raw[first + j] / maximum))];
            Near(expected, function.At(x), 1e-6);
        }

        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Between_two_samples_the_output_is_their_average(int bits, int inputs, int outputs)
    {
        int[] size = Sizes[inputs - 1];
        ulong[] raw = Table(size, outputs, bits);
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Sampled(size, outputs, bits, raw));
        double maximum = Math.Pow(2, bits) - 1;

        // Halfway along the first dimension between grid points 1 and 2, on grid point 0 elsewhere.
        float[] x = new float[inputs];
        x[0] = 1.5f / (size[0] - 1);
        int left = Index([1, .. new int[inputs - 1]], size) * outputs;
        int right = Index([2, .. new int[inputs - 1]], size) * outputs;
        float[] expected = [.. Enumerable.Range(0, outputs).Select(j => (float)((raw[left + j] + raw[right + j]) / 2.0 / maximum))];

        Near(expected, function.At(x), 1e-6);
    }

    [Fact]
    public void Bilinear_interpolation_weights_the_four_corners_of_the_cell()
    {
        // Size [2 2]: f(0,0) = 0, f(1,0) = 1, f(0,1) = 2, f(1,1) = 3 (as 8-bit samples over Decode [0 255]).
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream(
            "<< /FunctionType 0 /Domain [0 1 0 1] /Range [0 255] /Size [2 2] /BitsPerSample 8 >>",
            [0, 1, 2, 3]));

        Near([(0.75f * 0.5f * 0) + (0.25f * 0.5f * 1) + (0.75f * 0.5f * 2) + (0.25f * 0.5f * 3)], function.At(0.25f, 0.5f));
    }

    [Fact]
    public void The_example_2_table_of_21_by_31_four_bit_samples_takes_326_bytes()
    {
        int[] size = [21, 31];
        ulong[] raw = new ulong[21 * 31];
        for (int row = 0; row < 31; row++)
        {
            for (int column = 0; column < 21; column++)
            {
                raw[(row * 21) + column] = (ulong)((column + row) % 16);
            }
        }

        byte[] data = Pack(raw, 4);
        Assert.Equal(326, data.Length);
        const string dictionary = "<< /FunctionType 0 /Domain [-1.0 1.0 -1.0 1.0] /Size [21 31] /Encode [0 20 0 30] /BitsPerSample 4 /Range [-1.0 1.0] /Decode [-1.0 1.0] >>";

        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream(dictionary, data));
        Near([-1f], function.At(-1f, -1f));
        Near([-1f + (2f / 15)], function.At(-0.9f, -1f));
        Near([-1f + (2f * 5 / 15)], function.At(-0.9f, -0.8f + (0.2f / 3)));
        Assert.Empty(document.Diagnostics);

        using PdfDocument shortDocument = PdfDocument.Create();
        PdfFunction truncated = shortDocument.Function(Stream(dictionary, data[..325]));
        Assert.Equal(["FunctionSampleDataTruncated"], shortDocument.Codes());
        Near([-1f], truncated.At(1f, 1f));
    }

    [Fact]
    public void A_dimension_of_size_1_maps_every_input_to_its_one_sample()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream(
            "<< /FunctionType 0 /Domain [0 1 0 1] /Range [0 255] /Size [1 3] /BitsPerSample 8 >>",
            [10, 20, 30]));

        Near([20f], function.At(0f, 0.5f));
        Near([25f], function.At(1f, 0.75f));
        Near([30f], function.At(0.3f, 1f));
    }

    [Fact]
    public void A_non_integer_encode_reaches_between_samples()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream(
            "<< /FunctionType 0 /Domain [0 1] /Range [0 255] /Size [3] /Encode [0 1.5] /BitsPerSample 8 >>",
            [0, 100, 200]));

        Near([150f], function.At(1f));
        Near([75f], function.At(0.5f));
    }

    [Fact]
    public void Inputs_at_the_domain_ends_read_the_first_and_last_samples_and_beyond_them_are_clipped()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream(
            "<< /FunctionType 0 /Domain [-2 2] /Range [0 255] /Size [4] /BitsPerSample 8 >>",
            [7, 9, 11, 250]));

        Near([7f], function.At(-2f));
        Near([250f], function.At(2f));
        Near([7f], function.At(-100f));
        Near([250f], function.At(float.PositiveInfinity));
        Near([7f], function.At(float.NaN));
    }

    [Fact]
    public void Decode_maps_samples_and_the_range_clips_the_result()
    {
        using PdfDocument document = PdfDocument.Create();
        var function = (PdfSampledFunction)document.Function(Stream(
            "<< /FunctionType 0 /Domain [0 1] /Range [0 1] /Decode [-1 3] /Size [2] /BitsPerSample 8 >>",
            [0, 255]));

        Near([0f], function.At(0f));
        Near([0.5f], function.At(0.375f));
        Near([1f], function.At(1f));
        Assert.Equal([-1.0, 3.0], function.Decode);
        Assert.Equal([0.0, 1.0], function.Encode);
        Assert.Equal(8, function.BitsPerSample);
        Assert.Equal(new PdfVersion(1, 2), function.MinimumVersion);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    public void Order_3_interpolates_one_input_with_a_cubic_spline_through_the_samples(int count)
    {
        // 16-bit samples over Range [0 1]: the table is exact to 1.5e-5 of a level, so the 1e-5 comparison is about the spline.
        ulong[] raw = [.. Enumerable.Range(0, count).Select(static k => (ulong)((k * 40503) % 65536))];
        double[] samples = [.. raw.Select(static r => r / 65535.0)];
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream(
            string.Create(CultureInfo.InvariantCulture, $"<< /FunctionType 0 /Domain [0 1] /Range [0 1] /Size [{count}] /Order 3 /BitsPerSample 16 >>"),
            Pack(raw, 16)));

        // Every grid point, both domain edges, every cell's quarter points, and a point just inside each end.
        List<double> points = [0, 1, 0.0001, 0.9999];
        for (int k = 0; k <= 4 * (count - 1); k++)
        {
            points.Add(k / (4.0 * (count - 1)));
        }

        double fromLinear = 0;
        foreach (double x in points)
        {
            float input = (float)x;
            Near([(float)Spline(samples, input * (count - 1))], function.At(input));
            double position = input * (count - 1);
            int cell = Math.Min((int)position, count - 2);
            double linear = samples[cell] + ((position - cell) * (samples[cell + 1] - samples[cell]));
            fromLinear = Math.Max(fromLinear, Math.Abs(linear - function.At(input)[0]));
        }

        // Order 3 changes the answer between samples (linear interpolation would give another) and nothing is noted.
        Assert.True(fromLinear > 1e-3, $"The spline stays within {fromLinear} of linear interpolation.");
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Order_3_follows_a_quadratic_exactly_between_interior_samples()
    {
        // 8-bit samples of 40 + 10x + 4x^2 for x = 0..4 over Range [0 255]: the spline reproduces a quadratic in the interior cells.
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream(
            "<< /FunctionType 0 /Domain [0 4] /Range [0 255] /Size [5] /Order 3 /BitsPerSample 8 >>",
            [.. Enumerable.Range(0, 5).Select(static x => (byte)(40 + (10 * x) + (4 * x * x)))]));

        foreach (float x in (float[])[1.25f, 1.5f, 2.5f, 2.75f])
        {
            Near([40 + (10 * x) + (4 * x * x)], function.At(x), 2e-4);
        }
    }

    [Fact]
    public void Order_3_with_several_inputs_applies_the_spline_along_each_input_in_turn()
    {
        // Size [5 6], one output: the tensor product of the one-input spline (separable, so the order of the axes does not matter).
        ulong[] raw = [.. Enumerable.Range(0, 30).Select(static k => (ulong)((k * 28657) % 65536))];
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream(
            "<< /FunctionType 0 /Domain [0 1 0 1] /Range [0 1] /Size [5 6] /Order 3 /BitsPerSample 16 >>",
            Pack(raw, 16)));

        foreach ((float x, float y) in ((float, float)[])[(0f, 0f), (1f, 1f), (0f, 1f), (0.3f, 0.7f), (0.9f, 0.1f), (0.125f, 0.5f), (0.5f, 0.3f), (0.999f, 0.001f)])
        {
            double[] rows = new double[6];
            for (int row = 0; row < 6; row++)
            {
                rows[row] = Spline([.. Enumerable.Range(0, 5).Select(column => raw[(row * 5) + column] / 65535.0)], x * 4);
            }

            Near([(float)Spline(rows, y * 5)], function.At(x, y));
        }

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Order_3_applies_per_input_and_a_dimension_of_fewer_than_4_samples_stays_linear()
    {
        // Size [6 3]: cubic along the first input, linear along the second (which has too few samples for a spline).
        ulong[] raw = [.. Enumerable.Range(0, 18).Select(static k => (ulong)((k * 40503) % 65536))];
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream(
            "<< /FunctionType 0 /Domain [0 1 0 1] /Range [0 1] /Size [6 3] /Order 3 /BitsPerSample 16 >>",
            Pack(raw, 16)));

        foreach ((float x, float y) in ((float, float)[])[(0.3f, 0.25f), (0.7f, 0.75f), (0.5f, 0.5f), (1f, 0.9f)])
        {
            double[] rows = new double[3];
            for (int row = 0; row < 3; row++)
            {
                rows[row] = Spline([.. Enumerable.Range(0, 6).Select(column => raw[(row * 6) + column] / 65535.0)], x * 5);
            }

            double e = y * 2;
            int cell = Math.Min((int)e, 1);
            Near([(float)(rows[cell] + ((e - cell) * (rows[cell + 1] - rows[cell])))], function.At(x, y));
        }

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Order_3_honors_encode_and_the_overshoot_of_the_spline_is_clipped_to_the_range()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream(
            "<< /FunctionType 0 /Domain [0 1] /Range [0 200] /Decode [0 255] /Size [5] /Encode [4 0] /Order 3 /BitsPerSample 8 >>",
            [0, 0, 255, 0, 0]));

        double[] samples = [0, 0, 255, 0, 0];
        Near([(float)Math.Clamp(Spline(samples, 4 * (1 - 0.3)), 0, 200)], function.At(0.3f), 1e-4);
        Near([200f], function.At(0.5f));
        Near([0f], function.At(1f));
    }

    /// <summary>
    /// An independent computation of the spline the function is documented to evaluate: the cubic Hermite spline through the samples
    /// whose tangent at an interior sample is half the difference of its neighbors and at an end is the difference to its one neighbor.
    /// </summary>
    private static double Spline(double[] samples, double position)
    {
        int last = samples.Length - 1;
        position = Math.Clamp(position, 0, last);
        int cell = Math.Min((int)position, last - 1);
        double t = position - cell;
        double Tangent(int i) => i == 0 ? samples[1] - samples[0] : i == last ? samples[last] - samples[last - 1] : (samples[i + 1] - samples[i - 1]) / 2;
        double h00 = ((2 * t * t * t) - (3 * t * t)) + 1;
        double h10 = (t * t * t) - (2 * t * t) + t;
        double h01 = (-2 * t * t * t) + (3 * t * t);
        double h11 = (t * t * t) - (t * t);
        return (h00 * samples[cell]) + (h10 * Tangent(cell)) + (h01 * samples[cell + 1]) + (h11 * Tangent(cell + 1));
    }

    [Fact]
    public void Order_3_with_fewer_than_4_samples_in_a_dimension_is_ignored_silently()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream(
            "<< /FunctionType 0 /Domain [0 1 0 1] /Range [0 255] /Size [4 3] /Order 3 /BitsPerSample 8 >>",
            new byte[12]));

        Assert.True(function.IsValid);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Simplex_interpolation_beyond_eight_inputs_reproduces_a_linear_function()
    {
        // Ten inputs, Size 2 each, samples f(g) = sum of g_i × (i + 1), so f is linear and every interpolation scheme is exact.
        const int inputs = 10;
        int[] size = [.. Enumerable.Repeat(2, inputs)];
        ulong[] raw = [.. GridPoints(size).OrderBy(p => Index(p, size)).Select(static p => (ulong)p.Select(static (g, i) => g * (i + 1)).Sum())];
        string dictionary = string.Create(CultureInfo.InvariantCulture, $"<< /FunctionType 0 /Domain [{string.Join(" ", Enumerable.Repeat("0 1", inputs))}] /Range [0 65535] /Size [{string.Join(" ", size)}] /BitsPerSample 16 >>");
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream(dictionary, Pack(raw, 16)));

        float[] x = [0.9f, 0.1f, 0.5f, 0.3f, 0.7f, 0.2f, 0.8f, 0.4f, 0.6f, 0.05f];
        Near([x.Select(static (v, i) => v * (i + 1)).Sum()], function.At(x), 1e-4);
    }

    [Fact]
    public void A_missing_range_without_a_hint_makes_the_function_invalid()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream("<< /FunctionType 0 /Domain [0 1] /Size [2] /BitsPerSample 8 >>", [0, 255]));

        Assert.False(function.IsValid);
        Assert.Equal(["FunctionInvalid"], document.Codes());
    }

    [Fact]
    public void A_size_that_would_need_too_many_samples_is_rejected_without_allocating_them()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = null!;
        long allocated = Allocations.Measure(
            () => function = document.Function(Stream("<< /FunctionType 0 /Domain [0 1 0 1 0 1] /Range [0 1] /Size [100000 100000 100000] /BitsPerSample 8 >>", [1, 2, 3])),
            warmUpCalls: 0);

        Assert.False(function.IsValid);
        Assert.Equal(["FunctionInvalid"], document.Codes());
        Assert.True(allocated < 1_000_000, $"Allocated {allocated} bytes.");
    }

    [Fact]
    public void A_bit_width_outside_the_list_is_read_as_given_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Stream(
            "<< /FunctionType 0 /Domain [0 1] /Range [0 7] /Decode [0 7] /Size [3] /BitsPerSample 3 >>",
            Pack([5, 1, 7], 3)));

        Near([5f], function.At(0f));
        Near([1f], function.At(0.5f));
        Near([7f], function.At(1f));
        Assert.Equal(["FunctionEntryInvalid"], document.Codes());
    }

    [Fact]
    public void A_type_0_dictionary_without_a_stream_is_invalid()
    {
        using PdfDocument document = PdfDocument.Create();
        var function = (PdfSampledFunction)document.Function(Cos("<< /FunctionType 0 /Domain [0 1] /Range [0 1] /Size [2] /BitsPerSample 8 >>"));

        Assert.False(function.IsValid);
        Assert.Null(function.Stream);
        Assert.Equal(["FunctionInvalid"], document.Codes());
    }

    private static Broadside.Objects.CosStream Sampled(int[] size, int outputs, int bits, ulong[] raw)
    {
        string domain = string.Join(" ", size.Select(static _ => "0 1"));
        string range = string.Join(" ", Enumerable.Repeat("0 1", outputs));
        return Stream(string.Create(CultureInfo.InvariantCulture, $"<< /FunctionType 0 /Domain [{domain}] /Range [{range}] /Size [{string.Join(" ", size)}] /BitsPerSample {bits} >>"), Pack(raw, bits));
    }

    /// <summary>A deterministic table of raw sample values spread over the whole bit width.</summary>
    private static ulong[] Table(int[] size, int outputs, int bits)
    {
        int count = size.Aggregate(outputs, static (product, s) => product * s);
        ulong maximum = (1UL << bits) - 1;
        return [.. Enumerable.Range(0, count).Select(k => ((ulong)k * 2654435761UL) % (maximum + 1))];
    }

    /// <summary>Packs values of <paramref name="bits"/> bits into bytes, most significant bit first, with no padding.</summary>
    private static byte[] Pack(ulong[] values, int bits)
    {
        byte[] data = new byte[((values.Length * bits) + 7) / 8];
        long bit = 0;
        foreach (ulong value in values)
        {
            for (int b = bits - 1; b >= 0; b--, bit++)
            {
                if (((value >> b) & 1) != 0)
                {
                    data[bit / 8] |= (byte)(0x80 >> (int)(bit % 8));
                }
            }
        }

        return data;
    }

    private static int Index(int[] point, int[] size)
    {
        int index = 0;
        for (int i = point.Length - 1; i >= 0; i--)
        {
            index = (index * size[i]) + point[i];
        }

        return index;
    }

    private static IEnumerable<int[]> GridPoints(int[] size)
    {
        int count = size.Aggregate(1, static (product, s) => product * s);
        for (int k = 0; k < count; k++)
        {
            int[] point = new int[size.Length];
            int rest = k;
            for (int i = 0; i < size.Length; i++)
            {
                point[i] = rest % size[i];
                rest /= size[i];
            }

            yield return point;
        }
    }
}
