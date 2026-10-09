namespace Broadside.Parsing;

/// <summary>
/// The codes of the diagnostics the file reader and the document model record, grouped by area. Object syntax codes are in
/// <see cref="Objects.CosRepairCodes"/>. Codes are stable: tests, logs and users match on them.
/// </summary>
/// <remarks>
/// Every ticket adds its own group here. The catalogue is internal while parallel tickets add codes; the codes reach users as
/// <see cref="Diagnostics.Diagnostic.Code"/> strings.
/// </remarks>
internal static class DiagnosticCodes
{
    // File header (ISO 32000-2 §7.5.2).
    public const string HeaderMissing = nameof(HeaderMissing);
    public const string HeaderVersionInvalid = nameof(HeaderVersionInvalid);

    // File trailer and startxref (§7.5.5).
    public const string StartxrefMissing = nameof(StartxrefMissing);
    public const string StartxrefInvalid = nameof(StartxrefInvalid);
    public const string EndOfFileMarkerMissing = nameof(EndOfFileMarkerMissing);
    public const string TrailerMissing = nameof(TrailerMissing);
    public const string TrailerPrevInvalid = nameof(TrailerPrevInvalid);
    public const string RootMissing = nameof(RootMissing);
    public const string RootNotIndirect = nameof(RootNotIndirect);

    // Repair by scanning (§7.5; issue #41).
    public const string CatalogNotFound = nameof(CatalogNotFound);

    // Cross-reference table (§7.5.4).
    public const string XrefSectionInvalid = nameof(XrefSectionInvalid);
    public const string XrefEntryInvalid = nameof(XrefEntryInvalid);
    public const string XrefEntryFormatInvalid = nameof(XrefEntryFormatInvalid);
    public const string XrefSubsectionNumberingInvalid = nameof(XrefSubsectionNumberingInvalid);
    public const string XrefPrevLoop = nameof(XrefPrevLoop);

    // Cross-reference streams and hybrid files (§7.5.8).
    public const string XrefStreamTypeInvalid = nameof(XrefStreamTypeInvalid);
    public const string XrefStreamWidthsInvalid = nameof(XrefStreamWidthsInvalid);
    public const string XrefStreamIndexInvalid = nameof(XrefStreamIndexInvalid);
    public const string XrefStreamSizeInvalid = nameof(XrefStreamSizeInvalid);
    public const string XrefStreamDataTruncated = nameof(XrefStreamDataTruncated);
    public const string TrailerXRefStmInvalid = nameof(TrailerXRefStmInvalid);

    // Object streams (§7.5.7).
    public const string ObjectStreamInvalid = nameof(ObjectStreamInvalid);
    public const string ObjectStreamTypeInvalid = nameof(ObjectStreamTypeInvalid);
    public const string ObjectStreamHeaderInvalid = nameof(ObjectStreamHeaderInvalid);
    public const string ObjectStreamIndexMismatch = nameof(ObjectStreamIndexMismatch);
    public const string ObjectStreamMemberMissing = nameof(ObjectStreamMemberMissing);
    public const string ObjectStreamMemberInvalid = nameof(ObjectStreamMemberInvalid);
    public const string ObjectStreamNested = nameof(ObjectStreamNested);
    public const string ObjectStreamCycle = nameof(ObjectStreamCycle);

    // Incremental updates (§7.5.6).
    public const string TrailerEntryFromOlderRevision = nameof(TrailerEntryFromOlderRevision);

    // Linearized files (Annex F).
    public const string LinearizationDictionaryInvalid = nameof(LinearizationDictionaryInvalid);
    public const string LinearizationHintsInvalid = nameof(LinearizationHintsInvalid);

    // Indirect objects (§7.3.10).
    public const string XrefEntryOffsetInvalid = nameof(XrefEntryOffsetInvalid);
    public const string MissingEndobj = nameof(MissingEndobj);
    public const string ReferenceChainTooDeep = nameof(ReferenceChainTooDeep);

    // Lazy loading (§7.5.4): an object whose loading needs the object itself.
    public const string ObjectReferenceCycle = nameof(ObjectReferenceCycle);

