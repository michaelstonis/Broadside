namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about CFF and CID-keyed CFF font programs.</summary>
/// <remarks>
/// <para>Adobe Technical Note #5176. Each code is reported under <see cref="DiagnosticArea.Cff"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class CffDiagnostics
{
    /// <summary>Gets the <c>FontCffHeaderInvalid</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCffHeaderInvalid { get; } = new(nameof(FontCffHeaderInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176");

    /// <summary>Gets the <c>FontCffIndexOutOfRange</c> code: an INDEX starts outside the program; it is read as empty.</summary>
    /// <remarks>Adobe Technical Note #5176 §5</remarks>
    public static DiagnosticCode FontCffIndexOutOfRange { get; } = new(nameof(FontCffIndexOutOfRange), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176 §5");

    /// <summary>
    /// Gets the <c>FontCffIndexTruncated</c> code: the program ends inside an INDEX's header or offset array; the objects whose offsets
    /// are readable are kept.
    /// </summary>
    /// <remarks>Adobe Technical Note #5176 §5</remarks>
    public static DiagnosticCode FontCffIndexTruncated { get; } = new(nameof(FontCffIndexTruncated), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176 §5");

    /// <summary>Gets the <c>FontCffIndexOffSizeInvalid</c> code: an INDEX's offSize is not 1 to 4; it is read as empty.</summary>
    /// <remarks>Adobe Technical Note #5176 §5, Table 7</remarks>
    public static DiagnosticCode FontCffIndexOffSizeInvalid { get; } = new(nameof(FontCffIndexOffSizeInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176 §5, Table 7");

    /// <summary>
    /// Gets the <c>FontCffIndexOffsetsInvalid</c> code: an INDEX's offsets do not start at 1, decrease or point past the program;
    /// each object is clamped into the data.
    /// </summary>
    /// <remarks>Adobe Technical Note #5176 §5</remarks>
    public static DiagnosticCode FontCffIndexOffsetsInvalid { get; } = new(nameof(FontCffIndexOffsetsInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176 §5");

    /// <summary>Gets the <c>FontCffDictInvalid</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCffDictInvalid { get; } = new(nameof(FontCffDictInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176");

    /// <summary>Gets the <c>FontCffPrivateMissing</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCffPrivateMissing { get; } = new(nameof(FontCffPrivateMissing), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176");

    /// <summary>Gets the <c>FontCffMultipleFonts</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCffMultipleFonts { get; } = new(nameof(FontCffMultipleFonts), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176");

    /// <summary>Gets the <c>FontCffCharsetInvalid</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCffCharsetInvalid { get; } = new(nameof(FontCffCharsetInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176");

    /// <summary>Gets the <c>FontCffEncodingInvalid</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCffEncodingInvalid { get; } = new(nameof(FontCffEncodingInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176");

    /// <summary>Gets the <c>FontCff2Unsupported</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCff2Unsupported { get; } = new(nameof(FontCff2Unsupported), DiagnosticArea.Cff, DiagnosticSeverity.Information, "Adobe Technical Note #5176");

    /// <summary>Gets the <c>FontCffFdArrayMissing</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176 §18-19</remarks>
    public static DiagnosticCode FontCffFdArrayMissing { get; } = new(nameof(FontCffFdArrayMissing), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176 §18-19");

    /// <summary>Gets the <c>FontCffFdSelectInvalid</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176 §18-19</remarks>
    public static DiagnosticCode FontCffFdSelectInvalid { get; } = new(nameof(FontCffFdSelectInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176 §18-19");

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
