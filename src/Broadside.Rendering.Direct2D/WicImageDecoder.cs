using Broadside.Images;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// Windows Imaging Component's JPEG decoder behind the platform image decoder seam, bit-identical to the managed decoder: the images a
/// <see cref="Direct2DRenderer"/> (or any renderer of the engine) will draw decode through WIC when they are prefetched before the
/// replay (<see cref="Direct2DRenderer.PrefetchImagesAsync"/>, <see cref="DisplayList.PrefetchImagesAsync"/>). Register it with
/// <see cref="WicImageDecoderExtensions.UseWicImageDecoder"/>; turn it off with
/// <c>PdfOptions.WithPlatformImageDecoder(WicImageDecoder.DecoderName, false)</c>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.8 (DCTDecode) and §8.9.3. The factory is <c>CLSID_WICImagingFactory2</c>; the decoder reads the bytes in place and
/// decodes frame 0, at the reduction the backend draws at (<c>IWICBitmapSourceTransform</c> and
/// <c>IWICPlanarBitmapSourceTransform</c> scale in the DCT domain by powers of two) or a finer one, and reports the reduction it
/// delivered.
/// </para>
/// <para>
/// Measured on the Windows runner against the managed decoder over the DCT vectors and the generated photographs, at full size and at
/// 1/2, 1/4 and 1/8 (<c>docs/benchmarks/wic-image-decoder.md</c>): WIC's inverse DCT and its reduced IDCTs equal libjpeg's
/// (<c>-dct int</c>), so
/// </para>
/// <list type="bullet">
/// <item>a YCbCr image with subsampled chroma is taken as its Y, Cb and Cr planes (Windows 8.1), put together as the managed decoder
/// does, chroma replicated and then libjpeg's integer transform (<see cref="YCbCrConversion"/>): bit-identical at every reduction;</item>
/// <item>a gray image, or a YCbCr image without subsampling, is taken as WIC's own <c>8bppGray</c> or <c>24bppBGR</c> pixels, which
/// are bit-identical there and faster than the planes. WIC's pixels are never used for subsampled chroma: WIC interpolates it
/// (measured max 34, 31% of pixels over 2 levels), outside even the tolerance.</item>
/// </list>
/// <para>
/// Arithmetic-coded data decodes (measured bit-identical, sequential and progressive). Twelve-bit and four-component (CMYK, YCCK) data
/// is never offered (WIC rejects 12-bit frames: "The image is unrecognized"); a subsampling WIC has no planes for (4:1:1), or anything
/// else WIC rejects, decodes with the managed codec and an <c>ImageDecodedManaged</c> diagnostic. WIC converts no colour and does not
/// apply the Exif orientation, so the data is handed over as it is.
/// </para>
/// <para>The in-box codecs are MTA-capable: one instance serves every thread. Off Windows nothing is offered to it.</para>
/// </remarks>
public sealed class WicImageDecoder : IPlatformImageDecoder
{
    /// <summary>The <see cref="Name"/> of the WIC image decoder: <c>wic</c>.</summary>
    public const string DecoderName = "wic";

    /// <summary>Gets the decoder <see cref="WicImageDecoderExtensions.UseWicImageDecoder"/> registers.</summary>
    public static WicImageDecoder Shared { get; } = new();

    /// <summary>Gets a value indicating whether WIC's imaging factory can be created here (false off Windows).</summary>
    public static bool IsSupported => WicCodec.IsAvailable;

    /// <inheritdoc/>
    public string Name => DecoderName;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.8. <see cref="PlatformImageFidelity.Exact"/>: bit-identical to the managed decoder at every reduction, measured.</remarks>
    public PlatformImageFidelity Fidelity => PlatformImageFidelity.Exact;

    /// <inheritdoc/>
    /// <remarks>
    /// ITU-T T.81 Annex D: arithmetic coding decodes, bit-identical. WIC neither applies the Exif orientation nor converts embedded
    /// profiles, so the data needs no copy.
    /// </remarks>
    public PlatformImageFeatures Features => PlatformImageFeatures.ArithmeticCoding;

    /// <inheritdoc/>
    public bool CanDecode(in PlatformImageRequest request) =>
        request.Filter.Value == "DCTDecode" && request.BitsPerComponent == 8 && request.Components is 1 or 3 && IsSupported;

    /// <inheritdoc/>
    /// <remarks>Synchronous: WIC has no asynchronous decode, and the prefetch runs before the replay, off the drawing.</remarks>
    /// <exception cref="InvalidOperationException">WIC cannot decode the image bit-identically (the reason is the message).</exception>
    /// <exception cref="System.Runtime.InteropServices.COMException">WIC failed on the data.</exception>
    public ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? reason;
        DecodedImage? image;
        if (request.Components == 1 || !request.ChromaSubsampled)
        {
            image = WicCodec.Decode(encoded.Span, request, WicPath.Pixels, out reason);
            if (image is null && request.Components == 3)
            {
                image = WicCodec.Decode(encoded.Span, request, WicPath.Planes, out reason);
            }
        }
        else
        {
            image = WicCodec.Decode(encoded.Span, request, WicPath.Planes, out reason);
        }

        return image is null ? throw new InvalidOperationException(reason ?? "WIC did not decode the image") : ValueTask.FromResult<DecodedImage?>(image);
    }
}
