using Broadside.Filters;
using Broadside.Objects;

namespace Broadside.Caching;

/// <summary>
/// Whether a cache of what a stream decodes to must record the document's <c>ObjectsVersion</c>: only when the stream's
/// <c>Filter</c> or <c>DecodeParms</c> is, or holds, an indirect reference.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.3.8.2, Table 5, and §7.3.10. A cache keyed by the stream and the <c>Version</c>s of its data and dictionary sees
/// every edit of them, but not an object added or removed: that changes what a reference in the unchanged dictionary resolves to (a
/// removed <c>Filter</c> name resolves to null, so the data is read as stored). Recording <c>ObjectsVersion</c> for every stream would
/// decode every stream again after each added object, so only streams whose filters or parameters are reached through a reference
/// record it (issue #305). Which streams those are follows from the dictionary, so the caches key on its <c>Version</c> too.
/// </remarks>
internal static class DecodeStamp
{
    /// <summary>How deeply a filter entry nests containers: an array of parameter dictionaries holding a reference is 2.</summary>
    private const int MaxDepth = 2;

    /// <summary>Returns the stamp a decode cache records for <paramref name="stream"/>.</summary>
    /// <param name="document">The document the stream's references resolve through.</param>
    /// <param name="stream">The stream.</param>
    /// <returns>The document's <c>ObjectsVersion</c> when decoding reads a reference; <see langword="null"/> otherwise.</returns>
    public static int? Of(PdfDocument document, CosStream stream) => ReadsReferences(stream.Dictionary) ? document.ObjectsVersion : null;

    /// <summary>Returns whether the <c>Filter</c> or <c>DecodeParms</c> entry of a stream dictionary is or holds an indirect reference.</summary>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <returns><see langword="true"/> when decoding the stream resolves a reference.</returns>
    public static bool ReadsReferences(CosDictionary dictionary) =>
        (dictionary.TryGetValue(FilterNames.Filter, out CosObject? filter) && HoldsReference(filter, MaxDepth))
        || (dictionary.TryGetValue(FilterNames.DecodeParms, out CosObject? parameters) && HoldsReference(parameters, MaxDepth));

    private static bool HoldsReference(CosObject value, int depth)
    {
        switch (value)
        {
            case CosReference:
                return true;
            case CosArray array when depth > 0:
                for (int i = 0; i < array.Count; i++)
                {
                    if (HoldsReference(array[i], depth - 1))
                    {
                        return true;
                    }
                }

                return false;
            case CosDictionary dictionary when depth > 0:
                for (int i = 0; i < dictionary.Count; i++)
                {
                    if (HoldsReference(dictionary.GetAt(i).Value, depth - 1))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }
}
