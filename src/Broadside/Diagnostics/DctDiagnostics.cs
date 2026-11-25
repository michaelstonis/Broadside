namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the DCTDecode filter.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.4.8; ITU-T T.81. Each code is reported under <see cref="DiagnosticArea.Dct"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class DctDiagnostics
{
    /// <summary>Bytes before the SOI marker were skipped.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctLeadingJunk { get; } = new(nameof(DctLeadingJunk), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "The DCT data has bytes before the start-of-image marker. Those bytes are skipped.");

    /// <summary>Bytes that are not a marker were skipped between segments or after an entropy-coded segment.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctExtraneousData { get; } = new(nameof(DctExtraneousData), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "The DCT data has bytes between marker segments that belong to no segment. The extra bytes are skipped.");

    /// <summary>A marker segment's length or content is invalid; it was skipped or resynchronized.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctSegmentInvalid { get; } = new(nameof(DctSegmentInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "A DCT marker segment is malformed, with a length below 2, a too-short restart interval or an odd-length conditioning segment, or a baseline frame declares 12-bit samples. The segment is skipped or read leniently.");

    /// <summary>The frame header is missing or unusable.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctFrameInvalid { get; } = new(nameof(DctFrameInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "The DCT data has no start-of-image marker or no usable frame header, has a scan before its frame header, or has a truncated frame header. The image is not decoded.");

    /// <summary>A second frame header; the first frame is kept.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctFrameRepeated { get; } = new(nameof(DctFrameRepeated), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "The DCT data has a second frame header. Only the first frame is decoded and the later header is ignored.");

    /// <summary>The lossless or hierarchical coding process, which PDF does not use.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctProcessUnsupported { get; } = new(nameof(DctProcessUnsupported), DiagnosticArea.Dct, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "The DCT data uses the lossless or hierarchical JPEG process, which PDF does not use. The image is not decoded.");

    /// <summary>A table a scan needs was never defined.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctTableMissing { get; } = new(nameof(DctTableMissing), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "A DCT scan or component refers to a Huffman or quantization table that was never defined. A missing Huffman table is replaced by the standard typical table; a missing quantization table leaves the image undecoded.");

    /// <summary>A Huffman or quantization table definition is invalid.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctTableInvalid { get; } = new(nameof(DctTableInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "A DCT table is malformed or unusual, such as a truncated Huffman table, an invalid quantization or arithmetic conditioning table, or a 16-bit quantization table in an 8-bit frame. Invalid tables are ignored and 16-bit values are used.");

    /// <summary>A scan header is invalid; the scan, or the invalid parameters, were ignored.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctScanInvalid { get; } = new(nameof(DctScanInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "A DCT scan header is invalid, names a missing component or table, or has bad progressive or sequential parameters. The scan is skipped or its bad parameters are ignored.");

    /// <summary>The entropy-coded data holds a bit sequence that is not a Huffman code.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctDataInvalid { get; } = new(nameof(DctDataInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "The entropy-coded data of a DCT scan is damaged. The decoder reports the problem and continues with the data it can decode.");

    /// <summary>A restart marker is missing or out of sequence.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctRestartInvalid { get; } = new(nameof(DctRestartInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "The DCT data has a restart marker that is not the expected one. The decoder resynchronizes at the marker.");

    /// <summary>The data ends before every block is decoded; the rest is mid-grey.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctTruncated { get; } = new(nameof(DctTruncated), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "The DCT data ends early, before the first scan or inside a marker segment. The decoder keeps what it decoded, or the image is not decoded if no scan was reached.");

    /// <summary>The data ends without the EOI marker after all scans.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctEndMissing { get; } = new(nameof(DctEndMissing), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "The DCT data ends without the end-of-image marker. The data is decoded as far as it goes.");

    /// <summary>A component count PDF does not allow (2); decoded without a colour transform.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctComponentCountInvalid { get; } = new(nameof(DctComponentCountInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "The DCT data has two components, which PDF does not allow. The components are decoded without a color transform.");

    /// <summary>The frame's number of lines is 0 and came from a DNL segment or the image dictionary.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctLinesFromDnl { get; } = new(nameof(DctLinesFromDnl), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "The DCT frame declares zero lines. The height comes from the DNL segment when present, otherwise from the image dictionary Height.");

    /// <summary>The frame has 12-bit samples, which the filter delivers reduced to 8 bits (ISO 32000-2 Table 87).</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctPrecisionReduced { get; } = new(nameof(DctPrecisionReduced), DiagnosticArea.Dct, DiagnosticSeverity.Information, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "The DCT data has 12-bit samples. They are delivered reduced to 8 bits, as the image rules require.");

    /// <summary>The APP14 transform code does not fit the component count; YCbCr (three) or YCCK (four) is assumed.</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctAdobeTransformInvalid { get; } = new(nameof(DctAdobeTransformInvalid), DiagnosticArea.Dct, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "The Adobe APP14 transform code does not fit the number of components. A transform appropriate for the component count is assumed.");

    /// <summary>Three components identified R, G, B without APP14 or <c>ColorTransform</c> are read as RGB, not with the default ColorTransform 1 of ISO 32000-2 Table 13 (libjpeg compatibility).</summary>
    /// <remarks>ISO 32000-2 §7.4.8; ITU-T T.81</remarks>
    public static DiagnosticCode DctColorTransformInferred { get; } = new(nameof(DctColorTransformInferred), DiagnosticArea.Dct, DiagnosticSeverity.Information, "ISO 32000-2 §7.4.8; ITU-T T.81",
        "Three DCT components are identified as R, G, B and no APP14 segment or ColorTransform parameter says otherwise. The components are read as RGB instead of applying the default color transform.");

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
