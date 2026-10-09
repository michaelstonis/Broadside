using System.Buffers.Binary;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>
/// The widths, vertical metrics, CID-to-glyph map and program of a CIDFont, computed once from its dictionary, with the COS objects
/// they came from and their versions so that a change to any of them is noticed.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.7.4.1 (Table 115) and §9.7.4.3. <c>W</c> and <c>W2</c> become interval tables (a range costs one entry however
/// many CIDs it spans); where a CID is given twice, the first specification is used (§9.7.4.3). Immutable once built.
/// </remarks>
internal sealed class CidFontMetrics
{
    private const int MaxCid = 0xFFFF;

    private readonly CosObject[] _sources;
    private readonly int[] _versions;
    private readonly IntervalTable _widths;
    private readonly double[] _widthValues;
    private readonly IntervalTable _vertical;
    private readonly double[] _verticalValues;
    private readonly byte[]? _glyphMap;

    private CidFontMetrics(Builder builder)
    {
        _sources = [.. builder.Sources];
        _versions = [.. builder.Sources.Select(VersionOf)];
        DefaultWidth = builder.DefaultWidth;
        DefaultPositionY = builder.DefaultPositionY;
        DefaultVerticalAdvance = builder.DefaultVerticalAdvance;
        _widths = builder.Widths;
        _widthValues = [.. builder.WidthValues];
        _vertical = builder.Vertical;
        _verticalValues = [.. builder.VerticalValues];
        _glyphMap = builder.GlyphMap;
        IdentityGlyphMap = builder.GlyphMap is null;
        Program = builder.Program;
        IsEmbedded = builder.IsEmbedded;
    }

    /// <summary>Gets <c>DW</c>.</summary>
    public double DefaultWidth { get; }

    /// <summary>Gets the first element of <c>DW2</c>: v.y.</summary>
    public double DefaultPositionY { get; }

    /// <summary>Gets the second element of <c>DW2</c>: w1.y.</summary>
    public double DefaultVerticalAdvance { get; }

    /// <summary>Gets a value indicating whether CIDs are glyph ids (<c>CIDToGIDMap</c> <c>/Identity</c>, or absent).</summary>
    public bool IdentityGlyphMap { get; }

    /// <summary>Gets the parsed program of the descriptor's font file, if any.</summary>
    public FontProgram? Program { get; }

    /// <summary>Gets a value indicating whether the descriptor holds a font file.</summary>
    public bool IsEmbedded { get; }

