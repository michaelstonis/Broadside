namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about name trees, number trees, destinations and the document outline.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.9.6, §7.9.7, §12.3. Each code is reported under <see cref="DiagnosticArea.Navigation"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class NavigationDiagnostics
{
    /// <summary>Gets the <c>NameDictionaryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.4</remarks>
    public static DiagnosticCode NameDictionaryInvalid { get; } = new(nameof(NameDictionaryInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.4",
        "The catalog Names entry is not a dictionary; the document is read as having no name dictionary.");

    /// <summary>Gets the <c>NameTreeNodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeNodeInvalid { get; } = new(nameof(NameTreeNodeInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.6 Table 36",
        "A name tree node is not a dictionary or has no usable Kids or Names entry; the node is skipped.");

    /// <summary>Gets the <c>NameTreeLimitsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeLimitsInvalid { get; } = new(nameof(NameTreeLimitsInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.6 Table 36",
        "A name tree node has a Limits entry that is missing or does not match its keys; the node contents are used regardless.");

    /// <summary>Gets the <c>NameTreeKeysUnsorted</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeKeysUnsorted { get; } = new(nameof(NameTreeKeysUnsorted), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.6 Table 36",
        "The keys in a name tree are not in lexical order; the entries are still read and looked up.");

    /// <summary>Gets the <c>NameTreeDuplicateKey</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeDuplicateKey { get; } = new(nameof(NameTreeDuplicateKey), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.6 Table 36",
        "A name tree holds the same key more than once; the first entry found is used.");

    /// <summary>Gets the <c>NameTreeKeyInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeKeyInvalid { get; } = new(nameof(NameTreeKeyInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.6 Table 36",
        "A name tree key is not a string or its value is missing; the pair is skipped.");

    /// <summary>Gets the <c>NameTreeCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeCycle { get; } = new(nameof(NameTreeCycle), DiagnosticArea.Navigation, DiagnosticSeverity.Error, "ISO 32000-2 §7.9.6 Table 36",
        "A name tree node is reachable from itself through Kids; the repeated node is skipped.");

    /// <summary>Gets the <c>NameTreeTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.6 Table 36</remarks>
    public static DiagnosticCode NameTreeTooDeep { get; } = new(nameof(NameTreeTooDeep), DiagnosticArea.Navigation, DiagnosticSeverity.Error, "ISO 32000-2 §7.9.6 Table 36",
        "A name tree nests deeper than the supported maximum depth; the deeper nodes are skipped.");

    /// <summary>Gets the <c>NumberTreeNodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeNodeInvalid { get; } = new(nameof(NumberTreeNodeInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.7 Table 37",
        "A number tree node is not a dictionary or has no usable Kids or Nums entry; the node is skipped.");

    /// <summary>Gets the <c>NumberTreeLimitsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeLimitsInvalid { get; } = new(nameof(NumberTreeLimitsInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.7 Table 37",
        "A number tree node has a Limits entry that is missing or does not match its keys; the node contents are used regardless.");

    /// <summary>Gets the <c>NumberTreeKeysUnsorted</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeKeysUnsorted { get; } = new(nameof(NumberTreeKeysUnsorted), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.7 Table 37",
        "The keys in a number tree are not in ascending order; the entries are still read and looked up.");

    /// <summary>Gets the <c>NumberTreeDuplicateKey</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeDuplicateKey { get; } = new(nameof(NumberTreeDuplicateKey), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.7 Table 37",
        "A number tree holds the same key more than once; the first entry found is used.");

    /// <summary>Gets the <c>NumberTreeKeyInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeKeyInvalid { get; } = new(nameof(NumberTreeKeyInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.7 Table 37",
        "A number tree key is not an integer or its value is missing; the pair is skipped.");

    /// <summary>Gets the <c>NumberTreeCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeCycle { get; } = new(nameof(NumberTreeCycle), DiagnosticArea.Navigation, DiagnosticSeverity.Error, "ISO 32000-2 §7.9.7 Table 37",
        "A number tree node is reachable from itself through Kids; the repeated node is skipped.");

    /// <summary>Gets the <c>NumberTreeTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.7 Table 37</remarks>
    public static DiagnosticCode NumberTreeTooDeep { get; } = new(nameof(NumberTreeTooDeep), DiagnosticArea.Navigation, DiagnosticSeverity.Error, "ISO 32000-2 §7.9.7 Table 37",
        "A number tree nests deeper than the supported maximum depth; the deeper nodes are skipped.");

    /// <summary>Gets the <c>DestinationInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.2</remarks>
    public static DiagnosticCode DestinationInvalid { get; } = new(nameof(DestinationInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.2",
        "A destination is not a valid array, name or string, or a destination entry has the wrong form; it is ignored or read as the form it most resembles, and shows no page.");

    /// <summary>Gets the <c>DestinationPageNotFound</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.2</remarks>
    public static DiagnosticCode DestinationPageNotFound { get; } = new(nameof(DestinationPageNotFound), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.2",
        "An explicit destination refers to a page that cannot be found in the document; the destination shows no page.");

    /// <summary>Gets the <c>NamedDestinationNotFound</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.2</remarks>
    public static DiagnosticCode NamedDestinationNotFound { get; } = new(nameof(NamedDestinationNotFound), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.2",
        "A reference uses a named destination that the document does not define; the reference shows no page.");

    /// <summary>Gets the <c>DestinationPageRemoved</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.2</remarks>
    public static DiagnosticCode DestinationPageRemoved { get; } = new(nameof(DestinationPageRemoved), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.2",
        "A destination in an outline item, link or named destination shows a page that was removed from the document; it is saved showing no page.");

    /// <summary>Gets the <c>OutlineInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineInvalid { get; } = new(nameof(OutlineInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.3",
        "The catalog Outlines entry is not a dictionary; the document is read as having no outline.");

    /// <summary>Gets the <c>OutlineItemInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineItemInvalid { get; } = new(nameof(OutlineItemInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.3",
        "An outline item is not a dictionary, has no Title text string, or has a malformed color or flags entry; the item level ends or defaults are used.");

    /// <summary>Gets the <c>OutlineCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineCycle { get; } = new(nameof(OutlineCycle), DiagnosticArea.Navigation, DiagnosticSeverity.Error, "ISO 32000-2 §12.3.3",
        "An outline item is reached a second time through Next or First; reading of that level stops there.");

    /// <summary>Gets the <c>OutlineTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineTooDeep { get; } = new(nameof(OutlineTooDeep), DiagnosticArea.Navigation, DiagnosticSeverity.Error, "ISO 32000-2 §12.3.3",
        "The outline nests deeper than the supported maximum depth; the deeper items are skipped.");

    /// <summary>Gets the <c>OutlineLinkInconsistent</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineLinkInconsistent { get; } = new(nameof(OutlineLinkInconsistent), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.3",
        "The Last, Parent or Prev link of an outline item does not agree with the First and Next chain; the First and Next chain is used.");

    /// <summary>Gets the <c>OutlineCountInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineCountInvalid { get; } = new(nameof(OutlineCountInvalid), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.3",
        "An outline or outline item Count is negative, not an integer or missing where children exist; the item is read as closed.");

    /// <summary>Gets the <c>OutlineDestAndAction</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.3.3</remarks>
    public static DiagnosticCode OutlineDestAndAction { get; } = new(nameof(OutlineDestAndAction), DiagnosticArea.Navigation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.3.3",
        "An outline item has both a Dest and an A entry; both are read and the action is the one performed.");

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
