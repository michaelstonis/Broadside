using Broadside.Content;
using Broadside.Objects;

namespace Broadside.Rendering;

/// <summary>Display lists kept in the document's display-list cache (issue #103, spec #33 viewer capability 13).</summary>
internal sealed partial class DisplayListBuilder
{
    /// <summary>
    /// Returns the page's kept display list while it is current, else builds one and offers it to the document's display-list cache.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="document">The page's document.</param>
    /// <param name="options">Limits and cancellation for the content run; a list built with an optional-content state is not kept.</param>
    /// <returns>The list.</returns>
    public static DisplayList BuildCached(PdfPage page, PdfDocument document, ContentOptions options)
    {
        options.CancellationToken.ThrowIfCancellationRequested();
        if (options.OptionalContentState is not null)
        {
            return Build(page, document, options);
        }

        var key = (page.Dictionary, options.MaxOperands, options.MaxSaveDepth, options.MaxNestingDepth);
        if (document.Caches.DisplayLists.TryGet(key, out DisplayListEntry? cached))
        {
            return cached.List;
        }

        long epoch = CosMutation.Epoch;
        DisplayList list = Build(page, document, options);
        if (document.Caches.DisplayLists.Budget > 0)
        {
            document.Caches.DisplayLists.Add(key, new DisplayListEntry(epoch, list));
        }

        return list;
    }
}
