using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Structure;

/// <summary>The MCID-to-element map of one content stream (a page or a form XObject), built once per stream.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.7.5.4, Table 359: the stream's <c>StructParents</c> is a key of the parent tree, whose value is an array indexed
/// by MCID of the elements owning the stream's marked-content sequences (null entries are gaps). The map is keyed by the content
/// stream, because MCIDs are unique per stream, not per page (§14.7.5.2). Building it reads one number-tree entry; it does not walk
/// the whole structure tree.
/// </para>
/// <para>
/// Each entry is checked against the element it names: the element's <c>K</c> must hold that MCID for that stream. An entry that
/// does not, a parent-tree value that is not an array, or a stream with content items but no usable entry, is a
/// <c>ParentTreeEntryInvalid</c> diagnostic, and the answer comes from the <c>K</c> walk instead. Arrays longer than
/// <see cref="StructureContext.MaxParentTreeArray"/> are read up to that length.
/// </para>
/// </remarks>
internal sealed class MarkedContentIndex
{
    private static readonly Dictionary<int, PdfStructureElement> NoElements = [];

    private MarkedContentIndex(IReadOnlyDictionary<int, PdfStructureElement> elements) => Elements = elements;

    public static MarkedContentIndex Empty { get; } = new(NoElements);

    public IReadOnlyDictionary<int, PdfStructureElement> Elements { get; }

    public static MarkedContentIndex Build(StructureContext context, CosObject owner)
    {
        CosDictionary? ownerDictionary = owner as CosDictionary ?? (owner as CosStream)?.Dictionary;
        int? key = ownerDictionary is null ? null : StructureValues.Integer(context.Document, ownerDictionary, StructureNames.StructParents);
        if (context.ParentTree is not { } tree)
        {
            StructureWalk walk = context.Walk;
            return From(walk.MarkedContent(owner));
        }

        if (key is not { } structParents || !tree.TryGetValue(structParents, out CosObject? value) || value is not CosArray array)
        {
            IReadOnlyDictionary<int, PdfStructureElement>? fromWalk = context.Walk.MarkedContent(owner);
            if (fromWalk is { Count: > 0 })
            {
                context.Report(
                    DiagnosticCodes.ParentTreeEntryInvalid,
                    key is null
                        ? "A content stream holds content items but has no StructParents entry (Table 359); its MCIDs are mapped through K."
                        : $"The parent tree entry {key} of a content stream is missing or not an array of elements (§14.7.5.4); its MCIDs are mapped through K.",
                    owner is CosDictionary page && context.Document.Pages.TryGetPage(page, out PdfPage? found) ? found.Reference : null);
            }

            return From(fromWalk);
        }

        var elements = new Dictionary<int, PdfStructureElement>();
        var owned = new Dictionary<CosDictionary, HashSet<int>>(ReferenceEqualityComparer.Instance);
        int count = Math.Min(array.Count, StructureContext.MaxParentTreeArray);
        for (int mcid = 0; mcid < count; mcid++)
        {
            CosObject entry = array[mcid];
            CosObject resolved = context.Resolve(entry);
            if (resolved is CosNull)
            {
                continue;
            }

            if (resolved is CosDictionary dictionary && Owns(context, owned, dictionary, entry as CosReference, owner).Contains(mcid))
            {
                elements[mcid] = new PdfStructureElement(context, dictionary, entry as CosReference, null, parentKnown: false, depth: 0);
                continue;
            }

            context.Report(
                DiagnosticCodes.ParentTreeEntryInvalid,
                $"The parent tree names an element for MCID {mcid} whose K does not hold that MCID (§14.7.5.4); the element whose K holds it is used.",
                entry as CosReference);
            if (context.Walk.MarkedContent(owner) is { } fromWalk && fromWalk.TryGetValue(mcid, out PdfStructureElement? element))
            {
                elements[mcid] = element;
            }
        }

        return new MarkedContentIndex(elements);
    }

    private static MarkedContentIndex From(IReadOnlyDictionary<int, PdfStructureElement>? elements) => elements is null ? Empty : new MarkedContentIndex(elements);

    /// <summary>The MCIDs of <paramref name="owner"/>'s content that <paramref name="dictionary"/>'s <c>K</c> holds, read once per element.</summary>
    private static HashSet<int> Owns(StructureContext context, Dictionary<CosDictionary, HashSet<int>> owned, CosDictionary dictionary, CosReference? reference, CosObject owner)
    {
        if (owned.TryGetValue(dictionary, out HashSet<int>? mcids))
        {
            return mcids;
        }

        mcids = [];
        var element = new PdfStructureElement(context, dictionary, reference, null, parentKnown: false, depth: 0);
        foreach (PdfStructureItem item in element.Children)
        {
            if (item is PdfMarkedContentReference content && (content.StreamKey is null || ReferenceEquals(content.StreamKey, owner)))
            {
                mcids.Add(content.Mcid);
            }
        }

        owned.Add(dictionary, mcids);
        return mcids;
    }
}
