using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>The document outline (bookmarks): a tree of outline items. A live view over the outline dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.3.3, Table 150. <see cref="Items"/> walks the outline each time it is read: each level's items in the order of
/// their <c>First</c>/<c>Next</c> chain, iteratively, with one visited set for the whole outline and a depth cap of 256 levels, so a
/// cycle (a <c>Next</c> back to an earlier sibling, a <c>First</c> back to an ancestor) or a very deep chain ends.
/// </para>
/// <para>
/// The walk checks the outline as it goes and reports, once per item: an item reached twice (<c>OutlineCycle</c>, Error), a level
/// deeper than the cap, a <c>Last</c>, <c>Prev</c> or <c>Parent</c> that disagrees with the chain (the chain wins), a missing or
/// non-string <c>Title</c> (read as empty), an item that is not a dictionary, a malformed <c>C</c> or <c>F</c>, both <c>Dest</c> and
/// <c>A</c>, an item with children but no non-zero <c>Count</c> (read as closed), a negative outline <c>Count</c>, and the
/// diagnostics of each item's destination and action. In strict mode the walk throws the first of these.
/// </para>
/// </remarks>
public sealed class PdfOutline
{
    /// <summary>The deepest outline level the walk descends to.</summary>
    internal const int MaxDepth = 256;

    private readonly PdfDocument _document;

    internal PdfOutline(PdfDocument document, CosDictionary dictionary, CosReference? reference)
    {
        _document = document;
        Dictionary = dictionary;
        Reference = reference;
    }

    /// <summary>Gets the outline dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Table 150.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the outline dictionary, or <see langword="null"/> when it is direct.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the outline's <c>Count</c> as stored: the number of visible items at all levels; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Table 150.</remarks>
    public int? Count => PdfOutlineItem.ReadCount(_document, Dictionary);

    /// <summary>Walks the outline and returns its top-level items, each with its children.</summary>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation the walk finds.</exception>
    /// <remarks>ISO 32000-2 §12.3.3. A new walk on every call; the items are live views over their dictionaries.</remarks>
    public IReadOnlyList<PdfOutlineItem> Items => Walk();

    private List<PdfOutlineItem> Walk()
    {
        var top = new List<PdfOutlineItem>();
        var visitedReferences = new HashSet<CosReference>();
        var visitedItems = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        if (Reference is not null)
        {
            visitedReferences.Add(Reference);
        }

        visitedItems.Add(Dictionary);
        var levels = new Stack<Level>();
        levels.Push(new Level(null, Dictionary, Reference, 0));
        while (levels.TryPop(out Level level))
        {
            List<PdfOutlineItem> siblings = level.Parent?.ChildList ?? top;
            CosReference? previous = null;
            PdfOutlineItem? last = null;
            level.Node.TryGetValue(NavigationNames.First, out CosObject? entry);
            while (entry is not null)
            {
                var reference = entry as CosReference;
                CosObject resolved = _document.Resolve(entry);
                if (resolved is not CosDictionary dictionary)
                {
                    if (resolved is not CosNull)
                    {
                        Report(DiagnosticCodes.OutlineItemInvalid, DiagnosticSeverity.Warning, "An outline item is not a dictionary; its level ends there.", reference ?? level.Reference);
                    }

                    break;
                }

                if ((reference is not null && !visitedReferences.Add(reference)) || !visitedItems.Add(dictionary))
                {
                    Report(
                        DiagnosticCodes.OutlineCycle,
                        DiagnosticSeverity.Error,
                        "An outline item is reached a second time (a cycle through Next or First); its level ends there.",
                        reference ?? level.Reference);
                    break;
                }

                var item = new PdfOutlineItem(_document, dictionary, reference, level.Parent, level.Depth);
                CheckItem(item, level.Reference, previous);
                siblings.Add(item);
                previous = reference;
                last = item;
                dictionary.TryGetValue(NavigationNames.Next, out entry);
            }

            if (last is not null && !(level.Node.TryGetValue(NavigationNames.Last, out CosObject? lastEntry) && SameObject(lastEntry, last)))
            {
                Report(
                    DiagnosticCodes.OutlineLinkInconsistent,
                    DiagnosticSeverity.Warning,
                    "The Last entry does not name the item the First/Next chain ends with; the chain is used.",
                    level.Reference);
            }

            for (int index = siblings.Count - 1; index >= 0 && last is not null; index--)
            {
                PdfOutlineItem item = siblings[index];
                if (!item.Dictionary.ContainsKey(NavigationNames.First))
                {
                    continue;
                }

                if (level.Depth + 1 >= MaxDepth)
                {
                    Report(
                        DiagnosticCodes.OutlineTooDeep,
                        DiagnosticSeverity.Error,
                        string.Create(CultureInfo.InvariantCulture, $"The outline is deeper than {MaxDepth} levels; the deeper items are skipped."),
                        item.Reference ?? level.Reference);
                    continue;
                }

                levels.Push(new Level(item, item.Dictionary, item.Reference ?? level.Reference, level.Depth + 1));
            }
        }

        CheckCounts(top);
        return top;
    }

