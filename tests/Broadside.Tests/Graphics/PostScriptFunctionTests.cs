using System.Text;
using Broadside.Diagnostics;
using Broadside.Graphics;
using static Broadside.Tests.Graphics.FunctionTesting;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Type 4 (PostScript calculator) functions, ISO 32000-2 §7.10.5 and Annex B, with the operator semantics and examples of the
/// PostScript Language Reference, Third Edition, §8.2.
/// </summary>
public class PostScriptFunctionTests
{
    /// <summary>Each row: a program body run on an empty stack, and the values it leaves. Expected values are PLRM's own examples where it gives one.</summary>
    public static TheoryData<string, float[]> OperatorExamples => new()
    {
        // B.2 Arithmetic operators.
        { "3 4 add", [7f] },
        { "9.9 1.1 add", [11f] },
        { "2147483647 1 add", [2147483648f] },
        { "1 2 sub", [-1f] },
        { "2 3 mul", [6f] },
        { "3 2 div", [1.5f] },
        { "4 2 div", [2f] },
        { "-5 2 idiv", [-2f] },
        { "5 3 mod", [2f] },
        { "-5 3 mod", [-2f] },
        { "-3 abs", [3f] },
        { "4.5 neg", [-4.5f] },
        { "3.2 ceiling", [4f] },
        { "-4.8 ceiling", [-4f] },
        { "3.2 floor", [3f] },
        { "-4.8 floor", [-5f] },
        { "6.5 round", [7f] },
        { "-6.5 round", [-6f] },
        { "-4.8 round", [-5f] },
        { "-4.8 truncate", [-4f] },
        { "9 sqrt", [3f] },
        { "9 0.5 exp", [3f] },
        { "-9 -1 exp", [-0.111111f] },
        { "10 ln", [2.302585f] },
        { "100 log", [2f] },
        { "90 sin", [1f] },
        { "30 sin", [0.5f] },
        { "-90 sin", [-1f] },
        { "0 cos", [1f] },
        { "90 cos", [0f] },
        { "450 cos", [0f] },
        { "0 1 atan", [0f] },
        { "1 0 atan", [90f] },
        { "-100 0 atan", [270f] },
        { "4 4 atan", [45f] },
        { "-47.8 cvi", [-47f] },
        { "520.9 cvi", [520f] },
        { "3 cvr", [3f] },

        // B.3 Relational, boolean and bitwise operators (booleans are turned into numbers with ifelse).
        { "4.0 4 eq { 1 } { 0 } ifelse", [1f] },
        { "4 5 eq { 1 } { 0 } ifelse", [0f] },
        { "4 5 ne { 1 } { 0 } ifelse", [1f] },
        { "true 1 eq { 1 } { 0 } ifelse", [0f] },
        { "true true eq { 1 } { 0 } ifelse", [1f] },
        { "4.2 4 ge { 1 } { 0 } ifelse", [1f] },
        { "4 4 gt { 1 } { 0 } ifelse", [0f] },
        { "4 4 le { 1 } { 0 } ifelse", [1f] },
        { "3 4 lt { 1 } { 0 } ifelse", [1f] },
        { "true false and { 1 } { 0 } ifelse", [0f] },
        { "true false or { 1 } { 0 } ifelse", [1f] },
        { "true true xor { 1 } { 0 } ifelse", [0f] },
        { "true not { 1 } { 0 } ifelse", [0f] },
        { "false { 1 } { 0 } ifelse", [0f] },
        { "99 1 and", [1f] },
        { "52 7 and", [4f] },
        { "17 5 or", [21f] },
        { "5 3 xor", [6f] },
        { "12 3 xor", [15f] },
        { "52 not", [-53f] },
        { "7 3 bitshift", [56f] },
        { "142 -3 bitshift", [17f] },
        { "-1 -1 bitshift", [2147483647f] },
        { "1 32 bitshift", [0f] },

        // B.4 Conditional operators.
        { "1 true { 2 add } if", [3f] },
        { "1 false { 2 add } if", [1f] },
        { "true { true { 1 } { 2 } ifelse } { 3 } ifelse", [1f] },
        { "true { false { 1 } { 2 } ifelse } { 3 } ifelse", [2f] },
        { "false { true { 1 } { 2 } ifelse } { false { 3 } { 4 } ifelse } ifelse", [4f] },
        { "5 dup 3 gt { dup 4 gt { 10 add } if } { 100 add } ifelse", [15f] },

        // B.5 Stack operators.
        { "1 2 exch", [2f, 1f] },
        { "1 2 pop", [1f] },
        { "5 dup", [5f, 5f] },
        { "1 2 3 2 copy", [1f, 2f, 3f, 2f, 3f] },
        { "1 2 3 0 copy", [1f, 2f, 3f] },
        { "1 2 3 4 3 index", [1f, 2f, 3f, 4f, 1f] },
        { "1 2 3 0 index", [1f, 2f, 3f, 3f] },
        { "1 2 3 3 -1 roll", [2f, 3f, 1f] },
        { "1 2 3 3 1 roll", [3f, 1f, 2f] },
        { "1 2 3 3 4 roll", [3f, 1f, 2f] },
        { "1 2 3 3 0 roll", [1f, 2f, 3f] },
        { "9 1 2 3 3 -1 roll", [9f, 2f, 3f, 1f] },

        // Comments and PDF number syntax.
        { "% a comment\n4. .5 add", [4.5f] },
    };

