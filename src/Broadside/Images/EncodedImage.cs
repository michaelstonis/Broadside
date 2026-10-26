using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Images;

/// <summary>
/// An image kept encoded until it is drawn: the data its codec decodes (after the filters before the codec), the codec, the
/// codestream's header and the image dictionary's values, so that it can be decoded later, at the resolution it is drawn at, without
/// reading the file again.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4 (the filters before the last one have run), §8.9.5.1 Table 87 (the dictionary values the codec sees) and
/// §7.4.9 (the codestream's size and depth win over the dictionary's). Only an image whose codec says it decodes reduced
/// resolutions (<see cref="ImageHeader.MaxReduction"/>) is kept this way, and only when everything a renderer needs before the
/// samples (size, components, depth, colour space) is known from the header and the dictionary.
/// </para>
/// <para>
/// Immutable; <see cref="Decode"/> may run on any number of threads at once. It reads no COS object the parser has not loaded: its
/// data is the stream's data as read when it was made (which nothing writes in place) or, after other filters, its own copy, and the
/// parameter and image dictionaries are shallow copies taken when it was made.
/// </para>
/// </remarks>
internal sealed class EncodedImage
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly IImageFilter _filter;
    private readonly StreamDecoder _streams;
    private readonly CosDictionary? _parameters;
    private readonly CosDictionary _dictionary;
    private readonly int _dictionaryWidth;
    private readonly int _dictionaryHeight;
    private readonly int _dictionaryBits;

    /// <summary>Initializes a new instance of the <see cref="EncodedImage"/> class.</summary>
    /// <param name="data">The data the codec decodes, which nothing writes from now on.</param>
    /// <param name="filter">The codec.</param>
    /// <param name="streams">The document's filter pipeline: limits, diagnostics, references.</param>
    /// <param name="parameters">The codec's <c>DecodeParms</c>, or <see langword="null"/>.</param>
    /// <param name="dictionary">The image dictionary.</param>
    /// <param name="header">The codestream's header.</param>
    /// <param name="width">The dictionary's width.</param>
    /// <param name="height">The dictionary's height.</param>
    /// <param name="bitsPerComponent">The dictionary's bits per component.</param>
    public EncodedImage(ReadOnlyMemory<byte> data, IImageFilter filter, StreamDecoder streams, CosDictionary? parameters, CosDictionary dictionary, in ImageHeader header, int width, int height, int bitsPerComponent)
    {
        _data = data;
        _filter = filter;
        _streams = streams;
        _parameters = parameters is null ? null : Copy(parameters);
        _dictionary = Copy(dictionary);
        Header = header;
        _dictionaryWidth = width;
        _dictionaryHeight = height;
        _dictionaryBits = bitsPerComponent;
    }

    /// <summary>Gets the codestream's header: the size, components and depth every decode delivers at reduction 0.</summary>
    public ImageHeader Header { get; }

    /// <summary>Gets the data the codec decodes; never written.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>
    /// Applies the shared platform-decoder rule (<see cref="PlatformImageEligibility"/>): the request for this image at reduction 0
    /// when a platform decoder may decode it.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>Whether the image may go to a platform decoder.</returns>
    /// <remarks>ISO 32000-2 §7.4.8 Table 13 and §8.9.5.1 Table 87. Reads the markers in place; allocates nothing.</remarks>
    public bool TryCreatePlatformRequest(out PlatformImageRequest request)
    {
        request = default;
        if (!_filter.Name.Equals(FilterNames.DctDecode))
        {
            return false;
        }

        int colorTransform = -1;
        if (_parameters is not null && _parameters.TryGetValue(FilterNames.ColorTransform, out CosObject? entry) && _streams.Resolve(entry) is CosInteger { Value: var value })
        {
            colorTransform = value is 0 or 1 ? (int)value : -1;
        }

        return PlatformImageEligibility.TryCreateRequest(_data.Span, colorTransform, _dictionaryWidth, _dictionaryHeight, out request, out _);
    }

    /// <summary>Records a diagnostic of this image on its document.</summary>
    /// <param name="code">The code.</param>
    /// <param name="severity">The severity.</param>
    /// <param name="message">The message.</param>
    /// <param name="reference">The image stream's reference, which makes the diagnostic the image's own; <see langword="null"/> when direct.</param>
    public void Report(string code, DiagnosticSeverity severity, string message, CosReference? reference) =>
        _streams.Report(new Diagnostic(code, severity, message, null, reference));

    /// <summary>Gets the bytes the encoded data keeps.</summary>
    public long RetainedBytes => _data.Length;

    /// <summary>Decodes the image at a reduction of 2^<paramref name="reduction"/>, or less when the codec delivers less.</summary>
    /// <param name="reduction">The reduction asked for, 0 to <see cref="ImageHeader.MaxReduction"/>.</param>
    /// <param name="cancellationToken">Given to the codec.</param>
    /// <returns>
    /// The samples, which the caller disposes; <see langword="null"/> when nothing could be decoded or the codec delivered something
    /// other than its header promised (each recorded as a diagnostic of the document).
    /// </returns>
    /// <remarks>ISO 32000-2 §7.4 and §8.9.3.</remarks>
    public DecodedImage? Decode(int reduction, CancellationToken cancellationToken)
    {
        var filterContext = new FilterContext(_streams, 0) { Parameters = _parameters, StreamDictionary = _dictionary, CancellationToken = cancellationToken };
        var context = new ImageFilterContext(filterContext)
        {
            Width = _dictionaryWidth,
            Height = _dictionaryHeight,
            BitsPerComponent = _dictionaryBits,
            ColorComponents = Header.Components,
            MaxPixels = _streams.MaxImagePixels,
            MaxDecodedLength = _streams.MaxDecodedLength,
            Reduction = Math.Clamp(reduction, 0, Header.MaxReduction),
        };

        DecodedImage? decoded = _streams.RunImageFilter(_filter.Name, () => _filter.DecodeImage(_data, context), cancellationToken);
        if (decoded is null)
        {
            return null;
        }

        int delivered = decoded.Reduction;
        if (delivered > context.Reduction
            || decoded.Width != ImageGeometry.Reduce(Header.Width, delivered)
            || decoded.Height != ImageGeometry.Reduce(Header.Height, delivered)
            || decoded.Components != Header.Components
            || decoded.BitsPerComponent != Header.BitsPerComponent
            || decoded.SamplesInverted
            || decoded.Alpha is not null)
        {
            filterContext.Report(DiagnosticCodes.FilterFailed, DiagnosticSeverity.Error, FormattableString.Invariant(
                $"The filter /{_filter.Name.Value} decoded {decoded.Width} x {decoded.Height} x {decoded.Components} samples of {decoded.BitsPerComponent} bits at reduction {delivered} where its header promised {Header.Width} x {Header.Height} x {Header.Components} of {Header.BitsPerComponent} bits; the image is not decoded."));
            decoded.Dispose();
            return null;
        }

        return decoded;
    }

    /// <summary>Copies a dictionary's entries into a new one without counting it as a change (it is never written).</summary>
    private static CosDictionary Copy(CosDictionary source)
    {
        var entries = new OrderedDictionary<CosName, CosObject>(source.Count);
        foreach (KeyValuePair<CosName, CosObject> entry in source)
        {
            entries.Add(entry.Key, entry.Value);
        }

        return CosDictionary.FromOwnedEntries(entries);
    }
}
