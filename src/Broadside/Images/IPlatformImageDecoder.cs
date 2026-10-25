namespace Broadside.Images;

/// <summary>
/// A platform's own image decoder (a browser's WebCodecs, an operating system codec), offered the images a viewer is about to draw so
/// that they decode off the managed decoder: asynchronously before a tile is drawn, never during its replay.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.8 (DCTDecode) and §8.9.5 (image dictionaries). Register one with <see cref="PdfOptions.UsePlatformImageDecoder"/>.
/// <c>DisplayList.PrefetchImagesAsync</c> offers it the images kept encoded in a display list (<c>DisplayImage.MaxReduction</c> above
/// 0) that the region drawn reaches, at the reduction the backend will ask for; what it returns goes into the document's image cache,
/// where the replay finds it. A replay that finds nothing decodes with the managed codec and never waits on the platform, so a host
/// with one thread cannot deadlock.
/// </para>
/// <para>
/// The core decides which images may be offered: one rule over the codestream's header and the image dictionary, so that the
/// samples a platform returns mean what the managed codec's would (§7.4.8 Table 13 <c>ColorTransform</c> and the APP14 segment,
/// 8-bit samples of 1 or 3 components, the frame's size equal to the dictionary's). <see cref="CanDecode"/> can only narrow it.
/// Before the data is handed over, the core zero-fills the segments a platform would misread, as <see cref="Features"/> says.
/// </para>
/// <para>
/// When <see cref="DecodeAsync"/> fails (returns <see langword="null"/>, throws, or delivers other samples than asked for), the
/// image decodes with the managed codec and the document records <c>ImageDecodedManaged</c> (Information) once for it, with the
/// reason. Only rendering uses a platform decoder: <see cref="PdfImage.Decode()"/>, text extraction and export always decode with
/// the managed codecs, so their samples are exact.
/// </para>
/// <para>
/// One instance serves every document and thread of an engine: it must keep no state between calls that is not thread-safe.
/// </para>
/// </remarks>
public interface IPlatformImageDecoder
{
    /// <summary>Gets the decoder's name, in diagnostics and for <see cref="PdfOptions.WithPlatformImageDecoder"/>.</summary>
    string Name { get; }

    /// <summary>Gets how close the decoder's samples are to the managed codec's, at worst.</summary>
    /// <remarks>
    /// The bar <see cref="PdfOptions.VerifyPlatformImageDecoder"/> measures against: <see cref="PlatformImageFidelity.Exact"/> allows
    /// no difference; <see cref="PlatformImageFidelity.Tolerance"/> allows at most 24 levels, at least 50 dB and at most 1% of pixels
    /// over 2 levels at full size, and at least 55 dB against the managed decode at the same reduction.
    /// </remarks>
    PlatformImageFidelity Fidelity { get; }

    /// <summary>Gets what the decoder does beyond the shared rule: codings it decodes, and segments it would misread.</summary>
    /// <remarks>ITU-T T.81 Annex B; the Exif and ICC.1 Annex B.4 APP segments. Read at every offer, so it may change once the platform is probed.</remarks>
    PlatformImageFeatures Features { get; }

    /// <summary>Returns whether the decoder takes this image; cheap and synchronous, called only for images the shared rule allows.</summary>
    /// <param name="request">What the image's header and dictionary say, and the reduction wanted.</param>
    /// <returns><see langword="true"/> to be offered the data.</returns>
    bool CanDecode(in PlatformImageRequest request);

    /// <summary>Decodes the image at <see cref="PlatformImageRequest.Reduction"/>, or at a finer reduction when that is what the platform delivers.</summary>
    /// <param name="encoded">
    /// The codestream (for DCTDecode, the JPEG data), with the segments <see cref="Features"/> names zero-filled. Valid until the
    /// returned task completes; copy what must outlive it.
    /// </param>
    /// <param name="request">What the image's header and dictionary say, and the reduction wanted.</param>
    /// <param name="cancellationToken">Cancels the decode; the core then keeps nothing.</param>
    /// <returns>
    /// Samples made with <see cref="PlatformImageRequest.CreateImage"/> (8-bit, <see cref="PlatformImageRequest.Components"/> per
    /// sample: gray, or RGB after the YCbCr transform), which the core owns from then on; or <see langword="null"/> when the platform
    /// cannot decode the image, which then decodes with the managed codec.
    /// </returns>
    ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken);
}
