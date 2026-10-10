namespace Broadside.Parsing;

/// <summary>Diagnostic codes of the JPXDecode filter: JPEG 2000 files and codestreams (issue #67).</summary>
internal static partial class DiagnosticCodes
{
    // File format (ITU-T T.800 Annex I) and codestream syntax (Annex A).
    public const string JpxSignatureInvalid = nameof(JpxSignatureInvalid);
    public const string JpxBoxInvalid = nameof(JpxBoxInvalid);
    public const string JpxMarkerUnexpected = nameof(JpxMarkerUnexpected);
    public const string JpxMarkerSegmentInvalid = nameof(JpxMarkerSegmentInvalid);
    public const string JpxPsotInvalid = nameof(JpxPsotInvalid);
    public const string JpxCodestreamTruncated = nameof(JpxCodestreamTruncated);

    // Packets (Annex B) and code-blocks (Annex D).
    public const string JpxPacketHeaderTruncated = nameof(JpxPacketHeaderTruncated);
    public const string JpxEphMissing = nameof(JpxEphMissing);
    public const string JpxCodeBlockInvalid = nameof(JpxCodeBlockInvalid);

    // What is legal but not decoded (Information), and the output (ISO 32000-2 §7.4.9).
    public const string JpxUnsupportedFeature = nameof(JpxUnsupportedFeature);
    public const string JpxPrecisionUnsupported = nameof(JpxPrecisionUnsupported);
    public const string JpxChannelCountMismatch = nameof(JpxChannelCountMismatch);
    public const string JpxLimitExceeded = nameof(JpxLimitExceeded);
}
