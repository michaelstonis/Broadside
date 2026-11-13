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

    /// <summary>Gets the <c>FontCffIndexInvalid</c> code.</summary>
    /// <remarks>Adobe Technical Note #5176</remarks>
    public static DiagnosticCode FontCffIndexInvalid { get; } = new(nameof(FontCffIndexInvalid), DiagnosticArea.Cff, DiagnosticSeverity.Warning, "Adobe Technical Note #5176");

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
        FontCffIndexInvalid,
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
