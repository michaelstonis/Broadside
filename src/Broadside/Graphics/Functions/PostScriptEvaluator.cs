using System.Runtime.CompilerServices;

namespace Broadside.Graphics.Functions;

/// <summary>Runs a compiled Type 4 program over an operand stack on the call stack.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.10.5; the operators mean what PLRM §8.2 says. The stack holds <see cref="StackLimit"/> entries (§7.10.5.3: "at
/// least 100"), each a real, a 32-bit integer or a boolean. The inputs are pushed as reals, first input deepest; the top n entries
/// left are the outputs, deepest first.
/// </para>
/// <para>
/// Integer arithmetic stays integer while the result fits in 32 bits (<c>add</c>, <c>sub</c>, <c>mul</c>, <c>abs</c>, <c>neg</c>,
/// <c>idiv</c>), as PostScript does. <c>round</c> rounds halves up (−6.5 to −6); <c>sin</c> and <c>cos</c> take degrees and are exact
/// at multiples of 90; <c>atan</c> returns degrees in [0, 360); <c>bitshift</c> shifts in zeros on either side, as PLRM says. The
/// errors PostScript raises are repaired and reported through <see cref="FunctionStatus.Repaired"/>: an undefined result (division by
/// zero, the square root or logarithm of a number out of their domain, <c>0 0 atan</c>) is 0, an operand of the wrong type is
/// converted (a boolean to 1 or 0, a real to an integer by truncation), a number used as a condition is true when it is not 0, the
/// wrong number of results is cut to the topmost or padded with 0, and a boolean result is 1 or 0. A stack overflow or underflow, or
/// a <c>copy</c>, <c>index</c> or <c>roll</c> outside the stack, fails the evaluation (<see cref="FunctionStatus.Failed"/>).
/// </para>
/// </remarks>
internal sealed class PostScriptEvaluator : FunctionEvaluator
{
    /// <summary>The number of operand stack entries.</summary>
    public const int StackLimit = 100;

    private const byte Real = 0;
    private const byte Integer = 1;
    private const byte Boolean = 2;

    private readonly PostScriptInstruction[] _code;

    /// <summary>Initializes a new instance of the <see cref="PostScriptEvaluator"/> class.</summary>
    /// <param name="domain">m pairs.</param>
    /// <param name="range">n pairs, or <see langword="null"/> when missing (repaired).</param>
    /// <param name="outputCount">n.</param>
    /// <param name="code">The compiled program.</param>
    public PostScriptEvaluator(double[] domain, double[]? range, int outputCount, PostScriptInstruction[] code)
        : base(domain, range, outputCount)
    {
        _code = code;
    }

