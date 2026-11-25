namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the page tree and page objects.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.7.3. Each code is reported under <see cref="DiagnosticArea.Page"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class PageDiagnostics
{
    /// <summary>Gets the <c>PageTreeNodeTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeNodeTypeInvalid { get; } = new(nameof(PageTreeNodeTypeInvalid), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3",
        "A page tree node has a Type other than Pages but has a Kids array, so it is read as an intermediate node of the tree.");

    /// <summary>Gets the <c>PageTreeKidsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeKidsInvalid { get; } = new(nameof(PageTreeKidsInvalid), DiagnosticArea.Page, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.3",
        "A page tree node has no Kids array or its Kids entry is not an array, so the node contributes no pages.");

    /// <summary>Gets the <c>PageTreeKidInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeKidInvalid { get; } = new(nameof(PageTreeKidInvalid), DiagnosticArea.Page, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.3",
        "An entry in a Kids array does not resolve to a page tree node or a page object, so the entry is skipped.");

    /// <summary>Gets the <c>PageTreeKidNotIndirect</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeKidNotIndirect { get; } = new(nameof(PageTreeKidNotIndirect), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3",
        "An entry in a Kids array is a direct dictionary instead of an indirect reference; it is accepted and used as is.");

    /// <summary>Gets the <c>PageTreeCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeCycle { get; } = new(nameof(PageTreeCycle), DiagnosticArea.Page, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.3",
        "A page tree node or page is reachable more than once from the page tree, forming a repeat or loop; the repeated occurrence is skipped.");

    /// <summary>Gets the <c>PageTreeTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeTooDeep { get; } = new(nameof(PageTreeTooDeep), DiagnosticArea.Page, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.3",
        "The page tree nests deeper than the supported maximum depth; nodes below that depth are skipped.");

    /// <summary>Gets the <c>PageTreeParentMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeParentMismatch { get; } = new(nameof(PageTreeParentMismatch), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3",
        "A Parent entry in the page tree does not point to the node that lists the page or node in its Kids; inheritance follows the actual tree structure.");

    /// <summary>Gets the <c>PageTreeCountMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeCountMismatch { get; } = new(nameof(PageTreeCountMismatch), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3",
        "The Count of the page tree root differs from the number of pages found by walking the tree; the walked page count is used.");

    /// <summary>Gets the <c>PageMediaBoxMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, §7.9.5</remarks>
    public static DiagnosticCode PageMediaBoxMissing { get; } = new(nameof(PageMediaBoxMissing), DiagnosticArea.Page, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.3.3, §7.9.5",
        "A page has no MediaBox on itself or on any ancestor node; the US Letter size of 612 by 792 points is used instead.");

    /// <summary>Gets the <c>PageBoxInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, §7.9.5</remarks>
    public static DiagnosticCode PageBoxInvalid { get; } = new(nameof(PageBoxInvalid), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3, §7.9.5",
        "A page boundary box such as CropBox, BleedBox, TrimBox or ArtBox is not a rectangle of four numbers; a fallback box is used instead.");

    /// <summary>Gets the <c>PageRotateInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, §7.9.5</remarks>
    public static DiagnosticCode PageRotateInvalid { get; } = new(nameof(PageRotateInvalid), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3, §7.9.5",
        "A page Rotate value is not an integer multiple of 90; a real number holding a multiple of 90 is accepted as an integer, and any other value is read as 0.");

    /// <summary>Gets the <c>PageResourcesMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, §7.9.5</remarks>
    public static DiagnosticCode PageResourcesMissing { get; } = new(nameof(PageResourcesMissing), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3, §7.9.5",
        "A page has no Resources dictionary on itself or on any ancestor node; the page is read as having no resources.");

    /// <summary>Gets the <c>PageResourcesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, §7.9.5</remarks>
    public static DiagnosticCode PageResourcesInvalid { get; } = new(nameof(PageResourcesInvalid), DiagnosticArea.Page, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.3.3, §7.9.5",
        "A page Resources entry is not a dictionary; the page is read as having no resources.");

    /// <summary>Gets the <c>PageUserUnitInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, §7.9.5</remarks>
    public static DiagnosticCode PageUserUnitInvalid { get; } = new(nameof(PageUserUnitInvalid), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3, §7.9.5",
        "A page UserUnit entry is not a positive number; the default user unit of 1.0 is used.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        PageTreeNodeTypeInvalid,
        PageTreeKidsInvalid,
        PageTreeKidInvalid,
        PageTreeKidNotIndirect,
        PageTreeCycle,
        PageTreeTooDeep,
        PageTreeParentMismatch,
        PageTreeCountMismatch,
        PageMediaBoxMissing,
        PageBoxInvalid,
        PageRotateInvalid,
        PageResourcesMissing,
        PageResourcesInvalid,
        PageUserUnitInvalid,
    ];
}
