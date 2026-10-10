namespace Broadside.Parsing;

/// <summary>Diagnostic codes of the <c>DCTDecode</c> filter (issue #61; ISO 32000-2 §7.4.8, ITU-T T.81).</summary>
internal static partial class DiagnosticCodes
{
    /// <summary>Bytes before the SOI marker were skipped (Warning).</summary>
    public const string DctLeadingJunk = nameof(DctLeadingJunk);

    /// <summary>Bytes that are not a marker were skipped between segments or after an entropy-coded segment (Warning).</summary>
    public const string DctExtraneousData = nameof(DctExtraneousData);

    /// <summary>A marker segment's length or content is invalid; it was skipped or resynchronized (Warning).</summary>
    public const string DctSegmentInvalid = nameof(DctSegmentInvalid);

    /// <summary>The frame header is missing or unusable (Error: no samples).</summary>
    public const string DctFrameInvalid = nameof(DctFrameInvalid);

    /// <summary>A second frame header; the first frame is kept (Warning).</summary>
    public const string DctFrameRepeated = nameof(DctFrameRepeated);

    /// <summary>A coding process this decoder does not implement (Information when PDF allows it, else Error; no samples).</summary>
    public const string DctProcessUnsupported = nameof(DctProcessUnsupported);

    /// <summary>A table a scan needs was never defined (Warning for Huffman tables, which fall back to Annex K; Error for quantization tables).</summary>
    public const string DctTableMissing = nameof(DctTableMissing);

    /// <summary>A Huffman or quantization table definition is invalid (Warning).</summary>
    public const string DctTableInvalid = nameof(DctTableInvalid);

    /// <summary>A scan header is invalid; the scan, or the invalid parameters, were ignored (Warning).</summary>
    public const string DctScanInvalid = nameof(DctScanInvalid);

    /// <summary>The entropy-coded data holds a bit sequence that is not a Huffman code (Warning).</summary>
    public const string DctDataInvalid = nameof(DctDataInvalid);

    /// <summary>A restart marker is missing or out of sequence (Warning).</summary>
    public const string DctRestartInvalid = nameof(DctRestartInvalid);

    /// <summary>The data ends before every block is decoded; the rest is mid-grey (Warning).</summary>
    public const string DctTruncated = nameof(DctTruncated);

    /// <summary>The data ends without the EOI marker after all scans (Warning).</summary>
    public const string DctEndMissing = nameof(DctEndMissing);

    /// <summary>A component count PDF does not allow (2); decoded without a colour transform (Warning).</summary>
    public const string DctComponentCountInvalid = nameof(DctComponentCountInvalid);

    /// <summary>The frame's number of lines is 0 and came from a DNL segment or the image dictionary (Warning).</summary>
    public const string DctLinesFromDnl = nameof(DctLinesFromDnl);
}
