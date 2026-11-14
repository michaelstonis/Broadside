namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the JBIG2Decode filter.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.4.7; ITU-T T.88. Each code is reported under <see cref="DiagnosticArea.Jbig2"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class Jbig2Diagnostics
{
    /// <summary>A JBIG2 file header (T.88 D.4) starts the stream, which ISO 32000-2 §7.4.7 forbids; it is skipped.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2FileHeaderPresent { get; } = new(nameof(Jbig2FileHeaderPresent), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>An end-of-page or end-of-file segment is present, which ISO 32000-2 §7.4.7 forbids; decoding of the page stops there.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2EndOfPagePresent { get; } = new(nameof(Jbig2EndOfPagePresent), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>The page's segments are associated with a page other than 1; the first page met is decoded, segments of other pages are ignored.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2PageNumberNotOne { get; } = new(nameof(Jbig2PageNumberNotOne), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A region comes before any page information segment (T.88 §7.4.8); the page is white with the OR operator.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2PageInformationMissing { get; } = new(nameof(Jbig2PageInformationMissing), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>The page information size differs from the image's Width and Height; the image size wins and regions are clipped.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2PageSizeMismatch { get; } = new(nameof(Jbig2PageSizeMismatch), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A segment header cannot be parsed (T.88 §7.2); it and everything after it in that stream are ignored.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2SegmentHeaderInvalid { get; } = new(nameof(Jbig2SegmentHeaderInvalid), DiagnosticArea.Jbig2, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A segment's data runs past the end of the stream; what is present is decoded.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2SegmentTruncated { get; } = new(nameof(Jbig2SegmentTruncated), DiagnosticArea.Jbig2, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A segment has a reserved type (T.88 §7.3), such as the colour palette ISO 32000-2 §7.4.7 forbids; it is skipped.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2SegmentTypeReserved { get; } = new(nameof(Jbig2SegmentTypeReserved), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A segment's data is malformed (too short, reserved values, AT pixels outside Figure 7); it is skipped or read as far as it goes.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2SegmentInvalid { get; } = new(nameof(Jbig2SegmentInvalid), DiagnosticArea.Jbig2, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>The stream uses a JBIG2 feature this decoder does not implement (extended templates, colour, a necessary extension); when it paints the page the image is not decoded.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2UnsupportedFeature { get; } = new(nameof(Jbig2UnsupportedFeature), DiagnosticArea.Jbig2, DiagnosticSeverity.Information, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>The JBIG2Globals parameter is not a stream, is itself JBIG2-encoded, or holds page segments; those are ignored.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2GlobalsInvalid { get; } = new(nameof(Jbig2GlobalsInvalid), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A direct region uses another combination operator than the page default without the page allowing it (T.88 §7.4.8.5); the region's own operator is used, as §8.2 step 5 a) says.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2CombinationOperatorMismatch { get; } = new(nameof(Jbig2CombinationOperatorMismatch), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>An intermediate region was never refined into the page (T.88 §8.2 step 6); it is dropped.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2IntermediateRegionUnused { get; } = new(nameof(Jbig2IntermediateRegionUnused), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A region's coded data ends before its last row; the missing rows are 0.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2RegionDataTruncated { get; } = new(nameof(Jbig2RegionDataTruncated), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A region's MMR data has an invalid code; the rows after it are 0.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2RegionDataInvalid { get; } = new(nameof(Jbig2RegionDataInvalid), DiagnosticArea.Jbig2, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A region is larger than the image limits allow; it is not decoded.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2LimitExceeded { get; } = new(nameof(Jbig2LimitExceeded), DiagnosticArea.Jbig2, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A text region instance or a refinement/aggregate symbol names a symbol ID beyond its symbols; an empty symbol is used.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2SymbolIdOutOfRange { get; } = new(nameof(Jbig2SymbolIdOutOfRange), DiagnosticArea.Jbig2, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A symbol dictionary's export run goes past its last symbol (T.88 §6.5.10); the run is cut.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2ExportRunOverflow { get; } = new(nameof(Jbig2ExportRunOverflow), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A symbol dictionary exports another number of symbols than its SDNUMEXSYMS (T.88 §6.5.10); the flagged symbols are exported.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2SymbolCountMismatch { get; } = new(nameof(Jbig2SymbolCountMismatch), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A symbol dictionary reuses the coding statistics of a referred dictionary that did not retain them or differs in its coding parameters (T.88 §7.4.2.2 step 3); the statistics are reset.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2ContextReuseInvalid { get; } = new(nameof(Jbig2ContextReuseInvalid), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A code table segment is malformed, or a segment selects a reserved or missing Huffman table (T.88 Annex B, §7.4.2.1.6, §7.4.3.1.6); what depends on it is not decoded.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2TableInvalid { get; } = new(nameof(Jbig2TableInvalid), DiagnosticArea.Jbig2, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A refinement region refers to no intermediate region still held (T.88 §7.4.7.4); it refines the page instead.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2RefinementReferenceMissing { get; } = new(nameof(Jbig2RefinementReferenceMissing), DiagnosticArea.Jbig2, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A segment refers to a segment that is absent or could not be decoded (T.88 §7.2.5); it is decoded without it.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2ReferredSegmentMissing { get; } = new(nameof(Jbig2ReferredSegmentMissing), DiagnosticArea.Jbig2, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>A halftone region has gray-scale values beyond its pattern dictionary (T.88 §6.6.5.2); the last pattern is used.</summary>
    /// <remarks>ISO 32000-2 §7.4.7; ITU-T T.88</remarks>
    public static DiagnosticCode Jbig2GrayValueOutOfRange { get; } = new(nameof(Jbig2GrayValueOutOfRange), DiagnosticArea.Jbig2, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.7; ITU-T T.88");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        Jbig2FileHeaderPresent,
        Jbig2EndOfPagePresent,
        Jbig2PageNumberNotOne,
        Jbig2PageInformationMissing,
        Jbig2PageSizeMismatch,
        Jbig2SegmentHeaderInvalid,
        Jbig2SegmentTruncated,
        Jbig2SegmentTypeReserved,
        Jbig2SegmentInvalid,
        Jbig2UnsupportedFeature,
        Jbig2GlobalsInvalid,
        Jbig2CombinationOperatorMismatch,
        Jbig2IntermediateRegionUnused,
        Jbig2RegionDataTruncated,
        Jbig2RegionDataInvalid,
        Jbig2LimitExceeded,
        Jbig2SymbolIdOutOfRange,
        Jbig2ExportRunOverflow,
        Jbig2SymbolCountMismatch,
        Jbig2ContextReuseInvalid,
        Jbig2TableInvalid,
        Jbig2RefinementReferenceMissing,
        Jbig2ReferredSegmentMissing,
        Jbig2GrayValueOutOfRange,
    ];
}
