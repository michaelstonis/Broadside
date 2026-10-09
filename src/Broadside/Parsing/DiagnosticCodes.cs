namespace Broadside.Parsing;

/// <summary>
/// The codes of the diagnostics the file reader and the document model record, grouped by area. Object syntax codes are in
/// <see cref="Objects.CosRepairCodes"/>. Codes are stable: tests, logs and users match on them.
/// </summary>
/// <remarks>Issue #41 owns the public catalogue of codes; every ticket adds its own group here.</remarks>
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

    // Cross-reference table (§7.5.4).
    public const string XrefSectionInvalid = nameof(XrefSectionInvalid);
    public const string XrefEntryInvalid = nameof(XrefEntryInvalid);
    public const string XrefPrevLoop = nameof(XrefPrevLoop);
    public const string XrefStreamUnsupported = nameof(XrefStreamUnsupported);

    // Indirect objects (§7.3.10).
    public const string XrefEntryOffsetInvalid = nameof(XrefEntryOffsetInvalid);
    public const string MissingEndobj = nameof(MissingEndobj);
    public const string ReferenceChainTooDeep = nameof(ReferenceChainTooDeep);

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