    /// <summary>Gets a value indicating whether no source has changed since the metrics were built.</summary>
    public bool IsCurrent
    {
        get
        {
            for (int index = 0; index < _sources.Length; index++)
            {
                if (VersionOf(_sources[index]) != _versions[index])
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Computes the metrics of a CIDFont, recording deviations on it.</summary>
    public static CidFontMetrics Build(PdfCidFont font) => new(new Builder(font).Run());

    /// <summary>The width w0 of a CID (§9.7.4.3): its <c>W</c> entry, else <c>DW</c>.</summary>
    public double GetWidth(int cid) =>
        cid is >= 0 and <= MaxCid && _widths.TryFind((uint)cid, out int index) ? _widthValues[index] : DefaultWidth;

    /// <summary>The vertical metrics of a CID (§9.7.4.3): its <c>W2</c> entry, else <c>DW2</c> with v.x = w0 / 2.</summary>
    public CidVerticalMetrics GetVerticalMetrics(int cid)
    {
        if (cid is >= 0 and <= MaxCid && _vertical.TryFind((uint)cid, out int index))
        {
            return new CidVerticalMetrics(_verticalValues[index], _verticalValues[index + 1], _verticalValues[index + 2]);
        }

        return new CidVerticalMetrics(DefaultVerticalAdvance, GetWidth(cid) / 2, DefaultPositionY);
    }

    /// <summary>Maps a CID through a <c>CIDToGIDMap</c> stream (§9.7.4.1); <see langword="false"/> past its end.</summary>
    public bool TryMapGlyph(int cid, out int glyphId)
    {
        if (_glyphMap is null)
        {
            glyphId = cid;
            return true;
        }

        long offset = 2L * cid;
        if (cid >= 0 && offset + 1 < _glyphMap.Length)
        {
            glyphId = BinaryPrimitives.ReadUInt16BigEndian(_glyphMap.AsSpan((int)offset, 2));
            return true;
        }

        glyphId = 0;
        return false;
    }

    private static int VersionOf(CosObject source) => source switch
    {
        CosDictionary dictionary => dictionary.Version,
        CosArray array => array.Version,
        CosStream stream => stream.Version,
        _ => 0,
    };

    /// <summary>One computation: ISO 32000-2 §9.7.4.1, Table 115, and §9.7.4.3.</summary>
    private sealed class Builder(PdfCidFont font)
    {
        public List<CosObject> Sources { get; } = [font.Dictionary];

        public double DefaultWidth { get; private set; } = 1000;

        public double DefaultPositionY { get; private set; } = 880;

        public double DefaultVerticalAdvance { get; private set; } = -1000;

        public IntervalTable Widths { get; private set; } = IntervalTable.Empty;

        public List<double> WidthValues { get; } = [];

        public IntervalTable Vertical { get; private set; } = IntervalTable.Empty;

        public List<double> VerticalValues { get; } = [];

        public byte[]? GlyphMap { get; private set; }

        public FontProgram? Program { get; private set; }

        public bool IsEmbedded { get; private set; }

        public Builder Run()
        {
            ReadDefaultWidth();
            ReadWidths();
            ReadDefaultVertical();
            ReadVertical();
            ReadProgram();
            ReadGlyphMap();
            return this;
        }

        private void ReadDefaultWidth()
        {
            switch (font.Get(CompositeFontNames.DW))
            {
                case null:
                    break;
                case CosNumber number:
                    DefaultWidth = number.ToDouble();
                    break;
                default:
                    Report(DiagnosticCodes.CidFontWidthsInvalid, "DW shall be a number (ISO 32000-2 §9.7.4.1, Table 115); 1000 is used.");
                    break;
            }
        }

        private void ReadDefaultVertical()
        {
            switch (font.Get(CompositeFontNames.DW2))
            {
                case null:
                    break;
                case CosArray array when array.Count == 2 && Number(array[0]) is { } y && Number(array[1]) is { } advance:
                    Sources.Add(array);
                    DefaultPositionY = y;
                    DefaultVerticalAdvance = advance;
                    break;
                default:
                    Report(DiagnosticCodes.CidFontVerticalMetricsInvalid, "DW2 shall be an array of two numbers (ISO 32000-2 §9.7.4.1, Table 115); [880 -1000] is used.");
                    break;
            }
        }

        /// <summary><c>W</c>: groups <c>c [w1 … wn]</c> and <c>cfirst clast w</c> (§9.7.4.3).</summary>
        private void ReadWidths()
        {
            if (font.Get(CompositeFontNames.W) is not { } value)
            {
                return;
            }

            if (value is not CosArray array)
            {
                Report(DiagnosticCodes.CidFontWidthsInvalid, "W shall be an array (ISO 32000-2 §9.7.4.1, Table 115); DW is used for every CID.");
                return;
            }

            Sources.Add(array);
            var entries = new List<IntervalTable.Interval>();
            ReadGroups(array, 1, entries, WidthValues, CidFontWidthsMessage, DiagnosticCodes.CidFontWidthsInvalid);
            Widths = IntervalTable.Build(entries, increment: false, laterWins: false);
        }

        /// <summary><c>W2</c>: groups <c>c [w1y v1x v1y …]</c> and <c>cfirst clast w1y v1x v1y</c> (§9.7.4.3).</summary>
        private void ReadVertical()
        {
            if (font.Get(CompositeFontNames.W2) is not { } value)
            {
                return;
            }

            if (value is not CosArray array)
            {
                Report(DiagnosticCodes.CidFontVerticalMetricsInvalid, "W2 shall be an array (ISO 32000-2 §9.7.4.1, Table 115); DW2 is used for every CID.");
                return;
            }

            Sources.Add(array);
            var entries = new List<IntervalTable.Interval>();
            ReadGroups(array, 3, entries, VerticalValues, CidFontVerticalMessage, DiagnosticCodes.CidFontVerticalMetricsInvalid);
            Vertical = IntervalTable.Build(entries, increment: false, laterWins: false);
        }

        private const string CidFontWidthsMessage = "The W array is malformed (ISO 32000-2 §9.7.4.3)";
        private const string CidFontVerticalMessage = "The W2 array is malformed (ISO 32000-2 §9.7.4.3)";

        /// <summary>Reads the groups of a <c>W</c> (<paramref name="size"/> 1) or <c>W2</c> (3) array into intervals whose values index <paramref name="values"/>.</summary>
        private void ReadGroups(CosArray array, int size, List<IntervalTable.Interval> entries, List<double> values, string problem, string code)
        {
            int index = 0;
            while (index < array.Count)
            {
                if (Integer(array[index]) is not { } first)
                {
                    Report(code, $"{problem}: a group shall start with an integer CID; the rest is ignored.");
                    return;
                }

                index++;
                if (index < array.Count && font.Document.Resolve(array[index]) is CosArray run)
                {
                    Sources.Add(run);
                    index++;
                    if (run.Count % size != 0)
                    {
                        Report(code, $"{problem}: an array of {run.Count} numbers is not a whole number of {size}-number groups; the extra numbers are ignored.");
                    }

                    for (int element = 0; element + size <= run.Count; element += size)
                    {
                        long cid = first + (element / size);
                        if (!TryReadNumbers(run, element, size, out double a, out double b, out double c))
                        {
                            Report(code, $"{problem}: an element that is not a number; that CID keeps its default.");
                            continue;
                        }

                        if (cid is >= 0 and <= MaxCid)
                        {
                            entries.Add(new IntervalTable.Interval((uint)cid, (uint)cid, Add(values, size, a, b, c)));
                        }
                    }

                    continue;
                }

                if (index + size < array.Count && Integer(array[index]) is { } last && TryReadNumbers(array, index + 1, size, out double x, out double y, out double z))
                {
                    index += 1 + size;
                    if (last < first)
                    {
                        Report(code, $"{problem}: a range whose last CID is below its first; ignored.");
                        continue;
                    }

                    long low = Math.Max(first, 0);
                    long high = Math.Min(last, MaxCid);
                    if (low <= high)
                    {
                        entries.Add(new IntervalTable.Interval((uint)low, (uint)high, Add(values, size, x, y, z)));
                    }

                    continue;
                }

                Report(code, $"{problem}: an incomplete group; the rest is ignored.");
                return;
            }
        }

        private static int Add(List<double> values, int size, double a, double b, double c)
        {
            int index = values.Count;
            values.Add(a);
            if (size == 3)
            {
                values.Add(b);
                values.Add(c);
            }

            return index;
        }

        private bool TryReadNumbers(CosArray array, int start, int size, out double a, out double b, out double c)
        {
            a = b = c = 0;
            if (start + size > array.Count || Number(array[start]) is not { } first)
            {
                return false;
            }

            a = first;
            if (size == 1)
            {
                return true;
            }

            if (Number(array[start + 1]) is not { } second || Number(array[start + 2]) is not { } third)
            {
                return false;
            }

            b = second;
            c = third;
            return true;
        }

        private void ReadProgram()
        {
            if (font.DescriptorDictionary is not { } descriptor)
            {
                return;
            }

            Sources.Add(descriptor);
            IsEmbedded = font.IsEmbeddedIn(descriptor);
            Program = font.GetProgram(descriptor);
        }

        /// <summary><c>CIDToGIDMap</c> (§9.7.4.1, §9.7.4.2): only an embedded CIDFontType2 uses it.</summary>
        private void ReadGlyphMap()
        {
            if (font.CidFontType != PdfCidFontType.CidFontType2 || !IsEmbedded)
            {
                return;
            }

            switch (font.Get(CompositeFontNames.CidToGidMap))
            {
                case CosName name when name.Equals(CompositeFontNames.Identity):
                    break;
                case CosStream stream:
                    Sources.Add(stream);
                    ReadOnlyMemory<byte> data = font.Document.DecodeStream(stream);
                    if (data.Length % 2 == 1)
                    {
                        Report(DiagnosticCodes.CidToGidMapInvalid, "A CIDToGIDMap stream has an odd number of bytes (ISO 32000-2 §9.7.4.1, Table 115); the last byte is ignored.");
                    }

                    GlyphMap = data[..(data.Length & ~1)].ToArray();
                    break;
                case null:
                    Report(DiagnosticCodes.CidToGidMapMissing, "An embedded CIDFontType2 shall have a CIDToGIDMap (ISO 32000-2 §9.7.4.1, Table 115); CIDs are used as glyph ids.");
                    break;
                default:
                    Report(DiagnosticCodes.CidToGidMapInvalid, "CIDToGIDMap shall be a stream or /Identity (ISO 32000-2 §9.7.4.1, Table 115); CIDs are used as glyph ids.");
                    break;
            }
        }

        private long? Integer(CosObject value) => font.Document.Resolve(value) is CosInteger integer ? integer.Value : null;

        private double? Number(CosObject value) => font.Document.Resolve(value) is CosNumber number ? number.ToDouble() : null;

        private void Report(string code, string message) => font.Report(code, DiagnosticSeverity.Warning, message);
    }
}
