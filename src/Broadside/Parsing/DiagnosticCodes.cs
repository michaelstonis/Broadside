using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>
/// The codes of the diagnostics the file reader and the document model record, grouped by area. Object syntax codes are in
/// <see cref="Objects.CosRepairCodes"/>. Codes are stable: tests, logs and users match on them.
/// </summary>
/// <remarks>
/// Every ticket adds its own group here. The catalogue is internal while parallel tickets add codes; the codes reach users as
/// <see cref="Diagnostics.Diagnostic.Code"/> strings.
/// </remarks>
internal static partial class DiagnosticCodes
{
    // File header (ISO 32000-2 §7.5.2).
    public static string HeaderMissing => FileStructureDiagnostics.HeaderMissing.Name;
    public static string HeaderVersionInvalid => FileStructureDiagnostics.HeaderVersionInvalid.Name;
    public static string HeaderInvalid => FileStructureDiagnostics.HeaderInvalid.Name;
    public static string HeaderBinaryCommentMissing => FileStructureDiagnostics.HeaderBinaryCommentMissing.Name;

    // File trailer and startxref (§7.5.5).
    public static string StartxrefMissing => FileStructureDiagnostics.StartxrefMissing.Name;
    public static string StartxrefInvalid => FileStructureDiagnostics.StartxrefInvalid.Name;
    public static string EndOfFileMarkerMissing => FileStructureDiagnostics.EndOfFileMarkerMissing.Name;
    public static string EndOfFileMarkerNotLast => FileStructureDiagnostics.EndOfFileMarkerNotLast.Name;
    public static string TrailerIdMissing => FileStructureDiagnostics.TrailerIdMissing.Name;
    public static string TrailerMissing => FileStructureDiagnostics.TrailerMissing.Name;
    public static string TrailerPrevInvalid => FileStructureDiagnostics.TrailerPrevInvalid.Name;
    public static string RootMissing => FileStructureDiagnostics.RootMissing.Name;
    public static string RootNotIndirect => FileStructureDiagnostics.RootNotIndirect.Name;

    // Repair by scanning (§7.5; issue #41).
    public static string CatalogNotFound => FileStructureDiagnostics.CatalogNotFound.Name;

    // Cross-reference table (§7.5.4).
    public static string XrefSectionInvalid => FileStructureDiagnostics.XrefSectionInvalid.Name;
    public static string XrefEntryInvalid => FileStructureDiagnostics.XrefEntryInvalid.Name;
    public static string XrefEntryFormatInvalid => FileStructureDiagnostics.XrefEntryFormatInvalid.Name;
    public static string XrefSubsectionNumberingInvalid => FileStructureDiagnostics.XrefSubsectionNumberingInvalid.Name;
    public static string XrefSubsectionHeaderInvalid => FileStructureDiagnostics.XrefSubsectionHeaderInvalid.Name;
    public static string XrefPrevLoop => FileStructureDiagnostics.XrefPrevLoop.Name;

    // Cross-reference streams and hybrid files (§7.5.8).
    public static string XrefStreamTypeInvalid => FileStructureDiagnostics.XrefStreamTypeInvalid.Name;
    public static string XrefStreamWidthsInvalid => FileStructureDiagnostics.XrefStreamWidthsInvalid.Name;
    public static string XrefStreamIndexInvalid => FileStructureDiagnostics.XrefStreamIndexInvalid.Name;
    public static string XrefStreamSizeInvalid => FileStructureDiagnostics.XrefStreamSizeInvalid.Name;
    public static string CrossReferenceEntryLimitExceeded => FileStructureDiagnostics.CrossReferenceEntryLimitExceeded.Name;
    public static string XrefStreamDataTruncated => FileStructureDiagnostics.XrefStreamDataTruncated.Name;
    public static string TrailerXRefStmInvalid => FileStructureDiagnostics.TrailerXRefStmInvalid.Name;

    // Object streams (§7.5.7).
    public static string ObjectStreamInvalid => FileStructureDiagnostics.ObjectStreamInvalid.Name;
    public static string ObjectStreamTypeInvalid => FileStructureDiagnostics.ObjectStreamTypeInvalid.Name;
    public static string ObjectStreamHeaderInvalid => FileStructureDiagnostics.ObjectStreamHeaderInvalid.Name;
    public static string ObjectStreamIndexMismatch => FileStructureDiagnostics.ObjectStreamIndexMismatch.Name;
    public static string ObjectStreamMemberMissing => FileStructureDiagnostics.ObjectStreamMemberMissing.Name;
    public static string ObjectStreamMemberInvalid => FileStructureDiagnostics.ObjectStreamMemberInvalid.Name;
    public static string ObjectStreamNested => FileStructureDiagnostics.ObjectStreamNested.Name;
    public static string ObjectStreamCycle => FileStructureDiagnostics.ObjectStreamCycle.Name;

    // Incremental updates (§7.5.6).
    public static string TrailerEntryFromOlderRevision => FileStructureDiagnostics.TrailerEntryFromOlderRevision.Name;

