using Broadside.Diagnostics;
using Broadside.Graphics;
using static Broadside.Tests.Graphics.FunctionTesting;

namespace Broadside.Tests.Graphics;

/// <summary>Type 2 (exponential interpolation) functions, ISO 32000-2 §7.10.3, Table 40.</summary>
public sealed class ExponentialFunctionTests
{
    [Fact]
    public void Interpolates_between_C0_and_C1_by_x_to_the_N()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos("<< /FunctionType 2 /Domain [0 1] /C0 [0 0.5] /C1 [1 0] /N 2 >>"));

        Assert.Equal(PdfFunctionType.Exponential, function.FunctionType);
        Assert.Equal(1, function.InputCount);
        Assert.Equal(2, function.OutputCount);
        Near([0.25f, 0.375f], function.At(0.5f));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void C0_and_C1_default_to_0_and_1()
    {
        using PdfDocument document = PdfDocument.Create();
        var function = (PdfExponentialFunction)document.Function(Cos("<< /FunctionType 2 /Domain [0 1] /N 3 >>"));

        Assert.Equal([0.0], function.C0);
        Assert.Equal([1.0], function.C1);
        Assert.Equal(3.0, function.Exponent);
        Near([0.125f], function.At(0.5f));
        Assert.Equal(new PdfVersion(1, 3), function.MinimumVersion);
    }

    [Theory]
    [InlineData(0f, 0.2f)]
    [InlineData(0.25f, 0.4f)]
    [InlineData(1f, 1f)]
    public void N_of_1_is_linear_interpolation(float x, float expected)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos("<< /FunctionType 2 /Domain [0 1] /C0 [0.2] /C1 [1] /N 1 >>"));

        Near([expected], function.At(x));
    }

    [Fact]
    public void Inputs_are_clipped_to_the_domain_and_outputs_to_the_range()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos("<< /FunctionType 2 /Domain [0 2] /C0 [0 0] /C1 [1 -1] /N 1 /Range [0 1.5 -0.5 0] >>"));

        Near([1.5f, -0.5f], function.At(7f));
        Near([0f, 0f], function.At(-3f));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_non_integer_N_with_a_domain_reaching_below_zero_is_noted_and_negative_inputs_give_C0()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos("<< /FunctionType 2 /Domain [-1 1] /C0 [0.3] /C1 [1] /N 0.5 >>"));

        Assert.Equal(["FunctionEntryInvalid"], document.Codes());
        Near([0.3f + (0.5f * 0.7f)], function.At(0.25f));
        Near([0.3f], function.At(-0.25f));
        Assert.Equal(["FunctionEntryInvalid", "FunctionEvaluationRepaired"], document.Codes());
    }

    [Fact]
    public void A_negative_N_at_zero_gives_C0()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos("<< /FunctionType 2 /Domain [0 1] /C0 [0.1 0.2] /C1 [1 1] /N -1 >>"));

        Near([0.1f, 0.2f], function.At(0f));
        Near([0.1f + (2 * 0.9f), 0.2f + (2 * 0.8f)], function.At(0.5f));
        Assert.Contains("FunctionEvaluationRepaired", document.Codes());
    }

    [Fact]
    public void A_missing_N_makes_the_function_invalid_with_outputs_of_zero()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos("<< /FunctionType 2 /Domain [0 1] /C0 [0.5 0.5] /C1 [1 1] /Range [0.25 1 0 1] >>"));

        Assert.False(function.IsValid);
        Assert.Equal(2, function.OutputCount);
        Near([0.25f, 0f], function.At(0.5f));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, static d => d.Code == "FunctionInvalid");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void A_missing_domain_is_one_input_in_0_to_1()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos("<< /FunctionType 2 /N 1 >>"));

        Assert.True(function.IsValid);
        Assert.Empty(function.Domain);
        Near([1f], function.At(4f));
        Assert.Equal(["FunctionEntryInvalid"], document.Codes());
    }

    [Fact]
    public void C0_and_C1_of_different_lengths_use_the_shorter()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos("<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /N 1 >>"));

        Assert.Equal(1, function.OutputCount);
        Assert.Equal(["FunctionEntryInvalid"], document.Codes());
    }

    [Fact]
    public void Strict_mode_throws_for_a_function_that_needs_repair()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStrict());

        Assert.Throws<DiagnosticException>(() => document.GetFunction(Cos("<< /FunctionType 2 /N 1 >>")));
    }
}
