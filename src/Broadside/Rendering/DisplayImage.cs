using Broadside.Caching;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;

namespace Broadside.Rendering;

/// <summary>
/// An image interned in a display list: its samples, with the colour space and Decode mapping that give them meaning, shared by every
/// paint of the same image XObject in the list. Never converted: a backend converts the samples to its device model.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.5 (image dictionaries: Width, Height, ColorSpace, BitsPerComponent, Decode, Interpolate), §8.9.7 (inline
/// images, decoded once per paint) and §8.9.3 (the sample layout: rows top to bottom, samples left to right, components interleaved,
/// each row starting on a byte boundary, 16-bit samples big-endian). The image is painted into the unit square of user space
/// (§8.9.4): image space maps to it through <c>[1/Width 0 0 −1/Height 0 1]</c>, so the first row lands at the top, then through
/// the transform set before the paint.
/// </para>
/// <para>
/// Fonts, images and colours are resolved before an operation enters the list, so a backend never reads the file. One
/// instance per distinct image XObject per list: a backend may key a per-surface cache (a converted bitmap, a native image) on the
/// instance. Immutable; safe to read from any number of threads.
/// </para>
/// <para>
/// Convert with <c>list.Document.GetColorConverter(image.ColorSpace, conversion with { Target = … }, defaultColorSpaces)</c>, the
/// raw samples first mapped through <see cref="DecodeMap"/> (for an Indexed image, <see cref="ImageDecodeMap.MapToIndices"/>).
/// </para>
/// <para>
/// Most images are decoded when the list is built. An image whose codec decodes reduced resolutions (<see cref="MaxReduction"/>
/// above 0; <c>ImageHeader.MaxReduction</c>) is instead kept encoded, its header read and its limits checked, and is decoded when it
/// is drawn: <see cref="GetSamplesAt(int)"/> decodes it at the reduction asked for, once per reduction however many tiles ask at the same
/// time, and the document's image cache keeps the result within its budget. A decode that fails then records its diagnostics on
/// the document when the image is first drawn, not when the list is built, and the image paints nothing. A finer reduction already
/// decoded serves a coarser request. The encoded data lives in the list, so a backend still never reads the file.
/// </para>
/// </remarks>
public sealed class DisplayImage
{
    /// <summary>
    /// The fewest samples an image decoded at build must have to be reduced when drawn (512 x 512); below it, converting it whole
    /// costs little, and the image keeps no reduction state.
    /// </summary>
    internal const long MinimumReducedPixels = 1 << 18;

    // Shared zero rows for GetRow of an image that cannot be decoded (it paints nothing; the bytes only keep old backends safe).
    private static byte[] _blank = [];

    // What the samples come from, one field so an image costs no more than it did before reductions (issue #362): the decoded
    // samples of an image decoded at build, or the Reductions of an image kept encoded or large enough to be reduced.
    private readonly object _source;
    private readonly double[] _colorKey = [];
    private readonly float[] _matte = [];
    private readonly byte[] _colorTable = [];

    internal DisplayImage(DecodedImage samples, PdfColorSpace colorSpace, ImageDecodeMap decodeMap, bool interpolate, ByteBudgetCache<(object Source, int Slot), IBudgetedEntry>? cache = null)
    {
        // Issue #362: a large image decoded when the list was built is reduced from its full decode, by a box average, when drawn
        // small (Flate, LZW, raw samples, CCITT, JBIG2); a small one is converted whole.
        _source = cache is not null && (long)samples.Width * samples.Height >= MinimumReducedPixels
            ? new Reductions(null, cache, ImageGeometry.MaxReduction) { Samples = samples }
            : samples;
        ColorSpace = colorSpace;
        DecodeMap = decodeMap;
        Interpolate = interpolate;
    }

    internal DisplayImage(EncodedImage encoded, ByteBudgetCache<(object Source, int Slot), IBudgetedEntry> cache, PdfColorSpace colorSpace, ImageDecodeMap decodeMap, bool interpolate)
    {
        _source = new Reductions(encoded, cache, Math.Clamp(encoded.Header.MaxReduction, 0, ImageGeometry.MaxReduction));
        ColorSpace = colorSpace;
        DecodeMap = decodeMap;
        Interpolate = interpolate;
    }

    /// <summary>Gets the samples decoded at build, or <see langword="null"/> for an image kept encoded.</summary>
    private DecodedImage? Decoded => _source as DecodedImage ?? (_source as Reductions)?.Samples;

