using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Broadside.Images;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

/// <summary>
/// SkiaSharp's JPEG decoder (libjpeg-turbo, through <see cref="SKCodec"/>) behind the platform image decoder seam: DCT images decode
/// natively, scaled in the DCT to the reduction the backend draws them at, before a tile is drawn. Register it with
/// <see cref="SkiaImageDecoderExtensions.UseSkiaImageDecoder"/>; it is off unless registered.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.8 (DCTDecode) and §8.9.3. <see cref="SKCodec.GetScaledDimensions"/> picks libjpeg-turbo's 1/2, 1/4 or 1/8 DCT
/// scaling, and the samples are read as 8-bit gray or RGBA (Skia's JPEG decoder has no RGBX output; the alpha byte is always
/// opaque) with no destination color space, so no embedded ICC profile is applied, then
/// packed into 1 or 3 components. The Exif orientation is reported by Skia but not applied, so the samples keep the image's own
/// orientation. Arithmetic-coded frames decode; 12-bit frames do not, and with every other image the core's rule leaves out
/// (CMYK and YCCK, which Skia converts to RGB, a <c>ColorTransform</c> that disagrees with the data, a frame of another size) they
/// decode with the managed codec.
/// </para>
/// <para>
/// Fidelity: <see cref="PlatformImageFidelity.Exact"/>. libjpeg-turbo's integer IDCT (full size and its reduced IDCTs) and YCbCr
/// transform are the ones the managed decoder ports, so its samples are bit-identical, measured over the DCT test vectors and
/// generated photographs at every reduction, except where it upsamples chroma by interpolation ("fancy" upsampling: a 2:1 ratio
/// across, down or both, at 1/1 to 1/4) and the managed decoder replicates, as Adobe's DCTDecode does (measured up to 34 levels on
/// small 4:2:0 images, 1.1% of pixels over 2 levels on a 4:2:0 photograph). Those images are never taken (<see cref="CanDecode"/>):
/// 4:2:0 decodes natively from 1/2 down, where libjpeg-turbo decodes chroma at the output's resolution, and 4:2:2 or 4:4:0 at 1/8.
/// SkiaSharp 4.153 binds no YUV planes API (<c>SkCodec::getYUVAPlanes</c> has no C binding), so a full-size 4:2:0 image cannot be
/// read as planes and replicated here; it decodes with the managed codec.
/// </para>
/// <para>
/// The decode runs synchronously on the calling thread; the viewer's tile scheduler calls it from the thread pool. Rows are read in
/// bands into a pooled buffer, so the decode allocates no image-sized scratch memory beyond the samples it returns. The instance keeps
/// no state and serves every document and thread.
/// </para>
/// </remarks>
public sealed class SkiaImageDecoder : IPlatformImageDecoder
{
    /// <summary>The <see cref="Name"/> of the decoder: <c>skia</c>.</summary>
    public const string DecoderName = "skia";

    // Rows read per call of SKCodec.GetScanlines: a band of at most this many RGBA rows is the only scratch memory.
    private const int BandRows = 16;

    private SkiaImageDecoder()
    {
    }

    /// <summary>Gets the decoder, which <see cref="SkiaImageDecoderExtensions.UseSkiaImageDecoder"/> registers; it keeps no state.</summary>
    public static SkiaImageDecoder Shared { get; } = new();

    /// <inheritdoc/>
    public string Name => DecoderName;

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §7.4.8. <see cref="PlatformImageFidelity.Exact"/>: bit-identical to the managed codec for every image
    /// <see cref="CanDecode"/> takes, at the reduction it takes it at.
    /// </remarks>
    public PlatformImageFidelity Fidelity => PlatformImageFidelity.Exact;

