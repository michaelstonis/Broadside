using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes of the <c>DCTDecode</c> filter (issues #61 and #62; ISO 32000-2 §7.4.8, ITU-T T.81).</summary>
internal static partial class DiagnosticCodes
{
    /// <summary>Bytes before the SOI marker were skipped (Warning).</summary>
    public static string DctLeadingJunk => DctDiagnostics.DctLeadingJunk.Name;

    /// <summary>Bytes that are not a marker were skipped between segments or after an entropy-coded segment (Warning).</summary>
    public static string DctExtraneousData => DctDiagnostics.DctExtraneousData.Name;

    /// <summary>A marker segment's length or content is invalid; it was skipped or resynchronized (Warning).</summary>
    public static string DctSegmentInvalid => DctDiagnostics.DctSegmentInvalid.Name;

    /// <summary>The frame header is missing or unusable (Error: no samples).</summary>
    public static string DctFrameInvalid => DctDiagnostics.DctFrameInvalid.Name;

    /// <summary>A second frame header; the first frame is kept (Warning).</summary>
    public static string DctFrameRepeated => DctDiagnostics.DctFrameRepeated.Name;

    /// <summary>The lossless or hierarchical coding process, which PDF does not use (Error; no samples).</summary>
    public static string DctProcessUnsupported => DctDiagnostics.DctProcessUnsupported.Name;

    /// <summary>A table a scan needs was never defined (Warning for Huffman tables, which fall back to Annex K; Error for quantization tables).</summary>
    public static string DctTableMissing => DctDiagnostics.DctTableMissing.Name;

    /// <summary>A Huffman or quantization table definition is invalid (Warning).</summary>
    public static string DctTableInvalid => DctDiagnostics.DctTableInvalid.Name;

    /// <summary>A scan header is invalid; the scan, or the invalid parameters, were ignored (Warning).</summary>
    public static string DctScanInvalid => DctDiagnostics.DctScanInvalid.Name;

    /// <summary>The entropy-coded data holds a bit sequence that is not a Huffman code (Warning).</summary>
    public static string DctDataInvalid => DctDiagnostics.DctDataInvalid.Name;

    /// <summary>A restart marker is missing or out of sequence (Warning).</summary>
    public static string DctRestartInvalid => DctDiagnostics.DctRestartInvalid.Name;

    /// <summary>The data ends before every block is decoded; the rest is mid-grey (Warning).</summary>
    public static string DctTruncated => DctDiagnostics.DctTruncated.Name;

    /// <summary>The data ends without the EOI marker after all scans (Warning).</summary>
    public static string DctEndMissing => DctDiagnostics.DctEndMissing.Name;

    /// <summary>A component count PDF does not allow (2); decoded without a colour transform (Warning).</summary>
    public static string DctComponentCountInvalid => DctDiagnostics.DctComponentCountInvalid.Name;

    /// <summary>The frame's number of lines is 0 and came from a DNL segment or the image dictionary (Warning).</summary>
    public static string DctLinesFromDnl => DctDiagnostics.DctLinesFromDnl.Name;

    /// <summary>The frame has 12-bit samples, which the filter delivers reduced to 8 bits (ISO 32000-2 Table 87) (Information).</summary>
    public static string DctPrecisionReduced => DctDiagnostics.DctPrecisionReduced.Name;

    /// <summary>The APP14 transform code does not fit the component count; YCbCr (three) or YCCK (four) is assumed (Warning).</summary>
    public static string DctAdobeTransformInvalid => DctDiagnostics.DctAdobeTransformInvalid.Name;

    /// <summary>
    /// Three components identified R, G, B without APP14 or <c>ColorTransform</c> are read as RGB, not with the default
    /// ColorTransform 1 of ISO 32000-2 Table 13 (libjpeg compatibility) (Information).
    /// </summary>
    public static string DctColorTransformInferred => DctDiagnostics.DctColorTransformInferred.Name;
}
