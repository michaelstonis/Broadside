using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Fonts.CharStrings;
using Broadside.Graphics;
using Broadside.Parsing;

namespace Broadside.Fonts.Cff;

/// <summary>The Private DICT values a charstring needs: local subroutines, their bias, the width defaults and the random seed.</summary>
/// <remarks>Adobe Technical Note #5176 §15, Table 23 (p.24-25), and §16 (subroutine bias, p.25).</remarks>
internal readonly record struct CffPrivate(CffIndex Subrs, int SubrBias, double DefaultWidthX, double NominalWidthX, int RandomSeed);

/// <summary>Which encoding the Top DICT names (5176 §12).</summary>
internal enum CffEncodingKind
{
    /// <summary>No encoding: CID-keyed fonts (5176 §18).</summary>
    None,

    /// <summary>StandardEncoding (Encoding 0).</summary>
    Standard,

    /// <summary>ExpertEncoding (Encoding 1).</summary>
    Expert,

    /// <summary>A custom encoding at an offset (formats 0 and 1, with supplements).</summary>
    Custom,
}

/// <summary>
/// The located tables of one CFF font: the INDEXes, the Top and Private DICT values, the charset and the encoding. Built once when
/// a program is parsed; glyph data is read from the program's bytes on request.
/// </summary>
/// <remarks>
/// Adobe Technical Note #5176: header, Name, Top DICT, String and Global Subr INDEXes (§6-10, §16), charsets (§13), encodings
/// (§12), CharStrings INDEX (§14) and the Private DICT (§15). ISO 32000-2 §9.9 (p.369): an embedded CFF shall hold exactly one
/// font, so the first is read. CID-keyed fonts (§18, ROS/FDArray/FDSelect) are recognized and their offsets kept; reading their
/// per-font-dictionary Private DICTs is issue #54's.
/// </remarks>
internal sealed class CffFont
{
    private const int StandardStringCount = 391;
    private const int MaxHeaderShift = 64;

    private readonly CffPrivate _private;
    private NameTables? _names;

    private CffFont(ReadOnlyMemory<byte> data, CffPrivate privateValues)
    {
        Data = data;
        _private = privateValues;
    }

    /// <summary>Gets the CFF data (the whole program, or the "CFF " table of an OpenType program).</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the font's name, the first Name INDEX entry.</summary>
    public string? Name { get; private init; }

    /// <summary>Gets the String INDEX: the strings of SIDs 391 and up.</summary>
    public CffIndex Strings { get; private init; }

    /// <summary>Gets the Global Subr INDEX.</summary>
    public CffIndex GlobalSubrs { get; private init; }

    /// <summary>Gets the bias of global subroutine numbers.</summary>
    public int GlobalSubrBias { get; private init; }

    /// <summary>Gets the CharStrings INDEX: one charstring per glyph.</summary>
    public CffIndex CharStrings { get; private init; }

    /// <summary>Gets the number of glyphs.</summary>
    public int GlyphCount => CharStrings.Count;

    /// <summary>Gets the charset: the SID (or, in a CID-keyed font, the CID) of each glyph; glyph 0 is 0.</summary>
    public ushort[] Charset { get; private init; } = [];

    /// <summary>Gets the kind of the font's encoding.</summary>
    public CffEncodingKind EncodingKind { get; private init; }

    /// <summary>Gets a custom encoding's code to glyph id table, supplements applied (0 = not encoded); empty otherwise.</summary>
    public ushort[] CodeToGlyph { get; private init; } = [];

    /// <summary>Gets the FontMatrix (default <c>[0.001 0 0 0.001 0 0]</c>).</summary>
    public Matrix FontMatrix { get; private init; }

    /// <summary>Gets the FontBBox.</summary>
    public PdfRectangle FontBBox { get; private init; }

    /// <summary>Gets a value indicating whether the font is CID-keyed (its Top DICT has ROS).</summary>
    public bool IsCidKeyed { get; private init; }

    /// <summary>Gets the CIDCount of a CID-keyed font (default 8720).</summary>
    public int CidCount { get; private init; } = 8720;

    /// <summary>Gets the offset of a CID-keyed font's FDArray INDEX, or -1.</summary>
    public int FontDictArrayOffset { get; private init; } = -1;

