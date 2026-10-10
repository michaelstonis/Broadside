using System.Buffers.Binary;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Parsing;

namespace Broadside.Fonts.Cff;

/// <summary>CID-keyed CFF fonts: FDArray, per-Font-DICT Private DICTs and matrices, FDSelect, and CID to glyph id lookup (issue #54).</summary>
internal sealed partial class CffFont
{
    /// <summary>FDSelect values are Card8, so at most 256 Font DICTs are reachable (5176 §19).</summary>
    private const int MaxFontDicts = 256;

    private const ushort NoGlyph = ushort.MaxValue;

    private readonly CidTables? _cid;
    private ushort[]? _cidToGlyph;

    /// <summary>
    /// Gets the matrix that maps a glyph's own character space into the space of <see cref="FontMatrix"/>, when the Font DICT
    /// FDSelect gives it has another matrix than the program's; <see langword="false"/> otherwise (always for name-keyed fonts).
    /// </summary>
    /// <param name="glyphId">The glyph id.</param>
    /// <param name="adjustment">The matrix to apply to the glyph's outline and advance.</param>
    /// <returns>Whether the glyph needs it.</returns>
    /// <remarks>Adobe Technical Note #5014 §4.2 (p.25): each FDArray dictionary's FontMatrix defines character space for its glyphs.</remarks>
    public bool TryGetMatrixAdjustment(int glyphId, out Matrix adjustment)
    {
        if (_cid is { } cid && cid.TryGetAdjustment(glyphId, out adjustment))
        {
            return true;
        }

        adjustment = Matrix.Identity;
        return false;
    }

    /// <summary>
    /// Looks up the glyph of a CID in a CID-keyed font: the glyph whose charset entry is the CID (5176 §13 and §18); the first
    /// glyph of a CID wins. The inverse table is built on first use (one array per font).
    /// </summary>
    /// <param name="cid">The CID.</param>
    /// <param name="glyphId">The glyph id; 0 when there is none.</param>
    /// <returns>Whether the font has a glyph for the CID.</returns>
    /// <remarks>ISO 32000-2 §9.7.4.2 (p.347): "the CIDs shall be mapped to GIDs through the charset". Allocates nothing once built.</remarks>
    public bool TryGetGlyphForCid(int cid, out int glyphId)
    {
        ushort[] table = Volatile.Read(ref _cidToGlyph) ?? BuildCidToGlyph();
        if ((uint)cid < (uint)table.Length && table[cid] != NoGlyph)
        {
            glyphId = table[cid];
            return true;
        }

        glyphId = 0;
        return false;
    }

    private ushort[] BuildCidToGlyph()
    {
        ushort[] charset = Charset;
        int max = 0;
        foreach (ushort cid in charset)
        {
            max = Math.Max(max, cid);
        }

        var table = new ushort[max + 1];
        Array.Fill(table, NoGlyph);
        for (int glyph = charset.Length - 1; glyph >= 0; glyph--)
        {
            // Glyph 0 is CID 0 (5176 §13: .notdef is omitted from the charset); later glyphs of the same CID give way to earlier ones.
            if (glyph < NoGlyph && (glyph == 0 || charset[glyph] != 0))
            {
                table[charset[glyph]] = (ushort)glyph;
            }
        }

        return Interlocked.CompareExchange(ref _cidToGlyph, table, null) ?? table;
    }

