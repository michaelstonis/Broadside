using Broadside.Images;

namespace Broadside.Rendering.Skia;

/// <summary>Registers SkiaSharp's JPEG decoder with an engine's options.</summary>
public static class SkiaImageDecoderExtensions
{
    /// <summary>
    /// Has the engine decode JPEG images with SkiaSharp's decoder (<see cref="SkiaImageDecoder.Shared"/>) before a viewer draws them,
    /// scaled to the resolution they are drawn at; the managed codec stays the fallback and the decoder of everything else.
    /// </summary>
    /// <param name="options">The engine's options.</param>
    /// <returns><paramref name="options"/>.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.4.8. Only images a viewer prefetches go to it (<see cref="DisplayList.PrefetchImagesAsync"/>, which the viewer's
    /// tile backends call); <see cref="PdfImage.Decode()"/>, text extraction and export decode with the managed codec. The decoder is
    /// within <see cref="PlatformImageFidelity.Tolerance"/> of the managed one, not identical (see <see cref="SkiaImageDecoder"/>).
    /// Turn it off again with <c>WithPlatformImageDecoder(SkiaImageDecoder.DecoderName, false)</c>.
    /// </remarks>
    public static PdfOptions UseSkiaImageDecoder(this PdfOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UsePlatformImageDecoder(SkiaImageDecoder.Shared);
    }
}
