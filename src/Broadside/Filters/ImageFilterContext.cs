using System.Diagnostics.CodeAnalysis;
using Broadside.Diagnostics;
using Broadside.Images;
using Broadside.Parsing;

namespace Broadside.Filters;

/// <summary>
/// What an <see cref="IImageFilter"/> needs to decode an image: the image dictionary's values, the engine's image limits, and the
/// filter context (parameters, stream dictionary, references, diagnostics).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.5.1, Table 87. The values are the dictionary's as read, 0 (or <see langword="false"/>) when absent; a codec whose
/// format carries its own (JPEG 2000) uses those instead. A context created with the public constructor stands alone, for tests and
/// for using a codec outside a document.
/// </para>
/// <para>A context belongs to one decode call on one thread.</para>
/// </remarks>
public sealed class ImageFilterContext
{
    /// <summary>Initializes a new instance of the <see cref="ImageFilterContext"/> class.</summary>
    /// <param name="filter">The filter context of the decode: the codec's <c>DecodeParms</c>, the image dictionary, diagnostics.</param>
    public ImageFilterContext(FilterContext filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        Filter = filter;
        MaxDecodedLength = filter.MaxDecodedLength;
    }

    /// <summary>Gets the filter context: <see cref="FilterContext.Parameters"/>, <see cref="FilterContext.StreamDictionary"/>, diagnostics.</summary>
    public FilterContext Filter { get; }

    /// <summary>Gets the image dictionary's <c>Width</c>, or 0.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87.</remarks>
    public int Width { get; init; }

    /// <summary>Gets the image dictionary's <c>Height</c>, or 0.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87.</remarks>
    public int Height { get; init; }

    /// <summary>Gets the image dictionary's <c>BitsPerComponent</c> (1 for an image mask), or 0.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87.</remarks>
    public int BitsPerComponent { get; init; }

    /// <summary>Gets the number of components of the dictionary's <c>ColorSpace</c> (1 for an image mask), or 0 when it has none.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87; §7.4.9 (a JPEG 2000 image may omit it).</remarks>
    public int ColorComponents { get; init; }

    /// <summary>Gets a value indicating whether the image is a stencil mask (<c>ImageMask true</c>, as <see cref="Images.PdfImage.IsStencil"/>): one component, one bit.</summary>
    /// <remarks>ISO 32000-2 §8.9.6.2; §7.4.9 (a JPEG 2000 image mask has one 1-bit channel).</remarks>
    public bool IsStencil { get; init; }

    /// <summary>Gets a value indicating whether the dictionary's <c>SMaskInData</c> asks for the codestream's opacity channel; without it a codec may skip that work.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87.</remarks>
    public bool WantsAlpha { get; init; }

    /// <summary>Gets the most pixels one image may have.</summary>
    public long MaxPixels { get; init; } = PdfOptions.DefaultMaxImagePixels;

    /// <summary>Gets the most bytes one image's samples may take; the same limit as <see cref="FilterContext.MaxDecodedLength"/>, which it defaults to.</summary>
    public long MaxDecodedLength { get; init; }

    /// <summary>
    /// Creates the builder an image is decoded into, after checking its size against <see cref="MaxPixels"/> and
    /// <see cref="MaxDecodedLength"/>; reports <c>ImageTooLarge</c> instead when it is too large.
    /// </summary>
    /// <param name="width">The number of samples in each row, at least 1.</param>
    /// <param name="height">The number of rows, at least 1.</param>
    /// <param name="components">The number of components, 1 to 32.</param>
    /// <param name="bitsPerComponent">The logical bits per component, 1 to 16.</param>
    /// <param name="image">The builder, with zeroed pooled memory.</param>
    /// <returns><see langword="false"/> when the image is larger than the limits allow.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A size is out of range.</exception>
    /// <exception cref="DiagnosticException">In strict mode, when the image is too large.</exception>
    /// <remarks>ISO 32000-2 §8.9.3. Rejecting before renting is what stops a few header bytes from claiming gigabytes.</remarks>
    public bool TryCreateImage(int width, int height, int components, int bitsPerComponent, [NotNullWhen(true)] out DecodedImageBuilder? image)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(components, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(components, ImageGeometry.MaxComponents);
        ArgumentOutOfRangeException.ThrowIfLessThan(bitsPerComponent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitsPerComponent, 16);
        if (ImageGeometry.CheckLimits(width, height, components, bitsPerComponent, MaxPixels, MaxDecodedLength) is { } reason)
        {
            Filter.Report(DiagnosticCodes.ImageTooLarge, DiagnosticSeverity.Error, $"The image is not decoded: {reason}.");
            image = null;
            return false;
        }

        image = new DecodedImageBuilder(width, height, components, bitsPerComponent);
        return true;
    }
}
