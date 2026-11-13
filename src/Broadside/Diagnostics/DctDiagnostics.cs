namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the DCTDecode filter.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.4.8; ITU-T T.81. Each code is reported under <see cref="DiagnosticArea.Dct"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class DctDiagnostics
{
    /// <summary>Bytes before the SOI marker were skipped.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctLeadingJunk { get; } = new(nameof(DctLeadingJunk), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>Bytes that are not a marker were skipped between segments or after an entropy-coded segment.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctExtraneousData { get; } = new(nameof(DctExtraneousData), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>A marker segment's length or content is invalid; it was skipped or resynchronized.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctSegmentInvalid { get; } = new(nameof(DctSegmentInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>The frame header is missing or unusable.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctFrameInvalid { get; } = new(nameof(DctFrameInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>A second frame header; the first frame is kept.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctFrameRepeated { get; } = new(nameof(DctFrameRepeated), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>The lossless or hierarchical coding process, which PDF does not use.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctProcessUnsupported { get; } = new(nameof(DctProcessUnsupported), DiagnosticArea.Dct, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>A table a scan needs was never defined.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctTableMissing { get; } = new(nameof(DctTableMissing), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>A Huffman or quantization table definition is invalid.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctTableInvalid { get; } = new(nameof(DctTableInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>A scan header is invalid; the scan, or the invalid parameters, were ignored.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctScanInvalid { get; } = new(nameof(DctScanInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>The entropy-coded data holds a bit sequence that is not a Huffman code.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctDataInvalid { get; } = new(nameof(DctDataInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>A restart marker is missing or out of sequence.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctRestartInvalid { get; } = new(nameof(DctRestartInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>The data ends before every block is decoded; the rest is mid-grey.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctTruncated { get; } = new(nameof(DctTruncated), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>The data ends without the EOI marker after all scans.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctEndMissing { get; } = new(nameof(DctEndMissing), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>A component count PDF does not allow (2); decoded without a colour transform.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctComponentCountInvalid { get; } = new(nameof(DctComponentCountInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>The frame's number of lines is 0 and came from a DNL segment or the image dictionary.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctLinesFromDnl { get; } = new(nameof(DctLinesFromDnl), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>The frame has 12-bit samples, which the filter delivers reduced to 8 bits (ISO 32000-2 Table 87).</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctPrecisionReduced { get; } = new(nameof(DctPrecisionReduced), DiagnosticArea.Dct, DiagnosticSeverity.Information, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>The APP14 transform code does not fit the component count; YCbCr (three) or YCCK (four) is assumed.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctAdobeTransformInvalid { get; } = new(nameof(DctAdobeTransformInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>Three components identified R, G, B without APP14 or <c>ColorTransform</c> are read as RGB, not with the default ColorTransform 1 of ISO 32000-2 Table 13 (libjpeg compatibility).</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctColorTransformInferred { get; } = new(nameof(DctColorTransformInferred), DiagnosticArea.Dct, DiagnosticSeverity.Information, "ISO 32000-2 §7.4.8; ITU-T T.81");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        DctLeadingJunk,
        DctExtraneousData,
        DctSegmentInvalid,
        DctFrameInvalid,
        DctFrameRepeated,
        DctProcessUnsupported,
        DctTableMissing,
        DctTableInvalid,
        DctScanInvalid,
        DctDataInvalid,
        DctRestartInvalid,
        DctTruncated,
        DctEndMissing,
        DctComponentCountInvalid,
        DctLinesFromDnl,
        DctPrecisionReduced,
        DctAdobeTransformInvalid,
        DctColorTransformInferred,
    ];
}
