using System.Text;
using Broadside.Graphics.Functions;
using Broadside.Parsing;

namespace Broadside.Tests.Graphics;

/// <summary>
/// The register form of Type 4 programs (<see cref="PostScriptLowering"/>) gives the interpreter's results, bit for bit, and its
/// statuses, for every input (ISO 32000-2 §7.10.5, Annex B).
/// </summary>
public sealed class PostScriptLoweringTests
{
    /// <summary>Each row: a program, its inputs and outputs, and whether it is expected to run as registers.</summary>
    public static TheoryData<string, int, int, bool> Programs => new()
    {
        // Tint transforms of the Ghent Output Suite and the benchmarks.
        { "{1 index 1.000000 cvr exch sub 3 1 roll 1.000000 3 1 roll 1.000000 3 1 roll 0 index 1.000000 cvr exch sub 3 1 roll 6 -1 roll 1.000000 cvr exch sub 6 1 roll 5 -1 roll 1.000000 cvr exch sub 5 1 roll 4 -1 roll 1.000000 cvr exch sub 4 1 roll 3 -1 roll 1.000000 cvr exch sub 3 1 roll pop pop }", 2, 4, true },
        { "{dup 0.84 mul exch 0.00 exch dup 0.44 mul exch 0.21 mul}", 1, 4, true },
        { "{360 mul sin 2 div exch 360 mul sin 2 div add}", 2, 1, true },
        { "{ 5 index 1 index 0.6 mul add 5 index 3 index 0.5 mul add 5 index 4 index 0.9 mul add 3 index 0.8 mul add 5 index 10 4 roll pop pop pop pop pop pop dup 0.99 gt { pop 1 } if }", 6, 4, true },
        { "{ 0 0 0 4 3 roll }", 1, 4, true },

        // Every operator on inputs.
        { "{ add } ", 2, 1, true },
        { "{ sub }", 2, 1, true },
        { "{ mul }", 2, 1, true },
        { "{ div }", 2, 1, true },
        { "{ atan }", 2, 1, true },
        { "{ exp }", 2, 1, true },
        { "{ idiv }", 2, 1, true },
        { "{ mod }", 2, 1, true },
        { "{ cvi exch cvi idiv }", 2, 1, true },
        { "{ cvi exch cvi mod }", 2, 1, true },
        { "{ abs exch neg }", 2, 2, true },
        { "{ ceiling exch floor }", 2, 2, true },
        { "{ round exch truncate }", 2, 2, true },
        { "{ sqrt exch ln }", 2, 2, true },
        { "{ log exch sin }", 2, 2, true },
        { "{ cos exch cvi }", 2, 2, true },
        { "{ cvr exch cvi cvr }", 2, 2, true },
        { "{ cvi exch cvi and }", 2, 1, true },
        { "{ cvi exch cvi or }", 2, 1, true },
        { "{ cvi exch cvi xor }", 2, 1, true },
        { "{ cvi not exch cvi bitshift }", 2, 1, true },
        { "{ and }", 2, 1, true },
        { "{ bitshift }", 2, 1, true },
        { "{ not exch not }", 2, 2, true },
        { "{ 2 copy eq 3 1 roll ne }", 2, 2, true },
        { "{ 2 copy ge 3 1 roll lt }", 2, 2, true },
        { "{ 2 copy gt 3 1 roll le }", 2, 2, true },
        { "{ gt { 1 } { 0 } ifelse }", 2, 1, true },
        { "{ 2 copy gt { exch } if pop }", 2, 1, true },
        { "{ 2 copy lt { pop } { exch pop } ifelse }", 2, 1, true },
        { "{ dup 0.5 gt { dup 0.75 gt { 1 } { 2 } ifelse add } { 10 mul } ifelse }", 1, 1, true },
        { "{ dup 0 lt { neg } if dup 1 gt { pop 1 } if }", 1, 1, true },
        { "{ 1 index 0 gt { exch } if 1 index 0 gt { 1 add } { 2 sub } ifelse }", 2, 2, true },
        { "{ dup dup mul exch dup 3 1 roll mul add }", 1, 1, true },
        { "{ 1 2 3 3 -1 roll 3 -2 roll 3 7 roll 2 index 1 index }", 1, 6, true },
        { "{ 1 2 4 copy 0 copy 3 1 roll }", 2, 6, true },
        { "{ 2 copy 2 copy mul 3 1 roll add exch }", 2, 4, true },
        { "{ true false and exch 0 gt or { 1 } { 0 } ifelse }", 1, 1, true },
        { "{ 0 gt }", 1, 1, true },
        { "{ 0 gt not }", 1, 1, true },
        { "{ pop }", 1, 1, true },
        { "{ dup }", 1, 1, true },
        { "{ 0 gt 2 add }", 1, 1, true },
        { "{ true abs exch cvr }", 1, 2, true },
        { "{ true cvi exch true sqrt }", 1, 3, true },
        { "{ dup 0 gt { cvi } if }", 1, 1, true },
        { "{ cvi dup 2147483647 add exch 1 add }", 1, 2, true },
        { "{ cvi 2147483647 mul cvi }", 1, 1, true },
        { "{ 1 exch { 2 } if }", 1, 2, false },
        { "{ -2147483648 abs -2147483648 neg -2147483648 -1 idiv }", 1, 4, true },
        { "{ 1 0 div 0 0 atan -1 sqrt 0 ln -1 log 1e308 10 mul }", 1, 7, true },
        { "{ 1 true eq exch true true eq }", 1, 3, true },
        { "{ 3.7 4 roll }", 4, 4, false },
        { "{ dup 0 gt { 1 } { 1.5 } ifelse 2 idiv }", 1, 2, false },
        { "{ cvi dup add 3 mod }", 1, 1, false },
        { "{ dup 0 gt { 1 } if }", 1, 1, false },
        { "{ dup 0 gt { true } { 1 } ifelse }", 1, 2, false },
        { "{ cvi index }", 2, 1, false },
        { "{ pop pop }", 1, 1, false },
        { "{ add add }", 2, 1, false },
        { "{ " + string.Concat(Enumerable.Repeat("dup ", 99)) + "}", 1, 100, true },
        { "{ " + string.Concat(Enumerable.Repeat("dup ", 100)) + "}", 1, 100, false },
    };

