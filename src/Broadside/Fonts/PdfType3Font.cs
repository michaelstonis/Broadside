using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>A Type 3 font, whose glyphs are content streams: a live view over its font dictionary.</summary>
/// <remarks>
/// ISO 32000-2 §9.6.4. Its encoding is entirely defined by its <c>Encoding</c> entry (§9.6.5.3): the <c>Differences</c> of the
/// encoding dictionary, over the predefined encoding its <c>BaseEncoding</c> names, if any. Its widths are in its glyph space,
/// which the font matrix maps to text space; it never takes Standard 14 metrics. The content interpreter runs a shown glyph's
/// <see cref="GetCharProc">description</see> for a processor that enters it (<see cref="Content.ContentProcessor.BeginType3Glyph"/>).
/// Deviations in the dictionary (a malformed <c>FontMatrix</c>, a missing <c>CharProcs</c>) are recorded on the font's object when
/// its glyph descriptions are first looked up.
/// </remarks>
public sealed class PdfType3Font : PdfSimpleFont
{
    private static readonly CosName FontMatrixKey = new("FontMatrix");
    private static readonly CosName CharProcsKey = new("CharProcs");

    private volatile CharProcTable? _procedures;

    internal PdfType3Font(PdfDocument document, CosDictionary dictionary, CosReference? reference)
        : base(document, dictionary, reference, PdfFontType.Type3)
    {
    }

    /// <summary>Gets the font matrix, which maps glyph space to text space; <c>[0.001 0 0 0.001 0 0]</c> when absent or malformed.</summary>
    /// <remarks>ISO 32000-2 §9.6.4, Table 110 (<c>FontMatrix</c>, required).</remarks>
    public Matrix FontMatrix => AnnotationValues.ReadMatrix(Document, Get(FontMatrixKey)) ?? ThousandthMatrix;

    /// <summary>
    /// Gets the font bounding box, in glyph space: the smallest rectangle enclosing every glyph painted at the same origin. All
    /// four numbers zero (or the entry missing or malformed) means no assumption is made about glyph sizes.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.6.4, Table 110 (<c>FontBBox</c>, required).</remarks>
    public PdfRectangle FontBBox => AnnotationValues.ReadRectangle(Document, Get(FontNames.FontBBox)) ?? default;

    /// <summary>Gets the resources the glyph descriptions use, or <see langword="null"/> when the font has none of its own.</summary>
    /// <remarks>
    /// ISO 32000-2 §9.6.4, Table 110 (<c>Resources</c>): without them, names resolve in the resources of the page or form that shows
    /// the glyph (normative in PDF 2.0).
    /// </remarks>
    public CosDictionary? Resources => Get(KnownNames.Resources) as CosDictionary;

    /// <inheritdoc/>
    internal override bool CanUseStandard14 => false;

    /// <inheritdoc/>
    internal override Matrix GlyphSpaceMatrix => FontMatrix;

    /// <summary>Returns the glyph description of a character code: the <c>CharProcs</c> stream its glyph name selects, or <see langword="null"/>.</summary>
    /// <param name="code">The character code.</param>
    /// <returns>The content stream, or <see langword="null"/> when the encoding or <c>CharProcs</c> has none.</returns>
    /// <remarks>ISO 32000-2 §9.6.4, Table 110 (<c>CharProcs</c>).</remarks>
    public CosStream? GetCharProc(byte code) => FindCharProc(code, out _);

    /// <summary>Returns the glyph description of a character code and its object reference (for diagnostics); allocation-free once built.</summary>
    /// <param name="code">The character code.</param>
    /// <param name="reference">The reference of the stream, when <c>CharProcs</c> refers to it indirectly.</param>
    /// <returns>The content stream, or <see langword="null"/>.</returns>
    internal CosStream? FindCharProc(byte code, out CosReference? reference)
    {
        CharProcTable table = Procedures;
        reference = table.References[code];
        return table.Streams[code];
    }

    /// <summary>Gets the glyph description of each code, rebuilt when the names or the <c>CharProcs</c> dictionary changed.</summary>
    private CharProcTable Procedures
    {
        get
        {
            SimpleFontMetrics metrics = Metrics;
            CosObject? procedures = Get(CharProcsKey);
            CharProcTable? table = _procedures;
            if (table is null || !table.IsCurrent(metrics, procedures))
            {
                table = CharProcTable.Build(this, metrics, procedures);
                _procedures = table;
            }

            return table;
        }
    }

    /// <summary>The 256 glyph descriptions of a Type 3 font (§9.6.4, Table 110 <c>CharProcs</c>), immutable once built.</summary>
    private sealed class CharProcTable
    {
        private readonly SimpleFontMetrics _metrics;
        private readonly CosObject? _source;
        private readonly int _version;

        private CharProcTable(SimpleFontMetrics metrics, CosObject? source)
        {
            _metrics = metrics;
            _source = source;
            _version = source is CosDictionary dictionary ? dictionary.Version : 0;
        }

        public CosStream?[] Streams { get; } = new CosStream?[256];

        public CosReference?[] References { get; } = new CosReference?[256];

        public bool IsCurrent(SimpleFontMetrics metrics, CosObject? source) =>
            ReferenceEquals(metrics, _metrics) && ReferenceEquals(source, _source) && (source is not CosDictionary dictionary || dictionary.Version == _version);

        public static CharProcTable Build(PdfType3Font font, SimpleFontMetrics metrics, CosObject? source)
        {
            var table = new CharProcTable(metrics, source);
            if (font.Get(FontMatrixKey) is { } matrix && AnnotationValues.ReadMatrix(font.Document, matrix) is null)
            {
                font.Report(
                    DiagnosticCodes.FontType3FontMatrixInvalid,
                    DiagnosticSeverity.Warning,
                    "A Type 3 font's FontMatrix shall be an array of six numbers (ISO 32000-2 §9.6.4, Table 110); [0.001 0 0 0.001 0 0] is used.");
            }

            if (source is not CosDictionary procedures)
            {
                font.Report(
                    DiagnosticCodes.FontType3CharProcsInvalid,
                    DiagnosticSeverity.Warning,
                    "A Type 3 font shall have a CharProcs dictionary (ISO 32000-2 §9.6.4, Table 110); its glyphs paint nothing.");
                return table;
            }

            var byName = new Dictionary<string, CosObject>(procedures.Count, StringComparer.Ordinal);
            for (int index = 0; index < procedures.Count; index++)
            {
                KeyValuePair<CosName, CosObject> pair = procedures.GetAt(index);
                byName.TryAdd(pair.Key.Value, pair.Value);
            }

            bool invalid = false;
            for (int code = 0; code < 256; code++)
            {
                if (!byName.TryGetValue(metrics.Names[code], out CosObject? value))
                {
                    continue;
                }

                if (font.Document.Resolve(value) is CosStream stream)
                {
                    table.Streams[code] = stream;
                    table.References[code] = value as CosReference;
                }
                else
                {
                    invalid |= font.Document.Resolve(value) is not CosNull;
                }
            }

            if (invalid)
            {
                font.Report(
                    DiagnosticCodes.FontType3CharProcsInvalid,
                    DiagnosticSeverity.Warning,
                    "A Type 3 font's CharProcs entry is not a content stream (ISO 32000-2 §9.6.4, Table 110); its glyph paints nothing.");
            }

            return table;
        }
    }

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §9.6.4: the width is in glyph space; the font matrix maps it to text space.</remarks>
    internal override double GetHorizontalDisplacement(byte code) => FontMatrix.TransformVector(Metrics.Widths[code], 0).X;
}
