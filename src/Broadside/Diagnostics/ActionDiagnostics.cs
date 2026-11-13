namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about actions and trigger events.</summary>
/// <remarks>
/// <para>ISO 32000-2 §12.6. Each code is reported under <see cref="DiagnosticArea.Action"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class ActionDiagnostics
{
    /// <summary>Gets the <c>ActionEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode ActionEntryInvalid { get; } = new(nameof(ActionEntryInvalid), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6");

    /// <summary>Gets the <c>ActionNextInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode ActionNextInvalid { get; } = new(nameof(ActionNextInvalid), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6");

    /// <summary>Gets the <c>ActionCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode ActionCycle { get; } = new(nameof(ActionCycle), DiagnosticArea.Action, DiagnosticSeverity.Information, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6");

    /// <summary>Gets the <c>ActionTreeTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode ActionTreeTooDeep { get; } = new(nameof(ActionTreeTooDeep), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6");

    /// <summary>Gets the <c>ActionTargetCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode ActionTargetCycle { get; } = new(nameof(ActionTargetCycle), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6");

    /// <summary>Gets the <c>ActionOutOfScope</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode ActionOutOfScope { get; } = new(nameof(ActionOutOfScope), DiagnosticArea.Action, DiagnosticSeverity.Information, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6");

    /// <summary>Gets the <c>UriInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode UriInvalid { get; } = new(nameof(UriInvalid), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6");

    /// <summary>Gets the <c>AdditionalActionsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.3, §7.7.2 Table 29</remarks>
    public static DiagnosticCode AdditionalActionsInvalid { get; } = new(nameof(AdditionalActionsInvalid), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.3, §7.7.2 Table 29");

    /// <summary>Gets the <c>OpenActionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.3, §7.7.2 Table 29</remarks>
    public static DiagnosticCode OpenActionInvalid { get; } = new(nameof(OpenActionInvalid), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.3, §7.7.2 Table 29");

    /// <summary>Gets the <c>ActionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196</remarks>
    public static DiagnosticCode ActionInvalid { get; } = new(nameof(ActionInvalid), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.2 Table 196");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        ActionEntryInvalid,
        ActionNextInvalid,
        ActionCycle,
        ActionTreeTooDeep,
        ActionTargetCycle,
        ActionOutOfScope,
        UriInvalid,
        AdditionalActionsInvalid,
        OpenActionInvalid,
        ActionInvalid,
    ];
}
