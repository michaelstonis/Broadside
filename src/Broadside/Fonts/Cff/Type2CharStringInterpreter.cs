using System.Buffers.Binary;
using Broadside.Fonts.CharStrings;

namespace Broadside.Fonts.Cff;

/// <summary>
/// Interprets Type 2 charstrings into cubic outlines: numbers, path construction, hints (parsed for their count and skipped),
/// subroutines with bias, flex, arithmetic and storage, and the accented characters of <c>endchar</c>. All state is on the stack,
/// so interpreting a glyph allocates nothing.
/// </summary>
/// <remarks>
/// Adobe Technical Note #5177 (Type 2 Charstring Format): §3 (encoding, grammar, width rule), §4.1 path, §4.2 endchar, §4.3 hints,
/// §4.4 arithmetic, §4.5 storage, §4.6 conditional, §4.7 subroutines, Appendix B limits, Appendix C endchar seac. Hostile programs
/// are bounded by the 48-entry stack, the 10-level subroutine nesting and an operator budget per glyph.
/// </remarks>
internal ref struct Type2CharStringInterpreter
{
    private readonly CffFont _font;
    private readonly CharStringReporter _reporter;
    private readonly int _glyphId;
    private readonly int _budget;
    private readonly bool _measure;
    private readonly bool _component;
    private readonly ReadOnlySpan<byte> _data;
    private readonly CffPrivate _private;
    private CharStringStack _stack;
    private CharStringTransientArray _transient;
    private CharStringPath _path;
    private int _count;
    private int _stems;
    private bool _haveWidth;
    private double _width;
    private int _depth;
    private uint _random;
    private bool _overflow;

    private Type2CharStringInterpreter(CffFont font, CharStringReporter reporter, int glyphId, GlyphOutline? outline, int budget, bool component, double offsetX, double offsetY)
    {
        _font = font;
        _reporter = reporter;
        _glyphId = glyphId;
        _budget = budget;
        _measure = outline is null;
        _component = component;
        _data = font.Data.Span;
        _private = font.GetPrivate(glyphId);
        _path = new CharStringPath(outline, offsetX, offsetY);
        _width = _private.DefaultWidthX;
        uint seed = unchecked((uint)_private.RandomSeed ^ ((uint)glyphId * 2654435761u));
        _random = seed == 0 ? 0x9E3779B9u : seed;
    }

    private enum Result
    {
        /// <summary>The charstring ran out (no endchar or return).</summary>
        Ended,

        /// <summary>A subroutine returned.</summary>
        Return,

        /// <summary>endchar (or, measuring, the width is known).</summary>
        End,

        /// <summary>The glyph is dropped.</summary>
        Fail,
    }

    /// <summary>Interprets a glyph's charstring into an outline (or, with no outline, only up to its width).</summary>
    /// <param name="font">The font.</param>
    /// <param name="glyphId">The glyph id, in range.</param>
    /// <param name="outline">The outline to fill (already cleared), or <see langword="null"/> to measure the width only.</param>
    /// <param name="reporter">Where issues go.</param>
    /// <param name="budget">The operator budget.</param>
    /// <param name="width">The glyph's advance width (nominalWidthX plus the width argument, or defaultWidthX).</param>
    /// <returns><see langword="false"/> when the glyph is dropped.</returns>
    public static bool Interpret(CffFont font, int glyphId, GlyphOutline? outline, CharStringReporter reporter, int budget, out double width)
    {
        var interpreter = new Type2CharStringInterpreter(font, reporter, glyphId, outline, budget, component: false, 0, 0);
        bool success = interpreter.RunGlyph(ref budget);
        width = interpreter._width;
        return success;
    }

    private bool RunGlyph(ref int budget)
    {
        ReadOnlySpan<byte> charString = _font.CharStrings.Get(_data, _glyphId);
        int operations = 0;
        Result result = Run(charString, ref operations);
        budget -= operations;
        if (result == Result.Fail)
        {
            return false;
        }

        if (result != Result.End)
        {
            Report(CharStringIssue.NoEndchar);
        }

        _path.Close();
        if (_path.MovetoMissing)
        {
            Report(CharStringIssue.MovetoMissing);
        }

        return true;
    }

    private Result Run(ReadOnlySpan<byte> code, ref int operations)
    {
        int position = 0;
        while (position < code.Length)
        {
            if (_measure && _haveWidth)
            {
                return Result.End;
            }

            if (++operations > _budget)
            {
                Report(CharStringIssue.BudgetExceeded);
                return Result.Fail;
            }

            int b0 = code[position++];
            if (b0 >= 32 || b0 == 28)
            {
                double value;
                if (b0 <= 246)
                {
                    if (b0 == 28)
                    {
                        if (position + 2 > code.Length)
                        {
                            return Truncated();
                        }

                        value = BinaryPrimitives.ReadInt16BigEndian(code[position..]);
                        position += 2;
                    }
                    else
                    {
                        value = b0 - 139;
                    }
                }
                else if (b0 <= 254)
                {
                    if (position >= code.Length)
                    {
                        return Truncated();
                    }

                    int b1 = code[position++];
                    value = b0 <= 250 ? ((b0 - 247) * 256) + b1 + 108 : (-(b0 - 251) * 256) - b1 - 108;
                }
                else
                {
                    if (position + 4 > code.Length)
                    {
                        return Truncated();
                    }

                    // 5177 Table 1: 255 is a 16.16 fixed-point number.
                    value = BinaryPrimitives.ReadInt32BigEndian(code[position..]) / 65536.0;
                    position += 4;
                }

                if (!Push(value))
                {
                    return Result.Fail;
                }

                continue;
            }

            switch (b0)
            {
                case 1: // hstem
                case 3: // vstem
                case 18: // hstemhm
                case 23: // vstemhm
                    Stems(TakeWidth(_count % 2 == 1));
                    break;
                case 19: // hintmask
                case 20: // cntrmask
                    // 5177 §4.3: pending arguments are an implicit vstem; the mask has one bit per stem, rounded up to bytes.
                    Stems(TakeWidth(_count % 2 == 1));
                    position += (_stems + 7) >> 3;
                    if (position > code.Length)
                    {
                        return Truncated();
                    }

                    break;
                case 21: // rmoveto
                    MoveTo(TakeWidth(_count > 2), 2);
                    break;
                case 22: // hmoveto
                    MoveTo(TakeWidth(_count > 1), 1);
                    break;
                case 4: // vmoveto
                    MoveTo(TakeWidth(_count > 1), -1);
                    break;
                case 5:
                    LineTo();
                    break;
                case 6:
                    AlternatingLineTo(horizontal: true);
                    break;
                case 7:
                    AlternatingLineTo(horizontal: false);
                    break;
                case 8:
                    CurveTo(0, _count);
                    Clear();
                    break;
                case 24:
                    CurveLine();
                    break;
                case 25:
                    LineCurve();
                    break;
                case 26:
                    VerticalCurves();
                    break;
                case 27:
                    HorizontalCurves();
                    break;
                case 30:
                    AlternatingCurves(horizontal: false);
                    break;
                case 31:
                    AlternatingCurves(horizontal: true);
                    break;
                case 10: // callsubr
                case 29: // callgsubr
                    {
                        Result called = Call(b0 == 10, ref operations);
                        if (called != Result.Return)
                        {
                            return called;
                        }

                        break;
                    }

                case 11: // return
                    return Result.Return;
                case 14:
                    return EndChar(ref operations);
                case 12:
                    if (position >= code.Length)
                    {
                        return Truncated();
                    }

                    Escape(code[position++]);
                    if (_overflow)
                    {
                        return Result.Fail;
                    }

                    break;
                default: // 0, 2, 9, 13, 15, 16, 17: reserved
                    Report(CharStringIssue.UnknownOperator);
                    Clear();
                    break;
            }
        }

        return Result.Ended;
    }

    /// <summary>
    /// 5177 §3.1 and Note 4 (p.16): the first stack-clearing operator may carry the advance width as an extra first argument; the
    /// width is nominalWidthX plus it, or defaultWidthX without it. Returns the index of the first real argument.
    /// </summary>
    private int TakeWidth(bool extra)
    {
        if (_haveWidth)
        {
            return 0;
        }

        _haveWidth = true;
        if (extra && _count > 0)
        {
            _width = _private.NominalWidthX + _stack[0];
            return 1;
        }

        return 0;
    }

    private void Stems(int first)
    {
        int arguments = _count - first;
        if (arguments % 2 != 0)
        {
            Report(CharStringIssue.ArgumentCount);
        }

        _stems += arguments / 2;
        Clear();
    }

    private void MoveTo(int first, int kind)
    {
        int needed = kind == 2 ? 2 : 1;
        if (_count - first != needed)
        {
            Report(CharStringIssue.ArgumentCount);
        }

        if (_count - first >= needed)
        {
            double a = _stack[first];
            switch (kind)
            {
                case 2:
                    _path.MoveTo(a, _stack[first + 1]);
                    break;
                case 1:
                    _path.MoveTo(a, 0);
                    break;
                default:
                    _path.MoveTo(0, a);
                    break;
            }
        }

        Clear();
    }

    private void LineTo()
    {
        TakeWidth(false);
        int index = 0;
        for (; index + 2 <= _count; index += 2)
        {
            _path.LineTo(_stack[index], _stack[index + 1]);
        }

        Finish(index);
    }

    private void AlternatingLineTo(bool horizontal)
    {
        TakeWidth(false);
        for (int index = 0; index < _count; index++, horizontal = !horizontal)
        {
            if (horizontal)
            {
                _path.LineTo(_stack[index], 0);
            }
            else
            {
                _path.LineTo(0, _stack[index]);
            }
        }

        Finish(_count == 0 ? -1 : _count);
    }

    /// <summary>rrcurveto sextets from <paramref name="first"/> up to <paramref name="end"/>; returns where they stopped.</summary>
    private int CurveTo(int first, int end)
    {
        TakeWidth(false);
        int index = first;
        for (; index + 6 <= end; index += 6)
        {
            Curve(index);
        }

        if (index != end || end == first)
        {
            Report(CharStringIssue.ArgumentCount);
        }

        return index;
    }

    private void Curve(int index) =>
        _path.CurveTo(_stack[index], _stack[index + 1], _stack[index + 2], _stack[index + 3], _stack[index + 4], _stack[index + 5]);

    /// <summary>rcurveline: curves, then one line from the last two arguments.</summary>
    private void CurveLine()
    {
        TakeWidth(false);
        int curves = Math.Max(0, (_count - 2) / 6);
        int index = 0;
        for (int curve = 0; curve < curves; curve++, index += 6)
        {
            Curve(index);
        }

        if (index + 2 <= _count)
        {
            _path.LineTo(_stack[index], _stack[index + 1]);
            index += 2;
        }

        Finish(curves == 0 ? -1 : index);
    }

    /// <summary>rlinecurve: lines, then one curve from the last six arguments.</summary>
    private void LineCurve()
    {
        TakeWidth(false);
        int lines = Math.Max(0, (_count - 6) / 2);
        int index = 0;
        for (int line = 0; line < lines; line++, index += 2)
        {
            _path.LineTo(_stack[index], _stack[index + 1]);
        }

        if (index + 6 <= _count)
        {
            Curve(index);
            index += 6;
        }

        Finish(lines == 0 ? -1 : index);
    }

    /// <summary>hhcurveto: <c>dy1? {dxa dxb dyb dxc}+</c>, curves starting and ending horizontal.</summary>
    private void HorizontalCurves()
    {
        TakeWidth(false);
        int index = _count % 2;
        double dy1 = index == 1 ? _stack[0] : 0;
        for (; index + 4 <= _count; index += 4)
        {
            _path.CurveTo(_stack[index], dy1, _stack[index + 1], _stack[index + 2], _stack[index + 3], 0);
            dy1 = 0;
        }

        Finish(_count < 4 ? -1 : index);
    }

    /// <summary>vvcurveto: <c>dx1? {dya dxb dyb dyc}+</c>, curves starting and ending vertical.</summary>
    private void VerticalCurves()
    {
        TakeWidth(false);
        int index = _count % 2;
        double dx1 = index == 1 ? _stack[0] : 0;
        for (; index + 4 <= _count; index += 4)
        {
            _path.CurveTo(dx1, _stack[index], _stack[index + 1], _stack[index + 2], 0, _stack[index + 3]);
            dx1 = 0;
        }

        Finish(_count < 4 ? -1 : index);
    }

    /// <summary>
    /// hvcurveto and vhcurveto: groups of four, alternately starting horizontal and ending vertical or the reverse; a fifth
    /// argument after the last group moves its end point along the free axis.
    /// </summary>
    private void AlternatingCurves(bool horizontal)
    {
        TakeWidth(false);
        int index = 0;
        while (index + 4 <= _count)
        {
            bool last = _count - index == 5;
            double extra = last ? _stack[index + 4] : 0;
            if (horizontal)
            {
                _path.CurveTo(_stack[index], 0, _stack[index + 1], _stack[index + 2], extra, _stack[index + 3]);
            }
            else
            {
                _path.CurveTo(0, _stack[index], _stack[index + 1], _stack[index + 2], _stack[index + 3], extra);
            }

            index += last ? 5 : 4;
            horizontal = !horizontal;
        }

        Finish(_count < 4 ? -1 : index);
    }

    /// <summary>Clears the stack after a path operator; reports arguments it did not use (<paramref name="used"/> −1: none were usable).</summary>
    private void Finish(int used)
    {
        if (used != _count)
        {
            Report(CharStringIssue.ArgumentCount);
        }

        Clear();
    }

    /// <summary>callsubr / callgsubr (5177 §4.7): the operand plus the bias numbers the subroutine.</summary>
    private Result Call(bool local, ref int operations)
    {
        if (_count == 0)
        {
            Report(CharStringIssue.ArgumentCount);
            return Result.Return;
        }

        CffIndex subrs = local ? _private.Subrs : _font.GlobalSubrs;
        double number = _stack[--_count] + (local ? _private.SubrBias : _font.GlobalSubrBias);
        if (!(number >= 0 && number < subrs.Count))
        {
            Report(CharStringIssue.SubroutineOutOfRange);
            return Result.Return;
        }

        if (_depth >= CharStringLimits.MaxSubroutineDepth)
        {
            Report(CharStringIssue.SubroutineDepth);
            return Result.Fail;
        }

        _depth++;
        Result result = Run(subrs.Get(_data, (int)number), ref operations);
        _depth--;
        if (result == Result.Ended)
        {
            Report(CharStringIssue.NoEndchar);
            return Result.Return;
        }

        return result;
    }

    /// <summary>endchar (5177 §4.2), with the four arguments <c>adx ady bchar achar</c> of an accented character (Appendix C).</summary>
    private Result EndChar(ref int operations)
    {
        int first = TakeWidth(_count is 1 or 5);
        int arguments = _count - first;
        _path.Close();
        if (_measure)
        {
            return Result.End;
        }

        if (arguments == 4)
        {
            if (_component)
            {
                Report(CharStringIssue.SeacComponentMissing);
            }
            else
            {
                double adx = _stack[first];
                double ady = _stack[first + 1];
                int baseCode = (int)_stack[first + 2];
                int accentCode = (int)_stack[first + 3];
                if (!Component(baseCode, 0, 0, ref operations) || !Component(accentCode, adx, ady, ref operations))
                {
                    return Result.Fail;
                }
            }
        }
        else if (arguments != 0)
        {
            Report(CharStringIssue.ArgumentCount);
        }

        Clear();
        return Result.End;
    }

    /// <summary>Draws a component of an accented character, with fresh state, named by its StandardEncoding code (5177 Appendix C).</summary>
    private readonly bool Component(int code, double offsetX, double offsetY, ref int operations)
    {
        int glyph = _font.GetStandardCodeGlyph(code);
        if (glyph <= 0)
        {
            Report(CharStringIssue.SeacComponentMissing);
            return true;
        }

        int remaining = _budget - operations;
        var component = new Type2CharStringInterpreter(_font, _reporter, glyph, _path.Outline, remaining, component: true, _path.OffsetX + offsetX, _path.OffsetY + offsetY);
        bool success = component.RunGlyph(ref remaining);
        operations = _budget - remaining;
        return success;
    }

    /// <summary>The two-byte operators <c>12 x</c>: dotsection, flex, arithmetic, storage and conditional operators.</summary>
    private void Escape(int operation)
    {
        switch (operation)
        {
            case 0: // dotsection: a deprecated hint, no effect.
                Clear();
                break;
            case 34:
                Flex(7);
                break;
            case 35:
                Flex(13);
                break;
            case 36:
                Flex(9);
                break;
            case 37:
                Flex(11);
                break;
            case 3: // and
                Binary(static (a, b) => a != 0 && b != 0 ? 1 : 0);
                break;
            case 4: // or
                Binary(static (a, b) => a != 0 || b != 0 ? 1 : 0);
                break;
            case 5: // not
                Unary(static a => a == 0 ? 1 : 0);
                break;
            case 9: // abs
                Unary(static a => Math.Abs(a));
                break;
            case 10: // add
                Binary(static (a, b) => a + b);
                break;
            case 11: // sub
                Binary(static (a, b) => a - b);
                break;
            case 12: // div
                {
                    double divisor = Pop();
                    double dividend = Pop();
                    if (divisor == 0)
                    {
                        Report(CharStringIssue.OperandInvalid);
                        Push(0);
                    }
                    else
                    {
                        Push(dividend / divisor);
                    }

                    break;
                }

            case 14: // neg
                Unary(static a => -a);
                break;
            case 15: // eq
                Binary(static (a, b) => a == b ? 1 : 0);
                break;
            case 18: // drop
                Pop();
                break;
            case 20: // put
                {
                    double index = Pop();
                    double value = Pop();
                    if (index >= 0 && index < CharStringLimits.TransientArrayLength)
                    {
                        _transient[(int)index] = value;
                    }
                    else
                    {
                        Report(CharStringIssue.OperandInvalid);
                    }

                    break;
                }

            case 21: // get
                {
                    double index = Pop();
                    if (index >= 0 && index < CharStringLimits.TransientArrayLength)
                    {
                        Push(_transient[(int)index]);
                    }
                    else
                    {
                        Report(CharStringIssue.OperandInvalid);
                        Push(0);
                    }

                    break;
                }

            case 22: // ifelse: s1 s2 v1 v2 -> v1 <= v2 ? s1 : s2
                {
                    double v2 = Pop();
                    double v1 = Pop();
                    double s2 = Pop();
                    double s1 = Pop();
                    Push(v1 <= v2 ? s1 : s2);
                    break;
                }

            case 23: // random: (0, 1], reproducible per glyph
                _random ^= _random << 13;
                _random ^= _random >> 17;
                _random ^= _random << 5;
                Push(((_random >> 8) + 1) / 16777216.0);
                break;
            case 24: // mul
                Binary(static (a, b) => a * b);
                break;
            case 26: // sqrt
                {
                    double value = Pop();
                    if (value < 0)
                    {
                        Report(CharStringIssue.OperandInvalid);
                        value = 0;
                    }

                    Push(Math.Sqrt(value));
                    break;
                }

            case 27: // dup
                {
                    double value = Pop();
                    if (Push(value))
                    {
                        Push(value);
                    }

                    break;
                }

            case 28: // exch
                {
                    double b = Pop();
                    double a = Pop();
                    Push(b);
                    Push(a);
                    break;
                }

            case 29: // index: i < 0 copies the top element
                {
                    double index = Pop();
                    if (index < 0)
                    {
                        index = 0;
                    }

                    if (_count == 0 || index >= _count)
                    {
                        Report(CharStringIssue.OperandInvalid);
                        Push(0);
                    }
                    else
                    {
                        Push(_stack[_count - 1 - (int)index]);
                    }

                    break;
                }

            case 30: // roll: N J roll shifts the top N elements by J, positive J toward the top
                Roll();
                break;
            default:
                Report(CharStringIssue.UnknownOperator);
                Clear();
                break;
        }
    }

    /// <summary>flex (13 arguments), hflex (7), hflex1 (9), flex1 (11): two curves; 5177 §4.1 (p.18-20).</summary>
    private void Flex(int needed)
    {
        TakeWidth(false);
        if (_count < needed)
        {
            Report(CharStringIssue.ArgumentCount);
            Clear();
            return;
        }

        if (_count > needed)
        {
            Report(CharStringIssue.ArgumentCount);
        }

        Span<double> s = _stack;
        switch (needed)
        {
            case 13:
                Curve(0);
                Curve(6);
                break;
            case 7:
                _path.CurveTo(s[0], 0, s[1], s[2], s[3], 0);
                _path.CurveTo(s[4], 0, s[5], -s[2], s[6], 0);
                break;
            case 9:
                _path.CurveTo(s[0], s[1], s[2], s[3], s[4], 0);
                _path.CurveTo(s[5], 0, s[6], s[7], s[8], -(s[1] + s[3] + s[7]));
                break;
            default:
                double dx = s[0] + s[2] + s[4] + s[6] + s[8];
                double dy = s[1] + s[3] + s[5] + s[7] + s[9];
                _path.CurveTo(s[0], s[1], s[2], s[3], s[4], s[5]);
                if (Math.Abs(dx) > Math.Abs(dy))
                {
                    _path.CurveTo(s[6], s[7], s[8], s[9], s[10], -dy);
                }
                else
                {
                    _path.CurveTo(s[6], s[7], s[8], s[9], -dx, s[10]);
                }

                break;
        }

        Clear();
    }

    private void Roll()
    {
        double shift = Pop();
        double size = Pop();
        if (size <= 0 || size > _count || !double.IsFinite(shift))
        {
            Report(CharStringIssue.OperandInvalid);
            return;
        }

        int n = (int)size;
        int j = (int)(shift % n);
        if (j < 0)
        {
            j += n;
        }

        if (j == 0)
        {
            return;
        }

        // PostScript roll: the element at window position k moves to (k + j) mod n.
        Span<double> window = ((Span<double>)_stack).Slice(_count - n, n);
        window.Reverse();
        window[..j].Reverse();
        window[j..].Reverse();
    }

    private void Unary(Func<double, double> operation) => Push(operation(Pop()));

    private void Binary(Func<double, double, double> operation)
    {
        double b = Pop();
        double a = Pop();
        Push(operation(a, b));
    }

    private double Pop()
    {
        if (_count == 0)
        {
            Report(CharStringIssue.ArgumentCount);
            return 0;
        }

        return _stack[--_count];
    }

    private bool Push(double value)
    {
        if (_count == CharStringLimits.MaxArguments)
        {
            Report(CharStringIssue.StackOverflow);
            _overflow = true;
            return false;
        }

        _stack[_count++] = double.IsFinite(value) ? value : 0;
        return true;
    }

    private void Clear() => _count = 0;

    private readonly Result Truncated()
    {
        Report(CharStringIssue.Truncated);
        return Result.Fail;
    }

    private readonly void Report(CharStringIssue issue) => _reporter.Report(issue, _glyphId);
}
