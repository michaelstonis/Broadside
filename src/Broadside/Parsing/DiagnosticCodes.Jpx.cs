namespace Broadside.Parsing;

/// <summary>Diagnostic codes of the JPXDecode filter: JPEG 2000 files and codestreams (issues #67 and #68).</summary>
internal static partial class DiagnosticCodes
{
    // File format (ITU-T T.800 Annex I) and codestream syntax (Annex A).
    public const string JpxSignatureInvalid = nameof(JpxSignatureInvalid);
    public const string JpxBoxInvalid = nameof(JpxBoxInvalid);
    public const string JpxMarkerUnexpected = nameof(JpxMarkerUnexpected);
    public const string JpxMarkerSegmentInvalid = nameof(JpxMarkerSegmentInvalid);
    public const string JpxPsotInvalid = nameof(JpxPsotInvalid);
    public const string JpxCodestreamTruncated = nameof(JpxCodestreamTruncated);
    public const string JpxTilePartOrder = nameof(JpxTilePartOrder);
    public const string JpxPocInvalid = nameof(JpxPocInvalid);
    public const string JpxPpmInvalid = nameof(JpxPpmInvalid);
    public const string JpxCodestreamRaw = nameof(JpxCodestreamRaw);
    public const string JpxCodestreamsIgnored = nameof(JpxCodestreamsIgnored);
    public const string JpxHighThroughputUnsupported = nameof(JpxHighThroughputUnsupported);

    // Packets (Annex B), code-blocks (Annex D) and the component transformation (Annex G).
    public const string JpxPacketHeaderTruncated = nameof(JpxPacketHeaderTruncated);
    public const string JpxEphMissing = nameof(JpxEphMissing);
    public const string JpxCodeBlockInvalid = nameof(JpxCodeBlockInvalid);
    public const string JpxSegmentationSymbolMismatch = nameof(JpxSegmentationSymbolMismatch);
    public const string JpxMctSkipped = nameof(JpxMctSkipped);

    // What is not decoded (limits), and the output (ISO 32000-2 §7.4.9, T.800 Annex I).
    public const string JpxPrecisionUnsupported = nameof(JpxPrecisionUnsupported);
    public const string JpxChannelCountMismatch = nameof(JpxChannelCountMismatch);
    public const string JpxLimitExceeded = nameof(JpxLimitExceeded);
    public const string JpxColorSpecificationUnsupported = nameof(JpxColorSpecificationUnsupported);
    public const string JpxPaletteIndexOutOfRange = nameof(JpxPaletteIndexOutOfRange);
    public const string JpxOpacityChannelAmbiguous = nameof(JpxOpacityChannelAmbiguous);
}
