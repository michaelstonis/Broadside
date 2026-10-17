using System.Buffers.Binary;
using Broadside.Fonts.Cff;
using Broadside.Graphics;

namespace Broadside.Fonts.Type1;

/// <summary>
/// Converts a Type 1 program to a bare, name-keyed CFF program with Type 2 charstrings (issue #152), which embeds and subsets
/// through the CFF path (<see cref="CffSubsetter"/>, <c>FontFile3</c> <c>CIDFontType0C</c>).
/// </summary>
/// <remarks>
/// <para>
/// Adobe Technical Note #5176 (CFF: header, INDEXes, Top and Private DICTs, charset format 0, standard strings) and #5177 (Type 2
/// charstrings: width, <c>rmoveto</c>, <c>rlineto</c>, <c>rrcurveto</c>, <c>endchar</c>, number encoding §3.2); Type 1 Font Format
/// chapters 6 and 8 (what each glyph draws).
/// </para>
/// <para>
/// Each charstring is translated from the glyph's interpreted outline (<see cref="Type1CharStringInterpreter"/>), so everything the
/// Type 1 interpreter resolves is resolved in the output: subroutines are inlined, <c>hsbw</c>/<c>sbw</c> become the Type 2 width
/// and an absolute first move, flex (OtherSubrs 0-2) becomes its two curves, multiple master blends (OtherSubrs 14-18) their
/// blended values, and <c>seac</c> a composed outline of the base and the moved accent (no <c>endchar</c> accent, so the subset
/// can be CID-keyed). Hints, hint replacement (OtherSubr 3), counter control and <c>dotsection</c> are not carried: they only
/// affect rasterization at small sizes, and Type 2 charstrings without stems are valid (#5177 §4.3). Coordinates are written as
/// 16.16 fixed-point numbers where they are not integers, so the converted outline equals the Type 1 outline within 1/65536 of a
/// unit. The vertical advance of <c>sbw</c> is dropped (CFF has none). A glyph the Type 1 interpreter drops (already reported)
/// converts to an empty glyph with its width.
/// </para>
/// </remarks>
internal static class Type1CffConverter
{
    /// <summary>The largest magnitude a Type 2 fixed-point operand holds.</summary>
    private const long MaxFixed = 32767L << 16;

    /// <summary>Operands per Type 2 command (#5177 Appendix B, argument stack limit).</summary>
    private const int MaxOperands = 48;

    private const int MaxCharString = 65535;

    private const byte RLineTo = 5;
    private const byte RRCurveTo = 8;
    private const byte EndChar = 14;
    private const byte RMoveTo = 21;

    private static readonly Dictionary<string, int> StandardStrings = CreateStandardStrings();

