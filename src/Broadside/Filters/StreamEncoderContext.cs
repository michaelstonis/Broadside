using System.IO.Compression;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>
/// Everything a <see cref="IStreamEncoder"/> may need beyond the bytes it encodes: the parameters the decoding filter will read, the
/// stream's dictionary and how hard to compress.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.3.8.2 (Table 5) and §7.4. A document creates one context per encoder of a stream's chain; a context created with
/// the public constructor stands alone, which is how an encoder is tested or used outside a document. A context belongs to one
/// encode call on one thread.
/// </remarks>
public sealed class StreamEncoderContext
{
    /// <summary>Gets the parameter dictionary the decoding filter will read, or <see langword="null"/> when every parameter takes its default.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.3.8.2, Table 5: the stream's <c>DecodeParms</c> entry, or the element of its array that belongs to this filter.
    /// An encoder writes data that decodes with these parameters; it never changes them. Values are direct objects.
    /// </remarks>
    public CosDictionary? Parameters { get; init; }

    /// <summary>Gets the dictionary of the stream being encoded, such as an image's <c>Width</c>, <c>Height</c> and <c>ColorSpace</c>.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2, Table 5; for an image, §8.9.5 Table 87.</remarks>
    public CosDictionary StreamDictionary { get; init; } = new();

    /// <summary>
    /// Gets how much an encoder that trades time for size compresses. The default is
    /// <see cref="System.IO.Compression.CompressionLevel.Optimal"/>; encoders that do not compress ignore it.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.4.4.1: RFC 1951 leaves the effort spent finding matches to the encoder.</remarks>
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Optimal;
}
