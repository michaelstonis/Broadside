namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the page tree and page objects.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.7.3. Each code is reported under <see cref="DiagnosticArea.Page"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class PageDiagnostics
{
    /// <summary>Gets the <c>PageTreeNodeTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeNodeTypeInvalid { get; } = new(nameof(PageTreeNodeTypeInvalid), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3");

    /// <summary>Gets the <c>PageTreeKidsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeKidsInvalid { get; } = new(nameof(PageTreeKidsInvalid), DiagnosticArea.Page, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.3");

    /// <summary>Gets the <c>PageTreeKidInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeKidInvalid { get; } = new(nameof(PageTreeKidInvalid), DiagnosticArea.Page, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.3");

    /// <summary>Gets the <c>PageTreeKidNotIndirect</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeKidNotIndirect { get; } = new(nameof(PageTreeKidNotIndirect), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3");

    /// <summary>Gets the <c>PageTreeCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeCycle { get; } = new(nameof(PageTreeCycle), DiagnosticArea.Page, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.3");

    /// <summary>Gets the <c>PageTreeTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeTooDeep { get; } = new(nameof(PageTreeTooDeep), DiagnosticArea.Page, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.3");

    /// <summary>Gets the <c>PageTreeParentMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeParentMismatch { get; } = new(nameof(PageTreeParentMismatch), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3");

    /// <summary>Gets the <c>PageTreeCountMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3</remarks>
    public static DiagnosticCode PageTreeCountMismatch { get; } = new(nameof(PageTreeCountMismatch), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3");

    /// <summary>Gets the <c>PageMediaBoxMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, §7.9.5</remarks>
    public static DiagnosticCode PageMediaBoxMissing { get; } = new(nameof(PageMediaBoxMissing), DiagnosticArea.Page, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.3.3, §7.9.5");

    /// <summary>Gets the <c>PageBoxInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, §7.9.5</remarks>
    public static DiagnosticCode PageBoxInvalid { get; } = new(nameof(PageBoxInvalid), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3, §7.9.5");

    /// <summary>Gets the <c>PageRotateInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, §7.9.5</remarks>
    public static DiagnosticCode PageRotateInvalid { get; } = new(nameof(PageRotateInvalid), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3, §7.9.5");

    /// <summary>Gets the <c>PageResourcesMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, §7.9.5</remarks>
    public static DiagnosticCode PageResourcesMissing { get; } = new(nameof(PageResourcesMissing), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3, §7.9.5");

    /// <summary>Gets the <c>PageResourcesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, §7.9.5</remarks>
    public static DiagnosticCode PageResourcesInvalid { get; } = new(nameof(PageResourcesInvalid), DiagnosticArea.Page, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.3.3, §7.9.5");

    /// <summary>Gets the <c>PageUserUnitInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, §7.9.5</remarks>
    public static DiagnosticCode PageUserUnitInvalid { get; } = new(nameof(PageUserUnitInvalid), DiagnosticArea.Page, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3, §7.9.5");

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
