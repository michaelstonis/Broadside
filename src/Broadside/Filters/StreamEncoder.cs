using System.Buffers;
using System.IO.Compression;
using Broadside.Images;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>
/// The encoding pipeline: runs the encoders a stream dictionary's <c>Filter</c> entry names, last name first, so that decoding in
/// the entry's order gives the data back. The inverse of <see cref="StreamDecoder"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.3.8.2 (Table 5) and §7.4.1: data encoded with LZW and then ASCII base-85 is decoded by
/// <c>/Filter [/ASCII85Decode /LZWDecode]</c>, so the encoders run from the end of the array. Each encoder gets the
/// <c>DecodeParms</c> its decoder will read; before an encoder named <c>LZWDecode</c> or <c>FlateDecode</c> the pipeline applies the
/// predictor those parameters name (§7.4.4.4), as the decoding pipeline undoes it after the filter.
/// </para>
/// <para>
/// The caller writes the dictionary, so it is held to the specification instead of repaired: a <c>Filter</c> that is not a name or
/// an array of names, an inline-image abbreviation (§8.9.7, Table 92), or a <c>DecodeParms</c> that does not match it throws
/// <see cref="ArgumentException"/>; <c>Crypt</c> (§7.4.10, applied by the security handler) and a name no encoder is registered
/// for throw <see cref="NotSupportedException"/>.
/// </para>
/// </remarks>
internal sealed class StreamEncoder
{
    private readonly StreamEncoderRegistry _encoders;
    private readonly Func<CosObject?, CosObject> _resolve;

    /// <summary>Initializes a new instance of the <see cref="StreamEncoder"/> class.</summary>
    /// <param name="encoders">The engine's encoders.</param>
    /// <param name="resolve">Resolves indirect references of the document.</param>
    public StreamEncoder(StreamEncoderRegistry encoders, Func<CosObject?, CosObject> resolve)
    {
        _encoders = encoders;
        _resolve = resolve;
    }

    /// <summary>Encodes <paramref name="data"/> as <paramref name="dictionary"/>'s filters describe and writes the result to <paramref name="output"/>.</summary>
    /// <param name="data">The decoded data.</param>
    /// <param name="dictionary">The stream dictionary whose <c>Filter</c> and <c>DecodeParms</c> describe the encoding.</param>
    /// <param name="output">Where to write the encoded data.</param>
    /// <param name="level">The compression level for encoders that compress.</param>
    /// <exception cref="ArgumentException">The dictionary's filter entries, or a filter's parameters, are invalid.</exception>
    /// <exception cref="NotSupportedException">A filter is <c>Crypt</c> or has no registered encoder.</exception>
    public void Encode(ReadOnlySpan<byte> data, CosDictionary dictionary, IBufferWriter<byte> output, CompressionLevel level)
    {
        List<(IStreamEncoder Encoder, CosDictionary? Parameters)> chain = ReadChain(dictionary);
        if (chain.Count == 0)
        {
            output.Write(data);
            return;
        }

        PooledBufferWriter? current = null;
        PooledBufferWriter? next = null;
        try
        {
            ReadOnlySpan<byte> input = data;
            for (int index = chain.Count - 1; index >= 0; index--)
            {
                (IStreamEncoder encoder, CosDictionary? parameters) = chain[index];
                if (IsPredicted(encoder.Name, parameters))
                {
                    next ??= new PooledBufferWriter(input.Length + 64, Array.MaxLength);
                    next.Clear();
                    PredictorEncoder.Encode(input, next, parameters, _resolve);
                    (current, next) = (next, current);
                    input = current.WrittenSpan;
                }

                var context = new StreamEncoderContext { Parameters = parameters, StreamDictionary = dictionary, CompressionLevel = level };
                if (index == 0)
                {
                    encoder.Encode(input, output, context);
                    break;
                }

                next ??= new PooledBufferWriter(input.Length + 64, Array.MaxLength);
                next.Clear();
                encoder.Encode(input, next, context);
                (current, next) = (next, current);
                input = current.WrittenSpan;
            }
        }
        finally
        {
            current?.Dispose();
            next?.Dispose();
        }
    }