    /// <summary>Gets the header of an image kept encoded, or the decoded samples' facts.</summary>
    private ImageHeader Facts => Decoded is { } decoded
        ? new ImageHeader(decoded.Width, decoded.Height, decoded.Components, decoded.BitsPerComponent)
        : ((Reductions)_source).Encoded!.Header;

    /// <summary>Gets the width in samples.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (Width), or what the codec reported (§7.4.9).</remarks>
    public int Width => Facts.Width;

    /// <summary>Gets the height in samples.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (Height), or what the codec reported (§7.4.9).</remarks>
    public int Height => Facts.Height;

    /// <summary>Gets the number of colour components per sample: the colour space's (1 for an Indexed image).</summary>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public int Components => Facts.Components;

    /// <summary>Gets the logical bits per component n: raw values run from 0 to 2^n − 1.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (BitsPerComponent).</remarks>
    public int BitsPerComponent => Facts.BitsPerComponent;

    /// <summary>Gets the bits each component occupies in <see cref="Samples"/>: 1, 2, 4, 8 or 16.</summary>
    /// <remarks>Equal to <see cref="BitsPerComponent"/> except for depths other than those five, stored at the next wider one, values unchanged.</remarks>
    public int StorageBits => Decoded?.StorageBits ?? ImageGeometry.StorageBits(BitsPerComponent);

    /// <summary>Gets the number of bytes per row of <see cref="Samples"/>.</summary>
    /// <remarks>ISO 32000-2 §8.9.3: each row starts on a byte boundary.</remarks>
    public int Stride => Decoded?.Stride ?? (int)ImageGeometry.Stride(Width, Components, StorageBits);

    /// <summary>Gets the raw samples at the image's own resolution, <see cref="Stride"/> × <see cref="Height"/> bytes in the §8.9.3 layout.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.3. Rows the data did not reach are zero. For an image kept encoded this decodes it at its full resolution
    /// (<see cref="GetSamplesAt(int)"/> with 0); empty when it cannot be decoded.
    /// </remarks>
    public ReadOnlySpan<byte> Samples => GetSamplesAt(0) is { } samples ? samples.Samples : default;

    /// <summary>
    /// Gets the largest reduction, as a power of two, that <see cref="GetSamplesAt(int)"/> can deliver: 0 for an image decoded when the list
    /// was built, up to 3 (an eighth) or more for one whose codec decodes reduced resolutions.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.4.8 (DCT) and §7.4.9 (JPEG 2000 resolution levels): what the codec's header says it can do.</remarks>
    public int MaxReduction => _source is Reductions reductions ? reductions.MaxReduction : 0;

    /// <summary>
    /// Returns the image's samples at a reduction of 2^<paramref name="reduction"/>, or at the finest one that is cheaper to get: a
    /// finer reduction already decoded, or the largest the codec delivers (<see cref="MaxReduction"/>).
    /// </summary>
    /// <param name="reduction">The reduction asked for: 0 for the image's own resolution, 1 for half, 2 for a quarter, 3 for an eighth.</param>
    /// <returns>
    /// The samples, with the reduction delivered (<see cref="DisplayImageSamples.Reduction"/>, never more than asked for) and the
    /// image's own size; <see langword="null"/> when the image cannot be decoded, which was recorded as a diagnostic of the document
    /// the first time, and which paints nothing.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reduction"/> is negative.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §8.9.3 and §8.9.4. Reduced samples are for displaying an image smaller than its resolution, where at least
    /// <see cref="GetReduction"/>'s samples per device pixel remain: never for printing, export or image extraction, which use the
    /// image's own samples (reduction 0, or <see cref="PdfImage.Decode()"/>).
    /// </para>
    /// <para>
    /// Thread-safe: each reduction is decoded at most once however many threads ask for it at the same time, and is then kept in
    /// the document's image cache, charged against <see cref="PdfOptions.ImageCacheBudget"/>; an evicted reduction is decoded again
    /// when next asked for. Samples larger than the budget, or with a budget of 0, stay with the image for as long as the list holds
    /// it.
    /// </para>
    /// </remarks>
    public DisplayImageSamples? GetSamplesAt(int reduction) => GetSamplesAt(reduction, CancellationToken.None);

