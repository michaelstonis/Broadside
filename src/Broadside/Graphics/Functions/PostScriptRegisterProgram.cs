using System.Runtime.CompilerServices;
using Broadside.Numerics;

namespace Broadside.Graphics.Functions;

/// <summary>The operations of a lowered Type 4 program (<see cref="PostScriptLowering"/>).</summary>
internal enum PostScriptRegisterOpCode : byte
{
    /// <summary>Copy Left into Destination.</summary>
    Move,

    /// <summary>Nothing but the repair it is marked with (a boolean converted where the value does not change).</summary>
    Repair,

    /// <summary>Go to Target.</summary>
    Jump,

    /// <summary>Go to Target when Left is 0 (false) or NaN.</summary>
    JumpIfFalse,

    /// <summary><c>add</c>.</summary>
    Add,

    /// <summary><c>sub</c>.</summary>
    Sub,

    /// <summary><c>mul</c>.</summary>
    Mul,

    /// <summary><c>div</c>.</summary>
    Div,

    /// <summary><c>atan</c>.</summary>
    Atan,

    /// <summary><c>exp</c>.</summary>
    Exp,

    /// <summary><c>idiv</c>.</summary>
    Idiv,

    /// <summary><c>mod</c>.</summary>
    Mod,

    /// <summary><c>abs</c>.</summary>
    Abs,

    /// <summary><c>neg</c>.</summary>
    Neg,

    /// <summary><c>ceiling</c>.</summary>
    Ceiling,

    /// <summary><c>floor</c>.</summary>
    Floor,

    /// <summary><c>round</c>.</summary>
    Round,

    /// <summary><c>truncate</c>.</summary>
    Truncate,

    /// <summary><c>sqrt</c>.</summary>
    Sqrt,

    /// <summary><c>sin</c>.</summary>
    Sin,

    /// <summary><c>cos</c>.</summary>
    Cos,

    /// <summary><c>ln</c>.</summary>
    Ln,

    /// <summary><c>log</c>.</summary>
    Log,

    /// <summary><c>cvi</c> of a real.</summary>
    Cvi,

    /// <summary><c>eq</c>.</summary>
    Eq,

    /// <summary><c>ne</c>.</summary>
    Ne,

    /// <summary><c>ge</c>.</summary>
    Ge,

    /// <summary><c>gt</c>.</summary>
    Gt,

    /// <summary><c>le</c>.</summary>
    Le,

    /// <summary><c>lt</c>.</summary>
    Lt,

    /// <summary><c>and</c>.</summary>
    And,

    /// <summary><c>or</c>.</summary>
    Or,

    /// <summary><c>xor</c>.</summary>
    Xor,

    /// <summary><c>not</c>.</summary>
    Not,

    /// <summary><c>bitshift</c>.</summary>
    Bitshift,
}

/// <summary>One instruction of a lowered Type 4 program.</summary>
/// <param name="OpCode">What to do.</param>
/// <param name="Destination">The register written.</param>
/// <param name="Left">The first (or only) operand register.</param>
/// <param name="Right">The second operand register.</param>
/// <param name="LeftKind">The kind of the first operand, known at compile time.</param>
/// <param name="RightKind">The kind of the second operand, known at compile time.</param>
/// <param name="Repairs">Whether running the instruction is a repair (a conversion the interpreter notes).</param>
/// <param name="Target">The instruction a jump goes to.</param>
internal readonly record struct PostScriptRegisterInstruction(
    PostScriptRegisterOpCode OpCode,
    int Destination,
    int Left,
    int Right,
    byte LeftKind,
    byte RightKind,
    bool Repairs,
    int Target = 0);

/// <summary>
/// A Type 4 program lowered to registers (<see cref="PostScriptLowering"/>): no stack, no run-time kinds, the same results and
/// statuses as <see cref="PostScriptEvaluator"/>'s interpreter. Immutable; evaluation allocates nothing.
/// </summary>
/// <remarks>ISO 32000-2 §7.10.5, Annex B.</remarks>
internal sealed class PostScriptRegisterProgram
{
    private const byte Boolean = PostScriptLowering.Boolean;
    private const byte Integer = PostScriptLowering.Integer;

    private readonly PostScriptRegisterInstruction[] _code;
    private readonly double[] _constants;
    private readonly int _inputBase;
    private readonly int[] _outputs;
    private readonly bool _endRepairs;

    /// <summary>Initializes a new instance of the <see cref="PostScriptRegisterProgram"/> class.</summary>
    /// <param name="code">The instructions.</param>
    /// <param name="constants">The constants, in registers 0 onward.</param>
    /// <param name="inputBase">The register of the first input.</param>
    /// <param name="inputs">m.</param>
    /// <param name="registerCount">The registers the program uses.</param>
    /// <param name="outputs">The register of each output, or −1 for a missing result (0).</param>
    /// <param name="endRepairs">Whether the results the program leaves are a repair (their number, or a boolean).</param>
    public PostScriptRegisterProgram(PostScriptRegisterInstruction[] code, double[] constants, int inputBase, int inputs, int registerCount, int[] outputs, bool endRepairs)
    {
        _code = code;
        _constants = constants;
        _inputBase = inputBase;
        InputCount = inputs;
        RegisterCount = registerCount;
        _outputs = outputs;
        _endRepairs = endRepairs;
    }

