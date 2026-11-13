using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for image XObjects, inline images and their masks (issue #60).</summary>
internal static partial class DiagnosticCodes
{
    // Image dictionaries (§8.9.5, Table 87).
    public static string ImageSubtypeInvalid => ImageDiagnostics.ImageSubtypeInvalid.Name;
    public static string ImageDimensionInvalid => ImageDiagnostics.ImageDimensionInvalid.Name;
    public static string ImageBitsPerComponentInvalid => ImageDiagnostics.ImageBitsPerComponentInvalid.Name;
    public static string ImageColorSpaceMissing => ImageDiagnostics.ImageColorSpaceMissing.Name;
    public static string ImageColorSpaceInvalid => ImageDiagnostics.ImageColorSpaceInvalid.Name;
    public static string ImageMaskConflict => ImageDiagnostics.ImageMaskConflict.Name;
    public static string ImageDecodeInvalid => ImageDiagnostics.ImageDecodeInvalid.Name;
    public static string ImageKeyAbbreviated => ImageDiagnostics.ImageKeyAbbreviated.Name;
    public static string ImageAlternatesInvalid => ImageDiagnostics.ImageAlternatesInvalid.Name;

    // Image data (§8.9.3).
    public static string ImageTooLarge => ImageDiagnostics.ImageTooLarge.Name;
    public static string ImageDataTruncated => ImageDiagnostics.ImageDataTruncated.Name;
    public static string ImageDataTooLong => ImageDiagnostics.ImageDataTooLong.Name;
    public static string ImageFilterNotLast => ImageDiagnostics.ImageFilterNotLast.Name;
    public static string ImageDimensionMismatch => ImageDiagnostics.ImageDimensionMismatch.Name;
    public static string ImageComponentMismatch => ImageDiagnostics.ImageComponentMismatch.Name;

    // Masks (§8.9.6, §11.6.5.2).
    public static string ImageMaskInvalid => ImageDiagnostics.ImageMaskInvalid.Name;
    public static string ImageSoftMaskInvalid => ImageDiagnostics.ImageSoftMaskInvalid.Name;
    public static string ImageMatteInvalid => ImageDiagnostics.ImageMatteInvalid.Name;
    public static string ImageSoftMaskInDataIgnored => ImageDiagnostics.ImageSoftMaskInDataIgnored.Name;

    // Platform image decoders (§7.4.8; issue #372).
    public static string ImageDecodedManaged => ImageDiagnostics.ImageDecodedManaged.Name;
    public static string PlatformImageVerified => ImageDiagnostics.PlatformImageVerified.Name;
    public static string PlatformImageMismatch => ImageDiagnostics.PlatformImageMismatch.Name;

    // Inline images (§8.9.7).
    public static string InlineImageFilterNotAllowed => ImageDiagnostics.InlineImageFilterNotAllowed.Name;
    public static string InlineImageKeyInvalid => ImageDiagnostics.InlineImageKeyInvalid.Name;
}
