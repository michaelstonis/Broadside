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
    private readonly byte _reduction;

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

    /// <summary>
    /// Gets the image dictionary's geometry: its <c>Width</c>, <c>Height</c> and <c>BitsPerComponent</c> (1 for an image mask), and
    /// the number of components of its <c>ColorSpace</c> (1 for an image mask); each 0 when the dictionary has no such entry.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87; §7.4.9 (a JPEG 2000 image may omit the <c>ColorSpace</c>, its components then 0).</remarks>
    public ImageGeometry Geometry { get; init; }

    /// <summary>Gets a value indicating whether the image is an image mask (<c>ImageMask true</c>, as <see cref="PdfImage.IsImageMask"/>): one component, one bit.</summary>
    /// <remarks>ISO 32000-2 §8.9.6.2; §7.4.9 (a JPEG 2000 image mask has one 1-bit channel).</remarks>
    public bool IsImageMask { get; init; }

    /// <summary>
    /// Gets the dictionary's <c>SMaskInData</c>: whether the codestream's opacity channel is wanted, and whether the colors are
    /// premultiplied by it. Without it (<see cref="PdfSoftMaskInData.None"/>) a codec may skip that work.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87.</remarks>
    public PdfSoftMaskInData SoftMaskInData { get; init; }

    /// <summary>
    /// Gets the reduction asked for, as a power of two: 0 (the default) for the full resolution, 1 for half, 2 for a quarter, 3 for
    /// an eighth. A codec that cannot reduce, or not that far, delivers less and says so in <see cref="DecodedImageBuilder.Reduction"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or more than 15.</exception>
    /// <remarks>
    /// Asked for only by a viewer drawing the image smaller than its resolution, never for printing, export or image extraction. A
    /// reduction s = 2^n of a W × H image is ⌈W / s⌉ × ⌈H / s⌉ samples, each standing for the s × s source samples it covers (see
    /// <see cref="DecodedImage.Reduction"/>); <see cref="TryCreateImage"/> takes the reduced size.
    /// </remarks>
    public int Reduction
    {
        get => _reduction;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ImageGeometry.MaxReduction);
            _reduction = (byte)value;
        }
    }

    /// <summary>Gets the most pixels one image may have.</summary>
    public long MaxPixels { get; init; } = PdfOptions.DefaultMaxImagePixels;

    /// <summary>Gets the most bytes one image's samples may take; the same limit as <see cref="FilterContext.MaxDecodedLength"/>, which it defaults to.</summary>
    public long MaxDecodedLength { get; init; }

    /// <summary>
    /// Creates the builder an image is decoded into, after checking its size against <see cref="MaxPixels"/> and
    /// <see cref="MaxDecodedLength"/>; reports <c>ImageTooLarge</c> instead when it is too large.
    /// </summary>
    /// <param name="geometry">The geometry of the samples to decode: at least 1 sample per row and 1 row, 1 to 32 components of 1 to 16 bits.</param>
    /// <param name="image">The builder, with zeroed pooled memory.</param>
    /// <returns><see langword="false"/> when the image is larger than the limits allow.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A part of <paramref name="geometry"/> is out of range.</exception>
    /// <exception cref="DiagnosticException">In strict mode, when the image is too large.</exception>
    /// <remarks>ISO 32000-2 §8.9.3. Rejecting before renting is what stops a few header bytes from claiming gigabytes.</remarks>
    public bool TryCreateImage(ImageGeometry geometry, [NotNullWhen(true)] out DecodedImageBuilder? image)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(geometry.Width, 1, nameof(geometry));
        ArgumentOutOfRangeException.ThrowIfLessThan(geometry.Height, 1, nameof(geometry));
        ArgumentOutOfRangeException.ThrowIfLessThan(geometry.Components, 1, nameof(geometry));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(geometry.Components, ImageGeometry.MaxComponents, nameof(geometry));
        ArgumentOutOfRangeException.ThrowIfLessThan(geometry.BitsPerComponent, 1, nameof(geometry));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(geometry.BitsPerComponent, 16, nameof(geometry));
        if (geometry.CheckLimits(MaxPixels, MaxDecodedLength) is { } reason)
        {
            Filter.Report(ImageDiagnostics.ImageTooLarge, DiagnosticSeverity.Error, $"The image is not decoded: {reason}.");
            image = null;
            return false;
        }

        image = new DecodedImageBuilder(geometry);
        return true;
    }
}