    /// <inheritdoc/>
    [SkipLocalsInit]
    protected override FunctionStatus EvaluateCore(ReadOnlySpan<double> input, Span<double> output)
    {
        Span<double> values = stackalloc double[StackLimit];
        Span<byte> kinds = stackalloc byte[StackLimit];
        int sp = 0;
        for (; sp < input.Length; sp++)
        {
            values[sp] = input[sp];
            kinds[sp] = Real;
        }

        bool repaired = false;
        ReadOnlySpan<PostScriptInstruction> code = _code;
        int pc = 0;
        while (pc < code.Length)
        {
            PostScriptInstruction instruction = code[pc++];
            int a = sp - 2;
            int b = sp - 1;
            switch (instruction.OpCode)
            {
                case PostScriptOpCode.PushReal:
                case PostScriptOpCode.PushInteger:
                case PostScriptOpCode.PushBoolean:
                    if (sp == StackLimit)
                    {
                        return Fail(output);
                    }

                    values[sp] = instruction.Literal;
                    kinds[sp++] = instruction.OpCode switch
                    {
                        PostScriptOpCode.PushReal => Real,
                        PostScriptOpCode.PushInteger => Integer,
                        _ => Boolean,
                    };
                    break;

                case PostScriptOpCode.JumpIfFalse:
                    if (sp < 1)
                    {
                        return Fail(output);
                    }

                    sp--;
                    repaired |= kinds[sp] != Boolean;
                    if (values[sp] == 0 || double.IsNaN(values[sp]))
                    {
                        pc = instruction.Target;
                    }

                    break;

                case PostScriptOpCode.Jump:
                    pc = instruction.Target;
                    break;

                case PostScriptOpCode.Add:
                case PostScriptOpCode.Sub:
                case PostScriptOpCode.Mul:
                    {
                        if (sp < 2)
                        {
                            return Fail(output);
                        }

                        Numeric(kinds, a, ref repaired);
                        Numeric(kinds, b, ref repaired);
                        double x = values[a];
                        double y = values[b];
                        double result = instruction.OpCode switch
                        {
                            PostScriptOpCode.Add => x + y,
                            PostScriptOpCode.Sub => x - y,
                            _ => x * y,
                        };
                        if (!double.IsFinite(result))
                        {
                            result = 0;
                            repaired = true;
                        }

                        SetNumber(values, kinds, a, result, kinds[a] == Integer && kinds[b] == Integer);
                        sp--;
                        break;
                    }

                case PostScriptOpCode.Div:
                case PostScriptOpCode.Atan:
                case PostScriptOpCode.Exp:
                    {
                        if (sp < 2)
                        {
                            return Fail(output);
                        }

                        Numeric(kinds, a, ref repaired);
                        Numeric(kinds, b, ref repaired);
                        double x = values[a];
                        double y = values[b];
                        double result = instruction.OpCode switch
                        {
                            PostScriptOpCode.Div => y == 0 ? double.NaN : x / y,
                            PostScriptOpCode.Atan => x == 0 && y == 0 ? double.NaN : Degrees(x, y),
                            _ => Math.Pow(x, y),
                        };
                        if (!double.IsFinite(result))
                        {
                            result = 0;
                            repaired = true;
                        }

                        values[a] = result;
                        kinds[a] = Real;
                        sp--;
                        break;
                    }

                case PostScriptOpCode.Idiv:
                case PostScriptOpCode.Mod:
                    {
                        if (sp < 2)
                        {
                            return Fail(output);
                        }

                        long x = ToInteger(values, kinds, a, ref repaired);
                        long y = ToInteger(values, kinds, b, ref repaired);
                        double result = 0;
                        if (y == 0)
                        {
                            repaired = true;
                        }
                        else
                        {
                            result = instruction.OpCode == PostScriptOpCode.Idiv ? x / y : x % y;
                        }

                        SetNumber(values, kinds, a, result, integer: true);
                        sp--;
                        break;
                    }

                case PostScriptOpCode.Abs:
                case PostScriptOpCode.Neg:
                case PostScriptOpCode.Ceiling:
                case PostScriptOpCode.Floor:
                case PostScriptOpCode.Round:
                case PostScriptOpCode.Truncate:
                    {
                        if (sp < 1)
                        {
                            return Fail(output);
                        }

                        Numeric(kinds, b, ref repaired);
                        double x = values[b];
                        double result = instruction.OpCode switch
                        {
                            PostScriptOpCode.Abs => Math.Abs(x),
                            PostScriptOpCode.Neg => -x,
                            PostScriptOpCode.Ceiling => Math.Ceiling(x),
                            PostScriptOpCode.Floor => Math.Floor(x),
                            PostScriptOpCode.Round => Math.Floor(x + 0.5),
                            _ => Math.Truncate(x),
                        };
                        SetNumber(values, kinds, b, result, kinds[b] == Integer);
                        break;
                    }

                case PostScriptOpCode.Sqrt:
                case PostScriptOpCode.Sin:
                case PostScriptOpCode.Cos:
                case PostScriptOpCode.Ln:
                case PostScriptOpCode.Log:
                case PostScriptOpCode.Cvr:
                    {
                        if (sp < 1)
                        {
                            return Fail(output);
                        }

                        Numeric(kinds, b, ref repaired);
                        double x = values[b];
                        double result = instruction.OpCode switch
                        {
                            PostScriptOpCode.Sqrt => x >= 0 ? Math.Sqrt(x) : double.NaN,
                            PostScriptOpCode.Sin => double.SinPi(x % 360 / 180),
                            PostScriptOpCode.Cos => double.CosPi(x % 360 / 180),
                            PostScriptOpCode.Ln => x > 0 ? Math.Log(x) : double.NaN,
                            PostScriptOpCode.Log => x > 0 ? Math.Log10(x) : double.NaN,
                            _ => x,
                        };
                        if (!double.IsFinite(result))
                        {
                            result = 0;
                            repaired = true;
                        }

                        values[b] = result;
                        kinds[b] = Real;
                        break;
                    }

                case PostScriptOpCode.Cvi:
                    {
                        if (sp < 1)
                        {
                            return Fail(output);
                        }

                        double x = values[b];
                        if (kinds[b] == Boolean)
                        {
                            repaired = true;
                        }
                        else if (kinds[b] == Real)
                        {
                            if (!(x > int.MinValue - 1.0 && x < int.MaxValue + 1.0))
                            {
                                repaired = true;
                                x = double.IsNaN(x) ? 0 : Math.Clamp(x, int.MinValue, int.MaxValue);
                            }

                            x = Math.Truncate(x);
                        }

                        values[b] = x;
                        kinds[b] = Integer;
                        break;
                    }

                case PostScriptOpCode.Eq:
                case PostScriptOpCode.Ne:
                    {
                        if (sp < 2)
                        {
                            return Fail(output);
                        }

                        bool equal = (kinds[a] == Boolean) == (kinds[b] == Boolean) && values[a] == values[b];
                        values[a] = equal == (instruction.OpCode == PostScriptOpCode.Eq) ? 1 : 0;
                        kinds[a] = Boolean;
                        sp--;
                        break;
                    }

                case PostScriptOpCode.Ge:
                case PostScriptOpCode.Gt:
                case PostScriptOpCode.Le:
                case PostScriptOpCode.Lt:
                    {
                        if (sp < 2)
                        {
                            return Fail(output);
                        }

                        Numeric(kinds, a, ref repaired);
                        Numeric(kinds, b, ref repaired);
                        double x = values[a];
                        double y = values[b];
                        bool result = instruction.OpCode switch
                        {
                            PostScriptOpCode.Ge => x >= y,
                            PostScriptOpCode.Gt => x > y,
                            PostScriptOpCode.Le => x <= y,
                            _ => x < y,
                        };
                        values[a] = result ? 1 : 0;
                        kinds[a] = Boolean;
                        sp--;
                        break;
                    }

                case PostScriptOpCode.And:
                case PostScriptOpCode.Or:
                case PostScriptOpCode.Xor:
                    {
                        if (sp < 2)
                        {
                            return Fail(output);
                        }

                        bool logical = kinds[a] == Boolean && kinds[b] == Boolean;
                        int x = ToInteger(values, kinds, a, ref repaired, logical);
                        int y = ToInteger(values, kinds, b, ref repaired, logical);
                        int result = instruction.OpCode switch
                        {
                            PostScriptOpCode.And => x & y,
                            PostScriptOpCode.Or => x | y,
                            _ => x ^ y,
                        };
                        values[a] = result;
                        kinds[a] = logical ? Boolean : Integer;
                        sp--;
                        break;
                    }

                case PostScriptOpCode.Not:
                    if (sp < 1)
                    {
                        return Fail(output);
                    }

                    if (kinds[b] == Boolean)
                    {
                        values[b] = values[b] == 0 ? 1 : 0;
                    }
                    else
                    {
                        values[b] = ~ToInteger(values, kinds, b, ref repaired);
                        kinds[b] = Integer;
                    }

                    break;

                case PostScriptOpCode.Bitshift:
                    {
                        if (sp < 2)
                        {
                            return Fail(output);
                        }

                        uint x = (uint)ToInteger(values, kinds, a, ref repaired);
                        int shift = ToInteger(values, kinds, b, ref repaired);
                        uint result = shift switch
                        {
                            >= 32 or <= -32 => 0,
                            >= 0 => x << shift,
                            _ => x >> -shift,
                        };
                        values[a] = (int)result;
                        kinds[a] = Integer;
                        sp--;
                        break;
                    }

                case PostScriptOpCode.Dup:
                    if (sp < 1 || sp == StackLimit)
                    {
                        return Fail(output);
                    }

                    values[sp] = values[b];
                    kinds[sp++] = kinds[b];
                    break;

                case PostScriptOpCode.Exch:
                    if (sp < 2)
                    {
                        return Fail(output);
                    }

                    (values[a], values[b]) = (values[b], values[a]);
                    (kinds[a], kinds[b]) = (kinds[b], kinds[a]);
                    break;

                case PostScriptOpCode.Pop:
                    if (sp < 1)
                    {
                        return Fail(output);
                    }

                    sp--;
                    break;

                case PostScriptOpCode.Copy:
                    {
                        if (sp < 1)
                        {
                            return Fail(output);
                        }

                        int count = ToInteger(values, kinds, b, ref repaired);
                        sp--;
                        if (count < 0 || count > sp || sp + count > StackLimit)
                        {
                            return Fail(output);
                        }

                        values.Slice(sp - count, count).CopyTo(values[sp..]);
                        kinds.Slice(sp - count, count).CopyTo(kinds[sp..]);
                        sp += count;
                        break;
                    }

                case PostScriptOpCode.Index:
                    {
                        if (sp < 1)
                        {
                            return Fail(output);
                        }

                        int index = ToInteger(values, kinds, b, ref repaired);
                        if (index < 0 || index >= b)
                        {
                            return Fail(output);
                        }

                        values[b] = values[b - 1 - index];
                        kinds[b] = kinds[b - 1 - index];
                        break;
                    }

                case PostScriptOpCode.Roll:
                    {
                        if (sp < 2)
                        {
                            return Fail(output);
                        }

                        int count = ToInteger(values, kinds, a, ref repaired);
                        int shift = ToInteger(values, kinds, b, ref repaired);
                        sp -= 2;
                        if (count < 0 || count > sp)
                        {
                            return Fail(output);
                        }

                        if (count > 1)
                        {
                            Roll(values.Slice(sp - count, count), kinds.Slice(sp - count, count), (int)(((shift % (long)count) + count) % count));
                        }

                        break;
                    }

                default:
                    return Fail(output);
            }
        }

        int n = output.Length;
        repaired |= sp != n;
        int first = Math.Max(sp - n, 0);
        for (int j = 0; j < n; j++)
        {
            int at = first + j;
            if (at < sp)
            {
                repaired |= kinds[at] == Boolean;
                output[j] = values[at];
            }
            else
            {
                output[j] = 0;
            }
        }

        return repaired ? FunctionStatus.Repaired : FunctionStatus.Ok;
    }

