namespace Broadside.Parsing;

/// <content>Codes for the document-level catalog entries, page labels, information and metadata (issue #69).</content>
internal static partial class DiagnosticCodes
{
    // Document catalog entries (§7.7.2 Table 29, §7.12, §12.2, §12.11).
    public const string CatalogEntryInvalid = nameof(CatalogEntryInvalid);
    public const string PageLayoutInvalid = nameof(PageLayoutInvalid);
    public const string PageModeInvalid = nameof(PageModeInvalid);
    public const string ViewerPreferencesInvalid = nameof(ViewerPreferencesInvalid);
    public const string ViewerPreferenceInvalid = nameof(ViewerPreferenceInvalid);
    public const string ExtensionsInvalid = nameof(ExtensionsInvalid);
    public const string ExtensionsNotDirect = nameof(ExtensionsNotDirect);
    public const string DeveloperExtensionInvalid = nameof(DeveloperExtensionInvalid);
    public const string RequirementInvalid = nameof(RequirementInvalid);

    // Page labels (§12.4.2).
    public const string PageLabelsMissingZeroKey = nameof(PageLabelsMissingZeroKey);
    public const string PageLabelInvalid = nameof(PageLabelInvalid);
    public const string PageLabelStyleInvalid = nameof(PageLabelStyleInvalid);
    public const string PageLabelStartInvalid = nameof(PageLabelStartInvalid);
    public const string PageLabelPrefixInvalid = nameof(PageLabelPrefixInvalid);
    public const string PageLabelTooLong = nameof(PageLabelTooLong);

    // Document information, metadata and file identifiers (§7.9.4, §14.3, §14.4).
    public const string InfoDictionaryInvalid = nameof(InfoDictionaryInvalid);
    public const string InfoValueNotTextString = nameof(InfoValueNotTextString);
    public const string InfoTrappedInvalid = nameof(InfoTrappedInvalid);
    public const string DateInvalid = nameof(DateInvalid);
    public const string DateUnreadable = nameof(DateUnreadable);
    public const string MetadataStreamInvalid = nameof(MetadataStreamInvalid);
    public const string MetadataStreamTypeInvalid = nameof(MetadataStreamTypeInvalid);
    public const string XmpMalformed = nameof(XmpMalformed);
    public const string XmpDtdProhibited = nameof(XmpDtdProhibited);
    public const string XmpLeadingJunk = nameof(XmpLeadingJunk);
    public const string XmpPropertyDuplicate = nameof(XmpPropertyDuplicate);
    public const string FileIdentifierInvalid = nameof(FileIdentifierInvalid);
}
