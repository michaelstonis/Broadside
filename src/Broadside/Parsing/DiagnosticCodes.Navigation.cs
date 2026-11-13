using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for name trees, number trees, destinations, outlines and actions (issue #70).</summary>
internal static partial class DiagnosticCodes
{
    // Name dictionary (§7.7.4).
    public static string NameDictionaryInvalid => NavigationDiagnostics.NameDictionaryInvalid.Name;

    // Name trees (§7.9.6, Table 36).
    public static string NameTreeNodeInvalid => NavigationDiagnostics.NameTreeNodeInvalid.Name;
    public static string NameTreeLimitsInvalid => NavigationDiagnostics.NameTreeLimitsInvalid.Name;
    public static string NameTreeKeysUnsorted => NavigationDiagnostics.NameTreeKeysUnsorted.Name;
    public static string NameTreeDuplicateKey => NavigationDiagnostics.NameTreeDuplicateKey.Name;
    public static string NameTreeKeyInvalid => NavigationDiagnostics.NameTreeKeyInvalid.Name;
    public static string NameTreeCycle => NavigationDiagnostics.NameTreeCycle.Name;
    public static string NameTreeTooDeep => NavigationDiagnostics.NameTreeTooDeep.Name;

    // Number trees (§7.9.7, Table 37).
    public static string NumberTreeNodeInvalid => NavigationDiagnostics.NumberTreeNodeInvalid.Name;
    public static string NumberTreeLimitsInvalid => NavigationDiagnostics.NumberTreeLimitsInvalid.Name;
    public static string NumberTreeKeysUnsorted => NavigationDiagnostics.NumberTreeKeysUnsorted.Name;
    public static string NumberTreeDuplicateKey => NavigationDiagnostics.NumberTreeDuplicateKey.Name;
    public static string NumberTreeKeyInvalid => NavigationDiagnostics.NumberTreeKeyInvalid.Name;
    public static string NumberTreeCycle => NavigationDiagnostics.NumberTreeCycle.Name;
    public static string NumberTreeTooDeep => NavigationDiagnostics.NumberTreeTooDeep.Name;

    // Destinations (§12.3.2).
    public static string DestinationInvalid => NavigationDiagnostics.DestinationInvalid.Name;
    public static string DestinationPageNotFound => NavigationDiagnostics.DestinationPageNotFound.Name;
    public static string NamedDestinationNotFound => NavigationDiagnostics.NamedDestinationNotFound.Name;

    // A destination to a page removed through the page editing API, reported on save (issue #158).
    public static string DestinationPageRemoved => NavigationDiagnostics.DestinationPageRemoved.Name;

    // Document outline (§12.3.3).
    public static string OutlineInvalid => NavigationDiagnostics.OutlineInvalid.Name;
    public static string OutlineItemInvalid => NavigationDiagnostics.OutlineItemInvalid.Name;
    public static string OutlineCycle => NavigationDiagnostics.OutlineCycle.Name;
    public static string OutlineTooDeep => NavigationDiagnostics.OutlineTooDeep.Name;
    public static string OutlineLinkInconsistent => NavigationDiagnostics.OutlineLinkInconsistent.Name;
    public static string OutlineCountInvalid => NavigationDiagnostics.OutlineCountInvalid.Name;
    public static string OutlineDestAndAction => NavigationDiagnostics.OutlineDestAndAction.Name;

    // Actions (§12.6.2, Table 196).
    public static string ActionInvalid => ActionDiagnostics.ActionInvalid.Name;
}
