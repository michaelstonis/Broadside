using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes of the JPXDecode filter: JPEG 2000 files and codestreams (issues #67 and #68).</summary>
internal static partial class DiagnosticCodes
{
    // File format (ITU-T T.800 Annex I) and codestream syntax (Annex A).
    public static string JpxSignatureInvalid => JpxDiagnostics.JpxSignatureInvalid.Name;
    public static string JpxBoxInvalid => JpxDiagnostics.JpxBoxInvalid.Name;
    public static string JpxMarkerUnexpected => JpxDiagnostics.JpxMarkerUnexpected.Name;
    public static string JpxMarkerSegmentInvalid => JpxDiagnostics.JpxMarkerSegmentInvalid.Name;
    public static string JpxPsotInvalid => JpxDiagnostics.JpxPsotInvalid.Name;
    public static string JpxCodestreamTruncated => JpxDiagnostics.JpxCodestreamTruncated.Name;
    public static string JpxTilePartOrder => JpxDiagnostics.JpxTilePartOrder.Name;
    public static string JpxPocInvalid => JpxDiagnostics.JpxPocInvalid.Name;
    public static string JpxPpmInvalid => JpxDiagnostics.JpxPpmInvalid.Name;
    public static string JpxCodestreamRaw => JpxDiagnostics.JpxCodestreamRaw.Name;
    public static string JpxCodestreamsIgnored => JpxDiagnostics.JpxCodestreamsIgnored.Name;
    public static string JpxHighThroughputUnsupported => JpxDiagnostics.JpxHighThroughputUnsupported.Name;

    // Packets (Annex B), code-blocks (Annex D) and the component transformation (Annex G).
    public static string JpxPacketHeaderTruncated => JpxDiagnostics.JpxPacketHeaderTruncated.Name;
    public static string JpxEphMissing => JpxDiagnostics.JpxEphMissing.Name;
    public static string JpxCodeBlockInvalid => JpxDiagnostics.JpxCodeBlockInvalid.Name;
    public static string JpxSegmentationSymbolMismatch => JpxDiagnostics.JpxSegmentationSymbolMismatch.Name;
    public static string JpxMctSkipped => JpxDiagnostics.JpxMctSkipped.Name;

    // What is not decoded (limits), and the output (ISO 32000-2 §7.4.9, T.800 Annex I).
    public static string JpxPrecisionUnsupported => JpxDiagnostics.JpxPrecisionUnsupported.Name;
    public static string JpxChannelCountMismatch => JpxDiagnostics.JpxChannelCountMismatch.Name;
    public static string JpxLimitExceeded => JpxDiagnostics.JpxLimitExceeded.Name;
    public static string JpxColorSpecificationUnsupported => JpxDiagnostics.JpxColorSpecificationUnsupported.Name;
    public static string JpxPaletteIndexOutOfRange => JpxDiagnostics.JpxPaletteIndexOutOfRange.Name;
    public static string JpxOpacityChannelAmbiguous => JpxDiagnostics.JpxOpacityChannelAmbiguous.Name;
}
