using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <content>Codes for the document-level catalog entries, page labels, information and metadata (issue #69).</content>
internal static partial class DiagnosticCodes
{
    // Document catalog entries (§7.7.2 Table 29, §7.12, §12.2, §12.11).
    public static string CatalogEntryInvalid => DocumentDiagnostics.CatalogEntryInvalid.Name;
    public static string PageLayoutInvalid => DocumentDiagnostics.PageLayoutInvalid.Name;
    public static string PageModeInvalid => DocumentDiagnostics.PageModeInvalid.Name;
    public static string ViewerPreferencesInvalid => DocumentDiagnostics.ViewerPreferencesInvalid.Name;
    public static string ViewerPreferenceInvalid => DocumentDiagnostics.ViewerPreferenceInvalid.Name;
    public static string ExtensionsInvalid => DocumentDiagnostics.ExtensionsInvalid.Name;
    public static string ExtensionsNotDirect => DocumentDiagnostics.ExtensionsNotDirect.Name;
    public static string DeveloperExtensionInvalid => DocumentDiagnostics.DeveloperExtensionInvalid.Name;
    public static string DeveloperExtensionBaseVersionLater => DocumentDiagnostics.DeveloperExtensionBaseVersionLater.Name;
    public static string RequirementInvalid => DocumentDiagnostics.RequirementInvalid.Name;

    // Output intents (§14.11.5, issue #119).
    public static string OutputIntentInvalid => DocumentDiagnostics.OutputIntentInvalid.Name;

    // Page labels (§12.4.2).
    public static string PageLabelsMissingZeroKey => DocumentDiagnostics.PageLabelsMissingZeroKey.Name;
    public static string PageLabelInvalid => DocumentDiagnostics.PageLabelInvalid.Name;
    public static string PageLabelStyleInvalid => DocumentDiagnostics.PageLabelStyleInvalid.Name;
    public static string PageLabelStartInvalid => DocumentDiagnostics.PageLabelStartInvalid.Name;
    public static string PageLabelPrefixInvalid => DocumentDiagnostics.PageLabelPrefixInvalid.Name;
    public static string PageLabelTooLong => DocumentDiagnostics.PageLabelTooLong.Name;

    // Document information, metadata and file identifiers (§7.9.4, §14.3, §14.4).
    public static string InfoDictionaryInvalid => DocumentDiagnostics.InfoDictionaryInvalid.Name;
    public static string InfoValueNotTextString => DocumentDiagnostics.InfoValueNotTextString.Name;
    public static string InfoTrappedInvalid => DocumentDiagnostics.InfoTrappedInvalid.Name;
    public static string DateInvalid => DocumentDiagnostics.DateInvalid.Name;
    public static string DateUnreadable => DocumentDiagnostics.DateUnreadable.Name;
    public static string MetadataStreamInvalid => DocumentDiagnostics.MetadataStreamInvalid.Name;
    public static string MetadataStreamTypeInvalid => DocumentDiagnostics.MetadataStreamTypeInvalid.Name;
    public static string XmpMalformed => DocumentDiagnostics.XmpMalformed.Name;
    public static string XmpDtdProhibited => DocumentDiagnostics.XmpDtdProhibited.Name;
    public static string XmpLeadingJunk => DocumentDiagnostics.XmpLeadingJunk.Name;
    public static string XmpPropertyDuplicate => DocumentDiagnostics.XmpPropertyDuplicate.Name;
    public static string FileIdentifierInvalid => DocumentDiagnostics.FileIdentifierInvalid.Name;

    // Saving (ADR 0003; §7.5.2, §7.7.2 Table 29): the version a save writes (issue #141).
    public static string VersionRaised => SavingDiagnostics.VersionRaised.Name;

    // Saving (§7.5.4, §7.5.6, Annex C Table C.1): an object number a save cannot keep or represent (issue #371).
    public static string ObjectNumberOutOfRange => SavingDiagnostics.ObjectNumberOutOfRange.Name;

    // Saving with a PDF/A conformance level (issue #201; veraPDF validation-profile rule identifiers): a rule the document cannot
    // meet (Warning, refused in strict mode), and a lenient save that therefore does not claim the conformance (Information).
    public static string PdfANonConformant => SavingDiagnostics.PdfANonConformant.Name;
    public static string PdfANotIdentified => SavingDiagnostics.PdfANotIdentified.Name;

    // Converting a document to a PDF/A level (issue #229): what the conversion changed or removed, and what it could not fix and why
    // (both Information; the rules still not met are PdfANonConformant warnings).
    public static string PdfAConverted => SavingDiagnostics.PdfAConverted.Name;
    public static string PdfANotConverted => SavingDiagnostics.PdfANotConverted.Name;

    // Saving with a PDF/UA conformance (issue #204; veraPDF PDF/UA-1 rule identifiers): a failure condition the document has
    // (Warning, refused in strict mode), and a lenient save that therefore does not claim the conformance (Information).
    public static string PdfUANonConformant => SavingDiagnostics.PdfUANonConformant.Name;
    public static string PdfUANotIdentified => SavingDiagnostics.PdfUANotIdentified.Name;

    // Saving with Well-Tagged PDF declarations (issue #205; veraPDF WTPDF 1.0 rule identifiers): a failure condition the document has
    // (Warning, refused in strict mode), and a lenient save that therefore does not declare the level (Information).
    public static string WellTaggedNonConformant => SavingDiagnostics.WellTaggedNonConformant.Name;
    public static string WellTaggedNotDeclared => SavingDiagnostics.WellTaggedNotDeclared.Name;
}