    /// <summary>Converts <paramref name="program"/> to a bare CFF program.</summary>
    /// <param name="program">The parsed Type 1 program.</param>
    /// <param name="problem">Why it cannot be converted, else <see langword="null"/>.</param>
    /// <returns>The CFF program, or <see langword="null"/> with <paramref name="problem"/>.</returns>
    public static byte[]? Convert(Type1FontProgram program, out string? problem)
    {
        problem = null;
        var charStrings = new List<byte[]>(program.GlyphCount);
        var outline = new GlyphOutline();
        var writer = new CharStringWriter();
        for (int glyph = 0; glyph < program.GlyphCount; glyph++)
        {
            outline.Clear();
            _ = program.GetOutline(glyph, outline);
            if (writer.Write(outline.Path, program.GetMetrics(glyph).AdvanceWidth) is not { } charString)
            {
                problem = $"glyph {program.GetGlyphName(glyph)} has a coordinate or width past the Type 2 range of ±32767, or a charstring longer than {MaxCharString} bytes";
                return null;
            }

            charStrings.Add(charString);
        }

        var strings = new List<byte[]>();
        var sids = new Dictionary<string, int>(StringComparer.Ordinal);
        byte[] charset = new byte[1 + (2 * (program.GlyphCount - 1))];
        for (int glyph = 1; glyph < program.GlyphCount; glyph++)
        {
            string name = program.GetGlyphName(glyph) ?? $"g{glyph}";
            if (!StandardStrings.TryGetValue(name, out int sid) && !sids.TryGetValue(name, out sid))
            {
                sid = StandardStrings.Count + strings.Count;
                sids.Add(name, sid);
                strings.Add(Ascii(name));
            }

            BinaryPrimitives.WriteUInt16BigEndian(charset.AsSpan(1 + (2 * (glyph - 1))), (ushort)sid);
        }

        var privateDict = new List<byte>();
        CffSubsetter.Int32(privateDict, 0);
        privateDict.Add(20); // defaultWidthX
        CffSubsetter.Int32(privateDict, 0);
        privateDict.Add(21); // nominalWidthX

        byte[] header = [1, 0, 4, 4];
        byte[] names = CffSubsetter.Index([Ascii(FontName(program.PostScriptName))]);
        byte[] stringIndex = CffSubsetter.Index(strings);
        byte[] globalSubrs = CffSubsetter.Index([]);
        byte[] charStringIndex = CffSubsetter.Index(charStrings);

        // The Top DICT's size does not depend on the offsets (five-byte integers), so it is laid out once with zeros.
        int topSize = CffSubsetter.Index([TopDict(program, 0, 0, 0, 0)]).Length;
        int charsetAt = header.Length + names.Length + topSize + stringIndex.Length + globalSubrs.Length;
        int charStringsAt = charsetAt + charset.Length;
        int privateAt = charStringsAt + charStringIndex.Length;
        byte[] top = CffSubsetter.Index([TopDict(program, charsetAt, charStringsAt, privateDict.Count, privateAt)]);

        var output = new byte[privateAt + privateDict.Count];
        int at = 0;
        foreach (byte[] part in (ReadOnlySpan<byte[]>)[header, names, top, stringIndex, globalSubrs, charset, charStringIndex, [.. privateDict]])
        {
            part.CopyTo(output, at);
            at += part.Length;
        }

        return output;
    }

    /// <summary>The Top DICT (#5176 §9): FontBBox, FontMatrix when not the default, charset, CharStrings and Private.</summary>
    private static byte[] TopDict(Type1FontProgram program, int charset, int charStrings, int privateSize, int privateAt)
    {
        var dict = new List<byte>();
        PdfRectangle box = program.FontBBox;
        foreach (double value in (ReadOnlySpan<double>)[box.Left, box.Bottom, box.Right, box.Top])
        {
            CffSubsetter.Number(dict, value);
        }

        dict.Add(5);
        Matrix m = program.FontMatrix;
        if (!m.Equals(new Matrix(0.001, 0, 0, 0.001, 0, 0)))
        {
            foreach (double value in (ReadOnlySpan<double>)[m.A, m.B, m.C, m.D, m.E, m.F])
            {
                CffSubsetter.Number(dict, value);
            }

            dict.Add(12);
            dict.Add(7);
        }

        CffSubsetter.Int32(dict, charset);
        dict.Add(15);
        CffSubsetter.Int32(dict, charStrings);
        dict.Add(17);
        CffSubsetter.Int32(dict, privateSize);
        CffSubsetter.Int32(dict, privateAt);
        dict.Add(18);
        return [.. dict];
    }

    /// <summary>A Name INDEX entry (#5176 §7): printable ASCII without the PostScript delimiters, at most 127 bytes.</summary>
    private static string FontName(string? name)
    {
        var text = new System.Text.StringBuilder();
        foreach (char character in name ?? string.Empty)
        {
            if (character is > ' ' and < (char)0x7F && "[](){}<>/%".IndexOf(character, StringComparison.Ordinal) < 0 && text.Length < 127)
            {
                _ = text.Append(character);
            }
        }

        return text.Length == 0 ? "Type1" : text.ToString();
    }

    private static byte[] Ascii(string text)
    {
        byte[] bytes = new byte[text.Length];
        for (int index = 0; index < text.Length; index++)
        {
            bytes[index] = text[index] < 0x80 ? (byte)text[index] : (byte)'?';
        }

        return bytes;
    }

