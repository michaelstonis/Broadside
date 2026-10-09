using System.Diagnostics.CodeAnalysis;
using System.Text;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Structure;

/// <summary>
/// One document-order walk of the structure hierarchy from the root's <c>K</c>, and the reverse indexes it yields: element by
/// dictionary (with its parent), (content stream, MCID) to element, object (OBJR) to element, and ID to element.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.7.2 and §14.7.5. The walk is iterative with an explicit stack and one visited set across all levels, so a
/// <c>K</c> graph that is a DAG or has cycles is read as a tree: an element met a second time is skipped
/// (<c>StructTreeCycle</c>), the hierarchy is cut at <see cref="StructureContext.MaxDepth"/> (<c>StructTreeDepthExceeded</c>).
/// </para>
/// <para>
/// It is the fallback for the parent tree (§14.7.5.4) when that is missing or wrong, and the source of <see cref="PdfStructureElement.Parent"/>
/// for elements found through it. MCIDs are keyed by the content stream they are in: the MCR's <c>Stm</c>, else the page (MCIDs are
/// unique per content stream, §14.7.5.2); a second item with the same key keeps the first (<c>McidDuplicate</c>).
/// </para>
/// </remarks>
internal sealed class StructureWalk
{
    private readonly List<PdfStructureElement> _elements = [];
    private readonly Dictionary<CosDictionary, PdfStructureElement> _byDictionary = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CosObject, Dictionary<int, PdfStructureElement>> _markedContent = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CosObject, PdfStructureElement> _objects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, PdfStructureElement> _ids = new(StringComparer.Ordinal);

    public static StructureWalk Empty { get; } = new();

    /// <summary>Gets every element reachable from the root, in document (depth-first, <c>K</c>) order.</summary>
    public IReadOnlyList<PdfStructureElement> Elements => _elements;

    /// <summary>Gets a value indicating whether any element has a content item.</summary>
    public bool HasContentItems { get; private set; }

    public static StructureWalk Build(StructureContext context)
    {
        var walk = new StructureWalk();
        var visited = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(CosObject Entry, PdfStructureElement? Parent, int Depth)>();
        Push(stack, KidReader.Entries(context, context.Root), null, 0);
        while (stack.TryPop(out (CosObject Entry, PdfStructureElement? Parent, int Depth) next))
        {
            (CosObject entry, PdfStructureElement? parent, int depth) = next;
            Kid kid = KidReader.Classify(context, entry, parent?.Reference ?? context.RootReference);
            if (parent is null && kid.Kind is KidKind.MarkedContent or KidKind.Object)
            {
                context.Report(DiagnosticCodes.StructTreeRootInvalid, "The structure tree root's K holds a content item; only structure elements may be its children (Table 354). Skipped.", context.RootReference);
                continue;
            }

            switch (kid.Kind)
            {
                case KidKind.Element:
                    walk.VisitElement(context, kid, parent, depth, visited, stack);
                    break;
                case KidKind.MarkedContent:
                    walk.VisitMarkedContent(context, parent!.CreateMarkedContent(kid));
                    break;
                case KidKind.Object:
                    PdfObjectReference reference = parent!.CreateObjectReference(kid);
                    walk.HasContentItems = true;
                    if (reference.ReferencedObject is not CosNull)
                    {
                        walk._objects.TryAdd(reference.ReferencedObject, parent);
                    }

                    break;
            }
        }

        context.CheckParentTree(walk);
        return walk;
    }

    public bool TryGet(CosDictionary dictionary, [MaybeNullWhen(false)] out PdfStructureElement element) => _byDictionary.TryGetValue(dictionary, out element);

    public bool TryGetObjectParent(CosObject value, [MaybeNullWhen(false)] out PdfStructureElement element) => _objects.TryGetValue(value, out element);

    public bool TryGetById(ReadOnlySpan<byte> id, [MaybeNullWhen(false)] out PdfStructureElement element) => _ids.TryGetValue(Encoding.Latin1.GetString(id), out element);

    /// <summary>The MCID-to-element map of one content stream (a page object or a stream), from <c>K</c>.</summary>
    public IReadOnlyDictionary<int, PdfStructureElement>? MarkedContent(CosObject streamKey) =>
        _markedContent.TryGetValue(streamKey, out Dictionary<int, PdfStructureElement>? map) ? map : null;

    private static void Push(Stack<(CosObject, PdfStructureElement?, int)> stack, IReadOnlyList<CosObject> entries, PdfStructureElement? parent, int depth)
    {
        for (int index = entries.Count - 1; index >= 0; index--)
        {
            stack.Push((entries[index], parent, depth));
        }
    }

    private void VisitElement(StructureContext context, Kid kid, PdfStructureElement? parent, int depth, HashSet<CosDictionary> visited, Stack<(CosObject, PdfStructureElement?, int)> stack)
    {
        CosDictionary dictionary = kid.Dictionary!;
        CosReference? where = kid.Reference ?? parent?.Reference;
        if (ReferenceEquals(dictionary, context.Root) || !visited.Add(dictionary))
        {
            context.Report(DiagnosticCodes.StructTreeCycle, "A structure element is reached a second time through K (§14.7.2: the hierarchy is a tree); the second occurrence is skipped.", where);
            return;
        }

        if (depth >= StructureContext.MaxDepth)
        {
            context.Report(DiagnosticCodes.StructTreeDepthExceeded, $"The structure hierarchy nests deeper than {StructureContext.MaxDepth} levels; the rest is not read.", where);
            return;
        }

        var element = new PdfStructureElement(context, dictionary, kid.Reference, parent, parentKnown: true, depth);
        _elements.Add(element);
        _byDictionary.Add(dictionary, element);

        CosDictionary expectedParent = parent?.Dictionary ?? context.Root;
        if (!dictionary.TryGetValue(StructureNames.P, out CosObject? declaredParent) || !ReferenceEquals(context.Resolve(declaredParent), expectedParent))
        {
            context.Report(DiagnosticCodes.StructElemParentMismatch, "A structure element's P is not the element (or root) whose K holds it (Table 355); the K hierarchy is used.", where);
        }

        if (StructureValues.Get(context.Document, dictionary, StructureNames.ID) is CosString id)
        {
            if (!_ids.TryAdd(Encoding.Latin1.GetString(id.Bytes), element))
            {
                context.Report(DiagnosticCodes.IdTreeDuplicate, "Two structure elements have the same ID (Table 355: unique in the document); lookups find the first.", where);
            }
            else if (context.IdTree is not { } idTree || !idTree.TryGetValue(id.Bytes, out CosObject? listed) || !ReferenceEquals(listed, dictionary))
            {
                context.Report(DiagnosticCodes.IdTreeEntryInvalid, "A structure element's ID is not in the structure tree root's IDTree, or maps to another element (Table 354); lookups find it through K.", where);
            }
        }

        Push(stack, KidReader.Entries(context, dictionary), element, depth + 1);
    }

    private void VisitMarkedContent(StructureContext context, PdfMarkedContentReference item)
    {
        HasContentItems = true;
        if (item.StreamKey is not { } key)
        {
            return;
        }

        if (!_markedContent.TryGetValue(key, out Dictionary<int, PdfStructureElement>? map))
        {
            map = [];
            _markedContent.Add(key, map);
        }

        if (!map.TryAdd(item.Mcid, item.Parent))
        {
            context.Report(
                DiagnosticCodes.McidDuplicate,
                $"MCID {item.Mcid} of one content stream is a content item of two structure elements (§14.7.5.2: MCIDs are unique per content stream); the first is kept.",
                item.Parent.Reference);
        }
    }
}
