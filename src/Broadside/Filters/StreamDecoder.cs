using System.Buffers;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Filters;

/// <summary>
/// The filter pipeline of one document: reads a stream's <c>Filter</c> and <c>DecodeParms</c>, runs the filters in order with the
/// predictor stage after LZW and Flate, routes <c>Crypt</c> to the security handler, enforces the decoded-length limit, and turns
/// every failure into a diagnostic (or, in strict mode, a <see cref="DiagnosticException"/>).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.3.8.2 (Table 5), §7.4.1, §7.4.4.4, §7.4.10. Deviations from Table 5 are repaired the way most readers do: a
/// <c>DecodeParms</c> array for a single filter uses its first element; a single dictionary for several filters is ignored; an
/// element that is not a dictionary means defaults; an abbreviation of §8.9.7 Table 92 outside an inline image is expanded; a chain
/// stops at the first filter it cannot run and the data decoded so far is the result. A stream with no data decodes to nothing,
/// with no diagnostic, whatever its filters.
/// </para>
/// <para>Thread-safe: stateless apart from <see cref="CryptFilter"/>, which is set once during open.</para>
/// </remarks>
internal sealed class StreamDecoder
{
    /// <summary>How many streams may decode one inside another (a JBIG2 stream decoding its globals) before the inner one is empty.</summary>
    public const int MaxDepth = 8;

    private readonly FilterRegistry _filters;
    private readonly DiagnosticSink _diagnostics;
    private readonly Func<CosObject?, CosObject> _resolve;
    private volatile ICryptFilterHandler _cryptFilter = IdentityCryptFilterHandler.Instance;

    /// <summary>Initializes a new instance of the <see cref="StreamDecoder"/> class.</summary>
    /// <param name="filters">The engine's filters.</param>
    /// <param name="maxDecodedLength">The most bytes one stream may decode to.</param>
    /// <param name="diagnostics">Where to report deviations; its strictness is the reading mode.</param>
    /// <param name="resolve">Resolves indirect references of the document.</param>
    public StreamDecoder(FilterRegistry filters, long maxDecodedLength, DiagnosticSink diagnostics, Func<CosObject?, CosObject> resolve)
    {
        _filters = filters;
        _diagnostics = diagnostics;
        _resolve = resolve;
        MaxDecodedLength = maxDecodedLength;
    }

    /// <summary>Gets the reading mode the diagnostics sink applies.</summary>
    public PdfReadingMode ReadingMode => _diagnostics.IsStrict ? PdfReadingMode.Strict : PdfReadingMode.Lenient;

    /// <summary>Gets the most bytes one stream may decode to.</summary>
    public long MaxDecodedLength { get; }

    /// <summary>Gets the most pixels one image may have (issue #60).</summary>
    public long MaxImagePixels { get; init; } = PdfOptions.DefaultMaxImagePixels;

    /// <summary>
    /// Gets or sets the security handler's crypt filters (issue #42). Set once during open, after the <c>Encrypt</c> dictionary is read.
    /// </summary>
    public ICryptFilterHandler CryptFilter
    {
        get => _cryptFilter;
        set => _cryptFilter = value;
    }

    /// <summary>Creates the decoder a stand-alone <see cref="FilterContext"/> uses: default filters, no document, its own diagnostics.</summary>
    /// <param name="readingMode">The reading mode.</param>
    /// <param name="maxDecodedLength">The decoded-length limit.</param>
    /// <returns>The decoder.</returns>
    public static StreamDecoder Standalone(PdfReadingMode readingMode, long maxDecodedLength) =>
        new(
            FilterRegistry.Create([]),
            maxDecodedLength,
            new DiagnosticSink(readingMode == PdfReadingMode.Strict),
            static value => value is null or CosReference ? CosNull.Instance : value);

    /// <summary>Resolves an indirect reference of the document.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The direct object.</returns>
    public CosObject Resolve(CosObject? value) => _resolve(value);

    /// <summary>Records a diagnostic; throws it in strict mode.</summary>
    /// <param name="diagnostic">The diagnostic.</param>
    public void Report(Diagnostic diagnostic) =>
        _diagnostics.Report(diagnostic.Code, diagnostic.Severity, diagnostic.Message, diagnostic.Offset, diagnostic.ObjectReference);

