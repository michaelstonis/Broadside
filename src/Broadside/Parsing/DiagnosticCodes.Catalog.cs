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
    public const string DeveloperExtensionBaseVersionLater = nameof(DeveloperExtensionBaseVersionLater);
    public const string RequirementInvalid = nameof(RequirementInvalid);

    // Output intents (§14.11.5, issue #119).
    public const string OutputIntentInvalid = nameof(OutputIntentInvalid);

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

    // Saving (ADR 0003; §7.5.2, §7.7.2 Table 29): the version a save writes (issue #141).
    public const string VersionRaised = nameof(VersionRaised);

    // Saving with a PDF/A conformance level (issue #201; veraPDF validation-profile rule identifiers): a rule the document cannot
    // meet (Warning, refused in strict mode), and a lenient save that therefore does not claim the conformance (Information).
    public const string PdfANonConformant = nameof(PdfANonConformant);
    public const string PdfANotIdentified = nameof(PdfANotIdentified);

    // Saving with a PDF/UA conformance (issue #204; veraPDF PDF/UA-1 rule identifiers): a failure condition the document has
    // (Warning, refused in strict mode), and a lenient save that therefore does not claim the conformance (Information).
    public const string PdfUANonConformant = nameof(PdfUANonConformant);
    public const string PdfUANotIdentified = nameof(PdfUANotIdentified);

    // Saving with Well-Tagged PDF declarations (issue #205; veraPDF WTPDF 1.0 rule identifiers): a failure condition the document has
    // (Warning, refused in strict mode), and a lenient save that therefore does not declare the level (Information).
    public const string WellTaggedNonConformant = nameof(WellTaggedNonConformant);
    public const string WellTaggedNotDeclared = nameof(WellTaggedNotDeclared);
}
