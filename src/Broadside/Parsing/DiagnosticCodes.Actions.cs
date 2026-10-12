namespace Broadside.Parsing;

/// <summary>Diagnostic codes for actions, trigger events and their catalog entries (issue #73).</summary>
internal static partial class DiagnosticCodes
{
    // Action dictionaries (§12.6.2, Table 196) and the per-type entries (§12.6.4, §12.7.6).
    public const string ActionEntryInvalid = nameof(ActionEntryInvalid);
    public const string ActionNextInvalid = nameof(ActionNextInvalid);
    public const string ActionCycle = nameof(ActionCycle);
    public const string ActionTreeTooDeep = nameof(ActionTreeTooDeep);
    public const string ActionTargetCycle = nameof(ActionTargetCycle);
    public const string ActionOutOfScope = nameof(ActionOutOfScope);
    public const string UriInvalid = nameof(UriInvalid);

    // Trigger events (§12.6.3) and the catalog's OpenAction and URI entries (§7.7.2, Table 29).
    public const string AdditionalActionsInvalid = nameof(AdditionalActionsInvalid);
    public const string OpenActionInvalid = nameof(OpenActionInvalid);
}