    /// <summary>Inputs: zeros, signs, halves, integers, the int32 bounds, large values and fractions.</summary>
    private static readonly double[] Values =
    [
        0, -0.0, 1, -1, 0.5, -0.5, 0.25, 0.75, 0.99, 1.5, 2, 3, -3, 7.25, -6.5, 6.5, 90, 180, 360, 1e-9, 1e6, -1e6,
        2147483647, -2147483648, 2147483648, -2147483649, 3e9, -3e9, 1e300, 0.1, 0.3, 0.7, 12345.678, -0.0001,
    ];

    [Theory]
    [MemberData(nameof(Programs))]
    public void The_register_program_gives_the_interpreters_results_and_status_for_every_input(string program, int inputs, int outputs, bool lowered)
    {
        using var document = PdfDocument.Create();
        var compilation = new FunctionCompilation(document, new DiagnosticSink(strict: false));
        PostScriptInstruction[] code = PostScriptCompiler.Parse(Encoding.ASCII.GetBytes(program), compilation)
            ?? throw new InvalidOperationException("The program does not compile.");
        double[] domain = [.. Enumerable.Repeat(new[] { -1e308, 1e308 }, inputs).SelectMany(static pair => pair)];
        var fast = new PostScriptEvaluator(domain, null, outputs, code);
        var slow = new PostScriptEvaluator(domain, null, outputs, code, lower: false);
        Assert.Equal(lowered, fast.IsLowered);
        Assert.False(slow.IsLowered);

        double[] input = new double[inputs];
        double[] expected = new double[outputs];
        double[] actual = new double[outputs];
        int points = (int)Math.Min(Math.Pow(Values.Length, inputs), 4096);
        for (int k = 0; k < points; k++)
        {
            int index = k;
            for (int i = 0; i < inputs; i++)
            {
                // Mixed radix over the values for one or two inputs, a deterministic walk for more.
                input[i] = Values[(index + (i * 7)) % Values.Length];
                index /= Values.Length;
            }

            Array.Fill(expected, double.NaN);
            Array.Fill(actual, double.NaN);
            FunctionStatus expectedStatus = slow.Evaluate(input, expected);
            FunctionStatus actualStatus = fast.Evaluate(input, actual);
            Assert.Equal(expectedStatus, actualStatus);
            for (int j = 0; j < outputs; j++)
            {
                Assert.True(
                    BitConverter.DoubleToInt64Bits(expected[j]) == BitConverter.DoubleToInt64Bits(actual[j]),
                    $"{program} at ({string.Join(", ", input)}): output {j} is {actual[j]}, the interpreter gives {expected[j]}.");
            }
        }
    }

    [Fact]
    public void Stack_operators_with_literal_operands_cost_no_instructions()
    {
        using var document = PdfDocument.Create();
        var compilation = new FunctionCompilation(document, new DiagnosticSink(strict: false));
        PostScriptInstruction[] code = PostScriptCompiler.Parse("{ 1 index exch 3 1 roll dup pop 2 copy pop pop 3 -1 roll sub sub }"u8, compilation)!;

        PostScriptRegisterProgram program = PostScriptLowering.Lower(code, 2, 1)!;

        Assert.Equal(2, program.Length);
    }

    [Fact]
    public void A_program_of_more_distinct_literals_than_registers_runs_in_the_interpreter()
    {
        // Constants take registers too, and the register program stack-allocates them all (issue #274): a file must not size a
        // stack allocation with its literal count.
        using var document = PdfDocument.Create();
        var compilation = new FunctionCompilation(document, new DiagnosticSink(strict: false));
        var text = new StringBuilder("{ ");
        for (int i = 0; i < 2 * PostScriptLowering.MaxRegisters; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{i + 1} pop ");
        }

        PostScriptInstruction[] code = PostScriptCompiler.Parse(Encoding.ASCII.GetBytes(text.Append('}').ToString()), compilation)!;

        Assert.Null(PostScriptLowering.Lower(code, 1, 1));
    }
}