    /// <summary>Gets the offset of a CID-keyed font's FDSelect, or -1.</summary>
    public int FontDictSelectOffset { get; private init; } = -1;

    /// <summary>Gets why the font's outlines cannot be interpreted, or <see langword="null"/> when they can.</summary>
    public string? OutlinesUnsupported { get; private init; }

    /// <summary>Gets the Private DICT values of a glyph's charstring (#54 selects them through FDSelect for CID-keyed fonts).</summary>
    /// <param name="glyphId">The glyph id.</param>
    /// <returns>The values.</returns>
    public CffPrivate GetPrivate(int glyphId) => _private;

    /// <summary>Gets a glyph's name through the charset; <see langword="null"/> in a CID-keyed font or when the charset names none.</summary>
    public string? GetGlyphName(int glyphId) => (uint)glyphId < (uint)GlyphCount ? Names.ByGlyph[glyphId] : null;

    /// <summary>Looks a glyph up by name through the charset; the first glyph of a name wins.</summary>
    public bool TryGetGlyphId(string name, out int glyphId) => Names.ByName.TryGetValue(name, out glyphId);

    /// <summary>The glyph a StandardEncoding code names (the components of an accented character); 0 when none.</summary>
    public int GetStandardCodeGlyph(int code) =>
        StandardEncodingNames.Get(code) is { } name && TryGetGlyphId(name, out int glyph) ? glyph : 0;

    /// <summary>Gets the built-in encoding as names per code, <c>.notdef</c> where unencoded (5176 §12); <see langword="null"/> for a CID-keyed font.</summary>
    public IReadOnlyList<string>? BuiltInEncoding => Names.Encoding;

    /// <summary>The bias added to a subroutine number (5176 §16, p.25).</summary>
    public static int Bias(int count) => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

    /// <summary>Gets the string of a SID: a standard string below 391, else the String INDEX's; <see langword="null"/> when there is none.</summary>
    public string? GetString(int sid)
    {
        if (sid < StandardStringCount)
        {
            return sid >= 0 ? CffStandardData.Strings[sid] : null;
        }

        if (sid - StandardStringCount >= Strings.Count)
        {
            return null;
        }

        return Encoding.Latin1.GetString(Strings.Get(Data.Span, sid - StandardStringCount));
    }

    /// <summary>Whether bytes start with what a CFF header looks like: major version 1, a header size of at least 4, offSize 1 to 4.</summary>
    public static bool IsHeader(ReadOnlySpan<byte> data) => data.Length >= 4 && data[0] == 1 && data[2] >= 4 && data[3] is >= 1 and <= 4;

    /// <summary>Reads a CFF font; reports what is wrong and returns <see langword="null"/> when nothing is usable.</summary>
    /// <param name="data">The CFF data.</param>
    /// <param name="context">The diagnostics and limits.</param>
    /// <returns>The font.</returns>
    public static CffFont? Read(ReadOnlyMemory<byte> data, FontProgramContext context)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (!IsHeader(span))
        {
            // pdf.js accepts CFF data shifted by a few junk bytes; look for a header a little further on.
            int shift = 1;
            int window = Math.Min(MaxHeaderShift, span.Length - 4);
            while (shift <= window && !IsHeader(span[shift..]))
            {
                shift++;
            }

            if (shift > window)
            {
                Report(context, DiagnosticCodes.FontCffHeaderInvalid, DiagnosticSeverity.Error, span.Length >= 1 && span[0] == 2
                    ? "The program is CFF2 (major version 2), not CFF; CFF2 outlines are not supported."
                    : "The program does not start with a CFF header (Adobe Technical Note #5176 §6: major version 1, hdrSize, offSize); it is not usable.");
                return null;
            }

            Report(context, DiagnosticCodes.FontCffHeaderInvalid, DiagnosticSeverity.Warning, string.Create(CultureInfo.InvariantCulture, $"The CFF header is preceded by {shift} bytes that are not part of it (Adobe Technical Note #5176 §6); they are skipped."));
            data = data[shift..];
            span = data.Span;
        }

        int position = span[2];
        CffIndex names = ReadIndex(span, position, context, "Name");
        CffIndex tops = ReadIndex(span, names.End, context, "Top DICT");
        CffIndex strings = ReadIndex(span, tops.End, context, "String");
        CffIndex globals = ReadIndex(span, strings.End, context, "Global Subr");
        if (tops.Count == 0)
        {
            Report(context, DiagnosticCodes.FontProgramInvalid, DiagnosticSeverity.Error, "The CFF program has no Top DICT (Adobe Technical Note #5176 §8); it is not usable.");
            return null;
        }

