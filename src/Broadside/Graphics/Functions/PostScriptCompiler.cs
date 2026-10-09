using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics.Functions;

/// <summary>Compiles Type 4 (PostScript calculator) functions into a flat instruction list.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.10.5, Table 42, Annex B. One pass, no recursion: <c>b {e} if</c> becomes <c>JumpIfFalse L; e; L:</c> and
/// <c>b {e1} {e2} ifelse</c> becomes <c>JumpIfFalse L1; e1; Jump L2; L1: e2; L2:</c>, with the open bodies on an explicit stack
/// (at most 256 deep, braces of the program included; writers stay within 255).
/// </para>
/// <para>
/// Repaired with a diagnostic: a program without its opening or closing brace, text after the closing brace, a number written with an
/// exponent. Invalid: an operator outside Table 42, a body not followed by <c>if</c> or <c>ifelse</c>, any other syntax
/// (strings, arrays, names, radix numbers). A static pass over the stack depth notes a program that provably underflows, overflows or
/// leaves the wrong number of results; it still runs, with the run-time checks.
/// </para>
/// </remarks>
internal static class PostScriptCompiler
{
    /// <summary>The most nested braces, the program's own included.</summary>
    public const int MaxNesting = 256;

    /// <summary>Compiles a Type 4 function.</summary>
    /// <param name="stream">The function stream.</param>
    /// <param name="compilation">The compilation.</param>
    /// <param name="expectedInputs">The consumer's number of inputs, for a missing Domain; 0 when unknown.</param>
    /// <param name="expectedOutputs">The consumer's number of outputs, for a missing Range; 0 when unknown.</param>
    /// <returns>The evaluator.</returns>
    public static FunctionEvaluator Compile(CosStream stream, FunctionCompilation compilation, int expectedInputs, int expectedOutputs)
    {
        CosDictionary dictionary = stream.Dictionary;
        double[]? domain = compilation.ReadDomain(dictionary, expectedInputs);
        if (domain is null)
        {
            return FunctionCompiler.Invalid(expectedInputs, expectedOutputs);
        }

        int m = domain.Length / 2;
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

        PostScriptInstruction[]? code = Parse(compilation.Decode(stream).Span, compilation);
        if (code is null)
        {
            return FunctionCompiler.Invalid(m, n, range);
        }

        CheckStack(code, m, n, compilation);
        return new PostScriptEvaluator(domain, range, n, code);
    }

    /// <summary>Parses the program into instructions; <see langword="null"/> (with a diagnostic) when it is not a valid program.</summary>
    /// <param name="text">The program text.</param>
    /// <param name="compilation">The compilation, for diagnostics.</param>
    /// <returns>The instructions.</returns>
    internal static PostScriptInstruction[]? Parse(ReadOnlySpan<byte> text, FunctionCompilation compilation)
    {
        var lexer = new PostScriptLexer(text);
        var code = new List<PostScriptInstruction>();
        var bodies = new List<Body>();
        bool exponents = false;
        PostScriptToken token = lexer.Next();
        bool braced = token.Kind == PostScriptTokenKind.OpenBrace;
        if (braced)
        {
            token = lexer.Next();
        }
        else
        {
            compilation.Report(DiagnosticCodes.FunctionProgramSyntaxInvalid, DiagnosticSeverity.Warning, "The program does not start with {; it is read as if it did.");
        }

        while (true)
        {
            switch (token.Kind)
            {
                case PostScriptTokenKind.End:
                    if (bodies.Count > 0)
                    {
                        return Invalid(compilation, "The program ends inside the body of an if or ifelse.");
                    }

                    if (braced)
                    {
                        compilation.Report(DiagnosticCodes.FunctionProgramSyntaxInvalid, DiagnosticSeverity.Warning, "The program does not end with }; it is read as if it did.");
                    }

                    return Finish(code, exponents, compilation);

                case PostScriptTokenKind.OpenBrace:
                    if (bodies.Count + 2 > MaxNesting)
                    {
                        return Invalid(compilation, string.Create(CultureInfo.InvariantCulture, $"Braces nest more than {MaxNesting} deep."));
                    }

                    bodies.Add(new Body(code.Count, First: true));
                    code.Add(new PostScriptInstruction(PostScriptOpCode.JumpIfFalse));
                    break;

                case PostScriptTokenKind.CloseBrace:
                    if (bodies.Count == 0)
                    {
                        if (lexer.Next().Kind != PostScriptTokenKind.End || !braced)
                        {
                            compilation.Report(DiagnosticCodes.FunctionProgramSyntaxInvalid, DiagnosticSeverity.Warning, "There is text after the } that closes the program; it is ignored.");
                        }

                        return Finish(code, exponents, compilation);
                    }

                    Body body = bodies[^1];
                    bodies.RemoveAt(bodies.Count - 1);
                    PostScriptToken next = lexer.Next();
                    if (body.First && IsName(lexer, next, "if"u8))
                    {
                        code[body.Jump] = code[body.Jump] with { Target = code.Count };
                    }
                    else if (body.First && next.Kind == PostScriptTokenKind.OpenBrace)
                    {
                        code[body.Jump] = code[body.Jump] with { Target = code.Count + 1 };
                        bodies.Add(new Body(code.Count, First: false));
                        code.Add(new PostScriptInstruction(PostScriptOpCode.Jump));
                    }
                    else if (!body.First && IsName(lexer, next, "ifelse"u8))
                    {
                        code[body.Jump] = code[body.Jump] with { Target = code.Count };
                    }
                    else
                    {
                        return Invalid(compilation, body.First ? "A { } body is not followed by if or by a second body and ifelse." : "Two { } bodies are not followed by ifelse.");
                    }

                    break;

                case PostScriptTokenKind.Integer:
                    code.Add(new PostScriptInstruction(PostScriptOpCode.PushInteger, Literal: token.Number));
                    break;

                case PostScriptTokenKind.Real:
                    exponents |= token.HasExponent;
                    code.Add(new PostScriptInstruction(PostScriptOpCode.PushReal, Literal: token.Number));
                    break;

                case PostScriptTokenKind.Name:
                    ReadOnlySpan<byte> name = lexer.TextOf(token);
                    if (name.SequenceEqual("true"u8) || name.SequenceEqual("false"u8))
                    {
                        code.Add(new PostScriptInstruction(PostScriptOpCode.PushBoolean, Literal: name[0] == 't' ? 1 : 0));
                    }
                    else if (Operator(name) is { } opCode)
                    {
                        code.Add(new PostScriptInstruction(opCode));
                    }
                    else
                    {
                        return Invalid(compilation, $"'{Encoding.Latin1.GetString(name)}' is not an operator of Type 4 functions, or is not after a {{ }} body.");
                    }

                    break;

                default:
                    return Invalid(compilation, $"'{Encoding.Latin1.GetString(lexer.TextOf(token))}' is not allowed in a Type 4 function.");
            }

            token = lexer.Next();
        }
    }

