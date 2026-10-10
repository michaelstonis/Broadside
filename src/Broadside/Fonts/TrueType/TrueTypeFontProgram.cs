using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Parsing;

namespace Broadside.Fonts.TrueType;

/// <summary>
/// A parsed TrueType program: tables located once, glyphs decoded on request from the program's bytes, which it keeps a reference to.
/// </summary>
/// <remarks>
/// Apple TrueType Reference Manual and OpenType specification: "head", "hhea", "hmtx", "maxp", "loca", "glyf", "cmap", "post".
/// ISO 32000-2 §9.6.3, §9.9. Immutable and safe for concurrent use; the "post" names are read on first use.
/// </remarks>
internal sealed partial class TrueTypeFontProgram : FontProgram
{
    private const int MaxComponentsPerGlyph = 4096;
    private const int MaxAncestorDepth = 256;

    private readonly SfntFile _sfnt;
    private readonly ReadOnlyMemory<byte> _glyf;
    private readonly ReadOnlyMemory<byte> _loca;
    private readonly bool _longOffsets;
    private readonly int _glyphCount;
    private readonly int _unitsPerEm;
    private readonly ReadOnlyMemory<byte>? _hmtx;
    private readonly int _horizontalMetricCount;
    private readonly ReadOnlyMemory<byte>? _post;
    private readonly CmapSubtable[] _cmaps;
    private readonly FontProgramContext _context;
    private readonly int _maxDepth;
    private PostTable? _postTable;
    private string? _postScriptName;
    private bool _postScriptNameRead;

    private TrueTypeFontProgram(
        SfntFile sfnt,
        ReadOnlyMemory<byte> glyf,
        ReadOnlyMemory<byte> loca,
        bool longOffsets,
        int glyphCount,
        int unitsPerEm,
        PdfRectangle bbox,
        (double Ascender, double Descender, double LineGap) vertical,
        ReadOnlyMemory<byte>? hmtx,
        int horizontalMetricCount,
        ReadOnlyMemory<byte>? post,
        ReadOnlyMemory<byte>? cmap,
        FontProgramContext context)
    {
        _sfnt = sfnt;
        _glyf = glyf;
        _loca = loca;
        _longOffsets = longOffsets;
        _glyphCount = glyphCount;
        _unitsPerEm = unitsPerEm;
        FontBBox = bbox;
        Ascender = vertical.Ascender;
        Descender = vertical.Descender;
        LineGap = vertical.LineGap;
        _hmtx = hmtx;
        _horizontalMetricCount = horizontalMetricCount;
        _post = post;
        _context = context;
        _maxDepth = Math.Clamp(context.MaxCompositeDepth, 0, MaxAncestorDepth - 1);
        _cmaps = cmap is { } table ? CmapSubtable.ReadAll(table, glyphCount, context) : [];
        FontMatrix = Matrix.CreateScale(1.0 / unitsPerEm, 1.0 / unitsPerEm);
    }

    /// <inheritdoc/>
    public override FontProgramFormat Format => _sfnt.Version == SfntFile.OpenTypeVersion ? FontProgramFormat.OpenType : FontProgramFormat.TrueType;

    /// <inheritdoc/>
    public override int GlyphCount => _glyphCount;

    /// <inheritdoc/>
    /// <remarks>OpenType "head" table: <c>[1/unitsPerEm 0 0 1/unitsPerEm 0 0]</c>.</remarks>
    public override Matrix FontMatrix { get; }

    /// <inheritdoc/>
    /// <remarks>OpenType "head" table, <c>xMin yMin xMax yMax</c>.</remarks>
    public override PdfRectangle FontBBox { get; }

    /// <inheritdoc/>
    /// <remarks>OpenType "hhea" table.</remarks>
    public override double Ascender { get; }

    /// <inheritdoc/>
    /// <remarks>OpenType "hhea" table.</remarks>
    public override double Descender { get; }

