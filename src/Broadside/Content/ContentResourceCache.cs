using System.Runtime.CompilerServices;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// What the content interpreter derives from a document's resources once and reuses on every run: the decoded content of pages,
/// form XObjects and other nested streams, form and image views, the parameters of graphics state parameter dictionaries, and the
/// metrics Type 3 glyph descriptions declare.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.8.2, §7.8.3, §8.4.5, §8.10, §9.6.4. Entries are keyed by the object's identity and rebuilt when the object's
/// <c>Version</c> moved (a public mutation), so an edit is seen by the next run. Thread-safe for concurrent runs: two threads may
/// compute the same entry and the last published wins; the values are immutable, so either is right. Entries live as long as the
/// object they are keyed on, which the document's object cache keeps for the document's lifetime.
/// </remarks>
internal sealed class ContentResourceCache(PdfDocument document)
{
    private readonly ConditionalWeakTable<CosStream, DecodedContent> _streams = [];
    private readonly ConditionalWeakTable<CosArray, JoinedContent> _joined = [];
    private readonly ConditionalWeakTable<CosDictionary, ExtGStateParameters> _graphicsStates = [];
    private readonly ConditionalWeakTable<CosStream, PdfFormXObject> _forms = [];
    private readonly ConditionalWeakTable<CosStream, Images.PdfImage> _images = [];
    private readonly ConditionalWeakTable<CosStream, GlyphHead> _glyphHeads = [];

    /// <summary>The scratch operands of <see cref="GetType3GlyphMetrics"/> on this thread.</summary>
    [ThreadStatic]
    private static OperandArena? _headArena;

    /// <summary>
    /// Returns the image view of an image XObject's stream, one per stream, so that painting an image allocates nothing (the view is
    /// stateless and live: it reads the stream's dictionary on every access).
    /// </summary>
    public Images.PdfImage? GetImage(CosStream stream, CosReference? reference)
    {
        if (_images.TryGetValue(stream, out Images.PdfImage? image) && Equals(image.Reference, reference))
        {
            return image;
        }

        image = Images.PdfImage.Create(document, (CosObject?)reference ?? stream);
        if (image is not null)
        {
            _images.AddOrUpdate(stream, image);
        }

        return image;
    }

    /// <summary>Returns the form view of a form XObject's stream, one per stream, so that painting a form allocates nothing.</summary>
    public PdfFormXObject GetForm(CosStream stream, CosReference? reference)
    {
        if (_forms.TryGetValue(stream, out PdfFormXObject? form) && Equals(form.Reference, reference))
        {
            return form;
        }

        form = new PdfFormXObject(document, stream, reference);
        _forms.AddOrUpdate(stream, form);
        return form;
    }

    /// <summary>Returns the decoded data of a content stream, decoded once while the stream and its dictionary are unchanged.</summary>
    public ReadOnlyMemory<byte> GetContent(CosStream stream, CancellationToken cancellationToken = default)
    {
        if (_streams.TryGetValue(stream, out DecodedContent? cached) && cached.DataVersion == stream.Version && cached.DictionaryVersion == stream.Dictionary.Version)
        {
            return cached.Data;
        }

        // A cancelled decode throws before anything is stored, so the next run decodes afresh.
        var decoded = new DecodedContent(stream.Version, stream.Dictionary.Version, document.Streams.Decode(stream, 0, cancellationToken));
        _streams.AddOrUpdate(stream, decoded);
        return decoded.Data;
    }

    /// <summary>
    /// Returns what a Type 3 glyph description declares with its first operator (§9.6.4, Table 111), read once while the stream and
    /// its dictionary are unchanged, from the same decoded data the interpreter runs.
    /// </summary>
    public Type3GlyphMetrics GetType3GlyphMetrics(CosStream stream)
    {
        if (_glyphHeads.TryGetValue(stream, out GlyphHead? cached) && cached.DataVersion == stream.Version && cached.DictionaryVersion == stream.Dictionary.Version)
        {
            return cached.Metrics;
        }

        int dataVersion = stream.Version;
        int dictionaryVersion = stream.Dictionary.Version;
        Type3GlyphMetrics metrics = Type3GlyphMetrics.Read(GetContent(stream).Span, _headArena ??= new OperandArena());
        _glyphHeads.AddOrUpdate(stream, new GlyphHead(dataVersion, dictionaryVersion, metrics));
        return metrics;
    }