    /// <summary>Reads the FDArray, each Font DICT's Private DICT and matrix, and FDSelect of a CID-keyed font (5176 §18-19).</summary>
    private static CidTables ReadCidTables(ReadOnlySpan<byte> data, TopDict top, int glyphCount, FontProgramContext context)
    {
        Matrix? topMatrix = top.HasMatrix ? top.Matrix : null;
        CffIndex fontDicts = top.FdArray >= 0 ? ReadIndex(data, top.FdArray, context, "FDArray") : default;
        if (fontDicts.Count == 0)
        {
            Report(context, DiagnosticCodes.FontCffFdArrayMissing, DiagnosticSeverity.Warning, top.FdArray < 0
                ? "The CID-keyed CFF font has no FDArray, which it shall have (Adobe Technical Note #5176 §18); its glyphs are read with the Private DICT defaults."
                : "The CID-keyed CFF font's FDArray holds no Font DICT (Adobe Technical Note #5176 §18); its glyphs are read with the Private DICT defaults.");
            Matrix only = Combine(topMatrix, null);
            return new CidTables([new CffPrivate(default, Bias(0), 0, 0, 0)], [only], new byte[glyphCount], only);
        }

        int count = Math.Min(fontDicts.Count, MaxFontDicts);
        var privates = new CffPrivate[count];
        var matrices = new Matrix[count];
        for (int index = 0; index < count; index++)
        {
            (int privateSize, int privateOffset, Matrix? matrix) = ReadFontDict(fontDicts.Get(data, index), context);
            privates[index] = ReadPrivate(data, privateOffset, privateSize, context, "Font DICT");
            matrices[index] = Combine(topMatrix, matrix);
        }

        byte[] select = ReadFdSelect(data, top.FdSelect, glyphCount, count, context);
        return new CidTables(privates, matrices, select, matrices[0]);
    }

    /// <summary>A Font DICT of the FDArray (5176 §18): its Private DICT's size and offset and its FontMatrix.</summary>
    private static (int PrivateSize, int PrivateOffset, Matrix? Matrix) ReadFontDict(ReadOnlySpan<byte> dict, FontProgramContext context)
    {
        var reader = new CffDictReader(dict);
        int size = -1;
        int offset = -1;
        Matrix? matrix = null;
        while (reader.Next())
        {
            switch (reader.Operator)
            {
                case 18 when reader.OperandCount >= 2:
                    size = Offset(reader[0]);
                    offset = Offset(reader[1]);
                    break;
                case 1207 when reader.OperandCount >= 6:
                    matrix = new Matrix(reader[0], reader[1], reader[2], reader[3], reader[4], reader[5]);
                    break;
            }
        }

        if (reader.Problem is { } problem)
        {
            Report(context, DiagnosticCodes.FontCffDictInvalid, DiagnosticSeverity.Warning, $"A CFF Font DICT {problem} (Adobe Technical Note #5176 §4).");
        }

        return (size, offset, matrix);
    }

    /// <summary>
    /// The matrix of a Font DICT's glyphs: its own FontMatrix followed by the Top DICT's when both are given (the PostScript
    /// CIDFontType 0 concatenation, as FreeType and PDFBox read it), else whichever is given, else <c>[0.001 0 0 0.001 0 0]</c>.
    /// </summary>
    /// <remarks>Adobe Technical Note #5014 §4.2 (p.25); #5176 §9, Table 9 (FontMatrix default).</remarks>
    private static Matrix Combine(Matrix? top, Matrix? fontDict) => (top, fontDict) switch
    {
        ({ } outer, { } inner) => inner * outer,
        (null, { } inner) => inner,
        ({ } outer, null) => outer,
        _ => Matrix.CreateScale(0.001, 0.001),
    };

    /// <summary>FDSelect (5176 §19, Tables 27-29): format 0 (one Card8 per glyph) or 3 (ranges with a sentinel); repaired leniently.</summary>
    private static byte[] ReadFdSelect(ReadOnlySpan<byte> data, int offset, int glyphCount, int fontDictCount, FontProgramContext context)
    {
        var select = new byte[glyphCount];
        if (offset < 0 || offset >= data.Length)
        {
            ReportFdSelect(context, offset < 0 ? "is missing" : "lies outside the program");
            return select;
        }

        int format = data[offset];
        string? problem = null;
        switch (format)
        {
            case 0:
                int available = Math.Min(glyphCount, data.Length - offset - 1);
                data.Slice(offset + 1, available).CopyTo(select);
                if (available < glyphCount)
                {
                    problem = "runs past the end of the program";
                }

                break;
            case 3:
                problem = ReadFdSelectRanges(data, offset + 1, select);
                break;
            default:
                ReportFdSelect(context, string.Create(CultureInfo.InvariantCulture, $"has format {format}, not 0 or 3"));
                return select;
        }

        for (int glyph = 0; glyph < select.Length; glyph++)
        {
            if (select[glyph] >= fontDictCount)
            {
                problem ??= string.Create(CultureInfo.InvariantCulture, $"selects Font DICT {select[glyph]}, but the FDArray has {fontDictCount}");
                select[glyph] = 0;
            }
        }

        if (problem is not null)
        {
            ReportFdSelect(context, problem);
        }

        return select;
    }

