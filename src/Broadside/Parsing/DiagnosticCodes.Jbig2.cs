using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for the JBIG2Decode filter (issues #64 and #65; ISO 32000-2 §7.4.7, ITU-T T.88). Each is reported once per decode.</summary>
internal static partial class DiagnosticCodes
{
    /// <summary>A JBIG2 file header (T.88 D.4) starts the stream, which ISO 32000-2 §7.4.7 forbids; it is skipped (Warning).</summary>
    public static string Jbig2FileHeaderPresent => Jbig2Diagnostics.Jbig2FileHeaderPresent.Name;

    /// <summary>An end-of-page or end-of-file segment is present, which ISO 32000-2 §7.4.7 forbids; decoding of the page stops there (Warning).</summary>
    public static string Jbig2EndOfPagePresent => Jbig2Diagnostics.Jbig2EndOfPagePresent.Name;

    /// <summary>The page's segments are associated with a page other than 1; the first page met is decoded, segments of other pages are ignored (Warning).</summary>
    public static string Jbig2PageNumberNotOne => Jbig2Diagnostics.Jbig2PageNumberNotOne.Name;

    /// <summary>A region comes before any page information segment (T.88 §7.4.8); the page is white with the OR operator (Warning).</summary>
    public static string Jbig2PageInformationMissing => Jbig2Diagnostics.Jbig2PageInformationMissing.Name;

    /// <summary>The page information size differs from the image's Width and Height; the image size wins and regions are clipped (Warning).</summary>
    public static string Jbig2PageSizeMismatch => Jbig2Diagnostics.Jbig2PageSizeMismatch.Name;

    /// <summary>A segment header cannot be parsed (T.88 §7.2); it and everything after it in that stream are ignored (Error).</summary>
    public static string Jbig2SegmentHeaderInvalid => Jbig2Diagnostics.Jbig2SegmentHeaderInvalid.Name;

    /// <summary>A segment's data runs past the end of the stream; what is present is decoded (Error).</summary>
    public static string Jbig2SegmentTruncated => Jbig2Diagnostics.Jbig2SegmentTruncated.Name;

    /// <summary>A segment has a reserved type (T.88 §7.3), such as the colour palette ISO 32000-2 §7.4.7 forbids; it is skipped (Warning).</summary>
    public static string Jbig2SegmentTypeReserved => Jbig2Diagnostics.Jbig2SegmentTypeReserved.Name;

    /// <summary>A segment's data is malformed (too short, reserved values, AT pixels outside Figure 7); it is skipped or read as far as it goes (Error).</summary>
    public static string Jbig2SegmentInvalid => Jbig2Diagnostics.Jbig2SegmentInvalid.Name;

    /// <summary>
    /// The stream uses a JBIG2 feature this decoder does not implement (extended templates, colour, a necessary extension); when it
    /// paints the page the image is not decoded (Information).
    /// </summary>
    public static string Jbig2UnsupportedFeature => Jbig2Diagnostics.Jbig2UnsupportedFeature.Name;

    /// <summary>The JBIG2Globals parameter is not a stream, is itself JBIG2-encoded, or holds page segments; those are ignored (Warning).</summary>
    public static string Jbig2GlobalsInvalid => Jbig2Diagnostics.Jbig2GlobalsInvalid.Name;

    /// <summary>A direct region uses another combination operator than the page default without the page allowing it (T.88 §7.4.8.5); the region's own operator is used, as §8.2 step 5 a) says (Warning).</summary>
    public static string Jbig2CombinationOperatorMismatch => Jbig2Diagnostics.Jbig2CombinationOperatorMismatch.Name;

    /// <summary>An intermediate region was never refined into the page (T.88 §8.2 step 6); it is dropped (Warning).</summary>
    public static string Jbig2IntermediateRegionUnused => Jbig2Diagnostics.Jbig2IntermediateRegionUnused.Name;

    /// <summary>A region's coded data ends before its last row; the missing rows are 0 (Warning).</summary>
    public static string Jbig2RegionDataTruncated => Jbig2Diagnostics.Jbig2RegionDataTruncated.Name;

    /// <summary>A region's MMR data has an invalid code; the rows after it are 0 (Error).</summary>
    public static string Jbig2RegionDataInvalid => Jbig2Diagnostics.Jbig2RegionDataInvalid.Name;

    /// <summary>A region is larger than the image limits allow; it is not decoded (Error).</summary>
    public static string Jbig2LimitExceeded => Jbig2Diagnostics.Jbig2LimitExceeded.Name;

    /// <summary>A text region instance or a refinement/aggregate symbol names a symbol ID beyond its symbols; an empty symbol is used (Error).</summary>
    public static string Jbig2SymbolIdOutOfRange => Jbig2Diagnostics.Jbig2SymbolIdOutOfRange.Name;

    /// <summary>A symbol dictionary's export run goes past its last symbol (T.88 §6.5.10); the run is cut (Warning).</summary>
    public static string Jbig2ExportRunOverflow => Jbig2Diagnostics.Jbig2ExportRunOverflow.Name;

    /// <summary>A symbol dictionary exports another number of symbols than its SDNUMEXSYMS (T.88 §6.5.10); the flagged symbols are exported (Warning).</summary>
    public static string Jbig2SymbolCountMismatch => Jbig2Diagnostics.Jbig2SymbolCountMismatch.Name;

    /// <summary>
    /// A symbol dictionary reuses the coding statistics of a referred dictionary that did not retain them or differs in its coding
    /// parameters (T.88 §7.4.2.2 step 3); the statistics are reset (Warning).
    /// </summary>
    public static string Jbig2ContextReuseInvalid => Jbig2Diagnostics.Jbig2ContextReuseInvalid.Name;

    /// <summary>A code table segment is malformed, or a segment selects a reserved or missing Huffman table (T.88 Annex B, §7.4.2.1.6, §7.4.3.1.6); what depends on it is not decoded (Error).</summary>
    public static string Jbig2TableInvalid => Jbig2Diagnostics.Jbig2TableInvalid.Name;

    /// <summary>A refinement region refers to no intermediate region still held (T.88 §7.4.7.4); it refines the page instead (Error).</summary>
    public static string Jbig2RefinementReferenceMissing => Jbig2Diagnostics.Jbig2RefinementReferenceMissing.Name;

    /// <summary>A segment refers to a segment that is absent or could not be decoded (T.88 §7.2.5); it is decoded without it (Error).</summary>
    public static string Jbig2ReferredSegmentMissing => Jbig2Diagnostics.Jbig2ReferredSegmentMissing.Name;

    /// <summary>A halftone region has gray-scale values beyond its pattern dictionary (T.88 §6.6.5.2); the last pattern is used (Warning).</summary>
    public static string Jbig2GrayValueOutOfRange => Jbig2Diagnostics.Jbig2GrayValueOutOfRange.Name;
}