        if (tops.Count > 1 || names.Count > 1)
        {
            Report(context, DiagnosticCodes.FontCffMultipleFonts, DiagnosticSeverity.Warning, "The CFF program holds more than one font; an embedded CFF program shall hold exactly one (ISO 32000-2 §9.9). The first is read.");
        }

        string? name = names.Count > 0 ? Encoding.Latin1.GetString(names.Get(span, 0)) : null;
        TopDict top = ReadTopDict(tops.Get(span, 0), context);
        if (top.CharStrings < 0)
        {
            Report(context, DiagnosticCodes.FontProgramInvalid, DiagnosticSeverity.Error, "The CFF Top DICT has no CharStrings offset (Adobe Technical Note #5176 §9, Table 9); the program is not usable.");
            return null;
        }

        CffIndex charStrings = ReadIndex(span, top.CharStrings, context, "CharStrings");
        if (charStrings.Count == 0)
        {
            Report(context, DiagnosticCodes.FontProgramInvalid, DiagnosticSeverity.Error, "The CFF CharStrings INDEX holds no glyph (Adobe Technical Note #5176 §14); the program is not usable.");
            return null;
        }

        CffPrivate privateValues = top.IsCid ? new CffPrivate(default, Bias(0), 0, 0, 0) : ReadPrivate(span, top, context);
        string? unsupported = top.IsCid
            ? "it is a CID-keyed CFF font, whose glyphs are read through FDArray and FDSelect (Adobe Technical Note #5176 §18-19)"
            : top.IsSynthetic ? "it is a synthetic font (SyntheticBase), which ISO 32000-2 does not allow and Broadside does not read (Adobe Technical Note #5176 §17)"
            : top.CharstringType != 2 ? string.Create(CultureInfo.InvariantCulture, $"its CharstringType is {top.CharstringType}; only Type 2 charstrings are read")
            : null;
        if (unsupported is not null && !top.IsCid)
        {
            Report(context, DiagnosticCodes.FontProgramUnsupported, DiagnosticSeverity.Information, $"The CFF program's outlines are not read: {unsupported}.");
        }