    /// <summary>As <see cref="GetSamplesAt(int)"/>, stopping a decode in progress when <paramref name="cancellationToken"/> is cancelled.</summary>
    /// <param name="reduction">The reduction asked for: 0 for the image's own resolution, 1 for half, 2 for a quarter, 3 for an eighth.</param>
    /// <param name="cancellationToken">Given to the codec; a render's token, so that a cancelled tile does not wait for a long decode.</param>
    /// <returns>As <see cref="GetSamplesAt(int)"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reduction"/> is negative.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled during the decode; nothing is kept, and the next request decodes again.</exception>
    /// <remarks>ISO 32000-2 §8.9.3 and §8.9.4; viewer capability 2 (cancellation).</remarks>
    public DisplayImageSamples? GetSamplesAt(int reduction, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reduction);
        if (_source is DecodedImage decodedAtBuild)
        {
            return new DisplayImageSamples(decodedAtBuild, decodedAtBuild.Width, decodedAtBuild.Height);
        }

        var reductions = (Reductions)_source;
        int wanted = Math.Min(reduction, reductions.MaxReduction);
        if (reductions.Samples is { } whole && wanted == 0)
        {
            return new DisplayImageSamples(whole, whole.Width, whole.Height);
        }

        if (reductions.Failed)
        {
            return null;
        }

        // A finer reduction already made serves this one (zoom-out); for an image decoded at build that excludes its full decode,
        // whose reduction is cheaper than painting it.
        for (int finer = wanted; finer >= (reductions.Samples is null ? 0 : 1); finer--)
        {
            if (Find(reductions, finer) is { } kept)
            {
                return kept;
            }
        }

