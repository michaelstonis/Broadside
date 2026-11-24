namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about actions and trigger events.</summary>
/// <remarks>
/// <para>ISO 32000-2 §12.6. Each code is reported under <see cref="DiagnosticArea.Action"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class ActionDiagnostics
{
    /// <summary>Gets the <c>ActionEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode ActionEntryInvalid { get; } = new(nameof(ActionEntryInvalid), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6",
        "An action has an entry of the wrong type or a disallowed value, or a name dictionary JavaScript entry is not a JavaScript action; the entry or action is skipped or read leniently.");

    /// <summary>Gets the <c>ActionNextInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode ActionNextInvalid { get; } = new(nameof(ActionNextInvalid), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6",
        "An action Next entry is not an action dictionary or an array of them, or an array element is not an action; the invalid part is skipped.");

    /// <summary>Gets the <c>ActionCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode ActionCycle { get; } = new(nameof(ActionCycle), DiagnosticArea.Action, DiagnosticSeverity.Information, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6",
        "An action Next chain leads back to an action it already follows; the cycle is cut and the action is not repeated.");

    /// <summary>Gets the <c>ActionTreeTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode ActionTreeTooDeep { get; } = new(nameof(ActionTreeTooDeep), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6",
        "An action chain nests deeper than the supported number of Next levels; the actions below that depth are not read.");

    /// <summary>Gets the <c>ActionTargetCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode ActionTargetCycle { get; } = new(nameof(ActionTargetCycle), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6",
        "The target dictionaries of an embedded go-to action form a cycle or nest too deeply; the action is not performed.");

    /// <summary>Gets the <c>ActionOutOfScope</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode ActionOutOfScope { get; } = new(nameof(ActionOutOfScope), DiagnosticArea.Action, DiagnosticSeverity.Information, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6",
        "An action of a kind Broadside does not perform, such as JavaScript or multimedia, is kept as data and never executed. This records that the action was preserved.");

    /// <summary>Gets the <c>UriInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6</remarks>
    public static DiagnosticCode UriInvalid { get; } = new(nameof(UriInvalid), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.2 Table 196, §12.6.4, §12.7.6",
        "A URI action has a URI that is not valid UTF-8 or is a name rather than a string; it is read as UTF-16, PDFDocEncoding or as its bytes.");

    /// <summary>Gets the <c>AdditionalActionsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.3, §7.7.2 Table 29</remarks>
    public static DiagnosticCode AdditionalActionsInvalid { get; } = new(nameof(AdditionalActionsInvalid), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.3, §7.7.2 Table 29",
        "An AA entry is not an additional-actions dictionary; it is ignored.");

    /// <summary>Gets the <c>OpenActionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.3, §7.7.2 Table 29</remarks>
    public static DiagnosticCode OpenActionInvalid { get; } = new(nameof(OpenActionInvalid), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.3, §7.7.2 Table 29",
        "The catalog OpenAction is neither a destination array nor an action dictionary; a name or string is read as a named destination.");

    /// <summary>Gets the <c>ActionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.6.2 Table 196</remarks>
    public static DiagnosticCode ActionInvalid { get; } = new(nameof(ActionInvalid), DiagnosticArea.Action, DiagnosticSeverity.Warning, "ISO 32000-2 §12.6.2 Table 196",
        "An action dictionary is malformed, for example a required entry is missing or has the wrong type; the action is read as far as it is valid.");

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
