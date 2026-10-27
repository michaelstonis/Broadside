using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// An image's samples converted to the rasterizer's premultiplied BGRA pixels (<see cref="ImageSource"/>), kept in the cache the
/// rasterizers drawing one document share (issue #432), with what they were converted from.
/// </summary>
/// <remarks>ISO 32000-2 §8.9.5. Never changed once published; the array is the collector's once freed.</remarks>
internal sealed class SharedPixels(uint[] pixels, DisplayImageSamples samples, bool folded, PdfColorConverter converter, PixelModel model) : SharedImage
{
    /// <summary>Gets the pixels, <see cref="DisplayImageSamples.Width"/> × <see cref="DisplayImageSamples.Height"/> of the samples, rows top to bottom.</summary>
    public uint[] Pixels { get; } = pixels;

    /// <summary>Whether these are the pixels of <paramref name="samples"/> converted this way, folded or not.</summary>
    public bool Matches(DisplayImageSamples samples, bool folded, PdfColorConverter converter, PixelModel model) =>
        samples == Samples && folded == Folded && ReferenceEquals(converter, Converter) && model == Model;

    private DisplayImageSamples Samples { get; } = samples;

    private bool Folded { get; } = folded;

    private PdfColorConverter Converter { get; } = converter;

    private PixelModel Model { get; } = model;

    /// <summary>Nothing to release: the array is managed, and goes once no one holds it.</summary>
    protected override void Free()
    {
    }
}