        Slot slot = reductions.Slots[wanted];
        lock (slot.Gate)
        {
            if (Find(reductions, wanted) is { } kept)
            {
                return kept;
            }

            if (reductions.Failed)
            {
                return null;
            }

#pragma warning disable CA2000 // The cache and the slots own the samples from here on.
            DecodedImage? decoded = reductions.Samples is { } source
                ? ImageReducer.Reduce(source, wanted, cancellationToken)
                : reductions.Encoded!.Decode(wanted, cancellationToken);
#pragma warning restore CA2000
            if (decoded is null)
            {
                reductions.Failed = true;
                return null;
            }

            Keep(reductions, decoded.Reduction, decoded);
            if (decoded.Reduction != wanted)
            {
                // The codec delivered less than was asked for: later requests for this reduction find the same samples.
                slot.Weak.SetTarget(decoded);
                slot.Alias = true;
            }

            return new DisplayImageSamples(decoded, Width, Height);
        }
    }

    /// <summary>
    /// Returns the reduction to ask <see cref="GetSamplesAt(int)"/> for when the image is drawn under <paramref name="transform"/>: the
    /// largest power of two that still leaves at least <paramref name="samplesPerPixel"/> samples per device pixel along the image's
    /// most enlarged direction, within what the codec delivers and what the colour conversion allows.
    /// </summary>
    /// <param name="transform">The transform from the unit square the image fills (user space, §8.9.4) to device pixels: the CTM times the device transform.</param>
    /// <param name="samplesPerPixel">
    /// The fewest reduced samples per device pixel: 2 keeps the page within 36 dB of a full decode on photographs (the default of
    /// the viewers); 1 is faster and coarser. At least 1, so an image is never enlarged by its reduction.
    /// </param>
    /// <param name="converter">
    /// The converter of the image's colour space to the device, or <see langword="null"/> when the samples are not converted (a soft
    /// mask's opacity). A codec averages samples before colour conversion, which equals averaging after it only for an affine
    /// conversion: for a curved one the reduction is capped at 1/2 (1/4 for a CalGray or CalRGB tone curve), for an Indexed image it
    /// is 0, and the rasterizer averages the rest after converting.
    /// </param>
    /// <returns>The reduction, from 0 to <see cref="MaxReduction"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="samplesPerPixel"/> is not a finite number of at least 1.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.9.4 (image space maps to the unit square through <c>[1/W 0 0 −1/H 0 1]</c>) and §8.9.5.3 (Interpolate only
    /// changes enlargement, so it does not change the choice). With A the linear part of image space to device space, the reduction
    /// s satisfies s ≤ 1 / (k · σmax(A)), σmax the larger singular value, which handles rotation and skew conservatively. The
    /// region drawn does not matter: the choice depends on scale alone, so every tile of a page agrees. An image with a colour key
    /// (§8.9.6.4, compared with raw samples) or a Matte (§11.6.5.2, removed with its soft mask at the image's size) is not reduced.
    /// </remarks>
    public int GetReduction(in Matrix transform, double samplesPerPixel, PdfColorConverter? converter)
    {
        if (!double.IsFinite(samplesPerPixel) || samplesPerPixel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(samplesPerPixel), samplesPerPixel, "The samples per pixel must be a finite number of at least 1.");
        }

        int cap = MaxReduction;
        if (cap == 0 || !ColorKey.IsEmpty || !Matte.IsEmpty)
        {
            return 0;
        }

        if (converter is not null)
        {
            // An image decoded at build is reduced before conversion only through an affine one; otherwise its rows are converted
            // first and the rasterizer averages them, as before.
            int limit = converter.MaxImageReduction;
            cap = Math.Min(cap, Decoded is not null && limit < Graphics.Colors.ImageReductions.Any ? 0 : limit);
        }

        return Reduction(Width, Height, transform, samplesPerPixel, cap);
    }

    /// <summary>
    /// The reduction rule of <see cref="GetReduction"/> for a <paramref name="width"/> × <paramref name="height"/> image: the largest
    /// power of two s with s ≤ 1 / (k · σmax(A)), at most 2^<paramref name="cap"/>.
    /// </summary>
    internal static int Reduction(int width, int height, in Matrix transform, double samplesPerPixel, int cap)
    {
        double a = transform.A / width;
        double b = transform.B / width;
        double c = transform.C / height;
        double d = transform.D / height;
        double sum = (a * a) + (b * b) + (c * c) + (d * d);
        double determinant = (a * d) - (b * c);
        double sigma = Math.Sqrt((sum + Math.Sqrt(Math.Max(0, (sum * sum) - (4 * determinant * determinant)))) / 2);
        if (!(sigma > 0) || !double.IsFinite(sigma))
        {
            return 0;
        }

        double largest = 1 / (samplesPerPixel * sigma);
        int reduction = 0;
        while (reduction < cap && (double)(1L << (reduction + 1)) <= largest)
        {
            reduction++;
        }

        return reduction;
    }

    /// <summary>
    /// Returns the mapping from the raw values of <paramref name="samples"/> (from <see cref="GetSamplesAt(int)"/>) to colour
    /// components: <see cref="DecodeMap"/>, at 8 bits when a reduction turned samples of fewer bits (a 1-bit scan, a 4-bit image)
    /// into 8-bit averages.
    /// </summary>
    /// <param name="samples">Samples of this image.</param>
    /// <returns>The mapping; the same instance on every call for samples of the same depth.</returns>
    /// <remarks>ISO 32000-2 §8.9.5.2, Table 88: linear, so the mean of raw values maps to the mean of their component values.</remarks>
    public ImageDecodeMap DecodeMapFor(DisplayImageSamples samples) => MapFor(samples.Decoded);

    /// <summary>The Decode mapping of reduced samples: the image's, at 8 bits when the reduction widened samples of fewer.</summary>
    private ImageDecodeMap MapFor(DecodedImage decoded)
    {
        ImageDecodeMap map = DecodeMap;
        if (decoded.BitsPerComponent == map.BitsPerComponent)
        {
            return map;
        }

        if (_source is Reductions { Widened: { } widened } && widened.BitsPerComponent == decoded.BitsPerComponent)
        {
            return widened;
        }

        // Table 88 is linear: the mean of raw values r / (2^n − 1) is the 8-bit value v / 255 with the same Dmin and Dmax.
        int top = (1 << map.BitsPerComponent) - 1;
        double[] decode = new double[2 * map.Components];
        for (int c = 0; c < map.Components; c++)
        {
            decode[2 * c] = map.Map(c, 0);
            decode[(2 * c) + 1] = map.Map(c, top);
        }

        ImageDecodeMap created = ImageDecodeMap.Create(decode, map.Components, decoded.BitsPerComponent);
        if (_source is Reductions reductions)
        {
            reductions.Widened = created;
        }

        return created;
    }

    /// <summary>Gets the colour space of the samples, as resolved before default colour spaces apply.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.5.1, Table 87 (ColorSpace); for a JPEG 2000 image without one, the codestream's (§7.4.9). Never a Pattern
    /// space; image masks (§8.9.6.2) are not interned here.
    /// </remarks>
    public PdfColorSpace ColorSpace { get; }

    /// <summary>Gets the mapping from raw sample values to colour components, the codec's polarity folded in.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.2, Table 88 (Decode).</remarks>
    public ImageDecodeMap DecodeMap { get; }

    /// <summary>Gets a value indicating whether the image asks to be interpolated (smoothed) when it is enlarged.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.3 and Table 87 (Interpolate; <c>I</c> for an inline image, Table 91).</remarks>
    public bool Interpolate { get; }

    /// <summary>
    /// Gets the image's soft mask: a one-component image (DeviceGray, its own Decode mapping) whose values, 0 to 1, are the opacity
    /// of the image at each point; <see langword="null"/> when the image has none.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §11.6.5.2 (a soft-mask image, <c>SMask</c>, Table 143) or §7.4.9 and §8.9.5.1 Table 87 (<c>SMaskInData</c>:
    /// the opacity channel of JPEG 2000 data). It maps onto the unit square like the image, at its own width and height, which may
    /// differ from the image's; a backend resamples it to the device, never to the image's size. It overrides any soft mask in the
    /// graphics state for this image (§11.6.4.3).
    /// </remarks>
    public DisplayImage? SoftMask { get; internal init; }

    /// <summary>Gets the image's explicit mask: where it paints, the image shows; elsewhere it leaves the backdrop; <see langword="null"/> when the image has none.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.6.3 (<c>Mask</c> naming an image mask): the mask maps onto the unit square like the image, at its own
    /// resolution.
    /// </remarks>
    public DisplayImageMask? Mask { get; internal init; }

    /// <summary>
    /// Gets the colour key ranges: a minimum and maximum raw sample value per component; a sample whose every component is in its
    /// range is not painted. Empty when the image has none.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.6.4 (<c>Mask</c> as an array): compared with the raw samples, before <see cref="DecodeMap"/>; for an Indexed
    /// image the values are indices. <see cref="ImageRows.ColorKey"/> computes the coverage of a row.
    /// </remarks>
    public ReadOnlySpan<double> ColorKey => _colorKey;

    /// <summary>
    /// Gets the colour the samples were pre-blended with, one value per component of <see cref="ColorSpace"/> (of its base for an
    /// Indexed space), to be removed with the <see cref="SoftMask"/>'s opacity before conversion; empty when there is none.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §11.6.5.2, Table 144 (<c>Matte</c>): c = m + (c′ − m) / α (see <see cref="ImageRows.Unpremultiply"/>); the soft
    /// mask then has the image's size. JPEG 2000 opacity premultiplied into the colour (<c>SMaskInData</c> 2, §8.9.5.1 Table 87) is
    /// a matte of zeros.
    /// </remarks>
    public ReadOnlySpan<float> Matte => _matte;

    /// <summary>
    /// Gets the colour table of an Indexed image as stored (m bytes per colour, m the base space's component count), read when the
    /// list is built so that a backend never reads the file for it; empty for any other image.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.6.6.3: each byte maps onto its base component's range. A backend needs it where it works in the base space,
    /// such as removing a <see cref="Matte"/> from the table's colours (§11.6.5.2).
    /// </remarks>
    public ReadOnlySpan<byte> ColorTable => _colorTable;

    /// <summary>Gets the colour table's backing array.</summary>
    internal byte[] ColorTableBytes
    {
        get => _colorTable;
        init => _colorTable = value;
    }

    /// <summary>Gets the colour key ranges' backing array.</summary>
    internal double[] ColorKeyRanges
    {
        get => _colorKey;
        init => _colorKey = value;
    }

    /// <summary>Gets the matte's backing array.</summary>
    internal float[] MatteColor
    {
        get => _matte;
        init => _matte = value;
    }

    /// <summary>Gets one row of <see cref="Samples"/>.</summary>
    /// <param name="y">The row, from 0 at the top of the image.</param>
    /// <returns>The row's <see cref="Stride"/> bytes; zeros when the image cannot be decoded (<see cref="GetSamplesAt(int)"/> is then <see langword="null"/>).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is outside the image.</exception>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public ReadOnlySpan<byte> GetRow(int y)
    {
        if (GetSamplesAt(0) is { } samples)
        {
            return samples.GetRow(y);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        int stride = Stride;
        byte[] blank = Volatile.Read(ref _blank);
        if (blank.Length < stride)
        {
            blank = new byte[stride];
            Volatile.Write(ref _blank, blank);
        }

        return blank.AsSpan(0, stride);
    }

    /// <summary>
    /// Gets the bytes this image keeps: its samples (with a codec's opacity channel), its masks unless the soft mask is that channel,
    /// its colour key, matte and colour table; what a render cache charges for it (issue #103).
    /// </summary>
    internal long RetainedBytes =>
        (Decoded?.RetainedBytes ?? 0L)
        + (_source is Reductions { Encoded: { } encoded } ? encoded.RetainedBytes : 0L)
        + (SoftMask is { } soft && (Decoded is not { } decoded || !ReferenceEquals(soft.Decoded, decoded.Alpha)) ? soft.RetainedBytes : 0L)
        + (Mask?.RetainedBytes ?? 0L)
        + (_colorKey.Length * (long)sizeof(double))
        + (_matte.Length * (long)sizeof(float))
        + _colorTable.Length;
    /// <summary>Returns the pooled samples (and the masks'), for an image no list will own: the build that decoded it failed.</summary>
    internal void Release()
    {
        Decoded?.Dispose();
        Mask?.Release();
        SoftMask?.Release();
    }

    /// <summary>Returns the samples of <paramref name="reduction"/> when they are still held, keeping them in the cache.</summary>
    private DisplayImageSamples? Find(Reductions reductions, int reduction)
    {
        Slot slot = reductions.Slots[reduction];
        if (slot.Strong is { } strong)
        {
            return new DisplayImageSamples(strong, Width, Height);
        }

        if (!slot.Weak.TryGetTarget(out DecodedImage? samples))
        {
            return null;
        }

        if (!slot.Alias && !reductions.Cache.TryGet((this, samples.Reduction), out _))
        {
            // Evicted but still alive (a render holds it): back into the cache, or held here when it no longer fits.
            Keep(reductions, samples.Reduction, samples);
        }

        return new DisplayImageSamples(samples, Width, Height);
    }

    /// <summary>Offers the samples to the document's image cache, and holds them here when the cache will not keep them.</summary>
    private void Keep(Reductions reductions, int reduction, DecodedImage samples)
    {
        Slot slot = reductions.Slots[reduction];
        slot.Weak.SetTarget(samples);
        slot.Alias = false;
        if (!reductions.Cache.Add((this, reduction), new DisplayImageSamplesEntry(samples)))
        {
            slot.Strong = samples;
        }
    }

    /// <summary>The state of an image decoded when drawn: its encoded data, the cache its samples go to, one slot per reduction.</summary>
    private sealed class Reductions(EncodedImage? encoded, ByteBudgetCache<(object Source, int Slot), IBudgetedEntry> cache, int maxReduction)
    {
        private Slot[]? _slots;
        private bool _failed;

        /// <summary>Gets the encoded image, or <see langword="null"/> for one decoded at build.</summary>
        public EncodedImage? Encoded { get; } = encoded;

        /// <summary>Gets or sets the full decode of an image decoded at build, reduced from by a box average.</summary>
        public DecodedImage? Samples { get; init; }

        /// <summary>Gets the document's image cache.</summary>
        public ByteBudgetCache<(object Source, int Slot), IBudgetedEntry> Cache { get; } = cache;

        /// <summary>Gets or sets the Decode mapping of reduced samples widened to 8 bits, made once.</summary>
        public ImageDecodeMap? Widened { get; set; }

        /// <summary>Gets the largest reduction.</summary>
        public int MaxReduction { get; } = maxReduction;

        /// <summary>Gets one slot per reduction, 0 to the largest, made on first use.</summary>
        public Slot[] Slots => Volatile.Read(ref _slots) ?? CreateSlots();

        /// <summary>Gets or sets a value indicating whether the decode failed (reported once; the image paints nothing).</summary>
        public bool Failed
        {
            get => Volatile.Read(ref _failed);
            set => Volatile.Write(ref _failed, value);
        }

        private Slot[] CreateSlots()
        {
            var slots = new Slot[MaxReduction + 1];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new Slot();
            }

            return Interlocked.CompareExchange(ref _slots, slots, null) ?? slots;
        }
    }

    /// <summary>The once-gate and the references of one reduction.</summary>
    private sealed class Slot
    {
        /// <summary>Gets the lock a decode of this reduction holds.</summary>
        public Lock Gate { get; } = new();

        /// <summary>Gets the samples while anything holds them (the cache, a render).</summary>
        public WeakReference<DecodedImage> Weak { get; } = new(null!);

        /// <summary>Gets or sets the samples the cache would not keep (over its budget), held for as long as the image is.</summary>
        public DecodedImage? Strong { get; set; }

        /// <summary>Gets or sets a value indicating whether <see cref="Weak"/> holds a coarser request's fallback to another reduction.</summary>
        public bool Alias { get; set; }
    }
}