    /// <summary>Format 3: Card16 nRanges, Range3 {Card16 first, Card8 fd} sorted from glyph 0, Card16 sentinel = nGlyphs.</summary>
    private static string? ReadFdSelectRanges(ReadOnlySpan<byte> data, int position, byte[] select)
    {
        if (position + 2 > data.Length)
        {
            return "runs past the end of the program";
        }

        int ranges = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
        position += 2;
        string? problem = null;
        int previousFirst = -1;
        byte previousFd = 0;
        for (int range = 0; range <= ranges; range++, position += 3)
        {
            bool sentinel = range == ranges;
            if (position + 2 > data.Length || (!sentinel && position + 3 > data.Length))
            {
                problem ??= "runs past the end of the program";
                Fill(select, Math.Max(previousFirst, 0), select.Length, previousFd);
                return problem;
            }

            int first = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
            if (range == 0 && first != 0)
            {
                problem ??= "does not start its first range at glyph 0";
                first = 0;
            }

            if (sentinel && first != select.Length)
            {
                problem ??= string.Create(CultureInfo.InvariantCulture, $"ends with the sentinel {first}, not the glyph count {select.Length}");
                first = select.Length;
            }

            if (first < previousFirst)
            {
                problem ??= "has ranges out of order";
                first = previousFirst;
            }

            if (previousFirst >= 0)
            {
                Fill(select, previousFirst, first, previousFd);
            }

            if (!sentinel)
            {
                previousFirst = first;
                previousFd = data[position + 2];
            }
        }

        return problem;
    }

    private static void Fill(byte[] select, int from, int to, byte fd)
    {
        from = Math.Clamp(from, 0, select.Length);
        to = Math.Clamp(to, from, select.Length);
        select.AsSpan(from, to - from).Fill(fd);
    }

    private static void ReportFdSelect(FontProgramContext context, string problem) =>
        Report(context, DiagnosticCodes.FontCffFdSelectInvalid, DiagnosticSeverity.Warning, $"The CID-keyed CFF font's FDSelect {problem} (Adobe Technical Note #5176 §19); the glyphs it does not assign use the first Font DICT.");

    /// <summary>The per-Font-DICT tables of a CID-keyed font: Private DICT values, matrices and each glyph's Font DICT.</summary>
    private sealed class CidTables
    {
        private readonly CffPrivate[] _privates;
        private readonly byte[] _select;
        private readonly Matrix[]? _adjustments;

        public CidTables(CffPrivate[] privates, Matrix[] matrices, byte[] select, Matrix fontMatrix)
        {
            _privates = privates;
            _select = select;
            FontMatrix = fontMatrix;
            if (matrices.Any(matrix => matrix != fontMatrix) && fontMatrix.TryInvert(out Matrix inverse))
            {
                _adjustments = [.. matrices.Select(matrix => matrix == fontMatrix ? Matrix.Identity : matrix * inverse)];
            }
        }

        /// <summary>Gets the program's matrix: that of the first Font DICT.</summary>
        public Matrix FontMatrix { get; }

        public CffPrivate GetPrivate(int glyphId) => _privates[(uint)glyphId < (uint)_select.Length ? _select[glyphId] : 0];

        public bool TryGetAdjustment(int glyphId, out Matrix adjustment)
        {
            if (_adjustments is { } adjustments && (uint)glyphId < (uint)_select.Length && !adjustments[_select[glyphId]].IsIdentity)
            {
                adjustment = adjustments[_select[glyphId]];
                return true;
            }

            adjustment = Matrix.Identity;
            return false;
        }
    }
}