    // Stream filters (§7.3.8.2 Table 5, §7.4).
    public const string FilterInvalid = nameof(FilterInvalid);
    public const string FilterUnsupported = nameof(FilterUnsupported);
    public const string FilterAbbreviationNotAllowed = nameof(FilterAbbreviationNotAllowed);
    public const string FilterDataInvalid = nameof(FilterDataInvalid);
    public const string FilterDataTruncated = nameof(FilterDataTruncated);
    public const string FilterFailed = nameof(FilterFailed);
    public const string DecodeParmsInvalid = nameof(DecodeParmsInvalid);
    public const string PredictorInvalid = nameof(PredictorInvalid);
    public const string CryptFilterNotFirst = nameof(CryptFilterNotFirst);
    public const string CryptFilterUnsupported = nameof(CryptFilterUnsupported);
    public const string StreamExternalFileUnsupported = nameof(StreamExternalFileUnsupported);
    public const string StreamDecodedLengthExceeded = nameof(StreamDecodedLengthExceeded);
    public const string StreamDecodingTooDeep = nameof(StreamDecodingTooDeep);

    // Encryption (§7.6; ISO/TS 32003; ISO/TS 32004).
    public const string EncryptDictionaryInvalid = nameof(EncryptDictionaryInvalid);
    public const string EncryptionIdMissing = nameof(EncryptionIdMissing);
    public const string EncryptionVersionInvalid = nameof(EncryptionVersionInvalid);
    public const string EncryptionRevisionInvalid = nameof(EncryptionRevisionInvalid);
    public const string EncryptionKeyLengthInvalid = nameof(EncryptionKeyLengthInvalid);
    public const string EncryptionEntryLengthInvalid = nameof(EncryptionEntryLengthInvalid);
    public const string EncryptionPermissionsMismatch = nameof(EncryptionPermissionsMismatch);
    public const string EncryptionExtensionMissing = nameof(EncryptionExtensionMissing);
    public const string OwnerPasswordKeyVariant = nameof(OwnerPasswordKeyVariant);
    public const string CryptFilterMissing = nameof(CryptFilterMissing);
    public const string CryptFilterMethodInvalid = nameof(CryptFilterMethodInvalid);
    public const string EncryptedDataInvalid = nameof(EncryptedDataInvalid);
    public const string EncryptedDataAuthenticationFailed = nameof(EncryptedDataAuthenticationFailed);
    public const string MetadataNotEncrypted = nameof(MetadataNotEncrypted);
    public const string IntegrityCodeMissing = nameof(IntegrityCodeMissing);
    public const string IntegrityCodeInvalid = nameof(IntegrityCodeInvalid);
    public const string IntegrityCodeMismatch = nameof(IntegrityCodeMismatch);
    public const string IntegrityCodeIncomplete = nameof(IntegrityCodeIncomplete);
    public const string IntegrityCodeNotVerified = nameof(IntegrityCodeNotVerified);

    // Document catalog (§7.7.2).
    public const string CatalogTypeInvalid = nameof(CatalogTypeInvalid);
    public const string CatalogVersionNotName = nameof(CatalogVersionNotName);
    public const string CatalogVersionInvalid = nameof(CatalogVersionInvalid);
    public const string PagesMissing = nameof(PagesMissing);

    // Page tree (§7.7.3).
    public const string PageTreeNodeTypeInvalid = nameof(PageTreeNodeTypeInvalid);
    public const string PageTreeKidsInvalid = nameof(PageTreeKidsInvalid);
    public const string PageTreeKidInvalid = nameof(PageTreeKidInvalid);
    public const string PageTreeKidNotIndirect = nameof(PageTreeKidNotIndirect);
    public const string PageTreeCycle = nameof(PageTreeCycle);
    public const string PageTreeTooDeep = nameof(PageTreeTooDeep);
    public const string PageTreeParentMismatch = nameof(PageTreeParentMismatch);
    public const string PageTreeCountMismatch = nameof(PageTreeCountMismatch);

    // Page objects (§7.7.3.3, §7.9.5).
    public const string PageMediaBoxMissing = nameof(PageMediaBoxMissing);
    public const string PageBoxInvalid = nameof(PageBoxInvalid);
    public const string PageRotateInvalid = nameof(PageRotateInvalid);
    public const string PageResourcesMissing = nameof(PageResourcesMissing);
    public const string PageResourcesInvalid = nameof(PageResourcesInvalid);
    public const string PageUserUnitInvalid = nameof(PageUserUnitInvalid);
}
