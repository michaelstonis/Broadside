namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about image XObjects, inline images and their masks.</summary>
/// <remarks>
/// <para>ISO 32000-2 §8.9. Each code is reported under <see cref="DiagnosticArea.Image"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class ImageDiagnostics
{
    /// <summary>Gets the <c>ImageDimensionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageDimensionInvalid { get; } = new(nameof(ImageDimensionInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87",
        "An image Width or Height is missing, not an integer, or not positive. A fractional value is rounded down and a mask falls back to its image size, otherwise the image cannot be painted.");

    /// <summary>Gets the <c>ImageBitsPerComponentInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageBitsPerComponentInvalid { get; } = new(nameof(ImageBitsPerComponentInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87",
        "An image BitsPerComponent value is not 1, 2, 4, 8 or 16, or is not a number. The samples are read at the stated depth, or the image cannot be painted if it is unusable.");

    /// <summary>Gets the <c>ImageColorSpaceMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageColorSpaceMissing { get; } = new(nameof(ImageColorSpaceMissing), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87",
        "An image has no ColorSpace entry, which only image masks and JPEG 2000 images may omit. The image cannot be painted.");

    /// <summary>Gets the <c>ImageColorSpaceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageColorSpaceInvalid { get; } = new(nameof(ImageColorSpaceInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87",
        "An image uses a color space that images may not use, such as a Pattern space. The image cannot be painted.");

    /// <summary>Gets the <c>ImageMaskConflict</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageMaskConflict { get; } = new(nameof(ImageMaskConflict), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87",
        "An image mask has a BitsPerComponent other than 1, or also carries ColorSpace, Mask or SMask entries. A depth of 1 is used and the extra entries are ignored.");

    /// <summary>Gets the <c>ImageDecodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageDecodeInvalid { get; } = new(nameof(ImageDecodeInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87",
        "An image Decode array has the wrong type or the wrong number of entries. The default is used, extra entries are dropped, or an image mask Decode is read by which end is larger.");

    /// <summary>Gets the <c>ImageKeyAbbreviated</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageKeyAbbreviated { get; } = new(nameof(ImageKeyAbbreviated), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87",
        "An image dictionary uses an inline-image abbreviated key outside an inline image. The abbreviation is read as the full key.");

    /// <summary>Gets the <c>ImageAlternatesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageAlternatesInvalid { get; } = new(nameof(ImageAlternatesInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87",
        "An element of the image Alternates array is not an alternate image dictionary with an image stream. That element is skipped.");

    /// <summary>Gets the <c>ImageTooLarge</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.3</remarks>
    public static DiagnosticCode ImageTooLarge { get; } = new(nameof(ImageTooLarge), DiagnosticArea.Image, DiagnosticSeverity.Error, "ISO 32000-2 §8.9.3",
        "An image would need more memory than the configured limits allow when decoded. The image is not decoded.");

    /// <summary>Gets the <c>ImageDataTruncated</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.3</remarks>
    public static DiagnosticCode ImageDataTruncated { get; } = new(nameof(ImageDataTruncated), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.3",
        "The image data has fewer bytes than its dimensions require. The complete rows are kept and the remaining samples are zero.");

    /// <summary>Gets the <c>ImageDataTooLong</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.3</remarks>
    public static DiagnosticCode ImageDataTooLong { get; } = new(nameof(ImageDataTooLong), DiagnosticArea.Image, DiagnosticSeverity.Information, "ISO 32000-2 §8.9.3",
        "The image data has more bytes than its dimensions require. The extra bytes are ignored.");

    /// <summary>Gets the <c>ImageDimensionMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.3</remarks>
    public static DiagnosticCode ImageDimensionMismatch { get; } = new(nameof(ImageDimensionMismatch), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.3",
        "The decoded image data has a different size than the image dictionary states. The size of the decoded data is used.");

    /// <summary>Gets the <c>ImageComponentMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.3</remarks>
    public static DiagnosticCode ImageComponentMismatch { get; } = new(nameof(ImageComponentMismatch), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.3",
        "The decoded image data has a different number of components than the color space expects. Extra components are dropped or missing ones are filled.");

    /// <summary>Gets the <c>ImageMaskInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.6, §11.6.5.2</remarks>
    public static DiagnosticCode ImageMaskInvalid { get; } = new(nameof(ImageMaskInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.6, §11.6.5.2",
        "An image Mask entry is invalid, such as a stream that is not an image mask, a color key array of the wrong length, or a value of the wrong type. The mask is ignored or the array is trimmed.");

    /// <summary>Gets the <c>ImageSoftMaskInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.6, §11.6.5.2</remarks>
    public static DiagnosticCode ImageSoftMaskInvalid { get; } = new(nameof(ImageSoftMaskInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.6, §11.6.5.2",
        "An image soft mask is invalid, such as an image mask used as a soft mask, a color space other than DeviceGray, or an SMask that is not a stream. The soft mask is read as gray, adjusted, or ignored.");

    /// <summary>Gets the <c>ImageMatteInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.6, §11.6.5.2</remarks>
    public static DiagnosticCode ImageMatteInvalid { get; } = new(nameof(ImageMatteInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.6, §11.6.5.2",
        "A soft mask Matte entry does not match the image, either in component count or in image size. The Matte is ignored.");

    /// <summary>Gets the <c>ImageSoftMaskInDataIgnored</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.6, §11.6.5.2</remarks>
    public static DiagnosticCode ImageSoftMaskInDataIgnored { get; } = new(nameof(ImageSoftMaskInDataIgnored), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.6, §11.6.5.2",
        "An image uses SMaskInData together with an SMask, or on data that is not JPXDecode. The SMask takes no part in one case, and the SMaskInData entry is ignored in the other.");

    /// <summary>Gets the <c>ImageDecodedManaged</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.8</remarks>
    public static DiagnosticCode ImageDecodedManaged { get; } = new(nameof(ImageDecodedManaged), DiagnosticArea.Image, DiagnosticSeverity.Information, "ISO 32000-2 §7.4.8",
        "A platform image decoder did not decode an image, so the managed codec decodes it instead. This records the fallback and the reason the platform decoder gave.");

    /// <summary>Gets the <c>PlatformImageVerified</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.8</remarks>
    public static DiagnosticCode PlatformImageVerified { get; } = new(nameof(PlatformImageVerified), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8",
        "A platform image decoder's output was compared with the managed codec and matched within tolerance. This is informational and records the measured difference.");

    /// <summary>Gets the <c>PlatformImageMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.8</remarks>
    public static DiagnosticCode PlatformImageMismatch { get; } = new(nameof(PlatformImageMismatch), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8",
        "A platform image decoder's output differs from the managed codec beyond tolerance, or could not be compared with it. The platform output is still used and the difference is recorded.");

    /// <summary>Gets the <c>InlineImageFilterNotAllowed</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.7</remarks>
    public static DiagnosticCode InlineImageFilterNotAllowed { get; } = new(nameof(InlineImageFilterNotAllowed), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.7",
        "An inline image uses a filter that is not allowed in inline images. The filter is applied anyway.");

    /// <summary>Gets the <c>ImageOptimizationSkipped</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.3</remarks>
    public static DiagnosticCode ImageOptimizationSkipped { get; } = new(nameof(ImageOptimizationSkipped), DiagnosticArea.Image, DiagnosticSeverity.Information, "ISO 32000-2 §8.9.5.3",
        "The save-time image pass could not decode an image or its mask, or the decoded samples did not match the image dictionary. The image is saved unchanged.");

    /// <summary>Gets the <c>ImageImportRepaired</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5</remarks>
    public static DiagnosticCode ImageImportRepaired { get; } = new(nameof(ImageImportRepaired), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5",
        "An imported TIFF, GIF or BMP file deviates from its format in a way that was repaired, such as a wrong strip byte count or image data that ends early. The image is created from what could be read.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        ImageDimensionInvalid,
        ImageBitsPerComponentInvalid,
        ImageColorSpaceMissing,
        ImageColorSpaceInvalid,
        ImageMaskConflict,
        ImageDecodeInvalid,
        ImageKeyAbbreviated,
        ImageAlternatesInvalid,
        ImageTooLarge,
        ImageDataTruncated,
        ImageDataTooLong,
        ImageDimensionMismatch,
        ImageComponentMismatch,
        ImageMaskInvalid,
        ImageSoftMaskInvalid,
        ImageMatteInvalid,
        ImageSoftMaskInDataIgnored,
        ImageDecodedManaged,
        PlatformImageVerified,
        PlatformImageMismatch,
        InlineImageFilterNotAllowed,
        ImageOptimizationSkipped,
        ImageImportRepaired,
    ];
}
