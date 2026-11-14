namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the logical structure and tagged PDF.</summary>
/// <remarks>
/// <para>ISO 32000-2 §14.6 to §14.9. Each code is reported under <see cref="DiagnosticArea.Structure"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class StructureDiagnostics
{
    /// <summary>Gets the <c>MarkInfoInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode MarkInfoInvalid { get; } = new(nameof(MarkInfoInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>StructTreeRootInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructTreeRootInvalid { get; } = new(nameof(StructTreeRootInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>StructElemInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructElemInvalid { get; } = new(nameof(StructElemInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>StructElemTypeUnknown</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructElemTypeUnknown { get; } = new(nameof(StructElemTypeUnknown), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>StructElemParentMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructElemParentMismatch { get; } = new(nameof(StructElemParentMismatch), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>StructElemPageMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructElemPageMissing { get; } = new(nameof(StructElemPageMissing), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>StructTreeCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructTreeCycle { get; } = new(nameof(StructTreeCycle), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>StructTreeDepthExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructTreeDepthExceeded { get; } = new(nameof(StructTreeDepthExceeded), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>ParentTreeMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode ParentTreeMissing { get; } = new(nameof(ParentTreeMissing), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>ParentTreeEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode ParentTreeEntryInvalid { get; } = new(nameof(ParentTreeEntryInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>McidDuplicate</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode McidDuplicate { get; } = new(nameof(McidDuplicate), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>IdTreeDuplicate</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode IdTreeDuplicate { get; } = new(nameof(IdTreeDuplicate), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>IdTreeEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode IdTreeEntryInvalid { get; } = new(nameof(IdTreeEntryInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>NamespaceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode NamespaceInvalid { get; } = new(nameof(NamespaceInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>NamespaceNotDeclared</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode NamespaceNotDeclared { get; } = new(nameof(NamespaceNotDeclared), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>StructureTypeUnresolved</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructureTypeUnresolved { get; } = new(nameof(StructureTypeUnresolved), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>AttributeObjectInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode AttributeObjectInvalid { get; } = new(nameof(AttributeObjectInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>AttributeOwnerInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode AttributeOwnerInvalid { get; } = new(nameof(AttributeOwnerInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>AttributeRevisionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode AttributeRevisionInvalid { get; } = new(nameof(AttributeRevisionInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8");

    /// <summary>Gets the <c>StructureRoleConflict</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.7</remarks>
    public static DiagnosticCode StructureRoleConflict { get; } = new(nameof(StructureRoleConflict), DiagnosticArea.Structure, DiagnosticSeverity.Information, "ISO 32000-2 §14.7");

    /// <summary>Gets the <c>StructureClassConflict</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.7</remarks>
    public static DiagnosticCode StructureClassConflict { get; } = new(nameof(StructureClassConflict), DiagnosticArea.Structure, DiagnosticSeverity.Information, "ISO 32000-2 §14.7");

    /// <summary>Gets the <c>StructureIdConflict</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.7</remarks>
    public static DiagnosticCode StructureIdConflict { get; } = new(nameof(StructureIdConflict), DiagnosticArea.Structure, DiagnosticSeverity.Information, "ISO 32000-2 §14.7");

    /// <summary>Gets the <c>FigureAlternateTextPlaceholder</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.9.3</remarks>
    public static DiagnosticCode FigureAlternateTextPlaceholder { get; } = new(nameof(FigureAlternateTextPlaceholder), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.9.3");

    /// <summary>Gets the <c>FigureWithoutAlternateText</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.9.3</remarks>
    public static DiagnosticCode FigureWithoutAlternateText { get; } = new(nameof(FigureWithoutAlternateText), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.9.3");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        MarkInfoInvalid,
        StructTreeRootInvalid,
        StructElemInvalid,
        StructElemTypeUnknown,
        StructElemParentMismatch,
        StructElemPageMissing,
        StructTreeCycle,
        StructTreeDepthExceeded,
        ParentTreeMissing,
        ParentTreeEntryInvalid,
        McidDuplicate,
        IdTreeDuplicate,
        IdTreeEntryInvalid,
        NamespaceInvalid,
        NamespaceNotDeclared,
        StructureTypeUnresolved,
        AttributeObjectInvalid,
        AttributeOwnerInvalid,
        AttributeRevisionInvalid,
        StructureRoleConflict,
        StructureClassConflict,
        StructureIdConflict,
        FigureAlternateTextPlaceholder,
        FigureWithoutAlternateText,
    ];
}