    // Linearized files (Annex F).
    public static string LinearizationDictionaryInvalid => FileStructureDiagnostics.LinearizationDictionaryInvalid.Name;
    public static string LinearizationHintsInvalid => FileStructureDiagnostics.LinearizationHintsInvalid.Name;
    public static string LinearizationDropped => FileStructureDiagnostics.LinearizationDropped.Name;

    // Indirect objects (§7.3.10).
    public static string XrefEntryOffsetInvalid => FileStructureDiagnostics.XrefEntryOffsetInvalid.Name;
    public static string MissingEndobj => FileStructureDiagnostics.MissingEndobj.Name;
    public static string ReferenceChainTooDeep => FileStructureDiagnostics.ReferenceChainTooDeep.Name;

    // Lazy loading (§7.5.4): an object whose loading needs the object itself.
    public static string ObjectReferenceCycle => FileStructureDiagnostics.ObjectReferenceCycle.Name;

    // Stream filters (§7.3.8.2 Table 5, §7.4).
    public static string FilterInvalid => FilterDiagnostics.FilterInvalid.Name;
    public static string FilterUnsupported => FilterDiagnostics.FilterUnsupported.Name;
    public static string FilterAbbreviationNotAllowed => FilterDiagnostics.FilterAbbreviationNotAllowed.Name;
    public static string FilterDataInvalid => FilterDiagnostics.FilterDataInvalid.Name;
    public static string FilterDataTruncated => FilterDiagnostics.FilterDataTruncated.Name;
    public static string FilterFailed => FilterDiagnostics.FilterFailed.Name;
    public static string DecodeParmsInvalid => FilterDiagnostics.DecodeParmsInvalid.Name;
    public static string PredictorInvalid => FilterDiagnostics.PredictorInvalid.Name;
    public static string CryptFilterNotFirst => FilterDiagnostics.CryptFilterNotFirst.Name;
    public static string CryptFilterUnsupported => FilterDiagnostics.CryptFilterUnsupported.Name;
    public static string StreamExternalFileUnsupported => FilterDiagnostics.StreamExternalFileUnsupported.Name;
    public static string StreamDecodedLengthExceeded => FilterDiagnostics.StreamDecodedLengthExceeded.Name;
    public static string StreamDecodingTooDeep => FilterDiagnostics.StreamDecodingTooDeep.Name;

    // Encryption (§7.6; ISO/TS 32003; ISO/TS 32004).
    public static string EncryptDictionaryInvalid => SecurityDiagnostics.EncryptDictionaryInvalid.Name;
    public static string EncryptionIdMissing => SecurityDiagnostics.EncryptionIdMissing.Name;
    public static string EncryptionVersionInvalid => SecurityDiagnostics.EncryptionVersionInvalid.Name;
    public static string EncryptionRevisionInvalid => SecurityDiagnostics.EncryptionRevisionInvalid.Name;
    public static string EncryptionKeyLengthInvalid => SecurityDiagnostics.EncryptionKeyLengthInvalid.Name;
    public static string EncryptionEntryLengthInvalid => SecurityDiagnostics.EncryptionEntryLengthInvalid.Name;
    public static string EncryptionPermissionsMismatch => SecurityDiagnostics.EncryptionPermissionsMismatch.Name;
    public static string EncryptionExtensionMissing => SecurityDiagnostics.EncryptionExtensionMissing.Name;
    public static string OwnerPasswordKeyVariant => SecurityDiagnostics.OwnerPasswordKeyVariant.Name;
    public static string CryptFilterMissing => SecurityDiagnostics.CryptFilterMissing.Name;
    public static string CryptFilterMethodInvalid => SecurityDiagnostics.CryptFilterMethodInvalid.Name;
    public static string EncryptedDataInvalid => SecurityDiagnostics.EncryptedDataInvalid.Name;
    public static string EncryptedDataAuthenticationFailed => SecurityDiagnostics.EncryptedDataAuthenticationFailed.Name;
    public static string MetadataNotEncrypted => SecurityDiagnostics.MetadataNotEncrypted.Name;
    public static string IntegrityCodeMissing => SecurityDiagnostics.IntegrityCodeMissing.Name;
    public static string IntegrityCodeInvalid => SecurityDiagnostics.IntegrityCodeInvalid.Name;
    public static string IntegrityCodeMismatch => SecurityDiagnostics.IntegrityCodeMismatch.Name;
    public static string IntegrityCodeIncomplete => SecurityDiagnostics.IntegrityCodeIncomplete.Name;
    public static string IntegrityCodeNotVerified => SecurityDiagnostics.IntegrityCodeNotVerified.Name;
    public static string PublicKeyRecipientInvalid => SecurityDiagnostics.PublicKeyRecipientInvalid.Name;
    public static string PublicKeyEnvelopeInvalid => SecurityDiagnostics.PublicKeyEnvelopeInvalid.Name;
    public static string CryptFilterNotAuthorized => SecurityDiagnostics.CryptFilterNotAuthorized.Name;
    public static string EncryptionDeprecated => SecurityDiagnostics.EncryptionDeprecated.Name;

