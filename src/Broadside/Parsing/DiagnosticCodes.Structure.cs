namespace Broadside.Parsing;

/// <summary>Diagnostic codes of the logical structure and tagged PDF views (ISO 32000-2 §14.6-§14.8, issue #75).</summary>
internal static partial class DiagnosticCodes
{
    public const string MarkInfoInvalid = nameof(MarkInfoInvalid);
    public const string StructTreeRootInvalid = nameof(StructTreeRootInvalid);
    public const string StructElemInvalid = nameof(StructElemInvalid);
    public const string StructElemTypeUnknown = nameof(StructElemTypeUnknown);
    public const string StructElemParentMismatch = nameof(StructElemParentMismatch);
    public const string StructElemPageMissing = nameof(StructElemPageMissing);
    public const string StructTreeCycle = nameof(StructTreeCycle);
    public const string StructTreeDepthExceeded = nameof(StructTreeDepthExceeded);
    public const string ParentTreeMissing = nameof(ParentTreeMissing);
    public const string ParentTreeEntryInvalid = nameof(ParentTreeEntryInvalid);
    public const string McidDuplicate = nameof(McidDuplicate);
    public const string IdTreeDuplicate = nameof(IdTreeDuplicate);
    public const string IdTreeEntryInvalid = nameof(IdTreeEntryInvalid);
    public const string NamespaceInvalid = nameof(NamespaceInvalid);
    public const string NamespaceNotDeclared = nameof(NamespaceNotDeclared);
    public const string StructureTypeUnresolved = nameof(StructureTypeUnresolved);
    public const string AttributeObjectInvalid = nameof(AttributeObjectInvalid);
    public const string AttributeOwnerInvalid = nameof(AttributeOwnerInvalid);
    public const string AttributeRevisionInvalid = nameof(AttributeRevisionInvalid);

    // Names of appended pages' structure renamed because the document uses them otherwise (issue #161; Information).
    public const string StructureRoleConflict = nameof(StructureRoleConflict);
    public const string StructureClassConflict = nameof(StructureClassConflict);
    public const string StructureIdConflict = nameof(StructureIdConflict);

    // Auto-tagging (issue #191; Warning): a detected figure was given the placeholder alternate description.
    public const string FigureAlternateTextPlaceholder = nameof(FigureAlternateTextPlaceholder);
}
