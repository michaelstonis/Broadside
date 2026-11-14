namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the JPXDecode filter.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.4.9; ITU-T T.800. Each code is reported under <see cref="DiagnosticArea.Jpx"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class JpxDiagnostics
{
    /// <summary>Gets the <c>JpxSignatureInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxSignatureInvalid { get; } = new(nameof(JpxSignatureInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A");

    /// <summary>Gets the <c>JpxBoxInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxBoxInvalid { get; } = new(nameof(JpxBoxInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A");

    /// <summary>Gets the <c>JpxMarkerUnexpected</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxMarkerUnexpected { get; } = new(nameof(JpxMarkerUnexpected), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A");

    /// <summary>Gets the <c>JpxMarkerSegmentInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxMarkerSegmentInvalid { get; } = new(nameof(JpxMarkerSegmentInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A");

    /// <summary>Gets the <c>JpxPsotInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxPsotInvalid { get; } = new(nameof(JpxPsotInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A");

    /// <summary>Gets the <c>JpxCodestreamTruncated</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxCodestreamTruncated { get; } = new(nameof(JpxCodestreamTruncated), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A");

    /// <summary>Gets the <c>JpxTilePartOrder</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxTilePartOrder { get; } = new(nameof(JpxTilePartOrder), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A");

    /// <summary>Gets the <c>JpxPocInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxPocInvalid { get; } = new(nameof(JpxPocInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A");

    /// <summary>Gets the <c>JpxPpmInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxPpmInvalid { get; } = new(nameof(JpxPpmInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A");

    /// <summary>Gets the <c>JpxCodestreamRaw</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxCodestreamRaw { get; } = new(nameof(JpxCodestreamRaw), DiagnosticArea.Jpx, DiagnosticSeverity.Information, "ITU-T T.800 Annex I, Annex A");

    /// <summary>Gets the <c>JpxCodestreamsIgnored</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxCodestreamsIgnored { get; } = new(nameof(JpxCodestreamsIgnored), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A");

    /// <summary>Gets the <c>JpxHighThroughputUnsupported</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxHighThroughputUnsupported { get; } = new(nameof(JpxHighThroughputUnsupported), DiagnosticArea.Jpx, DiagnosticSeverity.Error, "ITU-T T.800 Annex I, Annex A");

    /// <summary>Gets the <c>JpxPacketHeaderTruncated</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex B, Annex D, Annex G</remarks>
    public static DiagnosticCode JpxPacketHeaderTruncated { get; } = new(nameof(JpxPacketHeaderTruncated), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex B, Annex D, Annex G");

    /// <summary>Gets the <c>JpxEphMissing</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex B, Annex D, Annex G</remarks>
    public static DiagnosticCode JpxEphMissing { get; } = new(nameof(JpxEphMissing), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex B, Annex D, Annex G");

    /// <summary>Gets the <c>JpxCodeBlockInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex B, Annex D, Annex G</remarks>
    public static DiagnosticCode JpxCodeBlockInvalid { get; } = new(nameof(JpxCodeBlockInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex B, Annex D, Annex G");

    /// <summary>Gets the <c>JpxSegmentationSymbolMismatch</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex B, Annex D, Annex G</remarks>
    public static DiagnosticCode JpxSegmentationSymbolMismatch { get; } = new(nameof(JpxSegmentationSymbolMismatch), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex B, Annex D, Annex G");

    /// <summary>Gets the <c>JpxMctSkipped</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex B, Annex D, Annex G</remarks>
    public static DiagnosticCode JpxMctSkipped { get; } = new(nameof(JpxMctSkipped), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex B, Annex D, Annex G");

    /// <summary>Gets the <c>JpxPrecisionUnsupported</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 Annex I</remarks>
    public static DiagnosticCode JpxPrecisionUnsupported { get; } = new(nameof(JpxPrecisionUnsupported), DiagnosticArea.Jpx, DiagnosticSeverity.Information, "ISO 32000-2 §7.4.9; ITU-T T.800 Annex I");

    /// <summary>Gets the <c>JpxChannelCountMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 Annex I</remarks>
    public static DiagnosticCode JpxChannelCountMismatch { get; } = new(nameof(JpxChannelCountMismatch), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.9; ITU-T T.800 Annex I");

    /// <summary>Gets the <c>JpxLimitExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 Annex I</remarks>
    public static DiagnosticCode JpxLimitExceeded { get; } = new(nameof(JpxLimitExceeded), DiagnosticArea.Jpx, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.9; ITU-T T.800 Annex I");

    /// <summary>Gets the <c>JpxColorSpecificationUnsupported</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 Annex I</remarks>
    public static DiagnosticCode JpxColorSpecificationUnsupported { get; } = new(nameof(JpxColorSpecificationUnsupported), DiagnosticArea.Jpx, DiagnosticSeverity.Information, "ISO 32000-2 §7.4.9; ITU-T T.800 Annex I");

    /// <summary>Gets the <c>JpxPaletteIndexOutOfRange</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 Annex I</remarks>
    public static DiagnosticCode JpxPaletteIndexOutOfRange { get; } = new(nameof(JpxPaletteIndexOutOfRange), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.9; ITU-T T.800 Annex I");

    /// <summary>Gets the <c>JpxOpacityChannelAmbiguous</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 Annex I</remarks>
    public static DiagnosticCode JpxOpacityChannelAmbiguous { get; } = new(nameof(JpxOpacityChannelAmbiguous), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.9; ITU-T T.800 Annex I");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        JpxSignatureInvalid,
        JpxBoxInvalid,
        JpxMarkerUnexpected,
        JpxMarkerSegmentInvalid,
        JpxPsotInvalid,
        JpxCodestreamTruncated,
        JpxTilePartOrder,
        JpxPocInvalid,
        JpxPpmInvalid,
        JpxCodestreamRaw,
        JpxCodestreamsIgnored,
        JpxHighThroughputUnsupported,
        JpxPacketHeaderTruncated,
        JpxEphMissing,
        JpxCodeBlockInvalid,
        JpxSegmentationSymbolMismatch,
        JpxMctSkipped,
        JpxPrecisionUnsupported,
        JpxChannelCountMismatch,
        JpxLimitExceeded,
        JpxColorSpecificationUnsupported,
        JpxPaletteIndexOutOfRange,
        JpxOpacityChannelAmbiguous,
    ];
}