    /// <summary>Checks one item's entries (Table 151) as the walk reaches it.</summary>
    private void CheckItem(PdfOutlineItem item, CosReference? parentReference, CosReference? previous)
    {
        CosDictionary dictionary = item.Dictionary;
        CosReference? reference = item.Reference ?? parentReference;
        bool parentMatches = parentReference is null || (dictionary.TryGetValue(KnownNames.Parent, out CosObject? parent) && parentReference.Equals(parent));
        bool hasPrev = dictionary.TryGetValue(KnownNames.Prev, out CosObject? prev);
        bool prevMatches = previous is null ? !hasPrev : previous.Equals(prev);
        if (!parentMatches || !prevMatches)
        {
            Report(
                DiagnosticCodes.OutlineLinkInconsistent,
                DiagnosticSeverity.Warning,
                "An outline item's Parent or Prev does not name the item the First/Next chain reached it from; the chain is used.",
                reference);
        }

        if (_document.Resolve(dictionary.TryGetValue(NavigationNames.Title, out CosObject? title) ? title : null) is not CosString)
        {
            Report(DiagnosticCodes.OutlineItemInvalid, DiagnosticSeverity.Warning, "An outline item has no Title text string; it reads as empty.", reference);
        }

        _ = item.ReadColor(out bool colorValid);
        _ = item.ReadFlags(out bool flagsValid);
        if (!colorValid || !flagsValid)
        {
            Report(
                DiagnosticCodes.OutlineItemInvalid,
                DiagnosticSeverity.Warning,
                "An outline item's C is not three numbers from 0 to 1, or its F is not an integer; black or no flags are used where they cannot be read.",
                reference);
        }

        bool hasDest = dictionary.ContainsKey(NavigationNames.Dest);
        if (hasDest && dictionary.ContainsKey(NavigationNames.A))
        {
            Report(
                DiagnosticCodes.OutlineDestAndAction,
                DiagnosticSeverity.Warning,
                "An outline item shall not have both Dest and A; both are read, and the action is the one to perform.",
                reference);
        }
        else if (hasDest && PdfOutlineItem.IsActionDictionary(_document.Resolve(dictionary[NavigationNames.Dest])))
        {
            Report(DiagnosticCodes.DestinationInvalid, DiagnosticSeverity.Warning, "An outline item's Dest holds an action dictionary; it is read as the item's action.", reference);
        }

        // Read the destination and the action so that their own deviations are reported with the walk.
        _ = item.Destination;
        _ = item.Action;
    }

    /// <summary>
    /// Checks the <c>Count</c> entries that decide something (Tables 150 and 151), parents before children: an item with children
    /// needs a non-zero <c>Count</c> to be open or closed, and the outline's <c>Count</c> cannot be negative.
    /// </summary>
    /// <remarks>
    /// The magnitude is not checked against the visible descendants: producers commonly write the number of immediate children
    /// (the Isartor PDF/A suite, PDFium's test files), viewers ignore it, and nothing here uses it, so a mismatch would only make
    /// strict mode reject files every reader shows alike.
    /// </remarks>
    private void CheckCounts(List<PdfOutlineItem> top)
    {
        if (Dictionary.ContainsKey(KnownNames.Count) && Count is not >= 0)
        {
            Report(
                DiagnosticCodes.OutlineCountInvalid,
                DiagnosticSeverity.Warning,
                "The outline's Count is negative or not an integer; it cannot be.",
                Reference);
        }

        var pending = new Stack<PdfOutlineItem>(top.AsEnumerable().Reverse());
        while (pending.TryPop(out PdfOutlineItem? item))
        {
            if (item.Children.Count > 0 && item.Count is null or 0)
            {
                Report(
                    DiagnosticCodes.OutlineCountInvalid,
                    DiagnosticSeverity.Warning,
                    "An outline item with children has no non-zero Count to say whether it is open; it is read as closed.",
                    item.Reference);
            }

            for (int index = item.Children.Count - 1; index >= 0; index--)
            {
                pending.Push(item.Children[index]);
            }
        }
    }

    private bool SameObject(CosObject entry, PdfOutlineItem item) =>
        item.Reference is { } reference ? reference.Equals(entry) : ReferenceEquals(_document.Resolve(entry), item.Dictionary);

    private void Report(string code, DiagnosticSeverity severity, string message, CosReference? reference) =>
        _document.DiagnosticSink.Report(code, severity, message, objectReference: reference);

    /// <summary>A level waiting to be walked: the children of <paramref name="Node"/>.</summary>
    /// <param name="Parent">The item whose children these are, or <see langword="null"/> for the top level.</param>
    /// <param name="Node">The outline dictionary or the parent item's dictionary.</param>
    /// <param name="Reference">The node's reference, or its nearest indirect ancestor's.</param>
    /// <param name="Depth">The level of the children: 0 for top-level items.</param>
    private readonly record struct Level(PdfOutlineItem? Parent, CosDictionary Node, CosReference? Reference, int Depth);
}
