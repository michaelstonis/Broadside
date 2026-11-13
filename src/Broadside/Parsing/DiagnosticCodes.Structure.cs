using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes of the logical structure and tagged PDF views (ISO 32000-2 §14.6-§14.8, issue #75).</summary>
internal static partial class DiagnosticCodes
{
    public static string MarkInfoInvalid => StructureDiagnostics.MarkInfoInvalid.Name;
    public static string StructTreeRootInvalid => StructureDiagnostics.StructTreeRootInvalid.Name;
    public static string StructElemInvalid => StructureDiagnostics.StructElemInvalid.Name;
    public static string StructElemTypeUnknown => StructureDiagnostics.StructElemTypeUnknown.Name;
    public static string StructElemParentMismatch => StructureDiagnostics.StructElemParentMismatch.Name;
    public static string StructElemPageMissing => StructureDiagnostics.StructElemPageMissing.Name;
    public static string StructTreeCycle => StructureDiagnostics.StructTreeCycle.Name;
    public static string StructTreeDepthExceeded => StructureDiagnostics.StructTreeDepthExceeded.Name;
    public static string ParentTreeMissing => StructureDiagnostics.ParentTreeMissing.Name;
    public static string ParentTreeEntryInvalid => StructureDiagnostics.ParentTreeEntryInvalid.Name;
    public static string McidDuplicate => StructureDiagnostics.McidDuplicate.Name;
    public static string IdTreeDuplicate => StructureDiagnostics.IdTreeDuplicate.Name;
    public static string IdTreeEntryInvalid => StructureDiagnostics.IdTreeEntryInvalid.Name;
    public static string NamespaceInvalid => StructureDiagnostics.NamespaceInvalid.Name;
    public static string NamespaceNotDeclared => StructureDiagnostics.NamespaceNotDeclared.Name;
    public static string StructureTypeUnresolved => StructureDiagnostics.StructureTypeUnresolved.Name;
    public static string AttributeObjectInvalid => StructureDiagnostics.AttributeObjectInvalid.Name;
    public static string AttributeOwnerInvalid => StructureDiagnostics.AttributeOwnerInvalid.Name;
    public static string AttributeRevisionInvalid => StructureDiagnostics.AttributeRevisionInvalid.Name;

    // Names of appended pages' structure renamed because the document uses them otherwise (issue #161; Information).
    public static string StructureRoleConflict => StructureDiagnostics.StructureRoleConflict.Name;
    public static string StructureClassConflict => StructureDiagnostics.StructureClassConflict.Name;
    public static string StructureIdConflict => StructureDiagnostics.StructureIdConflict.Name;

    // Auto-tagging (issue #191; Warning): a detected figure was given the placeholder alternate description.
    public static string FigureAlternateTextPlaceholder => StructureDiagnostics.FigureAlternateTextPlaceholder.Name;
}
