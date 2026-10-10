namespace Broadside.Parsing;

/// <summary>Diagnostic codes for image XObjects, inline images and their masks (issue #60).</summary>
internal static partial class DiagnosticCodes
{
    // Image dictionaries (§8.9.5, Table 87).
    public const string ImageSubtypeInvalid = nameof(ImageSubtypeInvalid);
    public const string ImageDimensionInvalid = nameof(ImageDimensionInvalid);
    public const string ImageBitsPerComponentInvalid = nameof(ImageBitsPerComponentInvalid);
    public const string ImageColorSpaceMissing = nameof(ImageColorSpaceMissing);
    public const string ImageColorSpaceInvalid = nameof(ImageColorSpaceInvalid);
    public const string ImageMaskConflict = nameof(ImageMaskConflict);
    public const string ImageDecodeInvalid = nameof(ImageDecodeInvalid);
    public const string ImageKeyAbbreviated = nameof(ImageKeyAbbreviated);
    public const string ImageAlternatesInvalid = nameof(ImageAlternatesInvalid);

    // Image data (§8.9.3).
    public const string ImageTooLarge = nameof(ImageTooLarge);
    public const string ImageDataTruncated = nameof(ImageDataTruncated);
    public const string ImageDataTooLong = nameof(ImageDataTooLong);
    public const string ImageFilterNotLast = nameof(ImageFilterNotLast);
    public const string ImageDimensionMismatch = nameof(ImageDimensionMismatch);
    public const string ImageComponentMismatch = nameof(ImageComponentMismatch);

    // Masks (§8.9.6, §11.6.5.2).
    public const string ImageMaskInvalid = nameof(ImageMaskInvalid);
    public const string ImageSoftMaskInvalid = nameof(ImageSoftMaskInvalid);
    public const string ImageMatteInvalid = nameof(ImageMatteInvalid);
    public const string ImageSoftMaskInDataIgnored = nameof(ImageSoftMaskInDataIgnored);

    // Inline images (§8.9.7).
    public const string InlineImageFilterNotAllowed = nameof(InlineImageFilterNotAllowed);
    public const string InlineImageKeyInvalid = nameof(InlineImageKeyInvalid);
}
