using Broadside.Objects;

namespace Broadside.Images;

/// <summary>What the save-time image pass does, or would do, to one image (issue #625).</summary>
/// <remarks>ISO 32000-2 §8.9.5.1 (image dictionaries), §8.9.5.3 (reduction), §7.4.4 and §7.4.8.</remarks>
[Flags]
public enum PdfImageOptimizationActions
{
    /// <summary>The image is left as it is.</summary>
    None = 0,

    /// <summary>The image is resampled to <see cref="PdfImageOptimizationResult.TargetWidth"/> × <see cref="PdfImageOptimizationResult.TargetHeight"/>.</summary>
    Resample = 1 << 0,

    /// <summary>The image's samples are encoded again, with <see cref="PdfImageOptimizationResult.TargetFilter"/>.</summary>
    Recompress = 1 << 1,
}

/// <summary>Why the save-time image pass leaves an image, or part of what it planned, unchanged (issue #625).</summary>
/// <remarks>ISO 32000-2 §8.9.5, §8.9.6 and §8.9.7.</remarks>
public enum PdfImageOptimizationReason
{
    /// <summary>Nothing stopped the pass: the image changes as planned, or the options ask for no change to it.</summary>
    None,

    /// <summary>No page content, form, pattern cell or annotation appearance paints the image, so its resolution is unknown.</summary>
    NotPlaced,

    /// <summary>An inline image (§8.9.7), which the pass does not change.</summary>
    InlineImage,

    /// <summary>The image's resolution is within the cap (times the threshold) and its filter is kept.</summary>
    WithinLimits,

    /// <summary>
    /// The image cannot be averaged or re-encoded faithfully: an Indexed space, a depth other than 1, 2, 4, 8 or 16, a multi-component
    /// 1-bit image, JPEG 2000 data, a <c>Crypt</c> filter, or truncated data.
    /// </summary>
    Unsupported,

    /// <summary>The image has a colour key mask (§8.9.6.4), which averaged or lossy samples would no longer match.</summary>
    ColorKeyMask,

    /// <summary>The image's soft mask has a <c>Matte</c> (§11.6.5.2), which ties the colours to the mask's samples.</summary>
    Matte,

    /// <summary>The image could not be decoded; a <c>ImageOptimizationSkipped</c> diagnostic names it.</summary>
    DecodeFailed,

    /// <summary>The new encoding was not smaller than the old one, which is kept.</summary>
    WouldGrow,

    /// <summary>The lossless encoding the pass writes (<c>FlateDecode</c>, PDF 1.2) is later than the document's version allows (ADR 0003).</summary>
    VersionTooLow,
}

/// <summary>
/// One image of the save-time image pass (issue #625): where it is painted largest, its effective resolution, and what the pass
/// does or would do to it. <see cref="PdfDocument.PlanImageOptimization"/> returns one per image XObject the document's content
/// paints (and one per inline image painted), in the order they are first painted.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.9.4 (the image's unit square in user space), §8.9.5 (image dictionaries), §8.9.5.3 (reduction), §8.9.6 (masks),
/// §7.4.4 and §7.4.8. A plan only decides: it does not decode or encode, so <see cref="PdfImageOptimizationReason.DecodeFailed"/>
/// and <see cref="PdfImageOptimizationReason.WouldGrow"/> appear only once a save has run the pass.
/// </remarks>
public sealed class PdfImageOptimizationResult
{
    internal PdfImageOptimizationResult(PdfImage image, int placements, double? effectiveResolution)
    {
        Image = image;
        Placements = placements;
        EffectiveResolution = effectiveResolution;
        TargetWidth = image.IsInline ? 0 : image.Width;
        TargetHeight = image.IsInline ? 0 : image.Height;
    }

    /// <summary>Gets the image.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 and §8.9.7.</remarks>
    public PdfImage Image { get; }

    /// <summary>Gets the image's indirect reference, or <see langword="null"/> for an inline image or a direct stream.</summary>
    /// <remarks>ISO 32000-2 §7.3.10.</remarks>
    public CosReference? Reference => Image.Reference;

    /// <summary>Gets how many times the walk found the image painted.</summary>
    /// <remarks>ISO 32000-2 §8.8 (<c>Do</c>) and §8.9.7.</remarks>
    public int Placements { get; }

    /// <summary>
    /// Gets the image's effective resolution in pixels per inch at its largest placement, on its less dense axis; <see langword="null"/>
    /// for an image placed nowhere or only with a degenerate size.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.9.4 and §8.3.2.3: the placement's lengths in default user space, times the page's <c>UserUnit</c>, in units of 1/72 inch.</remarks>
    public double? EffectiveResolution { get; }

    /// <summary>Gets what the pass does to the image.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.3, §7.4.4 and §7.4.8.</remarks>
    public PdfImageOptimizationActions Actions { get; internal set; }

    /// <summary>Gets why the image, or part of what was planned for it, is left unchanged; <see cref="PdfImageOptimizationReason.None"/> otherwise.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 and §8.9.6.</remarks>
    public PdfImageOptimizationReason Reason { get; internal set; }

    /// <summary>Gets the width the image has after the pass: its own unless it is resampled.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (<c>Width</c>).</remarks>
    public int TargetWidth { get; internal set; }

    /// <summary>Gets the height the image has after the pass: its own unless it is resampled.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (<c>Height</c>).</remarks>
    public int TargetHeight { get; internal set; }

    /// <summary>Gets the filter the image is encoded with after the pass, or <see langword="null"/> when its encoding is kept.</summary>
    /// <remarks>ISO 32000-2 §7.4.4 (<c>FlateDecode</c>) and §7.4.8 (<c>DCTDecode</c>).</remarks>
    public CosName? TargetFilter { get; internal set; }

    /// <summary>Gets the length of the image's encoded data before the pass.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2.</remarks>
    public long OriginalLength { get; internal init; }

    /// <summary>Gets the length of the image's encoded data after the pass: <see cref="OriginalLength"/> until a save has run it, or when the image was kept.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2.</remarks>
    public long OptimizedLength { get; internal set; }

    /// <summary>Gets the quality a <c>DCTDecode</c> encoding uses, or 0 for another filter.</summary>
    internal int Quality { get; set; }

    /// <summary>Gets the samples' depth after resampling.</summary>
    internal int TargetBits { get; set; }

    /// <summary>Gets a value indicating whether the image stays 1-bit.</summary>
    internal bool Bitonal { get; set; }
}