    private static FunctionStatus Fail(Span<double> output)
    {
        output.Clear();
        return FunctionStatus.Failed;
    }

    /// <summary>Makes the entry a number: a boolean becomes the integer 1 or 0 (a typecheck error in PostScript).</summary>
    private static void Numeric(Span<byte> kinds, int at, ref bool repaired)
    {
        if (kinds[at] == Boolean)
        {
            kinds[at] = Integer;
            repaired = true;
        }
    }

    /// <summary>Stores a numeric result: an integer when both operands were and the result fits in 32 bits, else a real.</summary>
    private static void SetNumber(Span<double> values, Span<byte> kinds, int at, double result, bool integer)
    {
        values[at] = result;
        kinds[at] = integer && result >= int.MinValue && result <= int.MaxValue ? Integer : Real;
    }

    /// <summary>Reads an entry as a 32-bit integer: a real is truncated and clamped, a boolean is 1 or 0 (noted unless allowed).</summary>
    private static int ToInteger(Span<double> values, Span<byte> kinds, int at, ref bool repaired, bool booleanAllowed = false)
    {
        double value = values[at];
        if (kinds[at] == Integer)
        {
            return (int)value;
        }

        if (kinds[at] == Boolean)
        {
            repaired |= !booleanAllowed;
            return value == 0 ? 0 : 1;
        }

        repaired = true;
        return double.IsNaN(value) ? 0 : (int)Math.Clamp(Math.Truncate(value), int.MinValue, int.MaxValue);
    }

    /// <summary>The angle of (den, num) in degrees, in [0, 360).</summary>
    private static double Degrees(double numerator, double denominator)
    {
        double degrees = Math.Atan2(numerator, denominator) * (180 / Math.PI);
        return degrees < 0 ? degrees + 360 : degrees;
    }

    /// <summary>Rotates the entries <paramref name="shift"/> places toward the top (PLRM <c>roll</c> with a positive j).</summary>
    private static void Roll(Span<double> values, Span<byte> kinds, int shift)
    {
        if (shift == 0)
        {
            return;
        }

        values.Reverse();
        values[..shift].Reverse();
        values[shift..].Reverse();
        kinds.Reverse();
        kinds[..shift].Reverse();
        kinds[shift..].Reverse();
    }
}
