namespace Broadside.Parsing;

/// <summary>Diagnostic codes for name trees, number trees, destinations, outlines and actions (issue #70).</summary>
internal static partial class DiagnosticCodes
{
    // Name dictionary (§7.7.4).
    public const string NameDictionaryInvalid = nameof(NameDictionaryInvalid);

    // Name trees (§7.9.6, Table 36).
    public const string NameTreeNodeInvalid = nameof(NameTreeNodeInvalid);
    public const string NameTreeLimitsInvalid = nameof(NameTreeLimitsInvalid);
    public const string NameTreeKeysUnsorted = nameof(NameTreeKeysUnsorted);
    public const string NameTreeDuplicateKey = nameof(NameTreeDuplicateKey);
    public const string NameTreeKeyInvalid = nameof(NameTreeKeyInvalid);
    public const string NameTreeCycle = nameof(NameTreeCycle);
    public const string NameTreeTooDeep = nameof(NameTreeTooDeep);

    // Number trees (§7.9.7, Table 37).
    public const string NumberTreeNodeInvalid = nameof(NumberTreeNodeInvalid);
    public const string NumberTreeLimitsInvalid = nameof(NumberTreeLimitsInvalid);
    public const string NumberTreeKeysUnsorted = nameof(NumberTreeKeysUnsorted);
    public const string NumberTreeDuplicateKey = nameof(NumberTreeDuplicateKey);
    public const string NumberTreeKeyInvalid = nameof(NumberTreeKeyInvalid);
    public const string NumberTreeCycle = nameof(NumberTreeCycle);
    public const string NumberTreeTooDeep = nameof(NumberTreeTooDeep);

    // Actions (§12.6.2, Table 196).
    public const string ActionInvalid = nameof(ActionInvalid);
}
