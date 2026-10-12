namespace Broadside;

/// <summary>What the first element of an explicit destination array designates.</summary>
/// <remarks>ISO 32000-2 §12.3.2.2 (page object or remote page number) and §12.3.2.3 (structure element or remote structure element ID).</remarks>
public enum PdfDestinationTarget
{
    /// <summary>The first element is missing or of no type a destination allows.</summary>
    None = 0,

    /// <summary>A page object of this document (an indirect reference), possibly one the page tree does not hold.</summary>
    Page,

    /// <summary>A 0-based page number: in another document for a remote destination (§12.6.4.3), or a producer's error in a local one.</summary>
    PageNumber,

    /// <summary>A structure element dictionary of this document: a structure destination (§12.3.2.3, PDF 2.0).</summary>
    StructureElement,

    /// <summary>A byte string naming a structure element by its ID in another document: a remote structure destination (§12.3.2.3).</summary>
    StructureElementId,
}