    // Document catalog (§7.7.2).
    public static string CatalogTypeInvalid => DocumentDiagnostics.CatalogTypeInvalid.Name;
    public static string CatalogVersionNotName => DocumentDiagnostics.CatalogVersionNotName.Name;
    public static string CatalogVersionInvalid => DocumentDiagnostics.CatalogVersionInvalid.Name;
    public static string PagesMissing => DocumentDiagnostics.PagesMissing.Name;

    // Page tree (§7.7.3).
    public static string PageTreeNodeTypeInvalid => PageDiagnostics.PageTreeNodeTypeInvalid.Name;
    public static string PageTreeKidsInvalid => PageDiagnostics.PageTreeKidsInvalid.Name;
    public static string PageTreeKidInvalid => PageDiagnostics.PageTreeKidInvalid.Name;
    public static string PageTreeKidNotIndirect => PageDiagnostics.PageTreeKidNotIndirect.Name;
    public static string PageTreeCycle => PageDiagnostics.PageTreeCycle.Name;
    public static string PageTreeTooDeep => PageDiagnostics.PageTreeTooDeep.Name;
    public static string PageTreeParentMismatch => PageDiagnostics.PageTreeParentMismatch.Name;
    public static string PageTreeCountMismatch => PageDiagnostics.PageTreeCountMismatch.Name;

    // Page objects (§7.7.3.3, §7.9.5).
    public static string PageMediaBoxMissing => PageDiagnostics.PageMediaBoxMissing.Name;
    public static string PageBoxInvalid => PageDiagnostics.PageBoxInvalid.Name;
    public static string PageRotateInvalid => PageDiagnostics.PageRotateInvalid.Name;
    public static string PageResourcesMissing => PageDiagnostics.PageResourcesMissing.Name;
    public static string PageResourcesInvalid => PageDiagnostics.PageResourcesInvalid.Name;
    public static string PageUserUnitInvalid => PageDiagnostics.PageUserUnitInvalid.Name;

    // Content streams (§7.8.2, §8.2, §8.4, §8.5; issue #55). Reported once per code per content stream (DiagnosticSink.ReportOnce),
    // on the stream's object (or the page's when the stream is direct), with the part and decoded offset in the message.
    public static string ContentStreamInvalid => ContentDiagnostics.ContentStreamInvalid.Name;
    public static string ContentUnknownOperator => ContentDiagnostics.ContentUnknownOperator.Name;
    public static string ContentOperandCount => ContentDiagnostics.ContentOperandCount.Name;
    public static string ContentOperandType => ContentDiagnostics.ContentOperandType.Name;
    public static string ContentOperandOverflow => ContentDiagnostics.ContentOperandOverflow.Name;
    public static string ContentSyntaxInvalid => ContentDiagnostics.ContentSyntaxInvalid.Name;
    public static string ContentGluedTokens => ContentDiagnostics.ContentGluedTokens.Name;
    public static string ContentOperatorOutOfContext => ContentDiagnostics.ContentOperatorOutOfContext.Name;
    public static string ContentStackUnderflow => ContentDiagnostics.ContentStackUnderflow.Name;
    public static string ContentStackOverflow => ContentDiagnostics.ContentStackOverflow.Name;
    public static string ContentUnbalancedSave => ContentDiagnostics.ContentUnbalancedSave.Name;
    public static string ContentNoCurrentPoint => ContentDiagnostics.ContentNoCurrentPoint.Name;
    public static string ContentNoCurrentPath => ContentDiagnostics.ContentNoCurrentPath.Name;
    public static string ContentPathNotPainted => ContentDiagnostics.ContentPathNotPainted.Name;
    public static string ContentGraphicsStateRange => ContentDiagnostics.ContentGraphicsStateRange.Name;
    public static string ContentCompatibilityUnbalanced => ContentDiagnostics.ContentCompatibilityUnbalanced.Name;
    public static string ContentTextObjectUnbalanced => ContentDiagnostics.ContentTextObjectUnbalanced.Name;
    public static string ContentInlineImageInvalid => ContentDiagnostics.ContentInlineImageInvalid.Name;

    // Functions (§7.10, issue #78).
    public static string FunctionInvalid => FunctionDiagnostics.FunctionInvalid.Name;
    public static string FunctionEntryInvalid => FunctionDiagnostics.FunctionEntryInvalid.Name;
    public static string FunctionSampleDataTruncated => FunctionDiagnostics.FunctionSampleDataTruncated.Name;
    public static string FunctionProgramSyntaxInvalid => FunctionDiagnostics.FunctionProgramSyntaxInvalid.Name;
    public static string FunctionProgramStackInvalid => FunctionDiagnostics.FunctionProgramStackInvalid.Name;
    public static string FunctionCycle => FunctionDiagnostics.FunctionCycle.Name;
    public static string FunctionTooDeep => FunctionDiagnostics.FunctionTooDeep.Name;
    public static string FunctionEvaluationRepaired => FunctionDiagnostics.FunctionEvaluationRepaired.Name;
}
