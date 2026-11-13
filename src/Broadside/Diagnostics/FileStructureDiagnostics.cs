namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the file structure: header, trailer, cross-reference tables and streams, object streams, incremental updates, linearization and indirect objects.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.5, §7.3.10, Annex F. Each code is reported under <see cref="DiagnosticArea.FileStructure"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class FileStructureDiagnostics
{
    /// <summary>Gets the <c>HeaderMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.2</remarks>
    public static DiagnosticCode HeaderMissing { get; } = new(nameof(HeaderMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.2");

    /// <summary>Gets the <c>HeaderVersionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.2</remarks>
    public static DiagnosticCode HeaderVersionInvalid { get; } = new(nameof(HeaderVersionInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.2");

    /// <summary>Gets the <c>HeaderInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.2</remarks>
    public static DiagnosticCode HeaderInvalid { get; } = new(nameof(HeaderInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.2");

    /// <summary>Gets the <c>HeaderBinaryCommentMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.2</remarks>
    public static DiagnosticCode HeaderBinaryCommentMissing { get; } = new(nameof(HeaderBinaryCommentMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.2");

    /// <summary>Gets the <c>StartxrefMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode StartxrefMissing { get; } = new(nameof(StartxrefMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5");

    /// <summary>Gets the <c>StartxrefInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode StartxrefInvalid { get; } = new(nameof(StartxrefInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5");

    /// <summary>Gets the <c>EndOfFileMarkerMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode EndOfFileMarkerMissing { get; } = new(nameof(EndOfFileMarkerMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5");

    /// <summary>Gets the <c>EndOfFileMarkerNotLast</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode EndOfFileMarkerNotLast { get; } = new(nameof(EndOfFileMarkerNotLast), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5");

    /// <summary>Gets the <c>TrailerIdMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode TrailerIdMissing { get; } = new(nameof(TrailerIdMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5");

    /// <summary>Gets the <c>TrailerMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode TrailerMissing { get; } = new(nameof(TrailerMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.5");

    /// <summary>Gets the <c>TrailerPrevInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode TrailerPrevInvalid { get; } = new(nameof(TrailerPrevInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5");

    /// <summary>Gets the <c>RootMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode RootMissing { get; } = new(nameof(RootMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5");

    /// <summary>Gets the <c>RootNotIndirect</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode RootNotIndirect { get; } = new(nameof(RootNotIndirect), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5");

    /// <summary>Gets the <c>CatalogNotFound</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5</remarks>
    public static DiagnosticCode CatalogNotFound { get; } = new(nameof(CatalogNotFound), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5");

    /// <summary>Gets the <c>XrefSectionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefSectionInvalid { get; } = new(nameof(XrefSectionInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.4");

    /// <summary>Gets the <c>XrefEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefEntryInvalid { get; } = new(nameof(XrefEntryInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.4");

    /// <summary>Gets the <c>XrefEntryFormatInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefEntryFormatInvalid { get; } = new(nameof(XrefEntryFormatInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.4");

    /// <summary>Gets the <c>XrefSubsectionNumberingInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefSubsectionNumberingInvalid { get; } = new(nameof(XrefSubsectionNumberingInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.4");

    /// <summary>Gets the <c>XrefSubsectionHeaderInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefSubsectionHeaderInvalid { get; } = new(nameof(XrefSubsectionHeaderInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.4");

    /// <summary>Gets the <c>XrefEntryBeyondSize</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode XrefEntryBeyondSize { get; } = new(nameof(XrefEntryBeyondSize), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5");

    /// <summary>Gets the <c>XrefCommentIgnored</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefCommentIgnored { get; } = new(nameof(XrefCommentIgnored), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.4");

    /// <summary>Gets the <c>XrefLineMalformed</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefLineMalformed { get; } = new(nameof(XrefLineMalformed), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.4");

    /// <summary>Gets the <c>StartxrefLastDamaged</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode StartxrefLastDamaged { get; } = new(nameof(StartxrefLastDamaged), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5");

    /// <summary>Gets the <c>XrefPrevLoop</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefPrevLoop { get; } = new(nameof(XrefPrevLoop), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.4");

    /// <summary>Gets the <c>XrefStreamTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode XrefStreamTypeInvalid { get; } = new(nameof(XrefStreamTypeInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.8");

    /// <summary>Gets the <c>XrefStreamWidthsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode XrefStreamWidthsInvalid { get; } = new(nameof(XrefStreamWidthsInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.8");

    /// <summary>Gets the <c>XrefStreamIndexInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode XrefStreamIndexInvalid { get; } = new(nameof(XrefStreamIndexInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.8");

    /// <summary>Gets the <c>XrefStreamSizeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode XrefStreamSizeInvalid { get; } = new(nameof(XrefStreamSizeInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.8");

    /// <summary>Gets the <c>CrossReferenceEntryLimitExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode CrossReferenceEntryLimitExceeded { get; } = new(nameof(CrossReferenceEntryLimitExceeded), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.8");

    /// <summary>Gets the <c>XrefStreamDataTruncated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode XrefStreamDataTruncated { get; } = new(nameof(XrefStreamDataTruncated), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.8");

    /// <summary>Gets the <c>TrailerXRefStmInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode TrailerXRefStmInvalid { get; } = new(nameof(TrailerXRefStmInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.8");

    /// <summary>Gets the <c>ObjectStreamInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamInvalid { get; } = new(nameof(ObjectStreamInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.7");

    /// <summary>Gets the <c>ObjectStreamTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamTypeInvalid { get; } = new(nameof(ObjectStreamTypeInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.7");

    /// <summary>Gets the <c>ObjectStreamHeaderInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamHeaderInvalid { get; } = new(nameof(ObjectStreamHeaderInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.7");

    /// <summary>Gets the <c>ObjectStreamIndexMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamIndexMismatch { get; } = new(nameof(ObjectStreamIndexMismatch), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.7");

    /// <summary>Gets the <c>ObjectStreamMemberMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamMemberMissing { get; } = new(nameof(ObjectStreamMemberMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.7");

    /// <summary>Gets the <c>ObjectStreamMemberInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamMemberInvalid { get; } = new(nameof(ObjectStreamMemberInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.7");

    /// <summary>Gets the <c>ObjectStreamNested</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamNested { get; } = new(nameof(ObjectStreamNested), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.7");

    /// <summary>Gets the <c>ObjectStreamCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamCycle { get; } = new(nameof(ObjectStreamCycle), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.7");

    /// <summary>Gets the <c>TrailerEntryFromOlderRevision</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.6</remarks>
    public static DiagnosticCode TrailerEntryFromOlderRevision { get; } = new(nameof(TrailerEntryFromOlderRevision), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.6");

    /// <summary>Gets the <c>LinearizationDictionaryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 Annex F</remarks>
    public static DiagnosticCode LinearizationDictionaryInvalid { get; } = new(nameof(LinearizationDictionaryInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 Annex F");

    /// <summary>Gets the <c>LinearizationHintsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 Annex F</remarks>
    public static DiagnosticCode LinearizationHintsInvalid { get; } = new(nameof(LinearizationHintsInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 Annex F");

    /// <summary>Gets the <c>LinearizationDropped</c> code.</summary>
    /// <remarks>ISO 32000-2 Annex F</remarks>
    public static DiagnosticCode LinearizationDropped { get; } = new(nameof(LinearizationDropped), DiagnosticArea.FileStructure, DiagnosticSeverity.Information, "ISO 32000-2 Annex F");

    /// <summary>Gets the <c>XrefEntryOffsetInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.10</remarks>
    public static DiagnosticCode XrefEntryOffsetInvalid { get; } = new(nameof(XrefEntryOffsetInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.10");

    /// <summary>Gets the <c>MissingEndobj</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.10</remarks>
    public static DiagnosticCode MissingEndobj { get; } = new(nameof(MissingEndobj), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.10");

    /// <summary>Gets the <c>ReferenceChainTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.10</remarks>
    public static DiagnosticCode ReferenceChainTooDeep { get; } = new(nameof(ReferenceChainTooDeep), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.10");

    /// <summary>Gets the <c>ObjectReferenceCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode ObjectReferenceCycle { get; } = new(nameof(ObjectReferenceCycle), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.4");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        HeaderMissing,
        HeaderVersionInvalid,
        HeaderInvalid,
        HeaderBinaryCommentMissing,
        StartxrefMissing,
        StartxrefInvalid,
        EndOfFileMarkerMissing,
        EndOfFileMarkerNotLast,
        TrailerIdMissing,
        TrailerMissing,
        TrailerPrevInvalid,
        RootMissing,
        RootNotIndirect,
        CatalogNotFound,
        XrefSectionInvalid,
        XrefEntryInvalid,
        XrefEntryFormatInvalid,
        XrefSubsectionNumberingInvalid,
        XrefSubsectionHeaderInvalid,
        XrefEntryBeyondSize,
        XrefCommentIgnored,
        XrefLineMalformed,
        StartxrefLastDamaged,
        XrefPrevLoop,
        XrefStreamTypeInvalid,
        XrefStreamWidthsInvalid,
        XrefStreamIndexInvalid,
        XrefStreamSizeInvalid,
        CrossReferenceEntryLimitExceeded,
        XrefStreamDataTruncated,
        TrailerXRefStmInvalid,
        ObjectStreamInvalid,
        ObjectStreamTypeInvalid,
        ObjectStreamHeaderInvalid,
        ObjectStreamIndexMismatch,
        ObjectStreamMemberMissing,
        ObjectStreamMemberInvalid,
        ObjectStreamNested,
        ObjectStreamCycle,
        TrailerEntryFromOlderRevision,
        LinearizationDictionaryInvalid,
        LinearizationHintsInvalid,
        LinearizationDropped,
        XrefEntryOffsetInvalid,
        MissingEndobj,
        ReferenceChainTooDeep,
        ObjectReferenceCycle,
    ];
}
