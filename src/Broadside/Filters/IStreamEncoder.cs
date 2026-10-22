using System.Buffers;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>
/// A stream encoder: writes data in the encoding a <c>Filter</c> entry names, so that the filter of that name decodes it back. The
/// encoder extension point, separate from the decoding contract <see cref="IStreamFilter"/> so that a decode-only filter never has to
/// encode: every engine holds one encoder per name, the managed defaults unless <see cref="PdfOptions.UseStreamEncoder"/> replaced
/// or added one.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.1: a PDF writer may encode the data of a stream to compress it or to convert it to a portable ASCII
/// representation, or both. A document encodes a stream (<see cref="PdfDocument.EncodeStream"/>) by running the encoders its
/// <c>Filter</c> entry names in the reverse of decoding order, last name first, each over the previous one's output. The pipeline,
/// not the encoder, applies the predictor functions of §7.4.4.4 before any encoder named <c>LZWDecode</c> or <c>FlateDecode</c>
/// (a replacement for either gets them too and must not apply them itself); <c>Crypt</c> (§7.4.10) is never an encoder.
/// </para>
/// <para>
/// Thread safety: one instance serves every document of every thread of the engines it is registered with. An implementation keeps
/// no state between calls.
/// </para>
/// <para>
/// Encoding is writing, not reading, so leniency does not apply: parameters an encoder cannot honour make it throw
/// <see cref="ArgumentException"/>. Whatever an encoder writes decodes, through the filter of the same name and the same
/// parameters, to exactly the data it was given, except for a lossy image encoder (<see cref="DctEncoder"/>, §7.4.8), whose output
/// decodes to samples of the same size close to it.
/// </para>
/// </remarks>
public interface IStreamEncoder
{
    /// <summary>Gets the name of the filter that decodes what this encoder writes, such as <c>FlateDecode</c>: the key it is registered under.</summary>
    /// <remarks>ISO 32000-2 §7.4.1, Table 6. Always the full name, never an inline-image abbreviation (§8.9.7, Table 92).</remarks>
    CosName Name { get; }

    /// <summary>Encodes <paramref name="data"/> and writes the result to <paramref name="output"/>.</summary>
    /// <param name="data">The data to encode: the stream's data, or the previous encoder's output in a chain. May be empty.</param>
    /// <param name="output">Where to write the encoded bytes.</param>
    /// <param name="context">The parameters the decoding filter will read, the stream's dictionary and the compression level.</param>
    /// <exception cref="ArgumentException">The parameters ask for something the encoder cannot write.</exception>
    /// <remarks>ISO 32000-2 §7.4.</remarks>
    void Encode(ReadOnlySpan<byte> data, IBufferWriter<byte> output, StreamEncoderContext context);
}
