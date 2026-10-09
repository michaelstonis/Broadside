using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>A content item that is a whole PDF object, such as an annotation or an XObject: an object reference dictionary (<c>/Type /OBJR</c>).</summary>
/// <remarks>
/// ISO 32000-2 §14.7.5.3, Table 358. The object carries a <c>StructParent</c> entry whose parent-tree value leads back to
/// <see cref="PdfStructureItem.Parent"/> (§14.7.5.4). The page is the dictionary's <c>Pg</c>, else the element's.
/// </remarks>
public sealed class PdfObjectReference : PdfStructureItem
{
    internal PdfObjectReference(PdfStructureElement parent, CosDictionary dictionary, CosObject referencedObject, CosReference? objectReference, PdfPage? page)
    {
        Parent = parent;
        Dictionary = dictionary;
        ReferencedObject = referencedObject;
        ObjectReference = objectReference;
        Page = page;
    }

    /// <inheritdoc/>
    public override PdfStructureElement Parent { get; }

    /// <summary>Gets the object reference dictionary.</summary>
    /// <remarks>ISO 32000-2 §14.7.5.3, Table 358.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the object that is the content item (<c>Obj</c>), resolved; <see cref="CosNull"/> when it does not exist.</summary>
    /// <remarks>ISO 32000-2 §14.7.5.3, Table 358 (required, an indirect reference).</remarks>
    public CosObject ReferencedObject { get; }

    /// <summary>Gets the indirect reference <c>Obj</c> holds, or <see langword="null"/> when it holds the object directly.</summary>
    public CosReference? ObjectReference { get; }

    /// <summary>Gets the page the object is rendered on, or <see langword="null"/> when no page could be found.</summary>
    /// <remarks>ISO 32000-2 Table 358 (<c>Pg</c>) and Table 355 (<c>Pg</c>).</remarks>
    public PdfPage? Page { get; }
}
