using Broadside.Objects;
using Broadside.Structure;

namespace Broadside;

/// <content>Writing a structure tree (issue #156): ISO 32000-2 §14.7 and §14.8.</content>
public sealed partial class PdfDocument
{
    /// <summary>Gives the document an empty structure tree and marks it as a tagged PDF.</summary>
    /// <returns>The new root; add elements with <see cref="PdfStructureTreeRoot.AddElement(PdfStructureType)"/>.</returns>
    /// <exception cref="InvalidOperationException">The document already has a structure tree (<see cref="StructureTree"/>).</exception>
    /// <remarks>
    /// ISO 32000-2 §7.7.2 (Table 29, <c>StructTreeRoot</c> and <c>MarkInfo</c>), §14.7.2 (Table 354), §14.7.5.4, §14.8.1. The root is a
    /// new indirect object with an empty <c>K</c>, an empty parent tree and <c>ParentTreeNextKey</c> 0; the catalog's <c>MarkInfo</c>
    /// gets <c>Marked</c> true. Structure trees need PDF 1.3 and tagged PDF 1.4 (<see cref="PdfStructureTreeRoot.MinimumVersion"/>).
    /// A mutation: no read may run concurrently with it.
    /// </remarks>
    public PdfStructureTreeRoot CreateStructureTree()
    {
        if (Catalog.TryGetValue(StructureNames.StructTreeRoot, out CosObject? existing) && Resolve(existing) is CosDictionary)
        {
            throw new InvalidOperationException("The document already has a structure tree; edit it through PdfDocument.StructureTree.");
        }

        PdfNumberTree parentTree = CreateNumberTree([]);
        var root = new CosDictionary
        {
            [KnownNames.Type] = StructureNames.StructTreeRoot,
            [StructureNames.K] = new CosArray(),
            [StructureNames.ParentTree] = parentTree.RootReference!,
            [StructureNames.ParentTreeNextKey] = new CosInteger(0),
        };
        Catalog[StructureNames.StructTreeRoot] = AddObject(root);
        MarkInfo.Marked = true;
        return StructureTree!;
    }
}
