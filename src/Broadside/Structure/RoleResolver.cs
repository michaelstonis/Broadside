using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>The outcome of resolving a structure type through the role maps: the chain followed and the standard type reached.</summary>
/// <param name="Chain">The types visited, starting with the element's own type.</param>
/// <param name="Standard">The standard type the chain resolves to, or <see langword="null"/>.</param>
/// <param name="EndsInCycle">Whether the chain returned to a type it had already visited (or hit the step cap).</param>
internal sealed record RoleResolution(IReadOnlyList<PdfStructureType> Chain, PdfStructureType? Standard, bool EndsInCycle);

/// <summary>Follows role maps from a (type, namespace) pair to a standard structure type.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.7.3, §14.7.4.2 (Table 356) and §14.8.6.2; ISO/TS 32005 §5.6. A type in the default namespace is mapped through
/// the root's <c>RoleMap</c>, always, even when it is already a standard type (§14.7.3: "shall always be mapped"); a type in an
/// explicit namespace only through that namespace's <c>RoleMapNS</c>. A <c>RoleMapNS</c> value that is a name lands in the DEFAULT
/// namespace (PDF 1.7), not in the source namespace; <c>[name namespace]</c> lands in the named namespace. The chain is followed until
/// no map applies; the type it ends on is the standard type when it is standard in its namespace.
/// </para>
/// <para>
/// Cycles are legal (§14.7.3 NOTE 2: "follow the chain ... until it either finds a structure type it recognises or returns to one it
/// has already encountered"): on a cycle, the first standard type met after the starting type wins, else the starting type when it is
/// standard. pdf.js and PDFBox apply one RoleMap step and ignore RoleMapNS; the chain is the specification's behavior.
/// </para>
/// </remarks>
internal static class RoleResolver
{
    public static RoleResolution Resolve(StructureContext context, CosName type, PdfStructureNamespace ns)
    {
        var chain = new List<PdfStructureType> { new(type.Value, ns) };
        CosName currentName = type;
        PdfStructureNamespace current = ns;
        bool cycle = false;
        for (int step = 0; ; step++)
        {
            if (step == StructureContext.MaxRoleSteps)
            {
                cycle = true;
                break;
            }

            CosDictionary? map = current.Dictionary is null
                ? current.IsDefault ? StructureValues.Get(context.Document, context.Root, StructureNames.RoleMap) as CosDictionary : null
                : current.RoleMap;
            if (map is null || !map.TryGetValue(currentName, out CosObject? mapped))
            {
                break;
            }

            CosName nextName;
            PdfStructureNamespace next;
            switch (context.Resolve(mapped))
            {
                case CosName name:
                    (nextName, next) = (name, PdfStructureNamespace.Pdf17);
                    break;
                case CosArray { Count: >= 2 } array when context.Resolve(array[0]) is CosName name && context.Resolve(array[1]) is CosDictionary target:
                    (nextName, next) = (name, context.Namespace(target, array[1] as CosReference));
                    break;
                default:
                    nextName = null!;
                    next = null!;
                    break;
            }

            if (next is null)
            {
                break;
            }

            if (Visited(chain, nextName, next))
            {
                cycle = true;
                break;
            }

            chain.Add(new PdfStructureType(nextName.Value, next));
            (currentName, current) = (nextName, next);
        }

        PdfStructureType? standard;
        if (!cycle)
        {
            standard = chain[^1].IsStandard ? chain[^1] : null;
        }
        else
        {
            standard = null;
            for (int index = 1; index < chain.Count && standard is null; index++)
            {
                if (chain[index].IsStandard)
                {
                    standard = chain[index];
                }
            }

            standard ??= chain[0].IsStandard ? chain[0] : null;
        }

        return new RoleResolution(chain, standard, cycle);
    }

    private static bool Visited(List<PdfStructureType> chain, CosName name, PdfStructureNamespace ns)
    {
        foreach (PdfStructureType visited in chain)
        {
            if (visited.Name == name.Value && SameNamespace(visited.Namespace, ns))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Same map source: the same dictionary, or both the default namespace (which maps through the root's RoleMap).</summary>
    private static bool SameNamespace(PdfStructureNamespace left, PdfStructureNamespace right) =>
        left.Dictionary is null ? right.Dictionary is null && left.Kind == right.Kind : ReferenceEquals(left.Dictionary, right.Dictionary);
}
