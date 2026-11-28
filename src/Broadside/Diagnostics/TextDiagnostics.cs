namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about mapping character codes to Unicode and about reading OCR output (hOCR, ALTO).</summary>
/// <remarks>
/// <para>ISO 32000-2 §9.10. Each code is reported under <see cref="DiagnosticArea.Text"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class TextDiagnostics
{
    /// <summary>Gets the <c>ToUnicodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.3</remarks>
    public static DiagnosticCode ToUnicodeInvalid { get; } = new(nameof(ToUnicodeInvalid), DiagnosticArea.Text, DiagnosticSeverity.Warning, "ISO 32000-2 §9.10.3",
        "A font's ToUnicode entry is not a CMap stream. It is ignored.");

    /// <summary>Gets the <c>ToUnicodeIdentityName</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.3</remarks>
    public static DiagnosticCode ToUnicodeIdentityName { get; } = new(nameof(ToUnicodeIdentityName), DiagnosticArea.Text, DiagnosticSeverity.Warning, "ISO 32000-2 §9.10.3",
        "A font's ToUnicode entry is a name such as Identity instead of a CMap stream. Each code is read as its own UTF-16 value, as other viewers do.");

    /// <summary>Gets the <c>ToUnicodeDestinationInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.3</remarks>
    public static DiagnosticCode ToUnicodeDestinationInvalid { get; } = new(nameof(ToUnicodeDestinationInvalid), DiagnosticArea.Text, DiagnosticSeverity.Warning, "ISO 32000-2 §9.10.3",
        "A destination string in a ToUnicode CMap is not valid, for example not a UTF-16BE string. The mapping is skipped or repaired.");

    /// <summary>Gets the <c>ToUnicodeBfRangeOverflow</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.3</remarks>
    public static DiagnosticCode ToUnicodeBfRangeOverflow { get; } = new(nameof(ToUnicodeBfRangeOverflow), DiagnosticArea.Text, DiagnosticSeverity.Warning, "ISO 32000-2 §9.10.3",
        "The destination of a bfrange in a ToUnicode CMap would overflow its last byte across the range. The last character of the destination is incremented as a whole.");

    /// <summary>Gets the <c>ToUnicodeCidMapping</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.3</remarks>
    public static DiagnosticCode ToUnicodeCidMapping { get; } = new(nameof(ToUnicodeCidMapping), DiagnosticArea.Text, DiagnosticSeverity.Warning, "ISO 32000-2 §9.10.3",
        "A ToUnicode CMap uses CID mappings instead of bfchar and bfrange. Each code maps to the Unicode value of its CID and notdef mappings are ignored.");

    /// <summary>Gets the <c>ToUnicodeCodeLengthMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.2</remarks>
    public static DiagnosticCode ToUnicodeCodeLengthMismatch { get; } = new(nameof(ToUnicodeCodeLengthMismatch), DiagnosticArea.Text, DiagnosticSeverity.Warning, "ISO 32000-2 §9.10.2",
        "The code lengths in a font's ToUnicode CMap differ from the lengths its encoding reads. Codes are matched by value.");

    /// <summary>Gets the <c>GlyphNameNonStandard</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.2</remarks>
    public static DiagnosticCode GlyphNameNonStandard { get; } = new(nameof(GlyphNameNonStandard), DiagnosticArea.Text, DiagnosticSeverity.Information, "ISO 32000-2 §9.10.2",
        "A glyph name uses lowercase hexadecimal digits, which the Adobe Glyph List specification does not allow. The name is read as written.");

    /// <summary>Gets the <c>TextUcs2CmapMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.2</remarks>
    public static DiagnosticCode TextUcs2CmapMissing { get; } = new(nameof(TextUcs2CmapMissing), DiagnosticArea.Text, DiagnosticSeverity.Information, "ISO 32000-2 §9.10.2",
        "Text extraction needs a UCS2 CMap table that is not available. Text for the affected font may be missing until predefined CMaps are installed or a font resolver supplies the table.");

    /// <summary>Gets the <c>TextUnicodeUnmapped</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.2</remarks>
    public static DiagnosticCode TextUnicodeUnmapped { get; } = new(nameof(TextUnicodeUnmapped), DiagnosticArea.Text, DiagnosticSeverity.Information, "ISO 32000-2 §9.10.2",
        "No method defined by the specification can map a character code of a font to Unicode. The extracted text for that code is U+FFFD.");

    /// <summary>Gets the <c>CidToUnicodeResourceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.2</remarks>
    public static DiagnosticCode CidToUnicodeResourceInvalid { get; } = new(nameof(CidToUnicodeResourceInvalid), DiagnosticArea.Text, DiagnosticSeverity.Information, "ISO 32000-2 §9.10.2",
        "A CID to Unicode table supplied by the font resolver deviates from the CMap file format. What could be read is used.");

    /// <summary>Gets the <c>OcrBoundsInvalid</c> code.</summary>
    /// <remarks>hOCR 1.2, the <c>bbox</c> property; ALTO, the <c>HPOS</c>, <c>VPOS</c>, <c>WIDTH</c> and <c>HEIGHT</c> of a <c>String</c>.</remarks>
    public static DiagnosticCode OcrBoundsInvalid { get; } = new(nameof(OcrBoundsInvalid), DiagnosticArea.Text, DiagnosticSeverity.Warning, "hOCR 1.2 bbox; ALTO String",
        "A word of hOCR or ALTO input has a box or coordinate that is missing or not a number. The word is skipped.");

    /// <summary>Gets the <c>OcrPageSizeInvalid</c> code.</summary>
    /// <remarks>hOCR 1.2, the <c>bbox</c> of an <c>ocr_page</c>; ALTO, the <c>WIDTH</c> and <c>HEIGHT</c> of a <c>Page</c>.</remarks>
    public static DiagnosticCode OcrPageSizeInvalid { get; } = new(nameof(OcrPageSizeInvalid), DiagnosticArea.Text, DiagnosticSeverity.Warning, "hOCR 1.2 bbox; ALTO Page",
        "A page of hOCR or ALTO input gives no valid image size. The size is taken from the extent of its words.");

    /// <summary>Gets the <c>OcrConfidenceInvalid</c> code.</summary>
    /// <remarks>hOCR 1.2, the <c>x_wconf</c> property; ALTO, the <c>WC</c> attribute.</remarks>
    public static DiagnosticCode OcrConfidenceInvalid { get; } = new(nameof(OcrConfidenceInvalid), DiagnosticArea.Text, DiagnosticSeverity.Information, "hOCR 1.2 x_wconf; ALTO WC",
        "A word of hOCR or ALTO input has a confidence that is not a number in range. The word is read with confidence 1.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        ToUnicodeInvalid,
        ToUnicodeIdentityName,
        ToUnicodeDestinationInvalid,
        ToUnicodeBfRangeOverflow,
        ToUnicodeCidMapping,
        ToUnicodeCodeLengthMismatch,
        GlyphNameNonStandard,
        TextUcs2CmapMissing,
        TextUnicodeUnmapped,
        CidToUnicodeResourceInvalid,
        OcrBoundsInvalid,
        OcrPageSizeInvalid,
        OcrConfidenceInvalid,
    ];
}
