using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>
/// A content item that is a marked-content sequence: an integer MCID in a structure element's <c>K</c>, or a marked-content reference
/// dictionary (<c>/Type /MCR</c>).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.7.5.2, Table 357. The sequence is the one in a content stream whose property list has this <see cref="Mcid"/>:
/// the stream is <see cref="ContentStream"/> (<c>Stm</c>, a form XObject or an annotation appearance) when the MCR names one,
/// otherwise the content of <see cref="Page"/>. MCIDs are unique within one content stream, not within a page (§14.7.5.2).
/// </para>
/// <para>
/// The page is the MCR's <c>Pg</c>, else the element's <c>Pg</c> (Table 357: the MCR's overrides). When neither has one, the
/// nearest ancestor element's <c>Pg</c> is used as a repair, else the page whose parent-tree array lists the element at this MCID, with a
/// <c>StructElemPageMissing</c> diagnostic.
/// </para>
/// </remarks>
public sealed class PdfMarkedContentReference : PdfStructureItem
{
    internal PdfMarkedContentReference(PdfStructureElement parent, CosDictionary? dictionary, int mcid, PdfPage? page, CosDictionary? pageObject, CosStream? stream, CosObject? streamOwner)
    {
        Parent = parent;
        Dictionary = dictionary;
        Mcid = mcid;
        Page = page;
        PageObject = pageObject;
        ContentStream = stream;
        ContentStreamOwner = streamOwner;
    }

    /// <inheritdoc/>
    public override PdfStructureElement Parent { get; }

    /// <summary>Gets the marked-content reference dictionary, or <see langword="null"/> when the item is a bare integer in <c>K</c>.</summary>
    /// <remarks>ISO 32000-2 §14.7.5.2, Table 357.</remarks>
    public CosDictionary? Dictionary { get; }

    /// <summary>Gets the marked-content identifier: the <c>MCID</c> of the sequence's property list.</summary>
    /// <remarks>ISO 32000-2 §14.7.5.2.</remarks>
    public int Mcid { get; }

    /// <summary>Gets the page the sequence is on, or <see langword="null"/> when no page could be found.</summary>
    /// <remarks>ISO 32000-2 Table 357 (<c>Pg</c>) and Table 355 (<c>Pg</c>).</remarks>
    public PdfPage? Page { get; }

    /// <summary>Gets the content stream holding the sequence when it is not the page's content (<c>Stm</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.7.5.2, Table 357: a form XObject or an annotation's appearance stream.</remarks>
    public CosStream? ContentStream { get; }

    /// <summary>Gets the object owning <see cref="ContentStream"/> (<c>StmOwn</c>, for instance the annotation of an appearance stream), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.7.5.2, Table 357.</remarks>
    public CosObject? ContentStreamOwner { get; }

    /// <summary>The page object of <see cref="Page"/> (or the <c>Pg</c> found, when it is not in the page tree).</summary>
    internal CosDictionary? PageObject { get; }

    /// <summary>The object whose content stream holds the MCID: <see cref="ContentStream"/>, else the page object. The MCID index key.</summary>
    internal CosObject? StreamKey => (CosObject?)ContentStream ?? PageObject;
}
