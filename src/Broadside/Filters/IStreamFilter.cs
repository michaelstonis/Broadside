using System.Buffers;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>
/// A stream filter: the codec for one encoding a stream's <c>Filter</c> entry can name. The filter extension point: every engine
/// holds one filter per name, the managed defaults unless <see cref="PdfOptions.UseFilter"/> replaced or added one.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.1. A document decodes a stream by running the filters its <c>Filter</c> entry names, in order, each one over the
/// previous one's output (§7.3.8.2, Table 5). The pipeline around the filters, not the filters, handles everything shared:
/// reading <c>Filter</c> and <c>DecodeParms</c>, the inline-image abbreviations of §8.9.7 Table 92, the predictor functions of
/// §7.4.4.4 after any filter named <c>LZWDecode</c> or <c>FlateDecode</c> (a replacement for either gets them too and must not apply
/// them itself), the <c>Crypt</c> filter of §7.4.10 (handled by the security handler, never by a registered filter), the
/// decoded-length limit (<see cref="PdfOptions.MaxDecodedStreamLength"/>), and converting any exception other than a strict-mode
/// <see cref="Diagnostics.DiagnosticException"/> into a diagnostic.
/// </para>
/// <para>
/// Thread safety: one instance serves every document of every thread of the engines it is registered with. An implementation
/// keeps no state between calls; everything a decode needs lives in locals, pooled buffers or the <see cref="FilterContext"/>.
/// </para>
/// <para>
/// Leniency (ADR 0005): on malformed data a filter writes what it can decode, reports one diagnostic per kind of deviation through
/// <see cref="FilterContext.Report"/>, and returns. In strict mode <see cref="FilterContext.Report"/> throws, which ends the decode.
/// </para>
/// </remarks>
public interface IStreamFilter
{
    /// <summary>Gets the name the filter has in a <c>Filter</c> entry, such as <c>FlateDecode</c>: the key it is registered under.</summary>
    /// <remarks>ISO 32000-2 §7.4.1, Table 6. Always the full name; the pipeline expands the abbreviations of §8.9.7 Table 92.</remarks>
    CosName Name { get; }

    /// <summary>Decodes <paramref name="encoded"/> and writes the result to <paramref name="output"/>.</summary>
    /// <param name="encoded">The data to decode: the stream's data, or the previous filter's output in a chain. Never empty.</param>
    /// <param name="output">Where to write the decoded bytes.</param>
    /// <param name="context">
    /// The filter's parameters (its <c>DecodeParms</c> dictionary), the stream's dictionary, the means to resolve references and
    /// decode other streams, and the diagnostics sink.
    /// </param>
    /// <remarks>ISO 32000-2 §7.4.</remarks>
    void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context);
}
