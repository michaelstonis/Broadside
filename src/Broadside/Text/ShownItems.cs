using System.Runtime.CompilerServices;
using Broadside.Content;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Text;

/// <summary>
/// Which content items of the structure tree show at least one glyph, per page: what decides where an element's replacement goes
/// when its first content items show none (issue #298). Each page is extracted for this at most once per walk, options and
/// mutation epoch, however many pages ask about it.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.9.4 and §14.9.3: an element's replacement stands for everything it encloses, so it is given once, by its first
/// glyph in logical content order (§14.8.2.5.1). That glyph can be on a later page than the element's first content item (an image,
/// an empty sequence), and only extracting the earlier page tells.
/// </para>
/// <para>
/// One cache per <see cref="StructureWalk"/> (a structure edit makes a new walk, and the cache goes with the old one), holding the
/// pages of one set of options. Any change to the document's objects or stream data advances <see cref="CosMutation.Epoch"/>, which
/// empties it, so a content edit is seen by the next extraction. Concurrent extractions share it: it is filled under a lock, each
/// page computed outside it (two threads may both compute a page; either result is kept).
/// </para>
/// </remarks>
internal sealed class ShownItems
{
    private static readonly ConditionalWeakTable<StructureWalk, ShownItems> Caches = [];

    private readonly Lock _gate = new();
    private readonly Dictionary<CosDictionary, HashSet<int>> _pages = new(ReferenceEqualityComparer.Instance);
    private long _epoch = -1;
    private (bool Hidden, bool Artifacts, ContentOptions? Content) _key;

    /// <summary>Gets the cache of <paramref name="walk"/>'s content items.</summary>
    public static ShownItems Of(StructureWalk walk) => Caches.GetValue(walk, static _ => new ShownItems());

    /// <summary>
    /// Whether the content item at position <paramref name="order"/> in logical content order shows a glyph on
    /// <paramref name="pageObject"/>, extracting that page with <paramref name="options"/> when it has not been yet.
    /// </summary>
    public bool Shows(PdfDocument document, StructureWalk walk, CosDictionary pageObject, int order, TextExtractionOptions options)
    {
        (bool, bool, ContentOptions?) key = (options.IncludeHiddenContent, options.IncludeArtifacts, options.ContentOptions);
        long epoch = CosMutation.Epoch;
        lock (_gate)
        {
            if (_epoch != epoch || _key != key)
            {
                _pages.Clear();
                _epoch = epoch;
                _key = key;
            }

            if (_pages.TryGetValue(pageObject, out HashSet<int>? known))
            {
                return known.Contains(order);
            }
        }

        HashSet<int> shown = Extract(document, walk, pageObject, options);
        lock (_gate)
        {
            if (_epoch == epoch && _key == key)
            {
                _pages.TryAdd(pageObject, shown);
            }
        }

        return shown.Contains(order);
    }

    /// <summary>The positions of the content items that show a glyph kept by <paramref name="options"/> on the page, artifacts left out unless included.</summary>
    private static HashSet<int> Extract(PdfDocument document, StructureWalk walk, CosDictionary pageObject, TextExtractionOptions options)
    {
        var shown = new HashSet<int>();
        if (!document.Pages.TryGetPage(pageObject, out PdfPage? page))
        {
            // A page outside the page tree is never extracted, so it shows nothing.
            return shown;
        }

        // A nested rent on this thread gets an extractor of its own; the caller's stays as it is (issue #284).
        TextExtractor extractor = TextExtractor.Rent(options);
        try
        {
            page.ProcessContent(extractor, options.ContentOptions ?? ContentInterpreter.DefaultOptions);
            ReadOnlySpan<ContentItemKey> items = extractor.ContentItems;
            var withGlyph = new bool[items.Length];
            foreach (int item in extractor.GlyphItems)
            {
                if (item >= 0)
                {
                    withGlyph[item] = true;
                }
            }

            for (int index = 0; index < items.Length; index++)
            {
                ContentItemKey key = items[index];
                bool found = key.Mcid >= 0
                    ? walk.TryGetMarkedContent(key.Owner, key.Mcid, out PdfStructureElement? element, out int order)
                    : walk.TryGetObject(key.Owner, out element, out order);
                if (withGlyph[index] && found && (options.IncludeArtifacts || !StructureOrder.IsArtifact(element!)))
                {
                    shown.Add(order);
                }
            }
        }
        finally
        {
            TextExtractor.Return(extractor);
        }

        return shown;
    }
}
