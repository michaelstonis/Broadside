using System.Globalization;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Document;
using static Broadside.Tests.Graphics.FunctionTesting;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Type 3 (stitching) functions, ISO 32000-2 §7.10.4, Table 41. Function i of the tests below returns 10 × i + x′, so an output
/// shows both which subdomain was chosen and the encoded input x′.
/// </summary>
public sealed class StitchingFunctionTests
{
    public static TheoryData<string, string, string, float, float> IntervalCases => new()
    {
        // k = 1: one interval [Domain0, Domain1].
        { "[0 1]", "[]", "[0 1]", 0f, 0f },
        { "[0 1]", "[]", "[0 1]", 0.4f, 0.4f },
        { "[0 1]", "[]", "[0 1]", 1f, 1f },

        // k = 2, Domain0 < Bounds0 <= Domain1: [Domain0, Bounds0); [Bounds0, Domain1].
        { "[0 1]", "[0.5]", "[0 1 0 1]", 0f, 0f },
        { "[0 1]", "[0.5]", "[0 1 0 1]", 0.25f, 0.5f },
        { "[0 1]", "[0.5]", "[0 1 0 1]", 0.5f, 10f },
        { "[0 1]", "[0.5]", "[0 1 0 1]", 1f, 11f },

        // k = 2, Domain0 = Bounds0 < Domain1: [Domain0, Bounds0]; (Bounds0, Domain1]. The point interval gives x' = Encode0.
        { "[0 1]", "[0]", "[0.3 1 0 1]", 0f, 0.3f },
        { "[0 1]", "[0]", "[0.3 1 0 1]", 0.25f, 10.25f },
        { "[0 1]", "[0]", "[0.3 1 0 1]", 1f, 11f },

        // k = 2, Bounds0 = Domain1: the last interval is the point Domain1 and x' = Encode2(k-1).
        { "[0 1]", "[1]", "[0 1 0.7 1]", 0.75f, 0.75f },
        { "[0 1]", "[1]", "[0 1 0.7 1]", 1f, 10.7f },

        // k = 3, Domain0 < Bounds0 < Bounds1 < Domain1: [D0, B0); [B0, B1); [B1, D1].
        { "[0 3]", "[1 2]", "[0 1 0 1 0 1]", 0f, 0f },
        { "[0 3]", "[1 2]", "[0 1 0 1 0 1]", 1f, 10f },
        { "[0 3]", "[1 2]", "[0 1 0 1 0 1]", 1.5f, 10.5f },
        { "[0 3]", "[1 2]", "[0 1 0 1 0 1]", 2f, 20f },
        { "[0 3]", "[1 2]", "[0 1 0 1 0 1]", 3f, 21f },

        // k = 3, Domain0 = Bounds0 < Bounds1 < Domain1: [D0, B0]; (B0, B1); [B1, D1].
        { "[0 2]", "[0 1]", "[0.4 1 0 1 0 1]", 0f, 0.4f },
        { "[0 2]", "[0 1]", "[0.4 1 0 1 0 1]", 0.5f, 10.5f },
        { "[0 2]", "[0 1]", "[0.4 1 0 1 0 1]", 1f, 20f },
        { "[0 2]", "[0 1]", "[0.4 1 0 1 0 1]", 2f, 21f },

        // Outside the domain: clipped first.
        { "[0 3]", "[1 2]", "[0 1 0 1 0 1]", -5f, 0f },
        { "[0 3]", "[1 2]", "[0 1 0 1 0 1]", 9f, 21f },
    };

    [Theory]
    [MemberData(nameof(IntervalCases))]
    public void Each_interval_of_example_1_selects_its_function_and_encodes_the_input(string domain, string bounds, string encode, float x, float expected)
    {
        using PdfDocument document = PdfDocument.Create();
        int k = bounds.Trim('[', ']').Split(' ', StringSplitOptions.RemoveEmptyEntries).Length + 1;
        PdfFunction function = document.Function(Cos($"<< /FunctionType 3 /Domain {domain} /Functions [{Tagged(k)}] /Bounds {bounds} /Encode {encode} >>"));

        Near([expected], function.At(x));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_encode_pair_in_reverse_order_turns_the_function_around()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos("<< /FunctionType 3 /Domain [0 1] /Functions [<< /FunctionType 2 /Domain [0 1] /N 2 >>] /Bounds [] /Encode [1 0] >>"));

