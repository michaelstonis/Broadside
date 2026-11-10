namespace Broadside.Rendering.Direct2D;

/// <summary>Registers Windows Imaging Component's JPEG decoder with an engine's options.</summary>
public static class WicImageDecoderExtensions
{
    /// <summary>Has the engine decode JPEG images with Windows Imaging Component before they are drawn (<see cref="WicImageDecoder.Shared"/>).</summary>
    /// <param name="options">The engine's options.</param>
    /// <returns><paramref name="options"/>.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.4.8. Images decode through WIC when a render's images are prefetched (<see cref="Direct2DRenderer.PrefetchImagesAsync"/>,
    /// <see cref="DisplayList.PrefetchImagesAsync"/>); a render without a prefetch decodes with the managed codec. The samples are the
    /// same either way (<see cref="WicImageDecoder.Fidelity"/> is exact). Off Windows nothing is offered to it.
    /// </remarks>
    public static PdfOptions UseWicImageDecoder(this PdfOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UsePlatformImageDecoder(WicImageDecoder.Shared);
    }
}
