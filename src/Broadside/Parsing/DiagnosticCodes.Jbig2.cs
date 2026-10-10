namespace Broadside.Parsing;

/// <summary>Diagnostic codes for the JBIG2Decode filter (issues #64 and #65; ISO 32000-2 §7.4.7, ITU-T T.88). Each is reported once per decode.</summary>
internal static partial class DiagnosticCodes
{
    /// <summary>A JBIG2 file header (T.88 D.4) starts the stream, which ISO 32000-2 §7.4.7 forbids; it is skipped (Warning).</summary>
    public const string Jbig2FileHeaderPresent = nameof(Jbig2FileHeaderPresent);

    /// <summary>An end-of-page or end-of-file segment is present, which ISO 32000-2 §7.4.7 forbids; decoding of the page stops there (Warning).</summary>
    public const string Jbig2EndOfPagePresent = nameof(Jbig2EndOfPagePresent);

    /// <summary>The page's segments are associated with a page other than 1; the first page met is decoded, segments of other pages are ignored (Warning).</summary>
    public const string Jbig2PageNumberNotOne = nameof(Jbig2PageNumberNotOne);

    /// <summary>A region comes before any page information segment (T.88 §7.4.8); the page is white with the OR operator (Warning).</summary>
    public const string Jbig2PageInformationMissing = nameof(Jbig2PageInformationMissing);

    /// <summary>The page information size differs from the image's Width and Height; the image size wins and regions are clipped (Warning).</summary>
    public const string Jbig2PageSizeMismatch = nameof(Jbig2PageSizeMismatch);

    /// <summary>A segment header cannot be parsed (T.88 §7.2); it and everything after it in that stream are ignored (Error).</summary>
    public const string Jbig2SegmentHeaderInvalid = nameof(Jbig2SegmentHeaderInvalid);

    /// <summary>A segment's data runs past the end of the stream; what is present is decoded (Error).</summary>
    public const string Jbig2SegmentTruncated = nameof(Jbig2SegmentTruncated);

    /// <summary>A segment has a reserved type (T.88 §7.3), such as the colour palette ISO 32000-2 §7.4.7 forbids; it is skipped (Warning).</summary>
    public const string Jbig2SegmentTypeReserved = nameof(Jbig2SegmentTypeReserved);

    /// <summary>A segment's data is malformed (too short, reserved values, AT pixels outside Figure 7); it is skipped or read as far as it goes (Error).</summary>
    public const string Jbig2SegmentInvalid = nameof(Jbig2SegmentInvalid);

    /// <summary>
    /// The stream uses a JBIG2 feature this decoder does not implement yet (symbol, text, halftone and refinement regions, extended
    /// templates, colour, a necessary extension); when it paints the page the image is not decoded (Information).
    /// </summary>
    public const string Jbig2UnsupportedFeature = nameof(Jbig2UnsupportedFeature);

    /// <summary>The JBIG2Globals parameter is not a stream, is itself JBIG2-encoded, or holds page segments; those are ignored (Warning).</summary>
    public const string Jbig2GlobalsInvalid = nameof(Jbig2GlobalsInvalid);

    /// <summary>A direct region uses another combination operator than the page default without the page allowing it (T.88 §7.4.8.5); the region's own operator is used, as §8.2 step 5 a) says (Warning).</summary>
    public const string Jbig2CombinationOperatorMismatch = nameof(Jbig2CombinationOperatorMismatch);

    /// <summary>An intermediate region was never refined into the page (T.88 §8.2 step 6); it is dropped (Warning).</summary>
    public const string Jbig2IntermediateRegionUnused = nameof(Jbig2IntermediateRegionUnused);

    /// <summary>A region's coded data ends before its last row; the missing rows are 0 (Warning).</summary>
    public const string Jbig2RegionDataTruncated = nameof(Jbig2RegionDataTruncated);

    /// <summary>A region's MMR data has an invalid code; the rows after it are 0 (Error).</summary>
    public const string Jbig2RegionDataInvalid = nameof(Jbig2RegionDataInvalid);

    /// <summary>A region is larger than the image limits allow; it is not decoded (Error).</summary>
    public const string Jbig2LimitExceeded = nameof(Jbig2LimitExceeded);
}