    /// <summary>
    /// Returns the parameters of a graphics state parameter dictionary, read once while the dictionary is unchanged and no object is
    /// added or removed (a removal changes what its references resolve to, §7.3.10).
    /// </summary>
    public ExtGStateParameters GetGraphicsState(CosDictionary dictionary, CosReference? reference)
    {
        if (_graphicsStates.TryGetValue(dictionary, out ExtGStateParameters? cached) && cached.Version == dictionary.Version && cached.ObjectsVersion == document.ObjectsVersion)
        {
            return cached;
        }

        ExtGStateParameters parameters = ExtGStateParameters.Read(document, dictionary, reference);
        _graphicsStates.AddOrUpdate(dictionary, parameters);
        return parameters;
    }

    /// <summary>
    /// Returns the content of a page's <c>Contents</c> array: its streams decoded and joined with a line feed after each (Table 31),
    /// built once while the array, the objects its elements resolve to and their data and dictionaries are unchanged.
    /// </summary>
    public JoinedContent GetContent(CosArray contents, CancellationToken cancellationToken = default)
    {
        if (_joined.TryGetValue(contents, out JoinedContent? cached) && cached.IsCurrent(contents, document))
        {
            return cached;
        }

        JoinedContent joined = JoinedContent.Create(contents, document, cancellationToken);
        _joined.AddOrUpdate(contents, joined);
        return joined;
    }

    private sealed record DecodedContent(int DataVersion, int DictionaryVersion, ReadOnlyMemory<byte> Data);

    private sealed record GlyphHead(int DataVersion, int DictionaryVersion, Type3GlyphMetrics Metrics);

    /// <summary>A page's <c>Contents</c> array read as one stream, and what it was built from.</summary>
    internal sealed class JoinedContent
    {
        private readonly int _arrayVersion;
        private readonly CosObject[] _elements;
        private readonly int[] _dataVersions;
        private readonly int[] _dictionaryVersions;

        private JoinedContent(int arrayVersion, CosObject[] elements, int[] dataVersions, int[] dictionaryVersions, byte[] data, ContentPart[] parts, string[] problems)
        {
            _arrayVersion = arrayVersion;
            _elements = elements;
            _dataVersions = dataVersions;
            _dictionaryVersions = dictionaryVersions;
            Data = data;
            Parts = parts;
            Problems = problems;
        }

        /// <summary>Gets the decoded streams, each followed by a line feed.</summary>
        public ReadOnlyMemory<byte> Data { get; }

        /// <summary>Gets where each stream starts in <see cref="Data"/>, in array order; empty when no element is a stream.</summary>
        public ContentPart[] Parts { get; }

        /// <summary>Gets the messages of the elements that are not streams, which every run reports again (<see cref="ContentIssue.StreamInvalid"/>).</summary>
        public string[] Problems { get; }

        public static JoinedContent Create(CosArray array, PdfDocument document, CancellationToken cancellationToken)
        {
            int count = array.Count;
            var elements = new CosObject[count];
            int[] dataVersions = new int[count];
            int[] dictionaryVersions = new int[count];
            var decoded = new ReadOnlyMemory<byte>[count];
            List<string>? problems = null;
            int total = 0;
            for (int index = 0; index < count; index++)
            {
                CosObject element = document.Resolve(array[index]);
                elements[index] = element;
                if (element is CosStream stream)
                {
                    dataVersions[index] = stream.Version;
                    dictionaryVersions[index] = stream.Dictionary.Version;
                    decoded[index] = document.Streams.Decode(stream, 0, cancellationToken);
                    total += decoded[index].Length + 1;
                }
                else
                {
                    (problems ??= []).Add($"Element {index} of the page's Contents array is not a stream; skipped.");
                }
            }

            byte[] data = new byte[total];
            var parts = new List<ContentPart>(count);
            int position = 0;
            for (int index = 0; index < count; index++)
            {
                if (elements[index] is CosStream)
                {
                    parts.Add(new ContentPart(position, array[index] as CosReference));
                    decoded[index].Span.CopyTo(data.AsSpan(position));
                    position += decoded[index].Length;
                    data[position++] = (byte)'\n';
                }
            }

            return new JoinedContent(array.Version, elements, dataVersions, dictionaryVersions, data, [.. parts], problems is null ? [] : [.. problems]);
        }

        /// <summary>Returns whether nothing this content was built from has changed since: the array, what its elements resolve to, their data and dictionaries.</summary>
        public bool IsCurrent(CosArray array, PdfDocument document)
        {
            if (array.Version != _arrayVersion || array.Count != _elements.Length)
            {
                return false;
            }

            for (int index = 0; index < _elements.Length; index++)
            {
                CosObject element = document.Resolve(array[index]);
                if (!ReferenceEquals(element, _elements[index])
                    || (element is CosStream stream && (stream.Version != _dataVersions[index] || stream.Dictionary.Version != _dictionaryVersions[index])))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
