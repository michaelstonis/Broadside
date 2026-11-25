namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the file structure: header, trailer, cross-reference tables and streams, object streams, incremental updates, linearization and indirect objects.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.5, §7.3.10, Annex F. Each code is reported under <see cref="DiagnosticArea.FileStructure"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class FileStructureDiagnostics
{
    /// <summary>Gets the <c>HeaderMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.2</remarks>
    public static DiagnosticCode HeaderMissing { get; } = new(nameof(HeaderMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.2",
        "The file does not start with a %PDF- header. Offsets are counted from the first byte, and the version is taken from the catalog.");

    /// <summary>Gets the <c>HeaderVersionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.2</remarks>
    public static DiagnosticCode HeaderVersionInvalid { get; } = new(nameof(HeaderVersionInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.2",
        "The header does not state a version of the form 1.n or 2.n. The version is taken from the catalog instead.");

    /// <summary>Gets the <c>HeaderInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.2</remarks>
    public static DiagnosticCode HeaderInvalid { get; } = new(nameof(HeaderInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.2",
        "The header line deviates from the required form, for example the version is not followed by an end-of-line marker. The version is read and the rest of the line is ignored.");

    /// <summary>Gets the <c>HeaderBinaryCommentMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.2</remarks>
    public static DiagnosticCode HeaderBinaryCommentMissing { get; } = new(nameof(HeaderBinaryCommentMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.2",
        "The file holds binary data but the header line is not followed by a comment line with at least four bytes of 128 or more. The file is read as is.");

    /// <summary>Gets the <c>StartxrefMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode StartxrefMissing { get; } = new(nameof(StartxrefMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5",
        "The file has no startxref keyword. The cross-reference information is rebuilt by scanning the file.");

    /// <summary>Gets the <c>StartxrefInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode StartxrefInvalid { get; } = new(nameof(StartxrefInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5",
        "The startxref keyword is missing a usable offset or the offset does not point at a cross-reference section. The nearest section is read, or the cross-reference information is rebuilt by scanning the file.");

    /// <summary>Gets the <c>EndOfFileMarkerMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode EndOfFileMarkerMissing { get; } = new(nameof(EndOfFileMarkerMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5",
        "The startxref offset, or the trailer of an earlier revision, is not followed by the end-of-file marker. The revision is read up to that point.");

    /// <summary>Gets the <c>EndOfFileMarkerNotLast</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode EndOfFileMarkerNotLast { get; } = new(nameof(EndOfFileMarkerNotLast), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5",
        "Data follows the last end-of-file marker, although the marker should be on the last line of the file. The trailing data is ignored.");

    /// <summary>Gets the <c>TrailerIdMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode TrailerIdMissing { get; } = new(nameof(TrailerIdMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5",
        "The document is PDF 2.0, whose trailer shall have an ID entry, but the trailer has none. The document is read without file identifiers.");

    /// <summary>Gets the <c>TrailerMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode TrailerMissing { get; } = new(nameof(TrailerMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.5",
        "A cross-reference section has no trailer keyword or dictionary, or the file has no trailer at all. The catalog is located by scanning the file when possible.");

    /// <summary>Gets the <c>TrailerPrevInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode TrailerPrevInvalid { get; } = new(nameof(TrailerPrevInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5",
        "The trailer Prev entry is not a usable non-negative integer or its offset does not point at a cross-reference section. The nearest section is read instead, or older sections are not read.");

    /// <summary>Gets the <c>RootMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode RootMissing { get; } = new(nameof(RootMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5",
        "The trailer has no Root entry that resolves to the catalog dictionary. The cross-reference information is rebuilt by scanning the file for a catalog.");

    /// <summary>Gets the <c>RootNotIndirect</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode RootNotIndirect { get; } = new(nameof(RootNotIndirect), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5",
        "The trailer Root entry is a direct dictionary instead of an indirect reference. The dictionary is used as the catalog.");

    /// <summary>Gets the <c>CatalogNotFound</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5</remarks>
    public static DiagnosticCode CatalogNotFound { get; } = new(nameof(CatalogNotFound), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5",
        "The cross-reference information cannot be read and scanning the file finds no catalog dictionary. The file cannot be read.");

    /// <summary>Gets the <c>XrefSectionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefSectionInvalid { get; } = new(nameof(XrefSectionInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.4",
        "The data at a cross-reference offset is not a valid cross-reference table or stream, for example a bad subsection header or a non-stream object. The section cannot be read.");

    /// <summary>Gets the <c>XrefEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefEntryInvalid { get; } = new(nameof(XrefEntryInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.4",
        "A cross-reference entry is not in the required form of offset, generation and in-use flag, or has an unknown type. The entry cannot be read as stated.");

    /// <summary>Gets the <c>XrefEntryFormatInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefEntryFormatInvalid { get; } = new(nameof(XrefEntryFormatInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.4",
        "A cross-reference table entry is not exactly 20 bytes long. The section is read entry by entry rather than by fixed width.");

    /// <summary>Gets the <c>XrefSubsectionNumberingInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefSubsectionNumberingInvalid { get; } = new(nameof(XrefSubsectionNumberingInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.4",
        "A cross-reference subsection starts at object 1 although its first entry is the head of the free list, which is object 0. The subsection is read as starting at 0.");

    /// <summary>Gets the <c>XrefSubsectionHeaderInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefSubsectionHeaderInvalid { get; } = new(nameof(XrefSubsectionHeaderInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.4",
        "A subsection header is not a line of exactly two integers separated by one space. The header is read as tokens.");

    /// <summary>Gets the <c>XrefEntryBeyondSize</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode XrefEntryBeyondSize { get; } = new(nameof(XrefEntryBeyondSize), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5",
        "The cross-reference table lists object numbers at or beyond the Size entry of the trailer. The extra entries are kept.");

    /// <summary>Gets the <c>XrefCommentIgnored</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefCommentIgnored { get; } = new(nameof(XrefCommentIgnored), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.4",
        "A cross-reference table has a comment between its tokens, which the table syntax does not allow. The comment is skipped.");

    /// <summary>Gets the <c>XrefLineMalformed</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefLineMalformed { get; } = new(nameof(XrefLineMalformed), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.4",
        "A line of the cross-reference table is neither a subsection header nor an entry. The line is skipped.");

    /// <summary>Gets the <c>StartxrefLastDamaged</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.5</remarks>
    public static DiagnosticCode StartxrefLastDamaged { get; } = new(nameof(StartxrefLastDamaged), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.5",
        "The last startxref is damaged: the one found is followed by the trailer of a newer revision. The cross-reference information is rebuilt by scanning the file.");

    /// <summary>Gets the <c>XrefPrevLoop</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode XrefPrevLoop { get; } = new(nameof(XrefPrevLoop), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.4",
        "The Prev or XRefStm chain of cross-reference sections loops back to a section already read. The chain ends there.");

    /// <summary>Gets the <c>XrefStreamTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode XrefStreamTypeInvalid { get; } = new(nameof(XrefStreamTypeInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.8",
        "The Type entry of a cross-reference stream is not XRef. The stream is read as a cross-reference stream anyway.");

    /// <summary>Gets the <c>XrefStreamWidthsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode XrefStreamWidthsInvalid { get; } = new(nameof(XrefStreamWidthsInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.8",
        "The W entry of a cross-reference stream is not an array of three direct integers with a positive sum. The section cannot be read.");

    /// <summary>Gets the <c>XrefStreamIndexInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode XrefStreamIndexInvalid { get; } = new(nameof(XrefStreamIndexInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.8",
        "The Index entry of a cross-reference stream is not an array of pairs of direct non-negative integers. The default range from 0 to Size is used.");

    /// <summary>Gets the <c>XrefStreamSizeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode XrefStreamSizeInvalid { get; } = new(nameof(XrefStreamSizeInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.8",
        "The Size entry of a cross-reference stream is not a direct non-negative integer, or entries lie at or beyond it. The entries are read as far as the data goes and kept.");

    /// <summary>Gets the <c>CrossReferenceEntryLimitExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode CrossReferenceEntryLimitExceeded { get; } = new(nameof(CrossReferenceEntryLimitExceeded), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.8",
        "The cross-reference data describes more objects than the file has bytes. The entries beyond the limit are not read.");

    /// <summary>Gets the <c>XrefStreamDataTruncated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode XrefStreamDataTruncated { get; } = new(nameof(XrefStreamDataTruncated), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.8",
        "The data of a cross-reference stream ends before the last entry described by its Index and W entries. The missing entries are absent.");

    /// <summary>Gets the <c>TrailerXRefStmInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.8</remarks>
    public static DiagnosticCode TrailerXRefStmInvalid { get; } = new(nameof(TrailerXRefStmInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.8",
        "The trailer XRefStm entry is not a usable offset or does not point at a cross-reference stream. The nearest stream is read when one is found, otherwise the stream is not read.");

    /// <summary>Gets the <c>ObjectStreamInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamInvalid { get; } = new(nameof(ObjectStreamInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.7",
        "An object stream is missing, is not a stream, or has invalid N or First entries. The objects it should hold are read as null.");

    /// <summary>Gets the <c>ObjectStreamTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamTypeInvalid { get; } = new(nameof(ObjectStreamTypeInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.7",
        "The Type entry of an object stream is not ObjStm. The stream is read as an object stream anyway.");

    /// <summary>Gets the <c>ObjectStreamHeaderInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamHeaderInvalid { get; } = new(nameof(ObjectStreamHeaderInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.7",
        "The header of an object stream has the wrong number of pairs, an invalid pair, or offsets that do not increase. The pairs present are used and objects that cannot be located are not read.");

    /// <summary>Gets the <c>ObjectStreamIndexMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamIndexMismatch { get; } = new(nameof(ObjectStreamIndexMismatch), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.7",
        "The cross-reference entry gives an index in the object stream that does not match the object's actual position. The object is read from the actual position.");

    /// <summary>Gets the <c>ObjectStreamMemberMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamMemberMissing { get; } = new(nameof(ObjectStreamMemberMissing), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.7",
        "The cross-reference entry places an object in an object stream that does not hold it. The object is read as null.");

    /// <summary>Gets the <c>ObjectStreamMemberInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamMemberInvalid { get; } = new(nameof(ObjectStreamMemberInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.7",
        "A stream object is stored inside an object stream, which is not allowed. The stream is read anyway.");

    /// <summary>Gets the <c>ObjectStreamNested</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamNested { get; } = new(nameof(ObjectStreamNested), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.7",
        "The object stream holding an object is itself listed as stored in an object stream, which a stream cannot be. The object is read as null.");

    /// <summary>Gets the <c>ObjectStreamCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.7</remarks>
    public static DiagnosticCode ObjectStreamCycle { get; } = new(nameof(ObjectStreamCycle), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.7",
        "Decoding an object stream requires an object that the same object stream holds. The object is read as null.");

    /// <summary>Gets the <c>TrailerEntryFromOlderRevision</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.6</remarks>
    public static DiagnosticCode TrailerEntryFromOlderRevision { get; } = new(nameof(TrailerEntryFromOlderRevision), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.6",
        "The newest trailer lacks an entry that every update trailer should repeat. The entry of an older revision trailer is used.");

    /// <summary>Gets the <c>LinearizationDictionaryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 Annex F</remarks>
    public static DiagnosticCode LinearizationDictionaryInvalid { get; } = new(nameof(LinearizationDictionaryInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 Annex F",
        "The linearization parameter dictionary lacks a required entry or has an entry of the wrong type. The file is read as not linearized.");

    /// <summary>Gets the <c>LinearizationHintsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 Annex F</remarks>
    public static DiagnosticCode LinearizationHintsInvalid { get; } = new(nameof(LinearizationHintsInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 Annex F",
        "The linearization hint tables are malformed or do not locate the pages they describe. The hints are ignored and the pages are read from the page tree.");

    /// <summary>Gets the <c>LinearizationDropped</c> code.</summary>
    /// <remarks>ISO 32000-2 Annex F</remarks>
    public static DiagnosticCode LinearizationDropped { get; } = new(nameof(LinearizationDropped), DiagnosticArea.FileStructure, DiagnosticSeverity.Information, "ISO 32000-2 Annex F",
        "An incremental update leaves a linearized file unlinearized, because the length recorded in the linearization dictionary no longer matches the file. This records that the file is no longer linearized.");

    /// <summary>Gets the <c>XrefEntryOffsetInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.10</remarks>
    public static DiagnosticCode XrefEntryOffsetInvalid { get; } = new(nameof(XrefEntryOffsetInvalid), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.10",
        "A cross-reference entry offset does not point at the object header. The object is read from where its header is found, or as null when none exists.");

    /// <summary>Gets the <c>MissingEndobj</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.10</remarks>
    public static DiagnosticCode MissingEndobj { get; } = new(nameof(MissingEndobj), DiagnosticArea.FileStructure, DiagnosticSeverity.Warning, "ISO 32000-2 §7.3.10",
        "An object is not followed by the endobj keyword. The object ends at that point.");

    /// <summary>Gets the <c>ReferenceChainTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.3.10</remarks>
    public static DiagnosticCode ReferenceChainTooDeep { get; } = new(nameof(ReferenceChainTooDeep), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.3.10",
        "Loading an object needs more nested object loads than the allowed depth. The object is read as null.");

    /// <summary>Gets the <c>ObjectReferenceCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4</remarks>
    public static DiagnosticCode ObjectReferenceCycle { get; } = new(nameof(ObjectReferenceCycle), DiagnosticArea.FileStructure, DiagnosticSeverity.Error, "ISO 32000-2 §7.5.4",
        "Loading an object requires the object itself, for example a stream Length that refers back to it. The object is read as null where it is needed.");

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
