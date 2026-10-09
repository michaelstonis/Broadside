using System.Buffers.Binary;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Parsing;

namespace Broadside.Fonts.Type1;

/// <summary>
/// Interprets one glyph's Type 1 charstring into a <see cref="GlyphOutline"/>: every command, subroutines, the OtherSubrs a reader
/// implements natively (flex, hint replacement, counter control, multiple master blends) and <c>seac</c>. All state is on the stack:
/// a run allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// Type 1 Font Format chapter 6 (number encoding §6.2, commands §6.4-6.5, Appendix 2), chapter 8 (subroutines §8.1, OtherSubrs and
/// <c>pop</c> §8.2, hint replacement §8.2, dotsection §8.3, flex §8.3, the first four Subrs §8.4) and TN 5015 (§2.2-2.3 counter
/// control OtherSubrs 12 and 13, §3.13 blend OtherSubrs 14-18, §6 the corrected <c>seac</c> offset).
/// </para>
/// <para>
/// Operands are doubles: a 5-byte integer operand exceeds a float's precision before <c>div</c>. A command takes its operands from
/// the top of the stack and clears it (FreeType's reading; a well-formed charstring has exactly as many). <c>closepath</c> does not
/// move the current point (§6.4); a contour starts at the first drawing command after a move, so consecutive moves make no empty
/// contours. Flex is always drawn as its two curves (the flex height only matters to hinting).
/// </para>
/// </remarks>
internal ref struct Type1CharstringInterpreter
{
    /// <summary>The operand stack size: 24 in Type 1 Font Format §6.1, doubled for multiple master and counter control data.</summary>
    private const int StackSize = 48;

    /// <summary>Subroutine nesting: 10 in Type 1 Font Format §8.1; 16 as FreeType allows.</summary>
    private const int MaxDepth = 16;

    /// <summary>The most bytes one glyph may execute, subroutines and seac components included, so fan-out through subroutines ends.</summary>
    private const int MaxOperations = 1 << 20;

    private const int Escape = 12;

    private readonly Type1FontProgram _program;
    private readonly GlyphOutline? _outline;
    private readonly bool _metricsOnly;
    private readonly int _maxPoints;
    private StackBuffer _stack;
    private StackBuffer _results;
    private FrameBuffer _frames;
    private FlexBuffer _flex;
    private int _count;
    private int _resultCount;
    private int _resultRead;
    private int _flexCount;
    private bool _inFlex;
    private PathPoint _flexStart;
    private double _x;
    private double _y;
    private double _originX;
    private double _originY;
    private bool _open;
    private bool _haveWidth;
    private bool _inSeac;
    private double _sideBearingX;
    private int _operations;
    private int _points;

    /// <summary>Initializes a new instance of the <see cref="Type1CharstringInterpreter"/> struct.</summary>
    /// <param name="program">The program whose charstrings run.</param>
    /// <param name="outline">Where the outline goes; <see langword="null"/> with <paramref name="metricsOnly"/>.</param>
    /// <param name="metricsOnly">Whether to stop at <c>hsbw</c> or <c>sbw</c>.</param>
    public Type1CharstringInterpreter(Type1FontProgram program, GlyphOutline? outline, bool metricsOnly)
    {
        _program = program;
        _outline = outline;
        _metricsOnly = metricsOnly;
        _maxPoints = program.Context.MaxGlyphPoints;
    }

    /// <summary>Gets the advance width and left side bearing from the glyph's <c>hsbw</c> or <c>sbw</c>.</summary>
    public GlyphMetrics Metrics { get; private set; }

    /// <summary>Runs a glyph's charstring.</summary>
    /// <param name="glyphId">The glyph id, in range.</param>
    /// <returns><see langword="false"/> when the charstring could not be finished and the glyph is dropped.</returns>
    /// <remarks>An empty charstring (the stand-in for a missing <c>.notdef</c>, already reported) draws nothing, silently.</remarks>
    public bool Run(int glyphId) => _program.Glyphs[glyphId].Length == 0 || Execute(_program.Glyphs[glyphId]) != Outcome.Failed;

    private enum Outcome
    {
        Finished,
        Failed,
    }

    private Outcome Execute((int Start, int Length) charstring)
    {
        ReadOnlySpan<byte> pool = _program.Pool;
        int depth = 0;
        int position = charstring.Start;
        int end = charstring.Start + charstring.Length;
        while (true)
        {
            if (position >= end)
            {
                if (depth == 0)
                {
                    Report(DiagnosticCodes.FontType1EndcharMissing, DiagnosticSeverity.Warning, "a charstring ends without endchar; the glyph ends there");
                    return Finish();
                }

                // A subroutine without return: return at its end.
                depth--;
                position = _frames[depth].Position;
                end = _frames[depth].End;
                continue;
            }

            if (++_operations > MaxOperations)
            {
                return Fail(DiagnosticCodes.FontType1GlyphTooComplex, "a charstring runs more than 1,048,576 bytes through its subroutines; the glyph is dropped");
            }

            int b0 = pool[position++];
            if (b0 >= 32)
            {
                double value;
                if (b0 <= 246)
                {
                    value = b0 - 139;
                }
                else if (b0 <= 254)
                {
                    if (position >= end)
                    {
                        return Fail(DiagnosticCodes.FontType1CharstringTruncated, "a charstring ends inside a number; the glyph is dropped");
                    }

                    int b1 = pool[position++];
                    value = b0 <= 250 ? ((b0 - 247) * 256) + b1 + 108 : -((b0 - 251) * 256) - b1 - 108;
                }
                else
                {
                    if (end - position < 4)
                    {
                        return Fail(DiagnosticCodes.FontType1CharstringTruncated, "a charstring ends inside a number; the glyph is dropped");
                    }

                    value = BinaryPrimitives.ReadInt32BigEndian(pool.Slice(position, 4));
                    position += 4;
                }

                if (!Push(value))
                {
                    return Outcome.Failed;
                }

                continue;
            }

            int op = b0;
            if (b0 == Escape)
            {
                if (position >= end)
                {
                    return Fail(DiagnosticCodes.FontType1CharstringTruncated, "a charstring ends after the escape byte; the glyph is dropped");
                }

                op = 32 + pool[position++];
            }

            if (op is not (Op.CallSubr or Op.Return or Op.Pop))
            {
                // Results of an OtherSubr not taken by pop are discarded by the next command (FreeType does the same).
                _resultCount = 0;
                _resultRead = 0;
            }

            switch (op)
            {
                case Op.CallSubr:
                    {
                        if (_count < 1)
                        {
                            return Fail(DiagnosticCodes.FontType1StackUnderflow, "callsubr has no subroutine number; the glyph is dropped");
                        }

                        double number = _stack[--_count];
                        (int Start, int Length)[] subrs = _program.Subrs;
                        if (number != Math.Floor(number) || number < 0 || number >= subrs.Length || subrs[(int)number].Length < 0)
                        {
                            return Fail(DiagnosticCodes.FontType1SubrMissing, "callsubr calls a subroutine the program does not have; the glyph is dropped");
                        }

                        if (depth + 1 >= MaxDepth)
                        {
                            return Fail(DiagnosticCodes.FontType1SubrDepthExceeded, "subroutines nest more than 16 deep (Type 1 Font Format §8.1 allows 10); the glyph is dropped");
                        }

                        _frames[depth] = new Frame(end, position);
                        depth++;
                        (int start, int length) = subrs[(int)number];
                        position = start;
                        end = start + length;
                        break;
                    }

                case Op.Return:
                    if (depth == 0)
                    {
                        Report(DiagnosticCodes.FontType1UnknownOperator, DiagnosticSeverity.Warning, "return outside a subroutine is ignored");
                        break;
                    }

                    depth--;
                    position = _frames[depth].Position;
                    end = _frames[depth].End;
                    break;

                case Op.Endchar:
                    _count = 0;
                    return Finish();

                case Op.Hsbw:
                    if (Arguments(2, out int hsbw))
                    {
                        SetWidth(_stack[hsbw], 0, _stack[hsbw + 1]);
                        if (_metricsOnly)
                        {
                            return Outcome.Finished;
                        }
                    }

                    break;

                case Op.Sbw:
                    if (Arguments(4, out int sbw))
                    {
                        SetWidth(_stack[sbw], _stack[sbw + 1], _stack[sbw + 2]);
                        if (_metricsOnly)
                        {
                            return Outcome.Finished;
                        }
                    }

                    break;

                case Op.Seac:
                    if (Arguments(5, out int seac))
                    {
                        return Seac(_stack[seac], _stack[seac + 1], _stack[seac + 2], _stack[seac + 3], _stack[seac + 4]);
                    }

                    break;

                case Op.Div:
                    if (_count < 2)
                    {
                        Report(DiagnosticCodes.FontType1StackUnderflow, DiagnosticSeverity.Warning, "div has fewer than two operands; 0 is used");
                        _count = 0;
                        _stack[_count++] = 0;
                        break;
                    }

                    double divisor = _stack[--_count];
                    double dividend = _stack[_count - 1];
                    double quotient = divisor == 0 ? 0 : dividend / divisor;
                    if (divisor == 0 || !double.IsFinite(quotient))
                    {
                        Report(DiagnosticCodes.FontType1DivideByZero, DiagnosticSeverity.Warning, "div divides by zero or overflows; 0 is used");
                        quotient = 0;
                    }

                    _stack[_count - 1] = quotient;

                    break;

                case Op.CallOtherSubr:
                    CallOtherSubr();
                    break;

                case Op.Pop:
                    if (_resultRead < _resultCount)
                    {
                        if (!Push(_results[_resultRead++]))
                        {
                            return Outcome.Failed;
                        }
                    }
                    else
                    {
                        Report(DiagnosticCodes.FontType1StackUnderflow, DiagnosticSeverity.Warning, "pop has no OtherSubr result to take; 0 is used");
                        if (!Push(0))
                        {
                            return Outcome.Failed;
                        }
                    }

                    break;

                default:
                    if (_metricsOnly)
                    {
                        // Metrics come from the first command; anything else before it means there are none.
                        Report(DiagnosticCodes.FontType1NoWidth, DiagnosticSeverity.Warning, "a charstring does not start with hsbw or sbw (Type 1 Font Format §6.4); its width is 0");
                        return Outcome.Failed;
                    }

                    if (!PathCommand(op))
                    {
                        return Outcome.Failed;
                    }

                    break;
            }
        }
    }

    /// <summary>The commands that draw, move and hint (Type 1 Font Format §6.4-6.5).</summary>
    private bool PathCommand(int op)
    {
        switch (op)
        {
            case Op.RMoveTo when Arguments(2, out int at):
                return MoveBy(_stack[at], _stack[at + 1]);
            case Op.HMoveTo when Arguments(1, out int at):
                return MoveBy(_stack[at], 0);
            case Op.VMoveTo when Arguments(1, out int at):
                return MoveBy(0, _stack[at]);
            case Op.RLineTo when Arguments(2, out int at):
                return LineTo(_x + _stack[at], _y + _stack[at + 1]);
            case Op.HLineTo when Arguments(1, out int at):
                return LineTo(_x + _stack[at], _y);
            case Op.VLineTo when Arguments(1, out int at):
                return LineTo(_x, _y + _stack[at]);
            case Op.RRCurveTo when Arguments(6, out int at):
                return CurveBy(_stack[at], _stack[at + 1], _stack[at + 2], _stack[at + 3], _stack[at + 4], _stack[at + 5]);
            case Op.VHCurveTo when Arguments(4, out int at):
                return CurveBy(0, _stack[at], _stack[at + 1], _stack[at + 2], _stack[at + 3], 0);
            case Op.HVCurveTo when Arguments(4, out int at):
                return CurveBy(_stack[at], 0, _stack[at + 1], _stack[at + 2], 0, _stack[at + 3]);
            case Op.ClosePath:
                _count = 0;
                if (_open)
                {
                    _outline!.Close();
                    _open = false;
                }

                return true;
            case Op.SetCurrentPoint when Arguments(2, out int at):
                _x = _stack[at];
                _y = _stack[at + 1];
                _inFlex = false;
                return true;
            case Op.HStem or Op.VStem or Op.DotSection or Op.VStem3 or Op.HStem3:
                _count = 0;
                return true;
            case Op.RMoveTo or Op.HMoveTo or Op.VMoveTo or Op.RLineTo or Op.HLineTo or Op.VLineTo or Op.RRCurveTo or Op.VHCurveTo
                or Op.HVCurveTo or Op.SetCurrentPoint:
                return true; // too few operands: reported and the stack cleared by Arguments
            default:
                _count = 0;
                Report(DiagnosticCodes.FontType1UnknownOperator, DiagnosticSeverity.Warning, "a charstring has an unknown command; it is skipped and the stack cleared");
                return true;
        }
    }

    /// <summary>OtherSubrs (Type 1 Font Format §8.2, TN 5015 §2.3 and §3.13): <c>arg1 … argn n othersubr# callothersubr</c>.</summary>
    private void CallOtherSubr()
    {
        if (_count < 2)
        {
            Report(DiagnosticCodes.FontType1StackUnderflow, DiagnosticSeverity.Warning, "callothersubr has fewer than two operands; it is skipped");
            _count = 0;
            return;
        }

        double number = _stack[--_count];
        double declared = _stack[--_count];
        int n = declared >= 0 && declared <= _count ? (int)declared : -1;
        if (n < 0)
        {
            Report(DiagnosticCodes.FontType1StackUnderflow, DiagnosticSeverity.Warning, "callothersubr declares more arguments than the stack holds; it takes those present");
            n = _count;
        }

        int first = _count - n;
        _count = first;
        ReadOnlySpan<double> arguments = ((Span<double>)_stack).Slice(first, n);
        switch (number)
        {
            case 0:
                EndFlex(arguments);
                break;
            case 1:
                _inFlex = true;
                _flexCount = 0;
                _flexStart = new PathPoint(_x, _y);
                break;
            case 2:
                if (!_inFlex || _flexCount == FlexBuffer.Size)
                {
                    Report(DiagnosticCodes.FontType1FlexMalformed, DiagnosticSeverity.Warning, "a flex point is recorded outside a flex or after the seventh; it is ignored");
                }
                else
                {
                    _flex[_flexCount++] = new PathPoint(_x, _y);
                }

                break;
            case 3:
                // Hint replacement: the result is the subroutine number, which the following pop and callsubr run (hints only).
                if (n > 0)
                {
                    SetResults(arguments[..1]);
                }
                else
                {
                    Span<double> unsupported = [3];
                    SetResults(unsupported);
                }

                break;
            case 12 or 13:
                break; // counter control data: hints only
            case >= 14 and <= 18:
                Blend((int)number, arguments);
                break;
            default:
                // An OtherSubr this reader does not implement: pop returns its arguments in order (§8.2).
                SetResults(arguments);
                break;
        }
    }

    /// <summary>OtherSubr 0 (§8.3): draws the two flex curves from the seven recorded points and returns the end point for <c>setcurrentpoint</c>.</summary>
    private void EndFlex(scoped ReadOnlySpan<double> arguments)
    {
        PathPoint endPoint = arguments.Length >= 3 ? new PathPoint(arguments[1], arguments[2]) : new PathPoint(_x, _y);
        if (_inFlex && _flexCount == FlexBuffer.Size)
        {
            // flex[0] is the reference point, never on the outline.
            _x = _flexStart.X;
            _y = _flexStart.Y;
            if (!CurveTo(_flex[1], _flex[2], _flex[3]) || !CurveTo(_flex[4], _flex[5], _flex[6]))
            {
                _inFlex = false;
                return;
            }

            endPoint = _flex[6];
        }
        else
        {
            Report(DiagnosticCodes.FontType1FlexMalformed, DiagnosticSeverity.Warning, "a flex does not record seven points between OtherSubrs 1 and 0 (Type 1 Font Format §8.3); it is dropped");
        }

        _inFlex = false;
        _x = endPoint.X;
        _y = endPoint.Y;
        Span<double> point = [endPoint.X, endPoint.Y];
        SetResults(point);
    }

    /// <summary>
    /// TN 5015 §3.13: OtherSubrs 14-18 blend 1, 2, 3, 4 or 6 values of k masters: <c>v_j = a_j + Σ δ_j,i × w_i</c> for i = 2 … k,
    /// the deltas grouped by value, with the program's <c>/WeightVector</c>.
    /// </summary>
    private void Blend(int number, scoped ReadOnlySpan<double> arguments)
    {
        int values = number == 18 ? 6 : number - 13;
        double[]? weights = _program.WeightVector;
        Span<double> results = stackalloc double[6];
        if (weights is null || arguments.Length != values * weights.Length)
        {
            Report(DiagnosticCodes.FontType1BlendUnavailable, DiagnosticSeverity.Warning, "a multiple master blend has no matching WeightVector (TN 5015 §3.13); the first master's values are used");
            int present = Math.Min(values, arguments.Length);
            arguments[..present].CopyTo(results);
            SetResults(results[..present]);
            return;
        }

        int delta = values;
        for (int value = 0; value < values; value++)
        {
            double result = arguments[value];
            for (int master = 1; master < weights.Length; master++)
            {
                result += arguments[delta++] * weights[master];
            }

            results[value] = double.IsFinite(result) ? result : 0;
        }

        SetResults(results[..values]);
    }

    /// <summary>
    /// <c>seac</c> (§6.4, TN 5015 §6): the base character at the origin, then the accent moved so that its left side bearing point
    /// lands at the composite's plus (adx, ady): offset (sbx + adx − asb, ady). Components are named by StandardEncoding codes.
    /// </summary>
    private Outcome Seac(double asb, double adx, double ady, double baseCode, double accentCode)
    {
        if (_inSeac)
        {
            Report(DiagnosticCodes.FontType1SeacNested, DiagnosticSeverity.Warning, "a seac component is itself a seac character; it is not drawn");
            return Outcome.Finished;
        }

        CloseContour();
        int baseGlyph = baseCode == Math.Floor(baseCode) ? _program.StandardGlyph((int)Math.Clamp(baseCode, -1, 256)) : -1;
        int accentGlyph = accentCode == Math.Floor(accentCode) ? _program.StandardGlyph((int)Math.Clamp(accentCode, -1, 256)) : -1;
        if (baseGlyph < 0 || accentGlyph < 0)
        {
            Report(DiagnosticCodes.FontType1SeacMissingComponent, DiagnosticSeverity.Warning, "a seac component is not a StandardEncoding code of a glyph the program has; it is not drawn");
        }

        double accentX = _sideBearingX + adx - asb;
        _inSeac = true;
        if (baseGlyph >= 0 && RunComponent(baseGlyph, 0, 0) == Outcome.Failed)
        {
            return Outcome.Failed;
        }

        if (accentGlyph >= 0 && RunComponent(accentGlyph, accentX, ady) == Outcome.Failed)
        {
            return Outcome.Failed;
        }

        return Outcome.Finished;
    }

    private Outcome RunComponent(int glyph, double originX, double originY)
    {
        _count = 0;
        _resultCount = 0;
        _resultRead = 0;
        _inFlex = false;
        _x = 0;
        _y = 0;
        _originX = originX;
        _originY = originY;
        _haveWidth = false;
        return Execute(_program.Glyphs[glyph]);
    }

    private void SetWidth(double sbx, double sby, double wx)
    {
        if (!_inSeac)
        {
            _sideBearingX = sbx;
            Metrics = new GlyphMetrics(wx, sbx);
        }

        _haveWidth = true;
        _x = sbx;
        _y = sby;
    }

    private Outcome Finish()
    {
        CloseContour();
        return Outcome.Finished;
    }

    private readonly Outcome Fail(string code, string problem)
    {
        Report(code, DiagnosticSeverity.Error, problem);
        return Outcome.Failed;
    }

    private bool Push(double value)
    {
        if (_count == StackBuffer.Size)
        {
            Report(DiagnosticCodes.FontType1StackOverflow, DiagnosticSeverity.Error, "a charstring pushes more than 48 operands; the glyph is dropped");
            return false;
        }

        _stack[_count++] = value;
        return true;
    }

    /// <summary>Takes a command's operands from the top of the stack and clears it; reports and clears when there are too few.</summary>
    private bool Arguments(int needed, out int at)
    {
        at = _count - needed;
        _count = 0;
        if (at < 0)
        {
            Report(DiagnosticCodes.FontType1StackUnderflow, DiagnosticSeverity.Warning, "a command has too few operands; it is skipped");
            return false;
        }

        return true;
    }

    private void SetResults(scoped ReadOnlySpan<double> results)
    {
        int count = Math.Min(results.Length, StackBuffer.Size);
        results[..count].CopyTo((Span<double>)_results);
        _resultCount = count;
        _resultRead = 0;
    }

    private bool MoveBy(double dx, double dy)
    {
        if (!_inFlex)
        {
            CloseContour();
        }

        _x += dx;
        _y += dy;
        return true;
    }

    private bool LineTo(double x, double y)
    {
        if (!StartContour(1, double.IsFinite(x) && double.IsFinite(y)))
        {
            return false;
        }

        _x = x;
        _y = y;
        _outline!.LineTo(_originX + x, _originY + y);
        return true;
    }

    private bool CurveBy(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
    {
        var c1 = new PathPoint(_x + dx1, _y + dy1);
        var c2 = new PathPoint(c1.X + dx2, c1.Y + dy2);
        return CurveTo(c1, c2, new PathPoint(c2.X + dx3, c2.Y + dy3));
    }

    private bool CurveTo(PathPoint c1, PathPoint c2, PathPoint end)
    {
        if (!StartContour(3, double.IsFinite(c1.X + c1.Y + c2.X + c2.Y + end.X + end.Y)))
        {
            return false;
        }

        _x = end.X;
        _y = end.Y;
        _outline!.CubicTo(_originX + c1.X, _originY + c1.Y, _originX + c2.X, _originY + c2.Y, _originX + end.X, _originY + end.Y);
        return true;
    }

    /// <summary>Opens a contour at the current point when none is open, and counts the points a segment adds.</summary>
    private bool StartContour(int points, bool finite)
    {
        if (!finite || !double.IsFinite(_x) || !double.IsFinite(_y))
        {
            Report(DiagnosticCodes.FontType1GlyphTooComplex, DiagnosticSeverity.Error, "a glyph's coordinates overflow; it is dropped");
            return false;
        }

        if (!_haveWidth)
        {
            Report(DiagnosticCodes.FontType1NoWidth, DiagnosticSeverity.Warning, "a charstring draws before hsbw or sbw (Type 1 Font Format §6.4); its side bearing is 0");
            _haveWidth = true;
        }

        _points += points + (_open ? 0 : 1);
        if (_points > _maxPoints)
        {
            Report(DiagnosticCodes.FontType1GlyphTooComplex, DiagnosticSeverity.Error, "a glyph has more points than the limit; it is dropped");
            return false;
        }

        if (!_open)
        {
            _outline!.MoveTo(_originX + _x, _originY + _y);
            _open = true;
        }

        return true;
    }

    private void CloseContour()
    {
        if (_open)
        {
            _outline!.Close();
            _open = false;
        }
    }

    private readonly void Report(string code, DiagnosticSeverity severity, string problem) =>
        _program.Context.Report(code, severity, $"Type 1 charstring: {problem}.");

    /// <summary>Command codes; escaped commands are 32 + their second byte.</summary>
    private static class Op
    {
        public const int HStem = 1;
        public const int VStem = 3;
        public const int VMoveTo = 4;
        public const int RLineTo = 5;
        public const int HLineTo = 6;
        public const int VLineTo = 7;
        public const int RRCurveTo = 8;
        public const int ClosePath = 9;
        public const int CallSubr = 10;
        public const int Return = 11;
        public const int Hsbw = 13;
        public const int Endchar = 14;
        public const int RMoveTo = 21;
        public const int HMoveTo = 22;
        public const int VHCurveTo = 30;
        public const int HVCurveTo = 31;
        public const int DotSection = 32 + 0;
        public const int VStem3 = 32 + 1;
        public const int HStem3 = 32 + 2;
        public const int Seac = 32 + 6;
        public const int Sbw = 32 + 7;
        public const int Div = 32 + 12;
        public const int CallOtherSubr = 32 + 16;
        public const int Pop = 32 + 17;
        public const int SetCurrentPoint = 32 + 33;
    }

    private readonly record struct Frame(int End, int Position);

    [System.Runtime.CompilerServices.InlineArray(Size)]
    private struct StackBuffer
    {
        public const int Size = StackSize;
        private double _element;
    }

    [System.Runtime.CompilerServices.InlineArray(MaxDepth)]
    private struct FrameBuffer
    {
        private Frame _element;
    }

    [System.Runtime.CompilerServices.InlineArray(Size)]
    private struct FlexBuffer
    {
        public const int Size = 7;
        private PathPoint _element;
    }
}