    /// <summary>Gets m.</summary>
    public int InputCount { get; }

    /// <summary>Gets the number of registers.</summary>
    public int RegisterCount { get; }

    /// <summary>Gets the number of instructions.</summary>
    public int Length => _code.Length;

    /// <summary>Runs the program.</summary>
    /// <param name="input">m inputs, clipped to the domain.</param>
    /// <param name="output">n outputs.</param>
    /// <returns><see cref="FunctionStatus.Ok"/> or <see cref="FunctionStatus.Repaired"/>; a lowered program never fails.</returns>
    [SkipLocalsInit]
    public FunctionStatus Evaluate(ReadOnlySpan<double> input, Span<double> output)
    {
        Span<double> r = RegisterCount <= 128 ? stackalloc double[128] : stackalloc double[RegisterCount];
        _constants.CopyTo(r);
        input[..InputCount].CopyTo(r[_inputBase..]);
        bool repaired = _endRepairs;
        ReadOnlySpan<PostScriptRegisterInstruction> code = _code;
        int pc = 0;
        while (pc < code.Length)
        {
            ref readonly PostScriptRegisterInstruction i = ref code[pc++];
            repaired |= i.Repairs;
            switch (i.OpCode)
            {
                case PostScriptRegisterOpCode.Move:
                    r[i.Destination] = r[i.Left];
                    break;
                case PostScriptRegisterOpCode.Repair:
                    break;
                case PostScriptRegisterOpCode.Jump:
                    pc = i.Target;
                    break;
                case PostScriptRegisterOpCode.JumpIfFalse:
                    {
                        double condition = r[i.Left];
                        if (condition == 0 || double.IsNaN(condition))
                        {
                            pc = i.Target;
                        }

                        break;
                    }

                case PostScriptRegisterOpCode.Add:
                    r[i.Destination] = Finite(r[i.Left] + r[i.Right], ref repaired);
                    break;
                case PostScriptRegisterOpCode.Sub:
                    r[i.Destination] = Finite(r[i.Left] - r[i.Right], ref repaired);
                    break;
                case PostScriptRegisterOpCode.Mul:
                    r[i.Destination] = Finite(r[i.Left] * r[i.Right], ref repaired);
                    break;
                case PostScriptRegisterOpCode.Div:
                    {
                        double y = r[i.Right];
                        r[i.Destination] = Finite(y == 0 ? double.NaN : r[i.Left] / y, ref repaired);
                        break;
                    }

                case PostScriptRegisterOpCode.Atan:
                    {
                        double x = r[i.Left];
                        double y = r[i.Right];
                        r[i.Destination] = Finite(x == 0 && y == 0 ? double.NaN : Degrees(x, y), ref repaired);
                        break;
                    }

                case PostScriptRegisterOpCode.Exp:
                    r[i.Destination] = Finite(DeterministicMath.Pow(r[i.Left], r[i.Right]), ref repaired);
                    break;
                case PostScriptRegisterOpCode.Idiv:
                case PostScriptRegisterOpCode.Mod:
                    {
                        long x = ToInteger(r[i.Left], i.LeftKind);
                        long y = ToInteger(r[i.Right], i.RightKind);
                        double result = 0;
                        if (y == 0)
                        {
                            repaired = true;
                        }
                        else
                        {
                            result = i.OpCode == PostScriptRegisterOpCode.Idiv ? x / y : x % y;
                        }

                        r[i.Destination] = result;
                        break;
                    }

                case PostScriptRegisterOpCode.Abs:
                    r[i.Destination] = Math.Abs(r[i.Left]);
                    break;
                case PostScriptRegisterOpCode.Neg:
                    r[i.Destination] = -r[i.Left];
                    break;
                case PostScriptRegisterOpCode.Ceiling:
                    r[i.Destination] = Math.Ceiling(r[i.Left]);
                    break;
                case PostScriptRegisterOpCode.Floor:
                    r[i.Destination] = Math.Floor(r[i.Left]);
                    break;
                case PostScriptRegisterOpCode.Round:
                    r[i.Destination] = Math.Floor(r[i.Left] + 0.5);
                    break;
                case PostScriptRegisterOpCode.Truncate:
                    r[i.Destination] = Math.Truncate(r[i.Left]);
                    break;
                case PostScriptRegisterOpCode.Sqrt:
                    {
                        double x = r[i.Left];
                        r[i.Destination] = Finite(x >= 0 ? Math.Sqrt(x) : double.NaN, ref repaired);
                        break;
                    }

                case PostScriptRegisterOpCode.Sin:
                    r[i.Destination] = Finite(DeterministicMath.SinDegrees(r[i.Left]), ref repaired);
                    break;
                case PostScriptRegisterOpCode.Cos:
                    r[i.Destination] = Finite(DeterministicMath.CosDegrees(r[i.Left]), ref repaired);
                    break;
                case PostScriptRegisterOpCode.Ln:
                    {
                        double x = r[i.Left];
                        r[i.Destination] = Finite(x > 0 ? DeterministicMath.Log(x) : double.NaN, ref repaired);
                        break;
                    }

                case PostScriptRegisterOpCode.Log:
                    {
                        double x = r[i.Left];
                        r[i.Destination] = Finite(x > 0 ? DeterministicMath.Log10(x) : double.NaN, ref repaired);
                        break;
                    }

                case PostScriptRegisterOpCode.Cvi:
                    {
                        double x = r[i.Left];
                        if (!(x > int.MinValue - 1.0 && x < int.MaxValue + 1.0))
                        {
                            repaired = true;
                            x = double.IsNaN(x) ? 0 : Math.Clamp(x, int.MinValue, int.MaxValue);
                        }

                        r[i.Destination] = Math.Truncate(x);
                        break;
                    }

                case PostScriptRegisterOpCode.Eq:
                case PostScriptRegisterOpCode.Ne:
                    {
                        bool equal = (i.LeftKind == Boolean) == (i.RightKind == Boolean) && r[i.Left] == r[i.Right];
                        r[i.Destination] = equal == (i.OpCode == PostScriptRegisterOpCode.Eq) ? 1 : 0;
                        break;
                    }

                case PostScriptRegisterOpCode.Ge:
                    r[i.Destination] = r[i.Left] >= r[i.Right] ? 1 : 0;
                    break;
                case PostScriptRegisterOpCode.Gt:
                    r[i.Destination] = r[i.Left] > r[i.Right] ? 1 : 0;
                    break;
                case PostScriptRegisterOpCode.Le:
                    r[i.Destination] = r[i.Left] <= r[i.Right] ? 1 : 0;
                    break;
                case PostScriptRegisterOpCode.Lt:
                    r[i.Destination] = r[i.Left] < r[i.Right] ? 1 : 0;
                    break;
                case PostScriptRegisterOpCode.And:
                    r[i.Destination] = ToInteger(r[i.Left], i.LeftKind) & ToInteger(r[i.Right], i.RightKind);
                    break;
                case PostScriptRegisterOpCode.Or:
                    r[i.Destination] = ToInteger(r[i.Left], i.LeftKind) | ToInteger(r[i.Right], i.RightKind);
                    break;
                case PostScriptRegisterOpCode.Xor:
                    r[i.Destination] = ToInteger(r[i.Left], i.LeftKind) ^ ToInteger(r[i.Right], i.RightKind);
                    break;
                case PostScriptRegisterOpCode.Not:
                    {
                        double x = r[i.Left];
                        r[i.Destination] = i.LeftKind == Boolean ? (x == 0 ? 1 : 0) : ~ToInteger(x, i.LeftKind);
                        break;
                    }

                case PostScriptRegisterOpCode.Bitshift:
                    {
                        uint x = (uint)ToInteger(r[i.Left], i.LeftKind);
                        int shift = ToInteger(r[i.Right], i.RightKind);
                        uint result = shift switch
                        {
                            >= 32 or <= -32 => 0,
                            >= 0 => x << shift,
                            _ => x >> -shift,
                        };
                        r[i.Destination] = (int)result;
                        break;
                    }
            }
        }

        ReadOnlySpan<int> outputs = _outputs;
        for (int j = 0; j < outputs.Length; j++)
        {
            int at = outputs[j];
            output[j] = at < 0 ? 0 : r[at];
        }

        return repaired ? FunctionStatus.Repaired : FunctionStatus.Ok;
    }

    private static double Finite(double value, ref bool repaired)
    {
        if (double.IsFinite(value))
        {
            return value;
        }

        repaired = true;
        return 0;
    }

    /// <summary>The interpreter's conversion of an operand of a known kind to a 32-bit integer (the repair is noted at compile time).</summary>
    private static int ToInteger(double value, byte kind) => kind switch
    {
        Integer => (int)value,
        Boolean => value == 0 ? 0 : 1,
        _ => double.IsNaN(value) ? 0 : (int)Math.Clamp(Math.Truncate(value), int.MinValue, int.MaxValue),
    };

    private static double Degrees(double numerator, double denominator)
    {
        double degrees = DeterministicMath.Atan2(numerator, denominator) * (180 / Math.PI);
        return degrees < 0 ? degrees + 360 : degrees;
    }
}
