namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the document catalog, page labels, document information and metadata.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.7.2, §7.9.4, §12.4.2, §14.3. Each code is reported under <see cref="DiagnosticArea.Document"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class DocumentDiagnostics
{
    /// <summary>Gets the <c>CatalogEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode CatalogEntryInvalid { get; } = new(nameof(CatalogEntryInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11");

    /// <summary>Gets the <c>PageLayoutInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode PageLayoutInvalid { get; } = new(nameof(PageLayoutInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11");

    /// <summary>Gets the <c>PageModeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode PageModeInvalid { get; } = new(nameof(PageModeInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11");

    /// <summary>Gets the <c>ViewerPreferencesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode ViewerPreferencesInvalid { get; } = new(nameof(ViewerPreferencesInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11");

    /// <summary>Gets the <c>ViewerPreferenceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode ViewerPreferenceInvalid { get; } = new(nameof(ViewerPreferenceInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11");

    /// <summary>Gets the <c>ExtensionsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode ExtensionsInvalid { get; } = new(nameof(ExtensionsInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11");

    /// <summary>Gets the <c>ExtensionsNotDirect</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode ExtensionsNotDirect { get; } = new(nameof(ExtensionsNotDirect), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11");

    /// <summary>Gets the <c>DeveloperExtensionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode DeveloperExtensionInvalid { get; } = new(nameof(DeveloperExtensionInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11");

    /// <summary>Gets the <c>DeveloperExtensionBaseVersionLater</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode DeveloperExtensionBaseVersionLater { get; } = new(nameof(DeveloperExtensionBaseVersionLater), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11");

    /// <summary>Gets the <c>RequirementInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11</remarks>
    public static DiagnosticCode RequirementInvalid { get; } = new(nameof(RequirementInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2 Table 29, §7.12, §12.2, §12.11");

    /// <summary>Gets the <c>OutputIntentInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.11.5</remarks>
    public static DiagnosticCode OutputIntentInvalid { get; } = new(nameof(OutputIntentInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §14.11.5");

    /// <summary>Gets the <c>PageLabelsMissingZeroKey</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.4.2</remarks>
    public static DiagnosticCode PageLabelsMissingZeroKey { get; } = new(nameof(PageLabelsMissingZeroKey), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §12.4.2");

    /// <summary>Gets the <c>PageLabelInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.4.2</remarks>
    public static DiagnosticCode PageLabelInvalid { get; } = new(nameof(PageLabelInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §12.4.2");

    /// <summary>Gets the <c>PageLabelStyleInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.4.2</remarks>
    public static DiagnosticCode PageLabelStyleInvalid { get; } = new(nameof(PageLabelStyleInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §12.4.2");

    /// <summary>Gets the <c>PageLabelStartInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.4.2</remarks>
    public static DiagnosticCode PageLabelStartInvalid { get; } = new(nameof(PageLabelStartInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §12.4.2");

    /// <summary>Gets the <c>PageLabelPrefixInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.4.2</remarks>
    public static DiagnosticCode PageLabelPrefixInvalid { get; } = new(nameof(PageLabelPrefixInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §12.4.2");

    /// <summary>Gets the <c>PageLabelTooLong</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.4.2</remarks>
    public static DiagnosticCode PageLabelTooLong { get; } = new(nameof(PageLabelTooLong), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §12.4.2");

    /// <summary>Gets the <c>InfoDictionaryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode InfoDictionaryInvalid { get; } = new(nameof(InfoDictionaryInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4");

    /// <summary>Gets the <c>InfoValueNotTextString</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode InfoValueNotTextString { get; } = new(nameof(InfoValueNotTextString), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4");

    /// <summary>Gets the <c>InfoTrappedInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode InfoTrappedInvalid { get; } = new(nameof(InfoTrappedInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4");

    /// <summary>Gets the <c>DateInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode DateInvalid { get; } = new(nameof(DateInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4");

    /// <summary>Gets the <c>DateUnreadable</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode DateUnreadable { get; } = new(nameof(DateUnreadable), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4");

    /// <summary>Gets the <c>MetadataStreamInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode MetadataStreamInvalid { get; } = new(nameof(MetadataStreamInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4");

    /// <summary>Gets the <c>MetadataStreamTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode MetadataStreamTypeInvalid { get; } = new(nameof(MetadataStreamTypeInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4");

    /// <summary>Gets the <c>XmpMalformed</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode XmpMalformed { get; } = new(nameof(XmpMalformed), DiagnosticArea.Document, DiagnosticSeverity.Error, "ISO 32000-2 §7.9.4, §14.3, §14.4");

    /// <summary>Gets the <c>XmpDtdProhibited</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode XmpDtdProhibited { get; } = new(nameof(XmpDtdProhibited), DiagnosticArea.Document, DiagnosticSeverity.Error, "ISO 32000-2 §7.9.4, §14.3, §14.4");

    /// <summary>Gets the <c>XmpLeadingJunk</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode XmpLeadingJunk { get; } = new(nameof(XmpLeadingJunk), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4");

    /// <summary>Gets the <c>XmpPropertyDuplicate</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode XmpPropertyDuplicate { get; } = new(nameof(XmpPropertyDuplicate), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4");

    /// <summary>Gets the <c>FileIdentifierInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.9.4, §14.3, §14.4</remarks>
    public static DiagnosticCode FileIdentifierInvalid { get; } = new(nameof(FileIdentifierInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.9.4, §14.3, §14.4");

    /// <summary>Gets the <c>CatalogTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2</remarks>
    public static DiagnosticCode CatalogTypeInvalid { get; } = new(nameof(CatalogTypeInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2");

    /// <summary>Gets the <c>CatalogVersionNotName</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2</remarks>
    public static DiagnosticCode CatalogVersionNotName { get; } = new(nameof(CatalogVersionNotName), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2");

    /// <summary>Gets the <c>CatalogVersionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2</remarks>
    public static DiagnosticCode CatalogVersionInvalid { get; } = new(nameof(CatalogVersionInvalid), DiagnosticArea.Document, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.2");

    /// <summary>Gets the <c>PagesMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.2</remarks>
    public static DiagnosticCode PagesMissing { get; } = new(nameof(PagesMissing), DiagnosticArea.Document, DiagnosticSeverity.Error, "ISO 32000-2 §7.7.2");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        CatalogEntryInvalid,
        PageLayoutInvalid,
        PageModeInvalid,
        ViewerPreferencesInvalid,
        ViewerPreferenceInvalid,
        ExtensionsInvalid,
        ExtensionsNotDirect,
        DeveloperExtensionInvalid,
        DeveloperExtensionBaseVersionLater,
        RequirementInvalid,
        OutputIntentInvalid,
        PageLabelsMissingZeroKey,
        PageLabelInvalid,
        PageLabelStyleInvalid,
        PageLabelStartInvalid,
        PageLabelPrefixInvalid,
        PageLabelTooLong,
        InfoDictionaryInvalid,
        InfoValueNotTextString,
        InfoTrappedInvalid,
        DateInvalid,
        DateUnreadable,
        MetadataStreamInvalid,
        MetadataStreamTypeInvalid,
        XmpMalformed,
        XmpDtdProhibited,
        XmpLeadingJunk,
        XmpPropertyDuplicate,
        FileIdentifierInvalid,
        CatalogTypeInvalid,
        CatalogVersionNotName,
        CatalogVersionInvalid,
        PagesMissing,
    ];
}