    private static Dictionary<string, int> CreateStandardStrings()
    {
        var strings = new Dictionary<string, int>(CffStandardData.Strings.Length, StringComparer.Ordinal);
        for (int sid = 0; sid < CffStandardData.Strings.Length; sid++)
        {
            _ = strings.TryAdd(CffStandardData.Strings[sid], sid);
        }

        return strings;
    }

    /// <summary>Writes one glyph's Type 2 charstring from its outline, in 16.16 fixed-point deltas so no error accumulates.</summary>
    private sealed class CharStringWriter
    {
        private readonly List<byte> _bytes = [];
        private readonly List<long> _operands = [];
        private byte _pending;
        private long _x;
        private long _y;
        private bool _failed;

        public byte[]? Write(PathView path, double width)
        {
            _bytes.Clear();
            _operands.Clear();
            (_x, _y, _pending, _failed) = (0, 0, 0, false);
            long fixedWidth = Fixed(width);
            if (fixedWidth != 0)
            {
                _operands.Add(fixedWidth);
            }

            ReadOnlySpan<PathPoint> points = path.Points;
            int point = 0;
            foreach (PathVerb verb in path.Verbs)
            {
                switch (verb)
                {
                    case PathVerb.MoveTo:
                        Flush();
                        Delta(points[point++]);
                        Flush(RMoveTo);
                        break;
                    case PathVerb.LineTo:
                        Command(RLineTo, 2);
                        Delta(points[point++]);
                        break;
                    case PathVerb.CubicTo:
                        Command(RRCurveTo, 6);
                        Delta(points[point++]);
                        Delta(points[point++]);
                        Delta(points[point++]);
                        break;
                    case PathVerb.QuadTo:
                        point += 2; // never produced by the Type 1 interpreter
                        break;
                }
            }

            Flush();
            Flush(EndChar);
            return _failed || _bytes.Count > MaxCharString ? null : [.. _bytes];
        }

        /// <summary>Starts or continues a run of <paramref name="op"/> (several segments per command, up to 48 operands).</summary>
        private void Command(byte op, int operands)
        {
            if (_pending != op || _operands.Count + operands > MaxOperands)
            {
                Flush();
                _pending = op;
            }
        }

        private void Flush()
        {
            if (_pending != 0)
            {
                Flush(_pending);
            }
        }

        private void Flush(byte op)
        {
            foreach (long operand in _operands)
            {
                Operand(operand);
            }

            _bytes.Add(op);
            _operands.Clear();
            _pending = 0;
        }

        private void Delta(PathPoint point)
        {
            long x = Fixed(point.X);
            long y = Fixed(point.Y);
            _operands.Add(x - _x);
            _operands.Add(y - _y);
            (_x, _y) = (x, y);
        }

        private long Fixed(double value)
        {
            double scaled = Math.Round(value * 65536);
            if (!double.IsFinite(scaled) || Math.Abs(scaled) > MaxFixed)
            {
                _failed = true;
                return 0;
            }

            return (long)scaled;
        }

        /// <summary>A Type 2 operand (#5177 §3.2): the shortest integer form, else 255 and a 16.16 fixed-point number.</summary>
        private void Operand(long value)
        {
            if (value is > MaxFixed or < -MaxFixed)
            {
                _failed = true;
                return;
            }

            if ((value & 0xFFFF) != 0)
            {
                _bytes.Add(255);
                _bytes.Add((byte)(value >> 24));
                _bytes.Add((byte)(value >> 16));
                _bytes.Add((byte)(value >> 8));
                _bytes.Add((byte)value);
                return;
            }

            int number = (int)(value >> 16);
            switch (number)
            {
                case >= -107 and <= 107:
                    _bytes.Add((byte)(number + 139));
                    break;
                case >= 108 and <= 1131:
                    _bytes.Add((byte)(((number - 108) >> 8) + 247));
                    _bytes.Add((byte)(number - 108));
                    break;
                case >= -1131 and <= -108:
                    _bytes.Add((byte)(((-number - 108) >> 8) + 251));
                    _bytes.Add((byte)(-number - 108));
                    break;
                default:
                    _bytes.Add(28);
                    _bytes.Add((byte)(number >> 8));
                    _bytes.Add((byte)number);
                    break;
            }
        }
    }
}
