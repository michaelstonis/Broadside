using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>filter-sliced</c> target (issue #566): decodes the input as a stream's data through the filter pipeline twice, once in one
/// step and once in slices that pause after every unit of work (<see cref="StreamDecoder.ChainJob"/>: Flate a chunk at a time,
/// predictor rows a few at a time), and checks that both give the same bytes, the same outcome and the same diagnostics. The first
/// four bytes choose the chain (Flate, LZW, ASCIIHex then Flate, Flate with no predictor) and the predictor's parameters.
/// </summary>
/// <remarks>ISO 32000-2 §7.3.8.2, §7.4.4.1 to §7.4.4.4.</remarks>
internal static class SlicedChainTarget
{
    private const long MaxDecodedLength = 1 << 20;

    public static void Target(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return;
        }

        ReadOnlySpan<int> predictors = [1, 2, 10, 15, 3];
        ReadOnlySpan<int> depths = [1, 2, 4, 8, 16];
        var parameters = new CosDictionary
        {
            [FilterNames.Predictor] = new CosInteger(predictors[data[1] % predictors.Length]),
            [FilterNames.Colors] = new CosInteger(1 + ((data[1] / predictors.Length) % 4)),
            [FilterNames.BitsPerComponent] = new CosInteger(depths[data[2] % depths.Length]),
            [FilterNames.Columns] = new CosInteger(1 + (data[3] % 200)),
        };
        var dictionary = new CosDictionary();
        switch (data[0] % 4)
        {
            case 0:
                dictionary[FilterNames.Filter] = FilterNames.FlateDecode;
                dictionary[FilterNames.DecodeParms] = parameters;
                break;
            case 1:
                dictionary[FilterNames.Filter] = FilterNames.LzwDecode;
                dictionary[FilterNames.DecodeParms] = parameters;
                break;
            case 2:
                dictionary[FilterNames.Filter] = new CosArray([FilterNames.AsciiHexDecode, FilterNames.FlateDecode]);
                dictionary[FilterNames.DecodeParms] = new CosArray([CosNull.Instance, parameters]);
                break;
            default:
                dictionary[FilterNames.Filter] = FilterNames.FlateDecode;
                break;
        }

        var stream = new CosStream(dictionary, data[4..].ToArray());
        (byte[] whole, bool wholeComplete, Diagnostic[] wholeDiagnostics) = Decode(stream, sliced: false);
        (byte[] sliced, bool slicedComplete, Diagnostic[] slicedDiagnostics) = Decode(stream, sliced: true);
        if (!whole.AsSpan().SequenceEqual(sliced) || wholeComplete != slicedComplete)
        {
            throw new InvalidOperationException($"The sliced decode gave {sliced.Length} bytes (complete: {slicedComplete}) where the one-step decode gave {whole.Length} (complete: {wholeComplete}).");
        }

        if (!wholeDiagnostics.Select(Describe).SequenceEqual(slicedDiagnostics.Select(Describe)))
        {
            throw new InvalidOperationException(
                $"The sliced decode reported [{string.Join("; ", slicedDiagnostics.Select(Describe))}] where the one-step decode reported [{string.Join("; ", wholeDiagnostics.Select(Describe))}].");
        }
    }

    private static (byte[] Bytes, bool Complete, Diagnostic[] Diagnostics) Decode(CosStream stream, bool sliced)
    {
        var sink = new DiagnosticSink(strict: false);
        var decoder = new StreamDecoder(FilterRegistry.Create([]), MaxDecodedLength, sink, static value => value ?? CosNull.Instance);
        var output = new PooledBufferWriter(256, MaxDecodedLength);
        try
        {
            bool complete;
            if (sliced)
            {
                using StreamDecoder.ChainJob job = decoder.BeginDecode(stream, output, stopBeforeImageFilter: false, CancellationToken.None);
                while (!job.Continue(deadline: 0))
                {
                }

                complete = job.Outcome.Complete;
            }
            else
            {
                decoder.Decode(stream, output, 0, stopBeforeImageFilter: false, out StreamDecoder.ChainOutcome outcome);
                complete = outcome.Complete;
            }

            return (output.WrittenSpan.ToArray(), complete, sink.Snapshot());
        }
        finally
        {
            output.Dispose();
        }
    }

    private static string Describe(Diagnostic diagnostic) => $"{diagnostic.Code} {diagnostic.Severity} {diagnostic.Message}";
}
