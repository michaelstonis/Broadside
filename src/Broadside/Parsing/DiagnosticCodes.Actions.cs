using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for actions, trigger events and their catalog entries (issue #73).</summary>
internal static partial class DiagnosticCodes
{
    // Action dictionaries (§12.6.2, Table 196) and the per-type entries (§12.6.4, §12.7.6).
    public static string ActionEntryInvalid => ActionDiagnostics.ActionEntryInvalid.Name;
    public static string ActionNextInvalid => ActionDiagnostics.ActionNextInvalid.Name;
    public static string ActionCycle => ActionDiagnostics.ActionCycle.Name;
    public static string ActionTreeTooDeep => ActionDiagnostics.ActionTreeTooDeep.Name;
    public static string ActionTargetCycle => ActionDiagnostics.ActionTargetCycle.Name;
    public static string ActionOutOfScope => ActionDiagnostics.ActionOutOfScope.Name;
    public static string UriInvalid => ActionDiagnostics.UriInvalid.Name;

    // Trigger events (§12.6.3) and the catalog's OpenAction and URI entries (§7.7.2, Table 29).
    public static string AdditionalActionsInvalid => ActionDiagnostics.AdditionalActionsInvalid.Name;
    public static string OpenActionInvalid => ActionDiagnostics.OpenActionInvalid.Name;
}
