using Broadside.Content;
using Broadside.Objects;

namespace Broadside.Rendering;

/// <summary>Display lists kept in the document's display-list cache (issue #103, spec #33 viewer capability 13).</summary>
internal sealed partial class DisplayListBuilder
{
    /// <summary>
    /// Returns the page's kept display list of these layers while it is current, else builds one and offers it to the document's
    /// display-list cache.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="document">The page's document.</param>
    /// <param name="layers">The layers the list holds.</param>
    /// <param name="intent">Whether the list is for the screen or for printing.</param>
    /// <param name="options">Limits and cancellation for the content run; a list built with an optional-content state is not kept.</param>
    /// <returns>The list.</returns>
    public static DisplayList BuildCached(PdfPage page, PdfDocument document, DisplayLayers layers, DisplayIntent intent, ContentOptions options)
    {
        options.CancellationToken.ThrowIfCancellationRequested();
        if (options.OptionalContentState is not null)
        {
            return BuildLayers(page, document, layers, intent, options);
        }

        var key = new DisplayListKey(page.Dictionary, layers, intent, options.MaxOperands, options.MaxSaveDepth, options.MaxNestingDepth);
        if (document.Caches.DisplayLists.TryGet(key, out DisplayListEntry? cached))
        {
            // Nothing is left to build: the run is complete (issue #97's progress ends at exactly 1).
            options.Progress?.Report(1);
            return cached.List;
        }

        long epoch = CosMutation.Epoch;
        DisplayList list = BuildLayers(page, document, layers, intent, options);
        if (document.Caches.DisplayLists.Budget > 0)
        {
            document.Caches.DisplayLists.Add(key, new DisplayListEntry(epoch, list));
        }

        return list;
    }

    private static DisplayList BuildLayers(PdfPage page, PdfDocument document, DisplayLayers layers, DisplayIntent intent, ContentOptions options) =>
        layers == DisplayLayers.Content && intent == DisplayIntent.View
            ? Build(page, document, options)
            : Build(page, document, layers, intent, options);
}
