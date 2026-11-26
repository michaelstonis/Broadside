namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the logical structure and tagged PDF.</summary>
/// <remarks>
/// <para>ISO 32000-2 §14.6 to §14.9. Each code is reported under <see cref="DiagnosticArea.Structure"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class StructureDiagnostics
{
    /// <summary>Gets the <c>MarkInfoInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode MarkInfoInvalid { get; } = new(nameof(MarkInfoInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "An entry of the MarkInfo dictionary is not a boolean; it is read as false.");

    /// <summary>Gets the <c>StructTreeRootInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructTreeRootInvalid { get; } = new(nameof(StructTreeRootInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "The structure tree root is not a dictionary, has a wrong Type, or its K holds something other than structure elements; the document is read without a tree or the entry is skipped or read as the root.");

    /// <summary>Gets the <c>StructElemInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructElemInvalid { get; } = new(nameof(StructElemInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "A K entry of a structure element is not a structure element, marked-content reference or object reference, or such a reference lacks its MCID or Obj; the entry is skipped.");

    /// <summary>Gets the <c>StructElemTypeUnknown</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructElemTypeUnknown { get; } = new(nameof(StructElemTypeUnknown), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "A K entry has a missing or unexpected Type. It is read as a structure element, marked-content reference or object reference from its other entries, or skipped when it fits none.");

    /// <summary>Gets the <c>StructElemParentMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructElemParentMismatch { get; } = new(nameof(StructElemParentMismatch), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "The P entry of a structure element is not the element or root whose K holds it; the K hierarchy is used instead.");

    /// <summary>Gets the <c>StructElemPageMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructElemPageMissing { get; } = new(nameof(StructElemPageMissing), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "A structure element holds marked-content IDs but has no Pg entry; the page of the nearest ancestor or the page found through the parent tree is used, or the page stays unknown.");

    /// <summary>Gets the <c>StructTreeCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructTreeCycle { get; } = new(nameof(StructTreeCycle), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "A structure element is reached twice through K, or its K holds itself or an ancestor, although the hierarchy should be a tree; the repeated occurrence is skipped.");

    /// <summary>Gets the <c>StructTreeDepthExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructTreeDepthExceeded { get; } = new(nameof(StructTreeDepthExceeded), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "The structure hierarchy nests deeper than the supported limit; the part below the limit is not read.");

    /// <summary>Gets the <c>ParentTreeMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode ParentTreeMissing { get; } = new(nameof(ParentTreeMissing), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "The structure tree root has no ParentTree although elements have content items; content is mapped to elements by walking K instead.");

    /// <summary>Gets the <c>ParentTreeEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode ParentTreeEntryInvalid { get; } = new(nameof(ParentTreeEntryInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "A parent tree entry is missing, is not an array of elements, or names an element whose K does not hold the content item; the element is found by searching K instead.");

    /// <summary>Gets the <c>McidDuplicate</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode McidDuplicate { get; } = new(nameof(McidDuplicate), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "One marked-content ID of a content stream is a content item of two structure elements, although IDs should be unique per stream; the first element is kept.");

    /// <summary>Gets the <c>IdTreeDuplicate</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode IdTreeDuplicate { get; } = new(nameof(IdTreeDuplicate), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "Two structure elements have the same ID although IDs should be unique in the document; lookups by ID find the first.");

    /// <summary>Gets the <c>IdTreeEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode IdTreeEntryInvalid { get; } = new(nameof(IdTreeEntryInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "The IDTree is missing an element ID or maps an ID to a different element; lookups find the element by searching K instead.");

    /// <summary>Gets the <c>NamespaceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode NamespaceInvalid { get; } = new(nameof(NamespaceInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "A structure namespace dictionary has no NS text string or a wrong Type; the name reads as empty or the dictionary is read as a namespace anyway.");

    /// <summary>Gets the <c>NamespaceNotDeclared</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode NamespaceNotDeclared { get; } = new(nameof(NamespaceNotDeclared), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "A structure element uses a namespace that is not in the Namespaces array of the structure tree root; it is used anyway.");

    /// <summary>Gets the <c>StructureTypeUnresolved</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode StructureTypeUnresolved { get; } = new(nameof(StructureTypeUnresolved), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "A structure type does not resolve to a standard structure type through the role maps; the type is kept as written.");

    /// <summary>Gets the <c>AttributeObjectInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode AttributeObjectInvalid { get; } = new(nameof(AttributeObjectInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "An A or C entry of a structure element holds something that is not an attribute object, revision number or class name; the entry or the element of the array is skipped or ignored.");

    /// <summary>Gets the <c>AttributeOwnerInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode AttributeOwnerInvalid { get; } = new(nameof(AttributeOwnerInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "An attribute object has no owner, is owned by NSO without an NS entry, or has NS but another owner; it is read as written.");

    /// <summary>Gets the <c>AttributeRevisionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6 to §14.8</remarks>
    public static DiagnosticCode AttributeRevisionInvalid { get; } = new(nameof(AttributeRevisionInvalid), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6 to §14.8",
        "A revision number in an A or C array does not follow an attribute object or class name; it is ignored.");

    /// <summary>Gets the <c>StructureRoleConflict</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.7</remarks>
    public static DiagnosticCode StructureRoleConflict { get; } = new(nameof(StructureRoleConflict), DiagnosticArea.Structure, DiagnosticSeverity.Information, "ISO 32000-2 §14.7",
        "While appending pages, the same structure type maps to different roles in the two role maps. This is recorded only; the appended elements use a renamed type.");

    /// <summary>Gets the <c>StructureClassConflict</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.7</remarks>
    public static DiagnosticCode StructureClassConflict { get; } = new(nameof(StructureClassConflict), DiagnosticArea.Structure, DiagnosticSeverity.Information, "ISO 32000-2 §14.7",
        "While appending pages, an attribute class has different attributes in the two class maps. This is recorded only; the appended elements use a renamed class.");

    /// <summary>Gets the <c>StructureIdConflict</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.7</remarks>
    public static DiagnosticCode StructureIdConflict { get; } = new(nameof(StructureIdConflict), DiagnosticArea.Structure, DiagnosticSeverity.Information, "ISO 32000-2 §14.7",
        "While appending pages, an appended structure element has an ID that already exists in the document. This is recorded only; the appended element gets a new ID.");

    /// <summary>Gets the <c>FigureAlternateTextPlaceholder</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.9.3</remarks>
    public static DiagnosticCode FigureAlternateTextPlaceholder { get; } = new(nameof(FigureAlternateTextPlaceholder), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.9.3",
        "Automatic tagging gave a figure placeholder alternate text; it should be replaced with a description of the figure.");

    /// <summary>Gets the <c>FigureWithoutAlternateText</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.9.3</remarks>
    public static DiagnosticCode FigureWithoutAlternateText { get; } = new(nameof(FigureWithoutAlternateText), DiagnosticArea.Structure, DiagnosticSeverity.Warning, "ISO 32000-2 §14.9.3",
        "A figure in the structure tree has no alternate text, so assistive technology has nothing to read for it.");

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
