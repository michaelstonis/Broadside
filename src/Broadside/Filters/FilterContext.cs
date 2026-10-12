using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Filters;

/// <summary>
/// Everything a <see cref="IStreamFilter"/> may need beyond the bytes it decodes: its parameters, the stream it decodes, the means
/// to resolve references and decode other streams of the same document, the limits, and the diagnostics sink.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.3.8.2 (Table 5) and §7.4. A document creates one context per filter in a stream's chain. A context created with
/// the public constructor stands alone: it decodes other streams with the default filters, resolves no references, and keeps its
/// diagnostics in <see cref="Diagnostics"/> only, which is how a filter is tested or used outside a document.
/// </para>
/// <para>
/// Image codecs read the image's entries (<c>Width</c>, <c>Height</c>, <c>BitsPerComponent</c>, <c>ColorSpace</c>, <c>Decode</c>)
/// from <see cref="StreamDictionary"/> and decode auxiliary streams such as <c>JBIG2Globals</c> with <see cref="DecodeStream"/>.
/// </para>
/// <para>A context belongs to one decode call on one thread.</para>
/// </remarks>
public sealed class FilterContext
{
    private StreamDecoder? _decoder;
    private readonly int _depth;
    private List<Diagnostic>? _diagnostics;

    /// <summary>Initializes a new instance of the <see cref="FilterContext"/> class that stands alone, outside any document.</summary>
    public FilterContext()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FilterContext"/> class for one filter of a document's chain.</summary>
    internal FilterContext(StreamDecoder decoder, int depth)
    {
        _decoder = decoder;
        _depth = depth;
        ReadingMode = decoder.ReadingMode;
        MaxDecodedLength = decoder.MaxDecodedLength;
    }

    /// <summary>Gets the filter's parameter dictionary, or <see langword="null"/> when it has none and every parameter takes its default.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.3.8.2, Table 5: the stream's <c>DecodeParms</c> entry, or the element of its array that belongs to this filter.
    /// Values may be indirect references; read them through <see cref="Resolve"/>.
    /// </remarks>
    public CosDictionary? Parameters { get; init; }

    /// <summary>Gets the dictionary of the stream being decoded; for an inline image, its dictionary with abbreviations expanded.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.2, Table 5; for an image, §8.9.5 Table 87.</remarks>
    public CosDictionary StreamDictionary { get; init; } = new();

    /// <summary>Gets how deviations are treated: in <see cref="PdfReadingMode.Strict"/> mode, <see cref="Report"/> throws.</summary>
    /// <remarks>ADR 0005.</remarks>
    public PdfReadingMode ReadingMode { get; init; } = PdfReadingMode.Lenient;

    /// <summary>Gets the most bytes one stream may decode to; the pipeline truncates longer output with a diagnostic.</summary>
    /// <remarks>
    /// Guards against decompression bombs. A filter that must allocate for its output up front (an image codec sizing a buffer from
    /// its header) checks the size against this first.
    /// </remarks>
    public long MaxDecodedLength { get; init; } = PdfOptions.DefaultMaxDecodedStreamLength;

    /// <summary>Gets the diagnostics reported through this context, in order.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => (IReadOnlyList<Diagnostic>?)_diagnostics ?? [];

    /// <summary>Records a deviation the filter found in its data and what it did about it.</summary>
    /// <param name="code">A stable PascalCase identifier of the kind of deviation, such as <c>FilterDataTruncated</c>.</param>
    /// <param name="severity">Whether the repair kept all content (<see cref="DiagnosticSeverity.Warning"/>) or probably lost some.</param>
    /// <param name="message">What was wrong and what the filter did.</param>
    /// <exception cref="DiagnosticException">In strict mode, always: the decode ends here.</exception>
    /// <remarks>ADR 0005. Inside a document the diagnostic is also recorded on the document.</remarks>
    public void Report(string code, DiagnosticSeverity severity, string message)
    {
        var diagnostic = new Diagnostic(code, severity, message);
        (_diagnostics ??= []).Add(diagnostic);
        Decoder.Report(diagnostic);
    }

    /// <summary>Returns <paramref name="value"/>, or the object it refers to when it is an indirect reference.</summary>
    /// <param name="value">A value from <see cref="Parameters"/> or <see cref="StreamDictionary"/>, or <see langword="null"/>.</param>
    /// <returns>The direct object; <see cref="CosNull.Instance"/> for <see langword="null"/>, and, outside a document, for any reference.</returns>
    /// <remarks>ISO 32000-2 §7.3.10.</remarks>
    public CosObject Resolve(CosObject? value) => Decoder.Resolve(value);

    /// <summary>Decodes another stream of the same document through the same filters, such as a JBIG2 <c>JBIG2Globals</c> stream.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The decoded data.</returns>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation.</exception>
    /// <remarks>ISO 32000-2 §7.4.</remarks>
    public ReadOnlyMemory<byte> DecodeStream(CosStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Decoder.Decode(stream, _depth + 1);
    }

    /// <summary>Gets the document's decoder, or, for a context that stands alone, one with the default filters created on first use.</summary>
    private StreamDecoder Decoder => _decoder ??= StreamDecoder.Standalone(ReadingMode, MaxDecodedLength);

    /// <summary>Reads an integer parameter; reports and returns the default when it is not an integer in range.</summary>
    /// <param name="key">The parameter.</param>
    /// <param name="defaultValue">The value when absent or invalid.</param>
    /// <param name="minimum">The least valid value.</param>
    /// <param name="maximum">The greatest valid value.</param>
    /// <returns>The value.</returns>
    internal long ReadInteger(CosName key, long defaultValue, long minimum, long maximum)
    {
        if (Parameters is null || !Parameters.TryGetValue(key, out CosObject? entry))
        {
            return defaultValue;
        }

        switch (Resolve(entry))
        {
            case CosNull:
                return defaultValue;
            case CosInteger { Value: var value } when value >= minimum && value <= maximum:
                return value;
            default:
                Report(
                    DiagnosticCodes.DecodeParmsInvalid,
                    DiagnosticSeverity.Warning,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The {key.Value} parameter shall be an integer from {minimum} to {maximum}; the default {defaultValue} is used."));
                return defaultValue;
        }
    }
}
