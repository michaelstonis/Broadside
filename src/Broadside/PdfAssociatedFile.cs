using Broadside.Objects;

namespace Broadside;

/// <summary>Where an associated file was found: the kind of object whose <c>AF</c> entry (or marked-content property list) lists it.</summary>
/// <remarks>ISO 32000-2 §14.13.3-§14.13.9 and PDF 2.0 Application Note 002 §3.1 and §6.2 (the list of locations is open).</remarks>
public enum PdfAssociatedFileLocation
{
    /// <summary>The document catalog: files associated with the whole document (§14.13.3).</summary>
    Catalog,

    /// <summary>A page (§14.13.4).</summary>
    Page,

    /// <summary>A form XObject (§14.13.7), including an annotation's appearance stream.</summary>
    FormXObject,

    /// <summary>An image XObject (§14.13.7).</summary>
    ImageXObject,

    /// <summary>An annotation (§14.13.9), including widgets and merged fields.</summary>
    Annotation,

    /// <summary>The structure tree root (Table 354).</summary>
    StructureTreeRoot,

    /// <summary>A structure element (§14.13.6).</summary>
    StructureElement,

    /// <summary>A document part (§14.13.8).</summary>
    DocumentPart,

    /// <summary>A metadata stream (§14.13.1).</summary>
    MetadataStream,

    /// <summary>A marked-content sequence: a property list in a <c>Properties</c> resource (§14.13.5, errata Table 409a).</summary>
    MarkedContent,

    /// <summary>Any other object: found by a deep scan, or an object kind the walk does not classify (Application Note 002 §3.2).</summary>
    Other,
}

/// <summary>An associated file together with the object it is associated with.</summary>
/// <remarks>ISO 32000-2 §14.13 (PDF 2.0) and PDF 2.0 Application Note 002.</remarks>
public sealed class PdfAssociatedFile
{
    internal PdfAssociatedFile(PdfFileSpecification file, PdfAssociatedFileLocation location, CosObject owner, CosReference? ownerReference, int? pageIndex, string? propertyName)
    {
        File = file;
        Location = location;
        Owner = owner;
        OwnerReference = ownerReference;
        PageIndex = pageIndex;
        PropertyName = propertyName;
    }

    /// <summary>Gets the file specification of the associated file.</summary>
    /// <remarks>ISO 32000-2 §14.13.2 and §7.11.3.</remarks>
    public PdfFileSpecification File { get; }

    /// <summary>Gets the kind of object the file is associated with.</summary>
    public PdfAssociatedFileLocation Location { get; }

    /// <summary>Gets the object whose <c>AF</c> entry lists the file: a dictionary or a stream, or for marked content the property list (a dictionary or an array).</summary>
    public CosObject Owner { get; }

    /// <summary>Gets the indirect reference to <see cref="Owner"/>, when it has one.</summary>
    public CosReference? OwnerReference { get; }

    /// <summary>Gets the index of the page the owner was found under, or <see langword="null"/> for document-level objects.</summary>
    public int? PageIndex { get; }

    /// <summary>Gets, for <see cref="PdfAssociatedFileLocation.MarkedContent"/>, the property list's name in the <c>Properties</c> resource.</summary>
    /// <remarks>ISO 32000-2 §14.13.5: the name a <c>/AF /name BDC</c> operator uses.</remarks>
    public string? PropertyName { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Location}: {File.FileName}";
}
