using System.Globalization;

namespace Broadside.Viewer;

/// <summary>
/// A document's outline (its bookmarks) as a navigable model: a tree of <see cref="OutlineEntry"/> whose destinations are resolved
/// to <see cref="ViewTarget"/>s once, at load, so a viewer lists them and goes to one (<see cref="ViewTarget.GetViewport"/>) without
/// reading the document again.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.3.3 (Tables 150 to 152) and §12.3.2 (destinations, including named ones). Spec #33 viewer capability 10.
/// An item with an action goes where its action goes (a viewer prefers the action to the destination, as the clause's readers do): a
/// GoTo action to its destination, a URI action to <see cref="OutlineEntry.Uri"/>; other actions are only reported as
/// <see cref="OutlineEntry.ActionKind"/>. A destination in another document, or one that is not valid, leaves the entry without a target.
/// </para>
/// <para>
/// The load reads the document under the session's read lease (on a worker in background mode, fetching what a remote document
/// misses), builds an immutable snapshot and records the repairs it makes as diagnostics of the document. After an edit load again.
/// </para>
/// </remarks>
public sealed class DocumentOutline
{
    private DocumentOutline(IReadOnlyList<OutlineEntry> roots, IReadOnlyList<OutlineEntry> entries)
    {
        Roots = roots;
        Entries = entries;
    }

    /// <summary>Gets the top-level entries, in outline order.</summary>
    public IReadOnlyList<OutlineEntry> Roots { get; }

    /// <summary>Gets every entry, depth first: a tree view's rows when everything is expanded. <see cref="OutlineEntry.Id"/> is the index.</summary>
    public IReadOnlyList<OutlineEntry> Entries { get; }

    /// <summary>Gets a value indicating whether the document has no outline items.</summary>
    public bool IsEmpty => Entries.Count == 0;

    /// <summary>Reads a session's outline.</summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The outline; <see cref="IsEmpty"/> when the document has none.</returns>
    /// <remarks>ISO 32000-2 §12.3.3.</remarks>
    public static async ValueTask<DocumentOutline> ReadAsync(DocumentSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return await session.ReadAsync(token => Create(session.Document, token), cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Returns the entries a tree view shows when each entry is open or closed as <paramref name="isOpen"/> says.</summary>
    /// <param name="isOpen">Whether an entry's children are shown; <see cref="OutlineEntry.IsOpen"/> gives the file's own choice.</param>
    /// <returns>The visible entries, depth first.</returns>
    public IReadOnlyList<OutlineEntry> GetVisible(Func<OutlineEntry, bool> isOpen)
    {
        ArgumentNullException.ThrowIfNull(isOpen);
        var visible = new List<OutlineEntry>();
        foreach (OutlineEntry root in Roots)
        {
            AddVisible(root, isOpen, visible);
        }

        return visible;
    }

    /// <summary>Returns the entries a tree view shows with each entry as the file asks (<see cref="OutlineEntry.IsOpen"/>).</summary>
    /// <returns>The visible entries, depth first.</returns>
    public IReadOnlyList<OutlineEntry> GetVisible() => GetVisible(static entry => entry.IsOpen);

    /// <summary>Finds the entry a page is under: the last entry, in outline order, whose target is on or before the page (a reader's "you are here").</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>The entry, or <see langword="null"/> when no entry's target is on or before the page.</returns>
    public OutlineEntry? FindEntryFor(int pageIndex)
    {
        OutlineEntry? best = null;
        int bestPage = -1;
        foreach (OutlineEntry entry in Entries)
        {
            if (entry.Target is { } target && target.PageIndex <= pageIndex && target.PageIndex >= bestPage)
            {
                (best, bestPage) = (entry, target.PageIndex);
            }
        }

        return best;
    }

    private static void AddVisible(OutlineEntry entry, Func<OutlineEntry, bool> isOpen, List<OutlineEntry> visible)
    {
        visible.Add(entry);
        if (entry.Children.Count > 0 && isOpen(entry))
        {
            foreach (OutlineEntry child in entry.Children)
            {
                AddVisible(child, isOpen, visible);
            }
        }
    }

    private static DocumentOutline Create(PdfDocument document, CancellationToken token)
    {
        if (document.Outline is not { } outline)
        {
            return new DocumentOutline([], []);
        }

        IReadOnlyList<string>? labels = document.PageLabels?.GetLabels();
        var entries = new List<OutlineEntry>();
        List<OutlineEntry> roots = Build(outline.Items, null, 0, entries, labels, token);
        return new DocumentOutline(roots, entries);
    }

    private static List<OutlineEntry> Build(
        IReadOnlyList<PdfOutlineItem> items,
        OutlineEntry? parent,
        int level,
        List<OutlineEntry> entries,
        IReadOnlyList<string>? labels,
        CancellationToken token)
    {
        var list = new List<OutlineEntry>(items.Count);
        foreach (PdfOutlineItem item in items)
        {
            token.ThrowIfCancellationRequested();
            var entry = new OutlineEntry(entries.Count, parent, item.Title, level)
            {
                IsOpen = item.IsOpen,
                IsBold = item.IsBold,
                IsItalic = item.IsItalic,
                Color = item.Color,
            };
            entries.Add(entry);
            list.Add(entry);
            Resolve(item, entry, labels);
            entry.Children = Build(item.Children, entry, level + 1, entries, labels, token);
        }

        return list;
    }

    private static void Resolve(PdfOutlineItem item, OutlineEntry entry, IReadOnlyList<string>? labels)
    {
        // The action wins over a destination when both are there (the clause forbids it, readers prefer the action).
        if (item.Action is { } action)
        {
            switch (action)
            {
                case PdfGoToAction goTo:
                    entry.Target = ViewTarget.Resolve(goTo.Destination);
                    break;
                case PdfUriAction uri:
                    entry.Uri = uri.ResolveUri();
                    entry.ActionKind = PdfActionKind.Uri;
                    break;
                default:
                    entry.ActionKind = action.Kind;
                    break;
            }
        }
        else
        {
            entry.Target = ViewTarget.Resolve(item.Destination);
        }

        if (entry.Target is { } target)
        {
            entry.PageLabel = labels is not null && target.PageIndex < labels.Count
                ? labels[target.PageIndex]
                : (target.PageIndex + 1).ToString(CultureInfo.InvariantCulture);
        }
    }
}