    private static PostScriptInstruction[] Finish(List<PostScriptInstruction> code, bool exponents, FunctionCompilation compilation)
    {
        if (exponents)
        {
            compilation.Report(DiagnosticCodes.FunctionProgramSyntaxInvalid, DiagnosticSeverity.Warning, "A number is written with an exponent, which PDF syntax does not allow; it is read.");
        }

        return [.. code];
    }

    private static PostScriptInstruction[]? Invalid(FunctionCompilation compilation, string message)
    {
        compilation.Invalid(message);
        return null;
    }

    private static bool IsName(PostScriptLexer lexer, PostScriptToken token, ReadOnlySpan<byte> name) =>
        token.Kind == PostScriptTokenKind.Name && lexer.TextOf(token).SequenceEqual(name);

    private static PostScriptOpCode? Operator(ReadOnlySpan<byte> name) => Encoding.ASCII.GetString(name) switch
    {
        "abs" => PostScriptOpCode.Abs,
        "add" => PostScriptOpCode.Add,
        "atan" => PostScriptOpCode.Atan,
        "ceiling" => PostScriptOpCode.Ceiling,
        "cos" => PostScriptOpCode.Cos,
        "cvi" => PostScriptOpCode.Cvi,
        "cvr" => PostScriptOpCode.Cvr,
        "div" => PostScriptOpCode.Div,
        "exp" => PostScriptOpCode.Exp,
        "floor" => PostScriptOpCode.Floor,
        "idiv" => PostScriptOpCode.Idiv,
        "ln" => PostScriptOpCode.Ln,
        "log" => PostScriptOpCode.Log,
        "mod" => PostScriptOpCode.Mod,
        "mul" => PostScriptOpCode.Mul,
        "neg" => PostScriptOpCode.Neg,
        "round" => PostScriptOpCode.Round,
        "sin" => PostScriptOpCode.Sin,
        "sqrt" => PostScriptOpCode.Sqrt,
        "sub" => PostScriptOpCode.Sub,
        "truncate" => PostScriptOpCode.Truncate,
        "and" => PostScriptOpCode.And,
        "bitshift" => PostScriptOpCode.Bitshift,
        "eq" => PostScriptOpCode.Eq,
        "ge" => PostScriptOpCode.Ge,
        "gt" => PostScriptOpCode.Gt,
        "le" => PostScriptOpCode.Le,
        "lt" => PostScriptOpCode.Lt,
        "ne" => PostScriptOpCode.Ne,
        "not" => PostScriptOpCode.Not,
        "or" => PostScriptOpCode.Or,
        "xor" => PostScriptOpCode.Xor,
        "copy" => PostScriptOpCode.Copy,
        "dup" => PostScriptOpCode.Dup,
        "exch" => PostScriptOpCode.Exch,
        "index" => PostScriptOpCode.Index,
        "pop" => PostScriptOpCode.Pop,
        "roll" => PostScriptOpCode.Roll,
        _ => null,
    };