    /// <inheritdoc/>
    /// <remarks>
    /// ITU-T T.81 Annex D: libjpeg-turbo in SkiaSharp decodes arithmetic-coded frames. It neither applies the Exif orientation nor,
    /// with no destination color space, an embedded ICC profile, so the core hands the data over unchanged.
    /// </remarks>
    public PlatformImageFeatures Features => PlatformImageFeatures.ArithmeticCoding;

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §7.4.8: DCTDecode with 8-bit samples (libjpeg-turbo in SkiaSharp does not decode 12-bit frames), and subsampled
    /// chroma only at a reduction where libjpeg-turbo replicates it or needs no upsampling (ITU-T T.81 A.1.1), so the samples are
    /// the managed decoder's.
    /// </remarks>
    public bool CanDecode(in PlatformImageRequest request) =>
        request.Filter.Value == "DCTDecode" && request.BitsPerComponent == 8 && request.Components is 1 or 3
        && (!request.ChromaSubsampled || !Interpolates(request.ChromaHorizontalSubsampling, request.ChromaVerticalSubsampling, request.Reduction));

    /// <inheritdoc/>
    public ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Decode(encoded.Span, request, cancellationToken));
    }

    /// <summary>Decodes <paramref name="encoded"/> at the request's reduction or the finest one Skia delivers above it; <see langword="null"/> when Skia cannot.</summary>
    internal static unsafe DecodedImage? Decode(ReadOnlySpan<byte> encoded, in PlatformImageRequest request, CancellationToken cancellationToken)
    {
        fixed (byte* data = encoded)
        {
            // Wraps the caller's memory without copying it: valid until this call returns, which is before the data is.
            // SkiaSharp returns null for data it does not recognize, though its annotations say otherwise.
#pragma warning disable CA1508
            using SKData? skData = SKData.Create((IntPtr)data, encoded.Length, null);
            using SKCodec? codec = skData is null ? null : SKCodec.Create(skData);
            if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Jpeg)
#pragma warning restore CA1508
            {
                return null;
            }

            SKImageInfo source = codec.Info;
            if (source.Width != request.Width || source.Height != request.Height)
            {
                return null;
            }

            // libjpeg-turbo scales by M/8: ⌈W·M/8⌉ samples. Take the coarsest power of two it delivers exactly, and never one where
            // it would interpolate chroma (the samples would not be the managed decoder's).
            int reduction = request.Reduction;
            SKSizeI size = default;
            for (; reduction > 0; reduction--)
            {
                size = codec.GetScaledDimensions(1f / (1 << reduction));
                if (size.Width == Reduce(request.Width, reduction) && size.Height == Reduce(request.Height, reduction))
                {
                    break;
                }
            }

            if (reduction == 0)
            {
                size = new SKSizeI(request.Width, request.Height);
            }

            if (request.ChromaSubsampled && Interpolates(request.ChromaHorizontalSubsampling, request.ChromaVerticalSubsampling, reduction))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"libjpeg-turbo would interpolate the chroma at reduction {reduction}, where the managed decoder replicates it"));
            }

            bool gray = request.Components == 1;
            var info = new SKImageInfo(size.Width, size.Height, gray ? SKColorType.Gray8 : SKColorType.Rgba8888, SKAlphaType.Unpremul, null);
            if (codec.StartScanlineDecode(info) != SKCodecResult.Success || codec.ScanlineOrder != SKCodecScanlineOrder.TopDown)
            {
                return null;
            }

            DecodedImageBuilder image = request.CreateImage(reduction);
            try
            {
                return Read(codec, image, info, gray, cancellationToken) ? image.Build() : null;
            }
            finally
            {
                image.Dispose();
            }
        }
    }

    /// <summary>Reads every row of a started scanline decode into <paramref name="image"/>; false when Skia stops short.</summary>
    private static unsafe bool Read(SKCodec codec, DecodedImageBuilder image, SKImageInfo info, bool gray, CancellationToken cancellationToken)
    {
        int width = info.Width;
        int height = info.Height;
        if (gray)
        {
            // Gray8 rows are the samples' own layout: decode straight into them.
            fixed (byte* samples = image.Samples)
            {
                for (int y = 0; y < height; y += BandRows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int rows = Math.Min(BandRows, height - y);
                    if (codec.GetScanlines((IntPtr)(samples + ((long)y * image.Stride)), rows, image.Stride) != rows)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        int rowBytes = width * 4;
        byte[] band = ArrayPool<byte>.Shared.Rent(rowBytes * Math.Min(BandRows, height));
        try
        {
            fixed (byte* pixels = band)
            {
                for (int y = 0; y < height; y += BandRows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int rows = Math.Min(BandRows, height - y);
                    if (codec.GetScanlines((IntPtr)pixels, rows, rowBytes) != rows)
                    {
                        return false;
                    }

                    for (int row = 0; row < rows; row++)
                    {
                        PackRgb(band.AsSpan(row * rowBytes, rowBytes), image.GetRow(y + row)[..(width * 3)]);
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(band);
        }

        return true;
    }

    /// <summary>
    /// Returns whether libjpeg-turbo, decoding chroma <paramref name="across"/> × <paramref name="down"/> times coarser than luma at
    /// 1/2^<paramref name="reduction"/>, upsamples it by interpolation; <see langword="true"/> for a pattern it cannot tell.
    /// </summary>
    /// <remarks>
    /// libjpeg-turbo's <c>jdmaster.c</c> (<c>jpeg_core_output_dimensions</c>) and <c>jdsample.c</c> (<c>jinit_upsampler</c>): a
    /// chroma component's IDCT is enlarged by powers of two while it stays within 8 and both ratios divide by it, so 4:2:0 at 1/2 and
    /// below is decoded at the output's resolution; a 2:1 ratio left across, down or both is upsampled by interpolation unless the
    /// luma IDCT is 1 (at 1/8); any other ratio is replicated, as the managed decoder does.
    /// </remarks>
    internal static bool Interpolates(int across, int down, int reduction)
    {
        if (across <= 0 || down <= 0)
        {
            return true;
        }

        int scaled = 8 >> reduction;
        if (scaled <= 1)
        {
            return false;
        }

        int enlarged = 1;
        while (scaled * enlarged * 2 <= 8 && across % (enlarged * 2) == 0 && down % (enlarged * 2) == 0)
        {
            enlarged *= 2;
        }

        return (across / enlarged, down / enlarged) is (2, 1) or (1, 2) or (2, 2);
    }

    /// <summary>⌈<paramref name="size"/> / 2^<paramref name="reduction"/>⌉: the size libjpeg's DCT scaling delivers.</summary>
    private static int Reduce(int size, int reduction) => (int)(((long)size + (1L << reduction) - 1) >> reduction);

    /// <summary>Drops the fourth (alpha) byte of each RGBA pixel: four pixels per vector, then one at a time.</summary>
    internal static void PackRgb(ReadOnlySpan<byte> rgba, Span<byte> rgb)
    {
        int pixels = rgb.Length / 3;
        ref byte from = ref MemoryMarshal.GetReference(rgba);
        ref byte to = ref MemoryMarshal.GetReference(rgb);
        int i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            Vector128<byte> order = Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 15, 15, 15, 15);

            // Each store writes 16 bytes of which 12 are kept; stop while the 4 spare bytes still land inside the row.
            for (; i + 6 <= pixels; i += 4)
            {
                Vector128.Shuffle(Vector128.LoadUnsafe(ref from, (nuint)(i * 4)), order).StoreUnsafe(ref to, (nuint)(i * 3));
            }
        }

        for (; i < pixels; i++)
        {
            Unsafe.Add(ref to, i * 3) = Unsafe.Add(ref from, i * 4);
            Unsafe.Add(ref to, (i * 3) + 1) = Unsafe.Add(ref from, (i * 4) + 1);
            Unsafe.Add(ref to, (i * 3) + 2) = Unsafe.Add(ref from, (i * 4) + 2);
        }
    }
}