    /// <inheritdoc/>
    /// <remarks>OpenType "hhea" table.</remarks>
    public override double LineGap { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<FontCharacterMap> CharacterMaps => _cmaps;

    /// <inheritdoc/>
    /// <remarks>OpenType "name" table, name id 6.</remarks>
    public override string? PostScriptName
    {
        get
        {
            if (!Volatile.Read(ref _postScriptNameRead))
            {
                _postScriptName = _sfnt.ReadPostScriptName();
                Volatile.Write(ref _postScriptNameRead, true);
            }

            return _postScriptName;
        }
    }

    /// <summary>Gets the units per em, from "head".</summary>
    internal int UnitsPerEm => _unitsPerEm;

    /// <summary>Gets the "post" names, read on first use. A plain volatile read once published: no delegate per call.</summary>
    private PostTable Post
    {
        get
        {
            PostTable? table = Volatile.Read(ref _postTable);
            if (table is null)
            {
                table = _post is { } post ? PostTable.Read(post.Span, _glyphCount, _context) : PostTable.None;
                table = Interlocked.CompareExchange(ref _postTable, table, null) ?? table;
            }

            return table;
        }
    }

    /// <summary>Builds the program from its located tables, repairing missing or inconsistent ones with diagnostics.</summary>
    internal static TrueTypeFontProgram Create(
        SfntFile sfnt,
        ReadOnlyMemory<byte> glyf,
        ReadOnlyMemory<byte> loca,
        FontProgramContext context,
        ReadOnlyMemory<byte>? head,
        ReadOnlyMemory<byte>? hhea,
        ReadOnlyMemory<byte>? hmtx,
        ReadOnlyMemory<byte>? maxp,
        ReadOnlyMemory<byte>? cmap,
        ReadOnlyMemory<byte>? post)
    {
        // maxp: numGlyphs at offset 4 in every version; the version 1.0 maxima are hints only.
        int glyphCount = -1;
        if (maxp is { Length: >= 6 } maxpTable)
        {
            glyphCount = BinaryPrimitives.ReadUInt16BigEndian(maxpTable.Span[4..]);
        }
        else
        {
            ReportTable(context, "maxp", "is missing or too short; the glyph count is taken from \"loca\"");
        }

        // head: unitsPerEm, the font box and the loca format.
        int unitsPerEm = 1000;
        var bbox = default(PdfRectangle);
        int locaFormat = -1;
        if (head is { Length: >= 54 } headTable)
        {
            ReadOnlySpan<byte> span = headTable.Span;
            int declared = BinaryPrimitives.ReadUInt16BigEndian(span[18..]);
            if (declared is >= 16 and <= 16384)
            {
                unitsPerEm = declared;
            }
            else
            {
                ReportTable(context, "head", string.Create(CultureInfo.InvariantCulture, $"gives unitsPerEm {declared}, outside 16 to 16384; 1000 is used"));
            }

            bbox = new PdfRectangle(
                BinaryPrimitives.ReadInt16BigEndian(span[36..]),
                BinaryPrimitives.ReadInt16BigEndian(span[38..]),
                BinaryPrimitives.ReadInt16BigEndian(span[40..]),
                BinaryPrimitives.ReadInt16BigEndian(span[42..]));
            locaFormat = BinaryPrimitives.ReadInt16BigEndian(span[50..]);
        }
        else
        {
            ReportTable(context, "head", "is missing or too short; 1000 units per em are used and the \"loca\" format is inferred from its length");
        }

        if (locaFormat is not (0 or 1))
        {
            bool fitsLong = glyphCount >= 0 ? loca.Length >= (glyphCount + 1) * 4 : loca.Length % 4 == 0 && loca.Length >= 8;
            bool fitsShort = glyphCount >= 0 && loca.Length == (glyphCount + 1) * 2;
            int inferred = fitsShort || !fitsLong ? 0 : 1;
            if (head is { Length: >= 54 })
            {
                ReportTable(context, "head", string.Create(CultureInfo.InvariantCulture, $"gives indexToLocFormat {locaFormat}, which is neither 0 nor 1; {inferred} is inferred from the \"loca\" length"));
            }

            locaFormat = inferred;
        }

        bool longOffsets = locaFormat == 1;
        int entries = loca.Length / (longOffsets ? 4 : 2);
        if (glyphCount < 0)
        {
            glyphCount = Math.Max(0, entries - 1);
        }
        else if (entries < glyphCount + 1)
        {
            ReportTable(context, "loca", string.Create(CultureInfo.InvariantCulture, $"has offsets for {Math.Max(0, entries - 1)} glyphs but \"maxp\" counts {glyphCount}; the glyphs without offsets do not exist"));
            glyphCount = Math.Max(0, entries - 1);
        }

        // hhea: the vertical metrics and how many hmtx entries carry an advance.
        (double, double, double) vertical = default;
        int metricCount = 0;
        if (hhea is { Length: >= 36 } hheaTable)
        {
            ReadOnlySpan<byte> span = hheaTable.Span;
            vertical = (BinaryPrimitives.ReadInt16BigEndian(span[4..]), BinaryPrimitives.ReadInt16BigEndian(span[6..]), BinaryPrimitives.ReadInt16BigEndian(span[8..]));
            metricCount = BinaryPrimitives.ReadUInt16BigEndian(span[34..]);
            if (metricCount > glyphCount)
            {
                metricCount = glyphCount;
            }
        }
        else
        {
            ReportTable(context, "hhea", "is missing or too short; glyph advances are 0");
        }

        if (hmtx is { } hmtxTable && metricCount > 0)
        {
            if (hmtxTable.Length < (4 * metricCount) + (2 * (glyphCount - metricCount)))
            {
                ReportTable(context, "hmtx", "is shorter than \"hhea\" and \"maxp\" require; the metrics present are used and the others are 0");
            }
        }
        else if (hhea is { Length: >= 36 })
        {
            ReportTable(context, "hmtx", metricCount == 0 ? "has no advance (numberOfHMetrics is 0); glyph advances are 0" : "is missing; glyph advances are 0");
            metricCount = 0;
        }

        if (cmap is null && !context.IsCidFont)
        {
            ReportTable(context, "cmap", "is missing; a simple TrueType font's program shall have one (ISO 32000-2 §9.9), so glyphs are selected by other means");
        }

        return new TrueTypeFontProgram(
            sfnt,
            glyf,
            loca,
            longOffsets,
            glyphCount,
            unitsPerEm,
            bbox,
            vertical,
            metricCount > 0 ? hmtx : null,
            metricCount,
            post,
            cmap,
            context);
    }

    /// <inheritdoc/>
    public override GlyphMetrics GetMetrics(int glyphId) => GetMetrics(glyphId, 0);

    /// <inheritdoc/>
    /// <remarks>OpenType "post" table.</remarks>
    public override string? GetGlyphName(int glyphId) => Post.NameOf(glyphId);

    /// <inheritdoc/>
    /// <remarks>OpenType "post" table; the first glyph of a name wins when several share it.</remarks>
    public override bool TryGetGlyphId(string glyphName, out int glyphId)
    {
        ArgumentNullException.ThrowIfNull(glyphName);
        if (Post.TryGetGlyphId(glyphName, out glyphId))
        {
            return true;
        }

        glyphId = 0;
        return false;
    }

    /// <summary>The "hmtx" entry of a glyph: the last advance repeats past numberOfHMetrics; missing data reads as 0.</summary>
    private (double Advance, double LeftSideBearing, bool Present) ReadHorizontalMetrics(int glyphId)
    {
        if (_hmtx is not { } hmtx || (uint)glyphId >= (uint)_glyphCount)
        {
            return (0, 0, false);
        }

        ReadOnlySpan<byte> span = hmtx.Span;
        int advanceIndex = Math.Min(glyphId, _horizontalMetricCount - 1);
        int advanceAt = 4 * advanceIndex;
        double advance = advanceAt + 2 <= span.Length ? BinaryPrimitives.ReadUInt16BigEndian(span[advanceAt..]) : 0;
        int bearingAt = glyphId < _horizontalMetricCount ? (4 * glyphId) + 2 : (4 * _horizontalMetricCount) + (2 * (glyphId - _horizontalMetricCount));
        bool present = bearingAt + 2 <= span.Length;
        double bearing = present ? BinaryPrimitives.ReadInt16BigEndian(span[bearingAt..]) : 0;
        return (advance, bearing, present);
    }

    /// <summary>A glyph's metrics; a composite with USE_MY_METRICS takes its component's (OpenType "glyf", composite flags).</summary>
    private GlyphMetrics GetMetrics(int glyphId, int depth)
    {
        if (depth <= _maxDepth && TryGetGlyphData(glyphId, report: false, out ReadOnlySpan<byte> glyph)
            && glyph.Length >= 10 && BinaryPrimitives.ReadInt16BigEndian(glyph) < 0)
        {
            int position = 10;
            int flags;
            do
            {
                if (position + 4 > glyph.Length)
                {
                    break;
                }

                flags = BinaryPrimitives.ReadUInt16BigEndian(glyph[position..]);
                int child = BinaryPrimitives.ReadUInt16BigEndian(glyph[(position + 2)..]);
                if ((flags & CompositeFlags.UseMyMetrics) != 0 && child != glyphId)
                {
                    return GetMetrics(child, depth + 1);
                }

                position += 4 + ComponentArgumentsLength(flags);
            }
            while ((flags & CompositeFlags.MoreComponents) != 0);
        }

        (double advance, double bearing, _) = ReadHorizontalMetrics(glyphId);
        return new GlyphMetrics(advance, bearing);
    }

    /// <summary>The bytes of a glyph's description through "loca"; empty for a glyph without an outline.</summary>
    /// <returns><see langword="false"/> when the glyph id is out of range or its location is unusable (reported when asked).</returns>
    private bool TryGetGlyphData(int glyphId, bool report, out ReadOnlySpan<byte> glyph)
    {
        glyph = default;
        if ((uint)glyphId >= (uint)_glyphCount)
        {
            return false;
        }

        ReadOnlySpan<byte> loca = _loca.Span;
        long start;
        long end;
        if (_longOffsets)
        {
            start = BinaryPrimitives.ReadUInt32BigEndian(loca[(4 * glyphId)..]);
            end = BinaryPrimitives.ReadUInt32BigEndian(loca[(4 * (glyphId + 1))..]);
        }
        else
        {
            start = BinaryPrimitives.ReadUInt16BigEndian(loca[(2 * glyphId)..]) * 2L;
            end = BinaryPrimitives.ReadUInt16BigEndian(loca[(2 * (glyphId + 1))..]) * 2L;
        }

        if (start == end)
        {
            return true;
        }

        int length = _glyf.Length;
        if (start >= length)
        {
            if (report)
            {
                ReportGlyph(glyphId, "its \"loca\" offset lies past the end of \"glyf\"; it has no outline");
            }

            return false;
        }

        if (end < start || end > length)
        {
            if (report)
            {
                ReportGlyph(glyphId, "its \"loca\" end offset is before its start or past the end of \"glyf\"; its data is bounded by the end of \"glyf\"");
            }

            end = length;
        }

        glyph = _glyf.Span[(int)start..(int)end];
        return true;
    }

    private void ReportGlyph(int glyphId, string problem) => _context.Report(
        DiagnosticCodes.FontGlyphInvalid,
        DiagnosticSeverity.Warning,
        string.Create(CultureInfo.InvariantCulture, $"Glyph {glyphId} of the TrueType program deviates from the OpenType \"glyf\" table format: {problem}."));

    private static void ReportTable(FontProgramContext context, string table, string problem) => context.Report(
        DiagnosticCodes.FontTableInvalid,
        DiagnosticSeverity.Warning,
        $"The TrueType program's \"{table}\" table {problem}.");

    /// <summary>The length of a component record after its flags and glyph index: arguments and transform.</summary>
    private static int ComponentArgumentsLength(int flags)
    {
        int length = (flags & CompositeFlags.ArgsAreWords) != 0 ? 4 : 2;
        if ((flags & CompositeFlags.HaveScale) != 0)
        {
            length += 2;
        }
        else if ((flags & CompositeFlags.HaveXAndYScale) != 0)
        {
            length += 4;
        }
        else if ((flags & CompositeFlags.HaveTwoByTwo) != 0)
        {
            length += 8;
        }

        return length;
    }

    /// <summary>The component flags of a composite glyph description (OpenType "glyf" table).</summary>
    private static class CompositeFlags
    {
        public const int ArgsAreWords = 0x0001;
        public const int ArgsAreXYValues = 0x0002;
        public const int HaveScale = 0x0008;
        public const int MoreComponents = 0x0020;
        public const int HaveXAndYScale = 0x0040;
        public const int HaveTwoByTwo = 0x0080;
        public const int UseMyMetrics = 0x0200;
        public const int ScaledComponentOffset = 0x0800;
        public const int UnscaledComponentOffset = 0x1000;
    }
}