        Near([0.5625f], function.At(0.25f));
        Near([1f], function.At(0f));
        Near([0f], function.At(1f));
    }

    [Fact]
    public void The_range_of_the_stitching_function_clips_after_the_sub_functions_clip_their_own()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos(
            "<< /FunctionType 3 /Domain [0 1] /Range [0 0.6] /Bounds [] /Encode [0 1] " +
            "/Functions [<< /FunctionType 2 /Domain [0 0.5] /C0 [0] /C1 [2] /N 1 /Range [0 0.8] >>] >>"));

        Near([0.6f], function.At(1f));
        Near([0.4f], function.At(0.2f));
    }

    [Fact]
    public void The_view_exposes_bounds_encode_and_the_stitched_functions()
    {
        using PdfDocument document = PdfDocument.Create();
        var function = (PdfStitchingFunction)document.Function(Cos($"<< /FunctionType 3 /Domain [0 1] /Functions [{Tagged(2)}] /Bounds [0.5] /Encode [0 1 0 1] >>"));

        Assert.Equal([0.5], function.Bounds);
        Assert.Equal([0.0, 1.0, 0.0, 1.0], function.Encode);
        Assert.Collection(
            function.Functions,
            static f => Assert.Equal(PdfFunctionType.Exponential, f!.FunctionType),
            static f => Assert.Equal([10.0], ((PdfExponentialFunction)f!).C0));
    }

    [Fact]
    public void Sub_functions_with_different_output_counts_make_the_function_invalid()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos(
            "<< /FunctionType 3 /Domain [0 1] /Bounds [0.5] /Encode [0 1 0 1] /Functions [" +
            "<< /FunctionType 2 /Domain [0 1] /N 1 >> << /FunctionType 2 /Domain [0 1] /C0 [0 0] /C1 [1 1] /N 1 >>] >>"));

        Assert.False(function.IsValid);
        Assert.Contains("FunctionInvalid", document.Codes());
    }

    [Fact]
    public void Too_few_bounds_make_the_function_invalid()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos($"<< /FunctionType 3 /Domain [0 1] /Functions [{Tagged(3)}] /Bounds [0.5] /Encode [0 1 0 1 0 1] >>"));

        Assert.False(function.IsValid);
        Assert.Equal(["FunctionInvalid"], document.Codes());
    }

    [Fact]
    public void Bounds_out_of_order_are_used_as_given_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos($"<< /FunctionType 3 /Domain [0 1] /Functions [{Tagged(3)}] /Bounds [0.6 0.3] /Encode [0 1 0 1 0 1] >>"));

        Assert.True(function.IsValid);
        Assert.Equal(["FunctionEntryInvalid"], document.Codes());
        Near([0.5f], function.At(0.3f));
        Near([20f + (0.3f / 0.7f)], function.At(0.6f));
    }

    [Fact]
    public void Stitching_functions_nest_eight_deep_but_not_nine()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfFunction eight = document.Function(Cos(Nested(7)));
        Assert.True(eight.IsValid);
        Near([0.25f], eight.At(0.25f));
        Assert.Empty(document.Diagnostics);

        PdfFunction nine = document.Function(Cos(Nested(8)));
        Assert.False(nine.IsValid);
        Assert.Contains("FunctionTooDeep", document.Codes());
    }

    [Fact]
    public void A_stitching_function_that_contains_itself_is_invalid()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] /Resources << >> >>",
            "<< /FunctionType 3 /Domain [0 1] /Functions [5 0 R] /Bounds [] /Encode [0 1] >>",
            "<< /FunctionType 3 /Domain [0 1] /Functions [4 0 R] /Bounds [] /Encode [0 1] >>");
        using PdfDocument document = PdfDocument.Open(file);

        PdfFunction function = document.Function(new CosReference(4, 0));

        Assert.False(function.IsValid);
        Assert.Equal(new CosReference(4, 0), Assert.Single(document.Diagnostics, static d => d.Code == "FunctionCycle").ObjectReference);
        Assert.Empty(function.At(0.5f));
    }

    /// <summary>k Type 2 functions, function i returning 10 × i + x.</summary>
    private static string Tagged(int k) =>
        string.Join(" ", Enumerable.Range(0, k).Select(static i => string.Create(CultureInfo.InvariantCulture, $"<< /FunctionType 2 /Domain [0 1] /C0 [{10 * i}] /C1 [{(10 * i) + 1}] /N 1 >>")));

    /// <summary><paramref name="levels"/> stitching functions around one Type 2 identity function.</summary>
    private static string Nested(int levels)
    {
        string function = "<< /FunctionType 2 /Domain [0 1] /N 1 >>";
        for (int i = 0; i < levels; i++)
        {
            function = $"<< /FunctionType 3 /Domain [0 1] /Functions [{function}] /Bounds [] /Encode [0 1] >>";
        }

        return function;
    }
}