        ushort[] charset = ReadCharset(span, top.Charset, charStrings.Count, top.IsCid, context);
        (CffEncodingKind kind, ushort[] codes) = top.IsCid ? (CffEncodingKind.None, [])
            : top.Encoding switch
            {
                0 => (CffEncodingKind.Standard, []),
                1 => (CffEncodingKind.Expert, []),
                _ => (CffEncodingKind.Custom, ReadEncoding(span, top.Encoding, charset, context)),
            };
        return new CffFont(data, privateValues)
        {
            Name = name,
            Strings = strings,
            GlobalSubrs = globals,
            GlobalSubrBias = Bias(globals.Count),
            CharStrings = charStrings,
            FontMatrix = top.Matrix,
            FontBBox = top.BBox,
            IsCidKeyed = top.IsCid,
            CidCount = top.CidCount,
            FontDictArrayOffset = top.FdArray,
            FontDictSelectOffset = top.FdSelect,
            OutlinesUnsupported = unsupported,
            Charset = charset,
            EncodingKind = kind,
            CodeToGlyph = codes,
        };
    }

    private static CffIndex ReadIndex(ReadOnlySpan<byte> data, int offset, FontProgramContext context, string what)
    {
        CffIndex index = CffIndex.Read(data, offset, out string? problem);
        if (problem is not null)
        {
            Report(context, DiagnosticCodes.FontCffIndexInvalid, DiagnosticSeverity.Warning, $"The CFF {what} INDEX {problem} (Adobe Technical Note #5176 §5); what is readable is used.");
        }

        return index;
    }

    private static TopDict ReadTopDict(ReadOnlySpan<byte> dict, FontProgramContext context)
    {
        var top = new TopDict();
        var reader = new CffDictReader(dict);
        bool first = true;
        while (reader.Next())
        {
            switch (reader.Operator)
            {
                case 15:
                    top.Charset = Offset(reader[0]);
                    break;
                case 16:
                    top.Encoding = Offset(reader[0]);
                    break;
                case 17:
                    top.CharStrings = Offset(reader[0]);
                    break;
                case 18:
                    if (reader.OperandCount >= 2)
                    {
                        top.PrivateSize = Offset(reader[0]);
                        top.PrivateOffset = Offset(reader[1]);
                    }

                    break;
                case 5 when reader.OperandCount >= 4:
                    top.BBox = new PdfRectangle(reader[0], reader[1], reader[2], reader[3]);
                    break;
                case 1207 when reader.OperandCount >= 6:
                    top.Matrix = new Matrix(reader[0], reader[1], reader[2], reader[3], reader[4], reader[5]);
                    break;
                case 1206:
                    top.CharstringType = (int)reader[0];
                    break;
                case 1220:
                    top.IsSynthetic = first;
                    break;
                case 1230:
                    top.IsCid = true;
                    break;
                case 1234:
                    top.CidCount = (int)Math.Clamp(reader[0], 0, int.MaxValue);
                    break;
                case 1236:
                    top.FdArray = Offset(reader[0]);
                    break;
                case 1237:
                    top.FdSelect = Offset(reader[0]);
                    break;
            }

            first = false;
        }

        if (reader.Problem is { } problem)
        {
            Report(context, DiagnosticCodes.FontCffDictInvalid, DiagnosticSeverity.Warning, $"The CFF Top DICT {problem} (Adobe Technical Note #5176 §4).");
        }

        return top;
    }

    private static CffPrivate ReadPrivate(ReadOnlySpan<byte> data, TopDict top, FontProgramContext context)
    {
        if (top.PrivateOffset < 0 || top.PrivateSize < 0 || top.PrivateOffset > data.Length)
        {
            Report(context, DiagnosticCodes.FontCffPrivateMissing, DiagnosticSeverity.Warning, top.PrivateOffset < 0
                ? "The CFF Top DICT has no Private entry, which a CFF font shall have (Adobe Technical Note #5176 §9, Table 9); the Private DICT defaults are used."
                : "The CFF Private DICT lies outside the program (Adobe Technical Note #5176 §15); the Private DICT defaults are used.");
            return new CffPrivate(default, Bias(0), 0, 0, 0);
        }

        int size = top.PrivateSize;
        if (size > data.Length - top.PrivateOffset)
        {
            Report(context, DiagnosticCodes.FontCffPrivateMissing, DiagnosticSeverity.Warning, "The CFF Private DICT runs past the end of the program (Adobe Technical Note #5176 §15); the part present is read.");
            size = data.Length - top.PrivateOffset;
        }

        var reader = new CffDictReader(data.Slice(top.PrivateOffset, size));
        int subrs = -1;
        double defaultWidth = 0;
        double nominalWidth = 0;
        int seed = 0;
        while (reader.Next())
        {
            switch (reader.Operator)
            {
                case 19:
                    subrs = Offset(reader[0]);
                    break;
                case 20:
                    defaultWidth = reader[0];
                    break;
                case 21:
                    nominalWidth = reader[0];
                    break;
                case 1219:
                    seed = (int)Math.Clamp(reader[0], int.MinValue, int.MaxValue);
                    break;
            }
        }

        if (reader.Problem is { } problem)
        {
            Report(context, DiagnosticCodes.FontCffDictInvalid, DiagnosticSeverity.Warning, $"The CFF Private DICT {problem} (Adobe Technical Note #5176 §4).");
        }

        CffIndex local = default;
        if (subrs >= 0)
        {
            // 5176 §15 (p.25): the Subrs offset is relative to the start of the Private DICT.
            local = ReadIndex(data, (int)Math.Min((long)top.PrivateOffset + subrs, int.MaxValue), context, "local Subrs");
        }

        return new CffPrivate(local, Bias(local.Count), defaultWidth, nominalWidth, seed);
    }

    /// <summary>The charset (5176 §13): SIDs (CIDs in CID-keyed fonts) of glyphs 1 and up; predefined ISOAdobe, Expert, ExpertSubset.</summary>
    private static ushort[] ReadCharset(ReadOnlySpan<byte> data, int offset, int glyphCount, bool isCid, FontProgramContext context)
    {
        var charset = new ushort[glyphCount];
        if (offset < 0)
        {
            ReportCharset(context, "has an offset that is not a non-negative integer; the glyphs are unnamed");
            return charset;
        }

        if (offset <= 2)
        {
            if (isCid)
            {
                // A CID-keyed font has no predefined charset; read the GIDs as CIDs, as Adobe Reader and PDFBox do (#54 reports).
                for (int glyph = 1; glyph < glyphCount; glyph++)
                {
                    charset[glyph] = (ushort)Math.Min(glyph, ushort.MaxValue);
                }

                return charset;
            }

            ReadOnlySpan<ushort> predefined = offset switch
            {
                1 => CffStandardData.ExpertCharset,
                2 => CffStandardData.ExpertSubsetCharset,
                _ => default,
            };
            int length = offset == 0 ? 228 : predefined.Length;
            for (int glyph = 1; glyph < glyphCount && glyph <= length; glyph++)
            {
                charset[glyph] = offset == 0 ? (ushort)glyph : predefined[glyph - 1];
            }

            if (glyphCount - 1 > length)
            {
                ReportCharset(context, "a predefined charset names fewer glyphs than the font has (Adobe Technical Note #5176 §13: it is valid only when it covers them); the others are unnamed");
            }

            return charset;
        }

        if (offset >= data.Length)
        {
            ReportCharset(context, "lies outside the program; the glyphs are unnamed");
            return charset;
        }

        int format = data[offset];
        int position = offset + 1;
        int glyphId = 1;
        bool truncated = false;
        switch (format)
        {
            case 0:
                for (; glyphId < glyphCount; glyphId++, position += 2)
                {
                    if (position + 2 > data.Length)
                    {
                        truncated = true;
                        break;
                    }

                    charset[glyphId] = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
                }

                break;
            case 1:
            case 2:
                int rangeLength = format == 1 ? 3 : 4;
                while (glyphId < glyphCount)
                {
                    if (position + rangeLength > data.Length)
                    {
                        truncated = true;
                        break;
                    }

                    int first = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
                    int left = format == 1 ? data[position + 2] : BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]);
                    position += rangeLength;
                    for (int item = 0; item <= left && glyphId < glyphCount; item++, glyphId++)
                    {
                        charset[glyphId] = (ushort)Math.Min(first + item, ushort.MaxValue);
                    }
                }

                break;
            default:
                ReportCharset(context, string.Create(CultureInfo.InvariantCulture, $"has format {format}, not 0, 1 or 2; the glyphs are unnamed"));
                return charset;
        }

        if (truncated)
        {
            ReportCharset(context, "runs past the end of the program; the glyphs it does not reach are unnamed");
        }

        return charset;
    }

    /// <summary>A custom encoding (5176 §12): format 0 codes or format 1 ranges for glyphs 1 and up, then supplements (code, SID).</summary>
    private static ushort[] ReadEncoding(ReadOnlySpan<byte> data, int offset, ushort[] charset, FontProgramContext context)
    {
        var codes = new ushort[256];
        if (offset < 0 || offset >= data.Length)
        {
            ReportEncoding(context, "lies outside the program; no code is encoded");
            return codes;
        }

        int format = data[offset] & 0x7F;
        bool supplemented = (data[offset] & 0x80) != 0;
        int position = offset + 1;
        int glyphCount = charset.Length;
        bool truncated = false;
        if (format is 0 or 1)
        {
            if (position >= data.Length)
            {
                truncated = true;
            }
            else
            {
                int count = data[position++];
                int glyph = 1;
                for (int item = 0; item < count; item++)
                {
                    if (format == 0)
                    {
                        if (position >= data.Length)
                        {
                            truncated = true;
                            break;
                        }

                        if (glyph < glyphCount)
                        {
                            codes[data[position]] = (ushort)glyph;
                        }

                        position++;
                        glyph++;
                    }
                    else
                    {
                        if (position + 2 > data.Length)
                        {
                            truncated = true;
                            break;
                        }

                        int first = data[position];
                        int left = data[position + 1];
                        position += 2;
                        for (int code = first; code <= first + left && code < 256; code++, glyph++)
                        {
                            if (glyph < glyphCount)
                            {
                                codes[code] = (ushort)glyph;
                            }
                        }
                    }
                }
            }
        }
        else
        {
            ReportEncoding(context, string.Create(CultureInfo.InvariantCulture, $"has format {format}, not 0 or 1; no code is encoded"));
            return codes;
        }

        if (supplemented && !truncated)
        {
            if (position >= data.Length)
            {
                truncated = true;
            }
            else
            {
                int count = data[position++];
                for (int item = 0; item < count; item++, position += 3)
                {
                    if (position + 3 > data.Length)
                    {
                        truncated = true;
                        break;
                    }

                    int code = data[position];
                    int sid = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 1)..]);
                    int glyph = Array.IndexOf(charset, (ushort)sid, 1);
                    if (glyph > 0)
                    {
                        codes[code] = (ushort)glyph;
                    }
                    else
                    {
                        ReportEncoding(context, "has a supplement naming a glyph the charset does not have; that code is not encoded");
                    }
                }
            }
        }

        if (truncated)
        {
            ReportEncoding(context, "runs past the end of the program; the codes it does not reach are not encoded");
        }

        return codes;
    }

    private static int Offset(double value) => value is >= 0 and <= int.MaxValue ? (int)value : -1;

    private static void ReportCharset(FontProgramContext context, string problem) =>
        Report(context, DiagnosticCodes.FontCffCharsetInvalid, DiagnosticSeverity.Warning, $"The CFF charset {problem}.");

    private static void ReportEncoding(FontProgramContext context, string problem) =>
        Report(context, DiagnosticCodes.FontCffEncodingInvalid, DiagnosticSeverity.Warning, $"The CFF encoding {problem} (Adobe Technical Note #5176 §12).");

    private static void Report(FontProgramContext context, string code, DiagnosticSeverity severity, string message) => context.Report(code, severity, message);

    /// <summary>Gets the name tables, built on first use (a benign race: every thread builds equal tables, one is kept).</summary>
    private NameTables Names => Volatile.Read(ref _names) ?? BuildNames();

    private NameTables BuildNames()
    {
        NameTables built = NameTables.Build(this);
        return Interlocked.CompareExchange(ref _names, built, null) ?? built;
    }

    /// <summary>Glyph names by glyph and glyphs by name (charset), and the built-in encoding as names.</summary>
    private sealed class NameTables
    {
        private NameTables(string?[] byGlyph, Dictionary<string, int> byName, IReadOnlyList<string>? encoding)
        {
            ByGlyph = byGlyph;
            ByName = byName;
            Encoding = encoding;
        }

        public string?[] ByGlyph { get; }

        public Dictionary<string, int> ByName { get; }

        public IReadOnlyList<string>? Encoding { get; }

        public static NameTables Build(CffFont font)
        {
            var byGlyph = new string?[font.GlyphCount];
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            if (font.IsCidKeyed)
            {
                return new NameTables(byGlyph, byName, null);
            }

            for (int glyph = 0; glyph < byGlyph.Length; glyph++)
            {
                string? name = font.GetString(font.Charset[glyph]);
                byGlyph[glyph] = glyph > 0 && font.Charset[glyph] == 0 ? null : name;
                if (byGlyph[glyph] is { } named)
                {
                    byName.TryAdd(named, glyph);
                }
            }

            var encoding = new string[256];
            ReadOnlySpan<ushort> predefined = font.EncodingKind == CffEncodingKind.Expert ? CffStandardData.ExpertEncoding : CffStandardData.StandardEncoding;
            for (int code = 0; code < 256; code++)
            {
                if (font.EncodingKind == CffEncodingKind.Custom)
                {
                    int glyph = font.CodeToGlyph[code];
                    encoding[code] = (glyph > 0 ? byGlyph[glyph] : null) ?? CffStandardData.Strings[0];
                }
                else
                {
                    encoding[code] = CffStandardData.Strings[predefined[code]];
                }
            }

            return new NameTables(byGlyph, byName, Array.AsReadOnly(encoding));
        }
    }

    /// <summary>The Top DICT values the parser uses, with their defaults (5176 §9, Tables 9 and 10).</summary>
    private struct TopDict()
    {
        public int Charset;
        public int Encoding;
        public int CharStrings = -1;
        public int PrivateSize = -1;
        public int PrivateOffset = -1;
        public Matrix Matrix = Matrix.CreateScale(0.001, 0.001);
        public PdfRectangle BBox;
        public int CharstringType = 2;
        public bool IsSynthetic;
        public bool IsCid;
        public int CidCount = 8720;
        public int FdArray = -1;
        public int FdSelect = -1;
    }
}
