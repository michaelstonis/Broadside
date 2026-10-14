using System.Runtime.CompilerServices;
using Broadside.Caching;
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
/// object they are keyed on, which the document's object cache keeps for the document's lifetime, except decoded content: pages',
/// form XObjects' and patterns' decoded streams live in the document's content cache (issue #103), within
/// <see cref="PdfOptions.ContentCacheBudget"/>, the least recently used dropped and decoded again at their next run.
/// </remarks>
internal sealed class ContentResourceCache(PdfDocument document)
{
    private readonly ConditionalWeakTable<CosDictionary, ExtGStateParameters> _graphicsStates = [];
    private readonly ConditionalWeakTable<CosStream, PdfFormXObject> _forms = [];
    private readonly ConditionalWeakTable<CosStream, Images.PdfImage> _images = [];
    private readonly ConditionalWeakTable<CosStream, GlyphHead> _glyphHeads = [];

    /// <summary>The bytes charged for a content cache entry beyond its data.</summary>
    private const long ContentOverhead = 64;

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

    /// <summary>
    /// Returns the decoded data of a content stream, decoded once while the stream and its dictionary are unchanged and the
    /// document's content cache holds it (issue #103: least recently used dropped beyond <see cref="PdfOptions.ContentCacheBudget"/>).
    /// </summary>
    public ReadOnlyMemory<byte> GetContent(CosStream stream)
    {
        if (document.Caches.Content.TryGet(stream, out IBudgetedEntry? cached) && cached is DecodedContent content)
        {
            return content.Data;
        }

        var decoded = new DecodedContent(stream, stream.Version, stream.Dictionary.Version, document.DecodeStream(stream));
        document.Caches.Content.Add(stream, decoded);
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
    public JoinedContent GetContent(CosArray contents)
    {
        if (document.Caches.Content.TryGet(contents, out IBudgetedEntry? cached) && cached is JoinedContent joined)
        {
            return joined;
        }

        joined = JoinedContent.Create(contents, document);
        document.Caches.Content.Add(contents, joined);
        return joined;
    }

    /// <summary>A content stream's decoded data, current while the stream's data and dictionary are unchanged.</summary>
    private sealed record DecodedContent(CosStream Stream, int DataVersion, int DictionaryVersion, ReadOnlyMemory<byte> Data) : IBudgetedEntry
    {
        /// <inheritdoc/>
        public long Bytes => ContentOverhead + Data.Length;

        /// <inheritdoc/>
        public bool IsCurrent => Stream.Version == DataVersion && Stream.Dictionary.Version == DictionaryVersion;
    }

    private sealed record GlyphHead(int DataVersion, int DictionaryVersion, Type3GlyphMetrics Metrics);

    /// <summary>A page's <c>Contents</c> array read as one stream, and what it was built from.</summary>
    internal sealed class JoinedContent : IBudgetedEntry
    {
        private readonly CosArray _array;
        private readonly PdfDocument _document;
        private readonly int _arrayVersion;
        private readonly CosObject[] _elements;
        private readonly int[] _dataVersions;
        private readonly int[] _dictionaryVersions;

        private JoinedContent(CosArray array, PdfDocument document, CosObject[] elements, int[] dataVersions, int[] dictionaryVersions, byte[] data, ContentPart[] parts, string[] problems)
        {
            _array = array;
            _document = document;
            _arrayVersion = array.Version;
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

        /// <inheritdoc/>
        public long Bytes => ContentOverhead + Data.Length;

        /// <inheritdoc/>
        bool IBudgetedEntry.IsCurrent => IsCurrent(_array, _document);

        public static JoinedContent Create(CosArray array, PdfDocument document)
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
                    decoded[index] = document.DecodeStream(stream);
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

            return new JoinedContent(array, document, elements, dataVersions, dictionaryVersions, data, [.. parts], problems is null ? [] : [.. problems]);
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
