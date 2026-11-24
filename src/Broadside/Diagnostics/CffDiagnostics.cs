namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about CFF and CID-keyed CFF font programs.</summary>
/// <remarks>
/// <para>Adobe Technical Note #5176. Each code is reported under <see cref="DiagnosticArea.Cff"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class CffDiagnostics
{
    /// <summary>Gets the <c>FontCffHeaderInvalid</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCffHeaderInvalid { get; } = new(nameof(FontCffHeaderInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176",
        "The program does not start with a valid CFF header, is a CFF2 program, or has extra bytes before the header. Extra leading bytes are skipped; otherwise the program is not usable.");

    /// <summary>Gets the <c>FontCffIndexOutOfRange</c> code: an INDEX starts outside the program; it is read as empty.</summary>
    /// <remarks>Adobe Technical Note #5176 §5</remarks>
    public static DiagnosticCode FontCffIndexOutOfRange { get; } = new(nameof(FontCffIndexOutOfRange), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176 §5",
        "A CFF INDEX starts outside the font program. It is read as empty.");

    /// <summary>
    /// Gets the <c>FontCffIndexTruncated</c> code: the program ends inside an INDEX's header or offset array; the objects whose offsets
    /// are readable are kept.
    /// </summary>
    /// <remarks>Adobe Technical Note #5176 §5</remarks>
    public static DiagnosticCode FontCffIndexTruncated { get; } = new(nameof(FontCffIndexTruncated), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176 §5",
        "A CFF INDEX is cut short by the end of the program. The objects whose offsets are readable are used.");

    /// <summary>Gets the <c>FontCffIndexOffSizeInvalid</c> code: an INDEX's offSize is not 1 to 4; it is read as empty.</summary>
    /// <remarks>Adobe Technical Note #5176 §5, Table 7</remarks>
    public static DiagnosticCode FontCffIndexOffSizeInvalid { get; } = new(nameof(FontCffIndexOffSizeInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176 §5, Table 7",
        "A CFF INDEX declares an offSize outside the range 1 to 4. It is read as empty.");

    /// <summary>
    /// Gets the <c>FontCffIndexOffsetsInvalid</c> code: an INDEX's offsets do not start at 1, decrease or point past the program;
    /// each object is clamped into the data.
    /// </summary>
    /// <remarks>Adobe Technical Note #5176 §5</remarks>
    public static DiagnosticCode FontCffIndexOffsetsInvalid { get; } = new(nameof(FontCffIndexOffsetsInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176 §5",
        "The offset array of a CFF INDEX is not valid, for example offsets that decrease or point outside the data. The affected objects are clamped or skipped.");

    /// <summary>Gets the <c>FontCffDictInvalid</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCffDictInvalid { get; } = new(nameof(FontCffDictInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176",
        "A CFF Top DICT, Font DICT or Private DICT has malformed or unusable entries, such as a FontMatrix that cannot be inverted. Defaults or a repaired value are used.");

    /// <summary>Gets the <c>FontCffPrivateMissing</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCffPrivateMissing { get; } = new(nameof(FontCffPrivateMissing), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176",
        "A CFF font has no Private entry, or its Private DICT lies outside the program or runs past its end. Private DICT defaults are used, or the part present is read.");

    /// <summary>Gets the <c>FontCffMultipleFonts</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCffMultipleFonts { get; } = new(nameof(FontCffMultipleFonts), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176",
        "An embedded CFF program holds more than one font, where exactly one is required. The first font is read.");

    /// <summary>Gets the <c>FontCffCharsetInvalid</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCffCharsetInvalid { get; } = new(nameof(FontCffCharsetInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176",
        "The CFF charset is malformed or inconsistent with the glyph count. It is repaired where possible.");

    /// <summary>Gets the <c>FontCffEncodingInvalid</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCffEncodingInvalid { get; } = new(nameof(FontCffEncodingInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176",
        "The CFF built-in encoding is malformed. It is repaired where possible.");

    /// <summary>Gets the <c>FontCff2Unsupported</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCff2Unsupported { get; } = new(nameof(FontCff2Unsupported), DiagnosticArea.Cff, DiagnosticSeverity.Information, "Adobe Technical Note #5176",
        "An OpenType font holds CFF2 variable outlines, which are not read. The font's glyphs are not available.");

    /// <summary>Gets the <c>FontCffFdArrayMissing</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176 §18-19</remarks>
    public static DiagnosticCode FontCffFdArrayMissing { get; } = new(nameof(FontCffFdArrayMissing), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176 §18-19",
        "A CID-keyed CFF font has no FDArray, or its FDArray holds no Font DICT. Its glyphs are read with the Private DICT defaults.");

    /// <summary>Gets the <c>FontCffFdSelectInvalid</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176 §18-19</remarks>
    public static DiagnosticCode FontCffFdSelectInvalid { get; } = new(nameof(FontCffFdSelectInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176 §18-19",
        "The FDSelect of a CID-keyed CFF font is malformed. Glyphs it does not assign use the first Font DICT.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        FontCffHeaderInvalid,
        FontCffIndexOutOfRange,
        FontCffIndexTruncated,
        FontCffIndexOffSizeInvalid,
        FontCffIndexOffsetsInvalid,
        FontCffDictInvalid,
        FontCffPrivateMissing,
        FontCffMultipleFonts,
        FontCffCharsetInvalid,
        FontCffEncodingInvalid,
        FontCff2Unsupported,
        FontCffFdArrayMissing,
        FontCffFdSelectInvalid,
    ];
}
