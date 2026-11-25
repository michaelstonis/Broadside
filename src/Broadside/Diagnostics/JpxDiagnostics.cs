namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the JPXDecode filter.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.4.9; ITU-T T.800. Each code is reported under <see cref="DiagnosticArea.Jpx"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class JpxDiagnostics
{
    /// <summary>Gets the <c>JpxSignatureInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxSignatureInvalid { get; } = new(nameof(JpxSignatureInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A",
        "The JPXDecode data is neither a JPEG 2000 file nor a recognizable codestream. The first start-of-codestream and SIZ markers found are used, or nothing is decoded.");

    /// <summary>Gets the <c>JpxBoxInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxBoxInvalid { get; } = new(nameof(JpxBoxInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A",
        "A JPEG 2000 file box is malformed, such as an impossible length, a too-short color specification, or a component mapping or palette that does not fit. The box is ignored or the affected channel is dropped.");

    /// <summary>Gets the <c>JpxMarkerUnexpected</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxMarkerUnexpected { get; } = new(nameof(JpxMarkerUnexpected), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A",
        "The JPEG 2000 codestream has a marker where it does not belong, such as a missing SOC or SIZ start or bytes that are not a marker segment. The marker or the remaining header is ignored.");

    /// <summary>Gets the <c>JpxMarkerSegmentInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxMarkerSegmentInvalid { get; } = new(nameof(JpxMarkerSegmentInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A",
        "A JPEG 2000 marker segment is invalid, such as an impossible length, an unusable SIZ segment, a missing COD or QCD, or a tile index out of range. The segment or tile-part is ignored where possible.");

    /// <summary>Gets the <c>JpxPsotInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxPsotInvalid { get; } = new(nameof(JpxPsotInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A",
        "A JPEG 2000 tile-part length (Psot) is impossible. The tile-part is taken to end at the next tile-part or the end of the codestream.");

    /// <summary>Gets the <c>JpxCodestreamTruncated</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxCodestreamTruncated { get; } = new(nameof(JpxCodestreamTruncated), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A",
        "The JPEG 2000 codestream ends early. The data present is decoded and the rest of the image is left empty.");

    /// <summary>Gets the <c>JpxTilePartOrder</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxTilePartOrder { get; } = new(nameof(JpxTilePartOrder), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A",
        "The tile-parts of a JPEG 2000 tile are duplicated or out of order. A duplicate is ignored and the parts are read in TPsot order.");

    /// <summary>Gets the <c>JpxPocInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxPocInvalid { get; } = new(nameof(JpxPocInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A",
        "A JPEG 2000 POC marker segment has an uneven length or an empty or unknown progression volume. Whole volumes are used and bad ones are skipped.");

    /// <summary>Gets the <c>JpxPpmInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxPpmInvalid { get; } = new(nameof(JpxPpmInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A",
        "A JPEG 2000 PPM or PPT marker segment is empty, misplaced, or holds fewer or truncated packet-header records. The segment is ignored or the records are cut to what exists.");

    /// <summary>Gets the <c>JpxCodestreamRaw</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxCodestreamRaw { get; } = new(nameof(JpxCodestreamRaw), DiagnosticArea.Jpx, DiagnosticSeverity.Information, "ITU-T T.800 Annex I, Annex A",
        "The JPXDecode data is a bare JPEG 2000 codestream rather than a JPX file. It is decoded normally; this records that the file wrapper is absent.");

    /// <summary>Gets the <c>JpxCodestreamsIgnored</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxCodestreamsIgnored { get; } = new(nameof(JpxCodestreamsIgnored), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex I, Annex A",
        "The JPX file holds several codestreams or a fragment table, which PDF does not support. Only the first or contiguous codestream is decoded.");

    /// <summary>Gets the <c>JpxFragmentExternal</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.9; ISO/IEC 15444-2 fragment table box</remarks>
    public static DiagnosticCode JpxFragmentExternal { get; } = new(nameof(JpxFragmentExternal), DiagnosticArea.Jpx, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.9; ISO/IEC 15444-2 fragment table box",
        "The JPX fragment table lists a fragment in another file or at a URL. External fragments are never opened or fetched, so the fragmented codestream is not read.");

    /// <summary>Gets the <c>JpxHighThroughputUnsupported</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex I, Annex A</remarks>
    public static DiagnosticCode JpxHighThroughputUnsupported { get; } = new(nameof(JpxHighThroughputUnsupported), DiagnosticArea.Jpx, DiagnosticSeverity.Error, "ITU-T T.800 Annex I, Annex A",
        "The JPEG 2000 codestream uses high-throughput block coding, which Broadside does not decode. The image is not decoded.");

    /// <summary>Gets the <c>JpxPacketHeaderTruncated</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex B, Annex D, Annex G</remarks>
    public static DiagnosticCode JpxPacketHeaderTruncated { get; } = new(nameof(JpxPacketHeaderTruncated), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex B, Annex D, Annex G",
        "A JPEG 2000 packet header runs past the end of the tile data. Decoding of the tile stops there.");

    /// <summary>Gets the <c>JpxEphMissing</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex B, Annex D, Annex G</remarks>
    public static DiagnosticCode JpxEphMissing { get; } = new(nameof(JpxEphMissing), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex B, Annex D, Annex G",
        "A JPEG 2000 packet header is not followed by the end-of-packet-header marker that the coding style announces. The data is read as if the marker were present.");

    /// <summary>Gets the <c>JpxCodeBlockInvalid</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex B, Annex D, Annex G</remarks>
    public static DiagnosticCode JpxCodeBlockInvalid { get; } = new(nameof(JpxCodeBlockInvalid), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex B, Annex D, Annex G",
        "A JPEG 2000 code-block claims more missing bit-planes than its sub-band has. The code-block is left empty.");

    /// <summary>Gets the <c>JpxSegmentationSymbolMismatch</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex B, Annex D, Annex G</remarks>
    public static DiagnosticCode JpxSegmentationSymbolMismatch { get; } = new(nameof(JpxSegmentationSymbolMismatch), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex B, Annex D, Annex G",
        "A JPEG 2000 code-block's segmentation symbol is not the expected value. The data is decoded anyway.");

    /// <summary>Gets the <c>JpxMctSkipped</c> code.</summary>
    /// <remarks>ITU-T T.800 Annex B, Annex D, Annex G</remarks>
    public static DiagnosticCode JpxMctSkipped { get; } = new(nameof(JpxMctSkipped), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ITU-T T.800 Annex B, Annex D, Annex G",
        "The JPEG 2000 multiple component transform cannot be applied because the first three components differ in size or wavelet filter. The transform is skipped.");

    /// <summary>Gets the <c>JpxPrecisionUnsupported</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 Annex I</remarks>
    public static DiagnosticCode JpxPrecisionUnsupported { get; } = new(nameof(JpxPrecisionUnsupported), DiagnosticArea.Jpx, DiagnosticSeverity.Information, "ISO 32000-2 §7.4.9; ITU-T T.800 Annex I",
        "A JPEG 2000 sub-band needs more magnitude bit-planes than the decoder supports. Those code-blocks are not decoded and are left empty.");

    /// <summary>Gets the <c>JpxChannelCountMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 Annex I</remarks>
    public static DiagnosticCode JpxChannelCountMismatch { get; } = new(nameof(JpxChannelCountMismatch), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.9; ITU-T T.800 Annex I",
        "The JPEG 2000 data has fewer color channels than the color space needs. The missing channels are filled with zero.");

    /// <summary>Gets the <c>JpxLimitExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 Annex I</remarks>
    public static DiagnosticCode JpxLimitExceeded { get; } = new(nameof(JpxLimitExceeded), DiagnosticArea.Jpx, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.9; ITU-T T.800 Annex I",
        "A JPEG 2000 image, tile or component set is larger than the configured size or structure limits. The image or tile is not decoded.");

    /// <summary>Gets the <c>JpxColorSpecificationUnsupported</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 Annex I</remarks>
    public static DiagnosticCode JpxColorSpecificationUnsupported { get; } = new(nameof(JpxColorSpecificationUnsupported), DiagnosticArea.Jpx, DiagnosticSeverity.Information, "ISO 32000-2 §7.4.9; ITU-T T.800 Annex I",
        "A JPEG 2000 color specification is unsupported or absent. The next specification or the channel count decides the color space, an enumerated space is read as RGB, or sub-sampled components are read as YCbCr.");

    /// <summary>Gets the <c>JpxPaletteIndexOutOfRange</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 Annex I</remarks>
    public static DiagnosticCode JpxPaletteIndexOutOfRange { get; } = new(nameof(JpxPaletteIndexOutOfRange), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.9; ITU-T T.800 Annex I",
        "A JPEG 2000 component holds palette indices past the palette's last entry. The last palette entry is used for them.");

    /// <summary>Gets the <c>JpxOpacityChannelAmbiguous</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 Annex I</remarks>
    public static DiagnosticCode JpxOpacityChannelAmbiguous { get; } = new(nameof(JpxOpacityChannelAmbiguous), DiagnosticArea.Jpx, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.9; ITU-T T.800 Annex I",
        "A JPEG 2000 image has several opacity channels, or one for a single color. PDF uses one opacity channel for all colors, so the first one is used.");

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
        JpxFragmentExternal,
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