    private static bool IsPredicted(CosName name, CosDictionary? parameters) =>
        parameters is not null
        && parameters.ContainsKey(FilterNames.Predictor)
        && (name.Equals(FilterNames.FlateDecode) || name.Equals(FilterNames.LzwDecode));

    private List<(IStreamEncoder Encoder, CosDictionary? Parameters)> ReadChain(CosDictionary dictionary)
    {
        var chain = new List<(IStreamEncoder, CosDictionary?)>();
        CosObject filterEntry = _resolve(dictionary.TryGetValue(FilterNames.Filter, out CosObject? filter) ? filter : null);
        CosObject parametersEntry = _resolve(dictionary.TryGetValue(FilterNames.DecodeParms, out CosObject? parameters) ? parameters : null);
        switch (filterEntry)
        {
            case CosNull when parametersEntry is CosNull:
                return chain;
            case CosName name:
                chain.Add((Find(name), Parameters(parametersEntry is CosArray { Count: 1 } single ? _resolve(single[0]) : parametersEntry)));
                return chain;
            case CosArray names:
                if (parametersEntry is not (CosNull or CosArray) && !(parametersEntry is CosDictionary && names.Count == 1))
                {
                    throw new ArgumentException("DecodeParms shall be an array with one entry per filter (ISO 32000-2 §7.3.8.2, Table 5).", nameof(dictionary));
                }

                if (parametersEntry is CosArray { Count: var count } && count != names.Count)
                {
                    throw new ArgumentException("The DecodeParms array shall have one entry per filter (ISO 32000-2 §7.3.8.2, Table 5).", nameof(dictionary));
                }

                for (int index = 0; index < names.Count; index++)
                {
                    if (_resolve(names[index]) is not CosName element)
                    {
                        throw new ArgumentException("Every element of the Filter array shall be a name (ISO 32000-2 §7.3.8.2, Table 5).", nameof(dictionary));
                    }

                    CosObject stageParameters = parametersEntry switch
                    {
                        CosArray array => _resolve(array[index]),
                        CosDictionary => parametersEntry,
                        _ => CosNull.Instance,
                    };
                    chain.Add((Find(element), Parameters(stageParameters)));
                }

                return chain;
            case CosNull:
                throw new ArgumentException("DecodeParms is given without a Filter (ISO 32000-2 §7.3.8.2, Table 5).", nameof(dictionary));
            default:
                throw new ArgumentException("The Filter entry shall be a name or an array of names (ISO 32000-2 §7.3.8.2, Table 5).", nameof(dictionary));
        }
    }

    private static CosDictionary? Parameters(CosObject entry) => entry switch
    {
        CosNull => null,
        CosDictionary dictionary => dictionary,
        _ => throw new ArgumentException("A filter's DecodeParms shall be a dictionary or null (ISO 32000-2 §7.3.8.2, Table 5).", nameof(entry)),
    };

    private IStreamEncoder Find(CosName name)
    {
        if (InlineImageAbbreviations.TryExpandFilter(name, out CosName? fullName))
        {
            throw new ArgumentException($"/{name.Value} is an inline-image abbreviation (ISO 32000-2 §8.9.7, Table 92); a stream names /{fullName.Value}.", nameof(name));
        }

        if (name.Equals(FilterNames.Crypt))
        {
            throw new NotSupportedException("The Crypt filter is applied by the security handler when an encrypted document is written (ISO 32000-2 §7.4.10), not by an encoder.");
        }

        return _encoders.TryGet(name, out IStreamEncoder? encoder)
            ? encoder
            : throw new NotSupportedException($"No encoder is registered for /{name.Value}; register one with PdfOptions.UseStreamEncoder.");
    }
}