    /// <summary>
    /// Follows the stack depth through every path (jumps only go forward, so one pass in order sees every path) and notes a
    /// program that provably underflows, overflows or ends with other than n values. Stops quietly where the depth stops being
    /// known: <c>copy</c>, <c>index</c> or <c>roll</c> with a computed operand, or branches that leave different depths.
    /// </summary>
    private static void CheckStack(PostScriptInstruction[] code, int m, int n, FunctionCompilation compilation)
    {
        const int unreached = -1;
        int[] depths = new int[code.Length + 1];
        Array.Fill(depths, unreached);
        depths[0] = m;
        for (int pc = 0; pc < code.Length; pc++)
        {
            int depth = depths[pc];
            if (depth == unreached)
            {
                continue;
            }

            PostScriptInstruction instruction = code[pc];
            if (!Effect(code, pc, out int needed, out int change))
            {
                return;
            }

            if (depth < needed)
            {
                compilation.Report(DiagnosticCodes.FunctionProgramStackInvalid, DiagnosticSeverity.Warning, "The program takes more values from the stack than it holds.");
                return;
            }

            int after = depth + change;
            if (after > PostScriptEvaluator.StackLimit)
            {
                compilation.Report(DiagnosticCodes.FunctionProgramStackInvalid, DiagnosticSeverity.Warning, string.Create(CultureInfo.InvariantCulture, $"The program needs more than the {PostScriptEvaluator.StackLimit} stack entries a reader provides."));
                return;
            }

            bool merged = instruction.OpCode switch
            {
                PostScriptOpCode.Jump => Merge(depths, instruction.Target, after),
                PostScriptOpCode.JumpIfFalse => Merge(depths, instruction.Target, after) && Merge(depths, pc + 1, after),
                _ => Merge(depths, pc + 1, after),
            };
            if (!merged)
            {
                return;
            }
        }

        if (depths[code.Length] is int final and not unreached && final != n)
        {
            compilation.Report(DiagnosticCodes.FunctionProgramStackInvalid, DiagnosticSeverity.Warning, string.Create(CultureInfo.InvariantCulture, $"The program leaves {final} values on the stack but Range has {n} outputs."));
        }
    }

    private static bool Merge(int[] depths, int target, int depth)
    {
        if (depths[target] == -1)
        {
            depths[target] = depth;
            return true;
        }

        return depths[target] == depth;
    }

    /// <summary>How many values an instruction needs and how it changes the depth; <see langword="false"/> when that is not known.</summary>
    private static bool Effect(PostScriptInstruction[] code, int pc, out int needed, out int change)
    {
        (needed, change) = code[pc].OpCode switch
        {
            PostScriptOpCode.PushReal or PostScriptOpCode.PushInteger or PostScriptOpCode.PushBoolean => (0, 1),
            PostScriptOpCode.Jump => (0, 0),
            PostScriptOpCode.JumpIfFalse or PostScriptOpCode.Pop => (1, -1),
            PostScriptOpCode.Abs or PostScriptOpCode.Ceiling or PostScriptOpCode.Cos or PostScriptOpCode.Cvi or PostScriptOpCode.Cvr
                or PostScriptOpCode.Floor or PostScriptOpCode.Ln or PostScriptOpCode.Log or PostScriptOpCode.Neg or PostScriptOpCode.Round
                or PostScriptOpCode.Sin or PostScriptOpCode.Sqrt or PostScriptOpCode.Truncate or PostScriptOpCode.Not => (1, 0),
            PostScriptOpCode.Dup => (1, 1),
            PostScriptOpCode.Exch => (2, 0),
            PostScriptOpCode.Copy or PostScriptOpCode.Index or PostScriptOpCode.Roll => (-1, 0),
            _ => (2, -1),
        };
        if (needed >= 0)
        {
            return true;
        }

        // copy, index and roll: known only when their operands are literal integers pushed just before (and nothing jumps between).
        bool Literal(int at, out int value)
        {
            value = 0;
            if (at < 0 || code[at].OpCode != PostScriptOpCode.PushInteger || JumpsInto(code, at + 1, pc))
            {
                return false;
            }

            value = (int)code[at].Literal;
            return value >= 0;
        }

        switch (code[pc].OpCode)
        {
            case PostScriptOpCode.Copy when Literal(pc - 1, out int count):
                (needed, change) = (count + 1, count - 1);
                return true;
            case PostScriptOpCode.Index when Literal(pc - 1, out int index):
                (needed, change) = (index + 2, 0);
                return true;
            case PostScriptOpCode.Roll when Literal(pc - 2, out int count) && code[pc - 1].OpCode == PostScriptOpCode.PushInteger:
                (needed, change) = (count + 2, -2);
                return true;
            default:
                return false;
        }
    }

    private static bool JumpsInto(PostScriptInstruction[] code, int first, int last)
    {
        foreach (PostScriptInstruction instruction in code)
        {
            if (instruction.OpCode is PostScriptOpCode.Jump or PostScriptOpCode.JumpIfFalse && instruction.Target >= first && instruction.Target <= last)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A body being compiled: where its jump is, and whether it is the first body (of <c>if</c> or <c>ifelse</c>).</summary>
    private readonly record struct Body(int Jump, bool First);
}
