namespace Broadside.Images;

/// <summary>
/// How a save resamples and re-encodes image XObjects to make the file smaller (issue #625): a resolution cap, a JPEG quality and
/// whether lossless images may become JPEG, set fluently with <c>With*</c> methods and passed through
/// <see cref="PdfSaveOptions.ImageOptimization"/> or <see cref="PdfDocument.PlanImageOptimization"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.5 (image dictionaries), §8.9.5.3 (reduction), §8.9.6 (masks), §7.4.4 (FlateDecode with predictors) and §7.4.8
/// (DCTDecode). An image's effective resolution is its pixels divided by the inches of its largest placement, found by walking every
/// page's content (hidden content, image masks and artifacts included, forms where they are painted, tiling pattern cells) and every
/// annotation appearance. An image that is placed nowhere is left alone.
/// </para>
/// <para>
/// The defaults change nothing: no resolution cap, no JPEG quality. Set <see cref="MaxResolution"/> to resample,
/// <see cref="JpegQuality"/> to re-encode. A new stream replaces the old one only when it is smaller.
/// </para>
/// </remarks>
public sealed class PdfImageOptimization
{
    private double? _maxResolution;
    private double _resampleThreshold = 1.5;
    private int? _jpegQuality;
    private double? _maxBitonalResolution;

    /// <summary>Initializes a new instance of the <see cref="PdfImageOptimization"/> class, which changes nothing until a cap or a quality is set.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.5.3. Creating options is what links the image pass into a save: an application that never creates them
    /// keeps the resampler and encoders out of a trimmed build.
    /// </remarks>
    public PdfImageOptimization() => PdfDocument.ImagePass ??= ImageOptimizer.ApplyForSave;

    /// <summary>
    /// Gets or sets the most pixels per inch an image keeps at its largest placement; <see langword="null"/> (the default) resamples
    /// nothing. Applies to every image except 1-bit images and image masks, which <see cref="MaxBitonalResolution"/> caps.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.9.5.3. An image above <see cref="MaxResolution"/> × <see cref="ResampleThreshold"/> is box-filtered down to this resolution, keeping its aspect ratio.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a finite number above 0.</exception>
    public double? MaxResolution
    {
        get => _maxResolution;
        set => _maxResolution = CheckResolution(value);
    }

    /// <summary>
    /// Gets or sets how far above the cap an image must be before it is resampled: an image is resampled only when its effective
    /// resolution exceeds the cap times this factor. The default is 1.5, so a 200 dpi image under a 150 dpi cap is kept.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.9.5.3. Resampling a little gains little and costs a generation of quality.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a finite number of at least 1.</exception>
    public double ResampleThreshold
    {
        get => _resampleThreshold;
        set => _resampleThreshold = double.IsFinite(value) && value >= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The threshold shall be a finite factor of at least 1.");
    }

    /// <summary>
    /// Gets or sets the JPEG quality, 1 to 100, that 8-bit Gray, RGB and CMYK images are re-encoded at with <c>DCTDecode</c>;
    /// <see langword="null"/> (the default) keeps each image's filter.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.4.8. A <c>DCTDecode</c> image is re-encoded at this quality; a lossless (Flate, LZW, RunLength or unfiltered)
    /// image only with <see cref="RecompressLossless"/>. A resampled <c>DCTDecode</c> image is encoded at this quality, or at
    /// <see cref="Filters.DctEncoder.DefaultQuality"/> when it is <see langword="null"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not 1 to 100.</exception>
    public int? JpegQuality
    {
        get => _jpegQuality;
        set => _jpegQuality = value is null or (>= 1 and <= 100)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The JPEG quality shall be 1 to 100.");
    }

    /// <summary>
    /// Gets or sets a value indicating whether images stored losslessly (Flate, LZW, RunLength or unfiltered) may become JPEG when
    /// <see cref="JpegQuality"/> is set. The default is <see langword="false"/>: they stay lossless, Flate-encoded with PNG
    /// predictors when resampled.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.4.4 and §7.4.8. Line art and screenshots compress better, and without artifacts, losslessly.</remarks>
    public bool RecompressLossless { get; set; }

    /// <summary>
    /// Gets or sets the most pixels per inch 1-bit images and image masks keep at their largest placement; <see langword="null"/>
    /// (the default) resamples none. Resampled bitonal images stay 1-bit: each new pixel is set when at least half the area it
    /// covers is set, without dithering.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.9.5.3 and §8.9.6.2.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a finite number above 0.</exception>
    public double? MaxBitonalResolution
    {
        get => _maxBitonalResolution;
        set => _maxBitonalResolution = CheckResolution(value);
    }

    /// <summary>Sets <see cref="MaxResolution"/>.</summary>
    /// <param name="dpi">The cap in pixels per inch, or <see langword="null"/> for none.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dpi"/> is not a finite number above 0.</exception>
    /// <remarks>ISO 32000-2 §8.9.5.3.</remarks>
    public PdfImageOptimization WithMaxResolution(double? dpi)
    {
        MaxResolution = dpi;
        return this;
    }

    /// <summary>Sets <see cref="ResampleThreshold"/>.</summary>
    /// <param name="factor">The factor, at least 1.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="factor"/> is not a finite number of at least 1.</exception>
    /// <remarks>ISO 32000-2 §8.9.5.3.</remarks>
    public PdfImageOptimization WithResampleThreshold(double factor)
    {
        ResampleThreshold = factor;
        return this;
    }

    /// <summary>Sets <see cref="JpegQuality"/>.</summary>
    /// <param name="quality">The quality, 1 to 100, or <see langword="null"/> to keep each image's filter.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quality"/> is not 1 to 100.</exception>
    /// <remarks>ISO 32000-2 §7.4.8.</remarks>
    public PdfImageOptimization WithJpegQuality(int? quality)
    {
        JpegQuality = quality;
        return this;
    }

    /// <summary>Sets <see cref="RecompressLossless"/>.</summary>
    /// <param name="allow">Whether lossless images may become JPEG.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.4.4 and §7.4.8.</remarks>
    public PdfImageOptimization WithRecompressLossless(bool allow = true)
    {
        RecompressLossless = allow;
        return this;
    }

    /// <summary>Sets <see cref="MaxBitonalResolution"/>.</summary>
    /// <param name="dpi">The cap in pixels per inch, or <see langword="null"/> for none.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dpi"/> is not a finite number above 0.</exception>
    /// <remarks>ISO 32000-2 §8.9.5.3 and §8.9.6.2.</remarks>
    public PdfImageOptimization WithMaxBitonalResolution(double? dpi)
    {
        MaxBitonalResolution = dpi;
        return this;
    }

    private static double? CheckResolution(double? value) => value is null || (double.IsFinite(value.Value) && value.Value > 0)
        ? value
        : throw new ArgumentOutOfRangeException(nameof(value), value, "A resolution shall be a finite number of pixels per inch above 0.");
}
