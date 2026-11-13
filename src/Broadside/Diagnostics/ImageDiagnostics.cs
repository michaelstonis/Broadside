namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about image XObjects, inline images and their masks.</summary>
/// <remarks>
/// <para>ISO 32000-2 §8.9. Each code is reported under <see cref="DiagnosticArea.Image"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class ImageDiagnostics
{
    /// <summary>Gets the <c>ImageSubtypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageSubtypeInvalid { get; } = new(nameof(ImageSubtypeInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87");

    /// <summary>Gets the <c>ImageDimensionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageDimensionInvalid { get; } = new(nameof(ImageDimensionInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87");

    /// <summary>Gets the <c>ImageBitsPerComponentInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageBitsPerComponentInvalid { get; } = new(nameof(ImageBitsPerComponentInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87");

    /// <summary>Gets the <c>ImageColorSpaceMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageColorSpaceMissing { get; } = new(nameof(ImageColorSpaceMissing), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87");

    /// <summary>Gets the <c>ImageColorSpaceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageColorSpaceInvalid { get; } = new(nameof(ImageColorSpaceInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87");

    /// <summary>Gets the <c>ImageMaskConflict</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageMaskConflict { get; } = new(nameof(ImageMaskConflict), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87");

    /// <summary>Gets the <c>ImageDecodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageDecodeInvalid { get; } = new(nameof(ImageDecodeInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87");

    /// <summary>Gets the <c>ImageKeyAbbreviated</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageKeyAbbreviated { get; } = new(nameof(ImageKeyAbbreviated), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87");

    /// <summary>Gets the <c>ImageAlternatesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 Table 87</remarks>
    public static DiagnosticCode ImageAlternatesInvalid { get; } = new(nameof(ImageAlternatesInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.5 Table 87");

    /// <summary>Gets the <c>ImageTooLarge</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.3</remarks>
    public static DiagnosticCode ImageTooLarge { get; } = new(nameof(ImageTooLarge), DiagnosticArea.Image, DiagnosticSeverity.Error, "ISO 32000-2 §8.9.3");

    /// <summary>Gets the <c>ImageDataTruncated</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.3</remarks>
    public static DiagnosticCode ImageDataTruncated { get; } = new(nameof(ImageDataTruncated), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.3");

    /// <summary>Gets the <c>ImageDataTooLong</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.3</remarks>
    public static DiagnosticCode ImageDataTooLong { get; } = new(nameof(ImageDataTooLong), DiagnosticArea.Image, DiagnosticSeverity.Information, "ISO 32000-2 §8.9.3");

    /// <summary>Gets the <c>ImageFilterNotLast</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.3</remarks>
    public static DiagnosticCode ImageFilterNotLast { get; } = new(nameof(ImageFilterNotLast), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.3");

    /// <summary>Gets the <c>ImageDimensionMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.3</remarks>
    public static DiagnosticCode ImageDimensionMismatch { get; } = new(nameof(ImageDimensionMismatch), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.3");

    /// <summary>Gets the <c>ImageComponentMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.3</remarks>
    public static DiagnosticCode ImageComponentMismatch { get; } = new(nameof(ImageComponentMismatch), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.3");

    /// <summary>Gets the <c>ImageMaskInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.6, §11.6.5.2</remarks>
    public static DiagnosticCode ImageMaskInvalid { get; } = new(nameof(ImageMaskInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.6, §11.6.5.2");

    /// <summary>Gets the <c>ImageSoftMaskInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.6, §11.6.5.2</remarks>
    public static DiagnosticCode ImageSoftMaskInvalid { get; } = new(nameof(ImageSoftMaskInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.6, §11.6.5.2");

    /// <summary>Gets the <c>ImageMatteInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.6, §11.6.5.2</remarks>
    public static DiagnosticCode ImageMatteInvalid { get; } = new(nameof(ImageMatteInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.6, §11.6.5.2");

    /// <summary>Gets the <c>ImageSoftMaskInDataIgnored</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.6, §11.6.5.2</remarks>
    public static DiagnosticCode ImageSoftMaskInDataIgnored { get; } = new(nameof(ImageSoftMaskInDataIgnored), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.6, §11.6.5.2");

    /// <summary>Gets the <c>ImageDecodedManaged</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.8</remarks>
    public static DiagnosticCode ImageDecodedManaged { get; } = new(nameof(ImageDecodedManaged), DiagnosticArea.Image, DiagnosticSeverity.Information, "ISO 32000-2 §7.4.8");

    /// <summary>Gets the <c>PlatformImageVerified</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.8</remarks>
    public static DiagnosticCode PlatformImageVerified { get; } = new(nameof(PlatformImageVerified), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8");

    /// <summary>Gets the <c>PlatformImageMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.4.8</remarks>
    public static DiagnosticCode PlatformImageMismatch { get; } = new(nameof(PlatformImageMismatch), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.8");

    /// <summary>Gets the <c>InlineImageFilterNotAllowed</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.7</remarks>
    public static DiagnosticCode InlineImageFilterNotAllowed { get; } = new(nameof(InlineImageFilterNotAllowed), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.7");

    /// <summary>Gets the <c>InlineImageKeyInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.7</remarks>
    public static DiagnosticCode InlineImageKeyInvalid { get; } = new(nameof(InlineImageKeyInvalid), DiagnosticArea.Image, DiagnosticSeverity.Warning, "ISO 32000-2 §8.9.7");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        ImageSubtypeInvalid,
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
        ImageFilterNotLast,
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
        InlineImageKeyInvalid,
    ];
}