    /// <summary>Decodes a stream into a new array.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="depth">How many decodes are in progress beneath this one.</param>
    /// <returns>The decoded data; for a stream without filters, its encoded data itself.</returns>
    public ReadOnlyMemory<byte> Decode(CosStream stream, int depth = 0)
    {
        if (!HasFilters(stream))
        {
            CheckExternalFile(stream);
            return stream.EncodedData;
        }

        using var output = new PooledBufferWriter(InitialCapacity(stream), MaxDecodedLength);
        Decode(stream, output, depth);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a stream into <paramref name="output"/>.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="output">Where to write the decoded data.</param>
    /// <param name="depth">How many decodes are in progress beneath this one.</param>
    public void Decode(CosStream stream, IBufferWriter<byte> output, int depth = 0) => Decode(stream, output, depth, stopBeforeImageFilter: false, out _);

    /// <summary>
    /// Decodes a stream into <paramref name="output"/>, optionally stopping before a last filter that has an image facet
    /// (<see cref="IImageFilter"/>), which the image layer then runs itself.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="output">Where to write the decoded data.</param>
    /// <param name="depth">How many decodes are in progress beneath this one.</param>
    /// <param name="stopBeforeImageFilter">Whether to leave a last image filter to the caller.</param>
    /// <param name="outcome">Whether every filter ran, and the image filter left to the caller, if any.</param>
    public void Decode(CosStream stream, IBufferWriter<byte> output, int depth, bool stopBeforeImageFilter, out ChainOutcome outcome)
    {
        outcome = new ChainOutcome(Complete: true, ImageFilter: null, ImageContext: null);
        CheckExternalFile(stream);
        if (depth > MaxDepth)
        {
            Report(DiagnosticCodes.StreamDecodingTooDeep, DiagnosticSeverity.Error, string.Create(
                CultureInfo.InvariantCulture,
                $"Decoding the stream needs more than {MaxDepth} nested stream decodes; it decodes to nothing."));
            return;
        }

        if (CryptFilter.IsLocked(stream.Dictionary))
        {
            // Encrypted with a crypt filter the credentials do not unlock: decoding the ciphertext would only report damage.
            outcome = outcome with { Complete = false };
            output.Write(stream.EncodedData.Span);
            return;
        }

        List<FilterStage> chain = ReadChain(stream.Dictionary);
        if (stopBeforeImageFilter && chain.Count > 0 && _filters.TryGet(chain[^1].Name, out IStreamFilter? last) && last is IImageFilter imageFilter)
        {
            FilterStage stage = chain[^1];
            chain.RemoveAt(chain.Count - 1);
            outcome = outcome with
            {
                ImageFilter = imageFilter,
                ImageContext = new FilterContext(this, depth) { Parameters = stage.Parameters, StreamDictionary = stream.Dictionary },
            };
        }

        ReadOnlyMemory<byte> data = stream.EncodedData;
        PooledBufferWriter? current = null;
        PooledBufferWriter? next = null;
        try
        {
            for (int index = 0; index < chain.Count && !data.IsEmpty; index++)
            {
                next ??= new PooledBufferWriter(InitialCapacity(stream), MaxDecodedLength);
                next.Clear();
                FilterStage stage = chain[index];
                var context = new FilterContext(this, depth) { Parameters = stage.Parameters, StreamDictionary = stream.Dictionary };
                bool stop = !RunStage(stage, index, data, next, context);
                if (!stop && FilterNames.TakesPredictor(stage.Name))
                {
                    current ??= new PooledBufferWriter(next.WrittenSpan.Length, MaxDecodedLength);
                    current.Clear();
                    stop = !RunPredictor(next.WrittenMemory, current, context);
                }
                else
                {
                    (current, next) = (next, current);
                }

                data = current!.WrittenMemory;
                if (stop)
                {
                    outcome = outcome with { Complete = false, ImageFilter = null, ImageContext = null };
                    break;
                }
            }

            output.Write(data.Span);
        }
        finally
        {
            current?.Dispose();
            next?.Dispose();
        }
    }

    private static bool HasFilters(CosStream stream) => stream.Dictionary.ContainsKey(FilterNames.Filter);

    private static long InitialCapacity(CosStream stream) =>
        stream.Dictionary.TryGetValue(FilterNames.DL, out CosObject? hint) && hint is CosInteger { Value: > 0 } length
            ? length.Value
            : (long)stream.EncodedLength * 4;

    /// <summary>Runs one filter; returns <see langword="false"/> when the chain cannot continue past it.</summary>
    private bool RunStage(FilterStage stage, int index, ReadOnlyMemory<byte> data, PooledBufferWriter output, FilterContext context)
    {
        CosName name = stage.Name;
        if (name.Equals(FilterNames.Crypt))
        {
            return RunCryptFilter(index, data, output, context);
        }

        if (!_filters.TryGet(name, out IStreamFilter? filter))
        {
            // A standard image codec that is not registered yet is legal content this engine cannot decode (issue #47): Information,
            // so strict mode still opens the file. Anything else is an error in the file.
            bool imageCodec = FilterNames.IsImageCodec(name);
            string kind = FilterNames.IsStandard(name) ? "is a standard filter this version does not implement" : "is not a filter this engine knows";
            Report(
                DiagnosticCodes.FilterUnsupported,
                imageCodec ? DiagnosticSeverity.Information : DiagnosticSeverity.Error,
                $"The filter /{name.Value} {kind}; the stream is left encoded from that filter on.");
            output.Write(data.Span);
            return false;
        }

        return Guard(output, name, () => filter.Decode(data, output, context));
    }

    /// <summary>Runs an image filter's facet, converting its failures into a diagnostic and <see langword="null"/>.</summary>
    /// <typeparam name="T">The result.</typeparam>
    /// <param name="name">The filter's name.</param>
    /// <param name="action">The call.</param>
    /// <returns>The result; <see langword="null"/> when the filter threw.</returns>
    public T? RunImageFilter<T>(CosName name, Func<T?> action)
        where T : class
    {
        try
        {
            return action();
        }
        catch (DiagnosticException) when (_diagnostics.IsStrict)
        {
            throw;
        }
#pragma warning disable CA1031 // A filter is an extension point; whatever it throws becomes a diagnostic, never a crash (ADR 0005).
        catch (Exception exception) when (exception is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            Report(
                DiagnosticCodes.FilterFailed,
                DiagnosticSeverity.Error,
                $"The filter /{name.Value} failed to decode the image ({exception.GetType().Name}: {exception.Message}); the image is not decoded.");
            return null;
        }
    }

    private bool RunPredictor(ReadOnlyMemory<byte> data, PooledBufferWriter output, FilterContext context) =>
        Guard(output, filterName: null, () => Predictor.Decode(data.Span, output, context));

    private bool RunCryptFilter(int index, ReadOnlyMemory<byte> data, PooledBufferWriter output, FilterContext context)
    {
        if (index != 0)
        {
            Report(DiagnosticCodes.CryptFilterNotFirst, DiagnosticSeverity.Warning, "The Crypt filter shall be the first filter in the Filter array; it is applied where it is.");
        }

        CosName cryptFilterName = context.Resolve(context.Parameters?.TryGetValue(FilterNames.Name, out CosObject? named) == true ? named : null) as CosName
            ?? FilterNames.Identity;
        bool decrypted = false;
        bool completed = Guard(output, FilterNames.Crypt, () => decrypted = CryptFilter.TryDecrypt(cryptFilterName, data.Span, output, context));
        if (completed && !decrypted)
        {
            Report(
                DiagnosticCodes.CryptFilterUnsupported,
                DiagnosticSeverity.Error,
                $"The crypt filter /{cryptFilterName.Value} is not known to the document's security handler; the stream is left encrypted.");
            output.Clear();
            output.Write(data.Span);
            return false;
        }

        return completed;
    }

    /// <summary>Runs a stage, converting its failures into diagnostics; returns <see langword="false"/> when the chain should stop.</summary>
    /// <param name="output">The stage's output.</param>
    /// <param name="filterName">The filter the stage runs, or <see langword="null"/> for the predictor stage.</param>
    /// <param name="action">The stage.</param>
    private bool Guard(PooledBufferWriter output, CosName? filterName, Action action)
    {
        try
        {
            action();
            return !output.Exceeded;
        }
        catch (DiagnosticException) when (_diagnostics.IsStrict)
        {
            throw;
        }
        catch (DecodedLengthExceededException)
        {
            ReportLengthExceeded();
            return false;
        }
#pragma warning disable CA1031 // A filter is an extension point; whatever it throws becomes a diagnostic, never a crash (ADR 0005).
        catch (Exception exception) when (exception is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            if (output.Exceeded)
            {
                ReportLengthExceeded();
                return false;
            }

            Report(
                DiagnosticCodes.FilterFailed,
                DiagnosticSeverity.Error,
                $"{(filterName is null ? "The predictor" : $"The filter /{filterName.Value}")} failed ({exception.GetType().Name}: {exception.Message}); the bytes it decoded before failing are kept.");
            return true;
        }
    }

    private void ReportLengthExceeded() =>
        Report(DiagnosticCodes.StreamDecodedLengthExceeded, DiagnosticSeverity.Error, string.Create(
            CultureInfo.InvariantCulture,
            $"The stream decodes to more than the limit of {MaxDecodedLength} bytes; it is truncated there."));

    private void CheckExternalFile(CosStream stream)
    {
        if (stream.Dictionary.ContainsKey(FilterNames.F))
        {
            Report(
                DiagnosticCodes.StreamExternalFileUnsupported,
                DiagnosticSeverity.Information,
                "The stream's data is in an external file (F), which is not read; the bytes in the stream itself are decoded instead.");
        }
    }

    /// <summary>Reads <c>Filter</c> and <c>DecodeParms</c> into the chain of filters to run, repairing deviations from Table 5.</summary>
    private List<FilterStage> ReadChain(CosDictionary dictionary)
    {
        var chain = new List<FilterStage>();
        CosObject filterEntry = Resolve(dictionary.TryGetValue(FilterNames.Filter, out CosObject? filter) ? filter : null);
        CosObject parametersEntry = Resolve(dictionary.TryGetValue(FilterNames.DecodeParms, out CosObject? parameters) ? parameters : null);
        switch (filterEntry)
        {
            case CosNull:
                return chain;
            case CosName name:
                chain.Add(new FilterStage(Expand(name), SingleParameters(parametersEntry)));
                return chain;
            case CosArray names:
                ReadArrayChain(names, parametersEntry, chain);
                return chain;
            default:
                Report(DiagnosticCodes.FilterInvalid, DiagnosticSeverity.Error, "The Filter entry shall be a name or an array of names; the stream is left encoded.");
                return chain;
        }
    }

    private void ReadArrayChain(CosArray names, CosObject parametersEntry, List<FilterStage> chain)
    {
        CosArray? parameterArray = parametersEntry as CosArray;
        switch (parametersEntry)
        {
            case CosNull or CosArray:
                break;
            case CosDictionary when names.Count == 1:
                break;
            case CosDictionary:
                Report(
                    DiagnosticCodes.DecodeParmsInvalid,
                    DiagnosticSeverity.Warning,
                    "DecodeParms shall be an array with one entry per filter when Filter is an array of several; the dictionary is ignored.");
                break;
            default:
                Report(DiagnosticCodes.DecodeParmsInvalid, DiagnosticSeverity.Warning, "DecodeParms shall be a dictionary or an array; it is ignored.");
                break;
        }

        if (parameterArray is not null && parameterArray.Count != names.Count)
        {
            Report(
                DiagnosticCodes.DecodeParmsInvalid,
                DiagnosticSeverity.Warning,
                "The DecodeParms array shall have one entry per filter; missing entries take the defaults.");
        }

        for (int index = 0; index < names.Count; index++)
        {
            if (Resolve(names[index]) is not CosName name)
            {
                Report(DiagnosticCodes.FilterInvalid, DiagnosticSeverity.Error, "Every element of the Filter array shall be a name; the chain stops before the first that is not.");
                return;
            }

            CosDictionary? stageParameters = parameterArray is not null
                ? index < parameterArray.Count ? ParametersElement(Resolve(parameterArray[index])) : null
                : names.Count == 1 ? parametersEntry as CosDictionary : null;
            chain.Add(new FilterStage(Expand(name), stageParameters));
        }
    }

    private CosDictionary? SingleParameters(CosObject parametersEntry)
    {
        switch (parametersEntry)
        {
            case CosNull:
                return null;
            case CosDictionary dictionary:
                return dictionary;
            case CosArray array:
                Report(
                    DiagnosticCodes.DecodeParmsInvalid,
                    DiagnosticSeverity.Warning,
                    "DecodeParms shall be a dictionary when Filter is a single name; the first element of the array is used.");
                return array.Count > 0 ? Resolve(array[0]) as CosDictionary : null;
            default:
                Report(DiagnosticCodes.DecodeParmsInvalid, DiagnosticSeverity.Warning, "DecodeParms shall be a dictionary; it is ignored.");
                return null;
        }
    }

    private CosDictionary? ParametersElement(CosObject element)
    {
        switch (element)
        {
            case CosNull:
                return null;
            case CosDictionary dictionary:
                return dictionary;
            default:
                Report(DiagnosticCodes.DecodeParmsInvalid, DiagnosticSeverity.Warning, "Each element of the DecodeParms array shall be a dictionary or null; this one is ignored.");
                return null;
        }
    }

    private CosName Expand(CosName name)
    {
        if (!InlineImageAbbreviations.TryExpandFilter(name, out CosName? fullName))
        {
            return name;
        }

        Report(
            DiagnosticCodes.FilterAbbreviationNotAllowed,
            DiagnosticSeverity.Warning,
            $"The abbreviation /{name.Value} is valid only in inline images; it is read as /{fullName.Value}.");
        return fullName;
    }

    private void Report(string code, DiagnosticSeverity severity, string message) => _diagnostics.Report(code, severity, message);

    /// <summary>One filter of a chain and its parameters.</summary>
    private readonly record struct FilterStage(CosName Name, CosDictionary? Parameters);

    /// <summary>What <see cref="Decode(CosStream, IBufferWriter{byte}, int, bool, out ChainOutcome)"/> did.</summary>
    /// <param name="Complete">Whether every filter ran; <see langword="false"/> when the chain stopped at a filter it could not run.</param>
    /// <param name="ImageFilter">The last filter, left to the caller because it has an image facet; <see langword="null"/> otherwise.</param>
    /// <param name="ImageContext">The context for that filter: its parameters and the stream dictionary.</param>
    internal readonly record struct ChainOutcome(bool Complete, IImageFilter? ImageFilter, FilterContext? ImageContext);
}