    [Theory]
    [MemberData(nameof(OperatorExamples))]
    public void Each_operator_matches_the_PLRM_examples(string body, float[] expected)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Calculator($"{{ pop {body} }}", 1, expected.Length));

        Near(expected, function.At(0f), 1e-5);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData(0.25f, 0f, 0.5f)]
    [InlineData(0.25f, 0.25f, 1f)]
    [InlineData(0f, 0f, 0f)]
    [InlineData(-0.25f, 0.125f, -0.14644661f)]
    [InlineData(4f, 0.25f, 0.5f)]
    public void The_DoubleDot_spot_function_of_the_specification(float x, float y, float expected)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Program(
            "<< /FunctionType 4 /Domain [-1.0 1.0 -1.0 1.0] /Range [-1.0 1.0] >>",
            "{360 mul sin\n2 div\nexch 360 mul sin\n2 div\nadd\n}"));

        Near([expected], function.At(x, y));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_LogoGreen_tint_transform_maps_a_tint_to_four_process_colorants()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Program(
            "<< /FunctionType 4 /Domain [0.0 1.0] /Range [0.0 1.0 0.0 1.0 0.0 1.0 0.0 1.0] >>",
            "{dup 0.84 mul\nexch 0.00 exch dup 0.44 mul exch 0.21 mul\n}"));

        Near([0.42f, 0f, 0.22f, 0.105f], function.At(0.5f));
        Near([0.84f, 0f, 0.44f, 0.21f], function.At(1f));
        Near([0.84f, 0f, 0.44f, 0.21f], function.At(3f));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_DeviceN_example_inserts_a_zero_component()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Program(
            "<< /FunctionType 4 /Domain [0.0 1.0 0.0 1.0 0.0 1.0] /Range [0.0 1.0 0.0 1.0 0.0 1.0 0.0 1.0] >>",
            "{ 0      exch }"));

        Near([0.1f, 0.2f, 0f, 0.3f], function.At(0.1f, 0.2f, 0.3f));
        Assert.Equal(PdfFunctionType.PostScriptCalculator, function.FunctionType);
        Assert.Equal(3, function.InputCount);
    }

    [Fact]
    public void A_hexachrome_tint_transform_maps_six_colorants_to_four()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Program(
            "<< /FunctionType 4 /Domain [0 1 0 1 0 1 0 1 0 1 0 1] /Range [0 1 0 1 0 1 0 1] >>",
            FunctionAllocationTests.HexachromeProgram));

        Near([0.25f, 0.45f, 0.95f, 0.4f], function.At(0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.25f));
        Near([1f, 1f, 1f, 1f], function.At(1f, 1f, 1f, 0.995f, 1f, 1f));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_stack_holds_100_entries()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction full = document.Function(Calculator($"{{ pop {Repeat("1 ", 100)}{Repeat("add ", 99)}}}", 1, 1));

        Near([100f], full.At(0f));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Overflowing_the_stack_fails_the_evaluation_with_outputs_of_zero_clipped_to_the_range()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Program(
            "<< /FunctionType 4 /Domain [0 1] /Range [0.5 1000] >>",
            $"{{ pop {Repeat("1 ", 101)}{Repeat("add ", 100)}}}"));

        Assert.Equal(["FunctionProgramStackInvalid"], document.Codes());
        Near([0.5f], function.At(0f));
        Assert.Equal(["FunctionProgramStackInvalid", "FunctionEvaluationRepaired"], document.Codes());
    }

    [Fact]
    public void Underflowing_the_stack_fails_the_evaluation()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Calculator("{ add }", 1, 1));

        Near([0f], function.At(7f));
        Assert.Equal(["FunctionProgramStackInvalid", "FunctionEvaluationRepaired"], document.Codes());
    }

    [Fact]
    public void Fewer_results_than_outputs_are_padded_with_zero_and_more_are_cut_to_the_topmost()
    {
        using PdfDocument fewerDocument = PdfDocument.Create();
        PdfFunction fewer = fewerDocument.Function(Calculator("{ 2 mul }", 1, 3));
        using PdfDocument moreDocument = PdfDocument.Create();
        PdfFunction more = moreDocument.Function(Calculator("{ dup 1 add dup 1 add }", 1, 2));

        Near([4f, 0f, 0f], fewer.At(2f));
        Near([3f, 4f], more.At(2f));
        Assert.Equal(["FunctionProgramStackInvalid", "FunctionEvaluationRepaired"], fewerDocument.Codes());
        Assert.Equal(["FunctionProgramStackInvalid", "FunctionEvaluationRepaired"], moreDocument.Codes());
    }

    [Fact]
    public void A_boolean_result_is_1_or_0()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Calculator("{ 0.5 gt }", 1, 1));

        Near([1f], function.At(0.75f));
        Near([0f], function.At(0.25f));
        Assert.Equal(["FunctionEvaluationRepaired"], document.Codes());
    }

    [Theory]
    [InlineData("{ 0 div }")]
    [InlineData("{ 0 idiv }")]
    [InlineData("{ 0 mod }")]
    [InlineData("{ neg sqrt }")]
    [InlineData("{ neg ln }")]
    [InlineData("{ 0 mul log }")]
    [InlineData("{ pop 0 0 atan }")]
    public void An_undefined_result_is_zero_and_noted_once(string code)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Calculator(code, 1, 1));

        Near([0f], function.At(4f));
        Near([0f], function.At(5f));
        Assert.Equal(["FunctionEvaluationRepaired"], document.Codes());
        Assert.Equal(DiagnosticSeverity.Warning, document.Diagnostics[0].Severity);
    }

    [Theory]
    [InlineData("{ true add }", 3f)]
    [InlineData("{ pop 2.7 3 idiv }", 0f)]
    [InlineData("{ 7.9 bitshift }", 256f)]
    [InlineData("{ pop 1 { 5 } { 6 } ifelse }", 5f)]
    [InlineData("{ pop true 3 and }", 1f)]
    public void An_operand_of_the_wrong_type_is_converted_and_noted(string code, float expected)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Calculator(code, 1, 1));

        Near([expected], function.At(2f));
        Assert.Equal(["FunctionEvaluationRepaired"], document.Codes());
    }

    [Fact]
    public void Inputs_are_clipped_to_the_domain_before_the_program_runs()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Program("<< /FunctionType 4 /Domain [0 1] /Range [-10 10] >>", "{ 3 mul }"));

        Near([3f], function.At(5f));
        Near([0f], function.At(-5f));
    }

    [Theory]
    [InlineData("dup mul", "FunctionProgramSyntaxInvalid")]
    [InlineData("{ dup mul", "FunctionProgramSyntaxInvalid")]
    [InlineData("{ dup mul } junk", "FunctionProgramSyntaxInvalid")]
    [InlineData("{ dup mul } }", "FunctionProgramSyntaxInvalid")]
    [InlineData("{ 1e0 mul }", "FunctionProgramSyntaxInvalid")]
    public void Syntax_a_reader_can_repair_is_read_with_a_diagnostic(string code, string diagnostic)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Calculator(code, 1, 1));

        Assert.True(function.IsValid);
        Assert.Equal(code.Contains("1e0", StringComparison.Ordinal) ? 3f : 9f, function.At(3f)[0]);
        Assert.Equal([diagnostic], document.Codes());
    }

    public static TheoryData<string> ProgramsOutsideTheSubset => new()
    {
        "{ 1 max }",
        "{ dup exec }",
        "{ (string) pop }",
        "{ [1] pop }",
        "{ /name pop }",
        "{ 16#FF add }",
        "{ { 1 } }",
        "{ true { 1 } 2 }",
        "{ true { 1 } { 2 } if }",
        "{ true { 1 ifelse }",
        "{ if }",
        "{ 1 - }",
    };

    [Theory]
    [MemberData(nameof(ProgramsOutsideTheSubset))]
    public void A_program_outside_the_subset_is_invalid(string code)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Calculator(code, 1, 2));

        Assert.False(function.IsValid);
        Assert.Equal(2, function.OutputCount);
        Near([0f, 0f], function.At(1f));
        Assert.Equal(["FunctionInvalid"], document.Codes());
    }

    [Fact]
    public void Braces_nest_256_deep_including_the_program_but_not_257()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction deep = document.Function(Calculator(Nested(255), 1, 1));
        Assert.True(deep.IsValid);
        Near([1f], deep.At(0.5f));

        PdfFunction deeper = document.Function(Calculator(Nested(256), 1, 1));
        Assert.False(deeper.IsValid);
        Assert.Equal(["FunctionInvalid"], document.Codes());
    }

    [Fact]
    public void A_missing_range_takes_the_output_count_from_nowhere_and_is_invalid()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Program("<< /FunctionType 4 /Domain [0 1] >>", "{ dup }"));

        Assert.False(function.IsValid);
        Assert.Equal(["FunctionInvalid"], document.Codes());
    }

    private static string Repeat(string text, int count) => new StringBuilder().Insert(0, text, count).ToString();

    /// <summary>A program of <paramref name="levels"/> nested <c>true { … } if</c> bodies that leaves 1 on the stack.</summary>
    private static string Nested(int levels) => $"{{ pop {Repeat("true { ", levels)}1 {Repeat("} if ", levels)}}}";
}
