using System.Runtime.CompilerServices;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// What the content interpreter derives from a document's resources once and reuses on every run: the decoded content of form
/// XObjects (and other nested streams), form and image views, and the parameters of graphics state parameter dictionaries.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.8.3, §8.4.5, §8.10. Entries are keyed by the object's identity and rebuilt when the object's <c>Version</c> moved
/// (a public mutation). Thread-safe for concurrent runs: two threads may compute the same entry and the first published wins; the
/// values are immutable.
/// </remarks>
internal sealed class ContentResourceCache(PdfDocument document)
{
    private readonly ConditionalWeakTable<CosStream, DecodedContent> _streams = [];
    private readonly ConditionalWeakTable<CosDictionary, ExtGStateParameters> _graphicsStates = [];
    private readonly ConditionalWeakTable<CosStream, PdfFormXObject> _forms = [];
    private readonly ConditionalWeakTable<CosStream, Images.PdfImage> _images = [];

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

    /// <summary>Returns the decoded data of a nested content stream, decoded once while the stream is unchanged.</summary>
    public ReadOnlyMemory<byte> GetContent(CosStream stream)
    {
        if (_streams.TryGetValue(stream, out DecodedContent? cached) && cached.DataVersion == stream.Version && cached.DictionaryVersion == stream.Dictionary.Version)
        {
            return cached.Data;
        }

        var decoded = new DecodedContent(stream.Version, stream.Dictionary.Version, document.DecodeStream(stream));
        _streams.AddOrUpdate(stream, decoded);
        return decoded.Data;
    }

    /// <summary>Returns the parameters of a graphics state parameter dictionary, read once while the dictionary is unchanged.</summary>
    public ExtGStateParameters GetGraphicsState(CosDictionary dictionary, CosReference? reference)
    {
        if (_graphicsStates.TryGetValue(dictionary, out ExtGStateParameters? cached) && cached.Version == dictionary.Version)
        {
            return cached;
        }

        ExtGStateParameters parameters = ExtGStateParameters.Read(document, dictionary, reference);
        _graphicsStates.AddOrUpdate(dictionary, parameters);
        return parameters;
    }

    private sealed record DecodedContent(int DataVersion, int DictionaryVersion, ReadOnlyMemory<byte> Data);
}
