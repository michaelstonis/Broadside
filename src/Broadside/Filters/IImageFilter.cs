using Broadside.Images;

namespace Broadside.Filters;

/// <summary>
/// The image facet of a stream filter: an image codec (DCT, CCITT fax, JBIG2, JPEG 2000) that can also decode straight into a
/// <see cref="DecodedImage"/> and report what it learned about the samples, beside the plain bytes-to-bytes path of
/// <see cref="IStreamFilter"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.6 to §7.4.9 and §8.9. Register the codec like any filter, with <see cref="PdfOptions.UseFilter"/>. When an image
/// is decoded and the last filter of its chain implements this interface, the image layer decodes the filters before it as bytes,
/// then calls <see cref="TryReadHeader"/> (to check the codestream's size against the engine's limits before anything is allocated)
/// and <see cref="DecodeImage"/>. Anywhere else (a stream decoded as bytes, a codec that is not last in its chain) the filter's
/// <see cref="IStreamFilter.Decode"/> runs, and must then deliver the §8.9.3 layout in PDF polarity.
/// </para>
/// <para>
/// What the codec reports wins over the image dictionary where §7.4.9 and the codec formats say so: the image's width, height and
/// bits per component come from the codestream (a mismatch with the dictionary is recorded as a diagnostic); the dictionary's
/// <c>ColorSpace</c> wins over <see cref="DecodedImage.ColorModel"/>; an alpha plane is kept only when the dictionary's
/// <c>SMaskInData</c> asks for it (<see cref="ImageFilterContext.WantsAlpha"/>).
/// </para>
/// <para>Thread safety and leniency as for <see cref="IStreamFilter"/>: stateless, every call self-contained.</para>
/// </remarks>
public interface IImageFilter : IStreamFilter
{
    /// <summary>Reads the codestream's header without decoding the samples.</summary>
    /// <param name="encoded">The data the filter would decode.</param>
    /// <param name="context">The image's dictionary values, limits and the filter context.</param>
    /// <param name="header">The image's size and components as the codestream declares them.</param>
    /// <returns><see langword="false"/> when the header cannot be read; <see cref="DecodeImage"/> is then not called.</returns>
    /// <remarks>ISO 32000-2 §7.4.9 (JPEG 2000 dimensions win over the dictionary's).</remarks>
    bool TryReadHeader(ReadOnlySpan<byte> encoded, ImageFilterContext context, out ImageHeader header);

    /// <summary>Decodes the codestream into an image.</summary>
    /// <param name="encoded">The data to decode.</param>
    /// <param name="context">The image's dictionary values, limits and the filter context; create the image with <see cref="ImageFilterContext.TryCreateImage"/>.</param>
    /// <returns>The image; <see langword="null"/> when nothing could be decoded (after reporting why through the context).</returns>
    /// <remarks>ISO 32000-2 §7.4 and §8.9.3.</remarks>
    DecodedImage? DecodeImage(ReadOnlyMemory<byte> encoded, ImageFilterContext context);
}
