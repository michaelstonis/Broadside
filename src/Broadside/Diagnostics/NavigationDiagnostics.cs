namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about name trees, number trees, destinations and the document outline.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.9.6, §7.9.7, §12.3. Each code is reported under <see cref="DiagnosticArea.Navigation"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class NavigationDiagnostics
{
    /// <summary>Gets the <c>NameDictionaryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.4</remarks>
    public static DiagnosticCode NameDictionaryInvalid { get; } = new(nameof(NameDictionaryInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.4");

    /// <summary>Gets the <c>NameTreeNodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeNodeInvalid { get; } = new(nameof(NameTreeNodeInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.6 Table 36");

    /// <summary>Gets the <c>NameTreeLimitsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeLimitsInvalid { get; } = new(nameof(NameTreeLimitsInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.6 Table 36");

    /// <summary>Gets the <c>NameTreeKeysUnsorted</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeKeysUnsorted { get; } = new(nameof(NameTreeKeysUnsorted), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.6 Table 36");

    /// <summary>Gets the <c>NameTreeDuplicateKey</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeDuplicateKey { get; } = new(nameof(NameTreeDuplicateKey), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.6 Table 36");

    /// <summary>Gets the <c>NameTreeKeyInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeKeyInvalid { get; } = new(nameof(NameTreeKeyInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.6 Table 36");

    /// <summary>Gets the <c>NameTreeCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeCycle { get; } = new(nameof(NameTreeCycle), DiagnosticArea.Navigation, DiagnosticSeverity.Error, "ISO 32000-2 §7.9.6 Table 36");

    /// <summary>Gets the <c>NameTreeTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeTooDeep { get; } = new(nameof(NameTreeTooDeep), DiagnosticArea.Navigation, DiagnosticSeverity.Error, "ISO 32000-2 §7.9.6 Table 36");

    /// <summary>Gets the <c>NumberTreeNodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeNodeInvalid { get; } = new(nameof(NumberTreeNodeInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.7 Table 37");

    /// <summary>Gets the <c>NumberTreeLimitsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeLimitsInvalid { get; } = new(nameof(NumberTreeLimitsInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.7 Table 37");

    /// <summary>Gets the <c>NumberTreeKeysUnsorted</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeKeysUnsorted { get; } = new(nameof(NumberTreeKeysUnsorted), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.7 Table 37");

    /// <summary>Gets the <c>NumberTreeDuplicateKey</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeDuplicateKey { get; } = new(nameof(NumberTreeDuplicateKey), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.7 Table 37");

    /// <summary>Gets the <c>NumberTreeKeyInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeKeyInvalid { get; } = new(nameof(NumberTreeKeyInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.7 Table 37");

    /// <summary>Gets the <c>NumberTreeCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeCycle { get; } = new(nameof(NumberTreeCycle), DiagnosticArea.Navigation, DiagnosticSeverity.Error, "ISO 32000-2 §7.9.7 Table 37");

    /// <summary>Gets the <c>NumberTreeTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeTooDeep { get; } = new(nameof(NumberTreeTooDeep), DiagnosticArea.Navigation, DiagnosticSeverity.Error, "ISO 32000-2 §7.9.7 Table 37");

    /// <summary>Gets the <c>DestinationInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.2</remarks>
    public static DiagnosticCode DestinationInvalid { get; } = new(nameof(DestinationInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.2");

    /// <summary>Gets the <c>DestinationPageNotFound</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.2</remarks>
    public static DiagnosticCode DestinationPageNotFound { get; } = new(nameof(DestinationPageNotFound), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.2");

    /// <summary>Gets the <c>NamedDestinationNotFound</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.2</remarks>
    public static DiagnosticCode NamedDestinationNotFound { get; } = new(nameof(NamedDestinationNotFound), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.2");

    /// <summary>Gets the <c>DestinationPageRemoved</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.2</remarks>
    public static DiagnosticCode DestinationPageRemoved { get; } = new(nameof(DestinationPageRemoved), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.2");

    /// <summary>Gets the <c>OutlineInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineInvalid { get; } = new(nameof(OutlineInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.3");

    /// <summary>Gets the <c>OutlineItemInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineItemInvalid { get; } = new(nameof(OutlineItemInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.3");

    /// <summary>Gets the <c>OutlineCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineCycle { get; } = new(nameof(OutlineCycle), DiagnosticArea.Navigation, DiagnosticSeverity.Error, "ISO 32000-2 §12.3.3");

    /// <summary>Gets the <c>OutlineTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineTooDeep { get; } = new(nameof(OutlineTooDeep), DiagnosticArea.Navigation, DiagnosticSeverity.Error, "ISO 32000-2 §12.3.3");

    /// <summary>Gets the <c>OutlineLinkInconsistent</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineLinkInconsistent { get; } = new(nameof(OutlineLinkInconsistent), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.3");

    /// <summary>Gets the <c>OutlineCountInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineCountInvalid { get; } = new(nameof(OutlineCountInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.3");

    /// <summary>Gets the <c>OutlineDestAndAction</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineDestAndAction { get; } = new(nameof(OutlineDestAndAction), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.3");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        NameDictionaryInvalid,
        NameTreeNodeInvalid,
        NameTreeLimitsInvalid,
        NameTreeKeysUnsorted,
        NameTreeDuplicateKey,
        NameTreeKeyInvalid,
        NameTreeCycle,
        NameTreeTooDeep,
        NumberTreeNodeInvalid,
        NumberTreeLimitsInvalid,
        NumberTreeKeysUnsorted,
        NumberTreeDuplicateKey,
        NumberTreeKeyInvalid,
        NumberTreeCycle,
        NumberTreeTooDeep,
        DestinationInvalid,
        DestinationPageNotFound,
        NamedDestinationNotFound,
        DestinationPageRemoved,
        OutlineInvalid,
        OutlineItemInvalid,
        OutlineCycle,
        OutlineTooDeep,
        OutlineLinkInconsistent,
        OutlineCountInvalid,
        OutlineDestAndAction,
    ];
}
