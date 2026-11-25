namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about file specifications, embedded files, collections and associated files.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.11, §12.3.5, §14.13. Each code is reported under <see cref="DiagnosticArea.FileSpecification"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class FileSpecificationDiagnostics
{
    /// <summary>Gets the <c>FileSpecificationInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode FileSpecificationInvalid { get; } = new(nameof(FileSpecificationInvalid), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13",
        "An entry in the embedded files name tree is not a valid file specification; the entry is skipped.");

    /// <summary>Gets the <c>EmbeddedFileMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode EmbeddedFileMissing { get; } = new(nameof(EmbeddedFileMissing), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13",
        "A file specification EF entry holds no embedded file stream; the file is treated as not embedded.");

    /// <summary>Gets the <c>EmbeddedFileSubtypeMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode EmbeddedFileSubtypeMissing { get; } = new(nameof(EmbeddedFileSubtypeMissing), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13",
        "An embedded file used as an associated file has no Subtype giving its MIME type; application/octet-stream is assumed.");

    /// <summary>Gets the <c>EmbeddedFileParamsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode EmbeddedFileParamsInvalid { get; } = new(nameof(EmbeddedFileParamsInvalid), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13",
        "The Params dictionary of an embedded file is malformed or lacks an entry required for an associated file, such as ModDate; the data is read as far as it is valid.");

    /// <summary>Gets the <c>RelatedFilesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode RelatedFilesInvalid { get; } = new(nameof(RelatedFilesInvalid), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13",
        "The RF related files array is not made of pairs of a name string and an embedded file stream; malformed pairs are skipped.");

    /// <summary>Gets the <c>AssociatedFilesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode AssociatedFilesInvalid { get; } = new(nameof(AssociatedFilesInvalid), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13",
        "An AF associated files entry or a file specification in it is malformed, for example it lacks an AFRelationship; invalid items are skipped or Unspecified is used.");

    /// <summary>Gets the <c>CollectionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode CollectionInvalid { get; } = new(nameof(CollectionInvalid), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13",
        "A portable collection dictionary or one of its fields is malformed, for example a navigator view without a Navigator; a default such as the details view is used.");

    /// <summary>Gets the <c>CollectionFolderCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode CollectionFolderCycle { get; } = new(nameof(CollectionFolderCycle), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13",
        "A collection folder Child or Next link leads back to a folder already reached; the link is ignored.");

    /// <summary>Gets the <c>CollectionFolderIdDuplicate</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode CollectionFolderIdDuplicate { get; } = new(nameof(CollectionFolderIdDuplicate), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13",
        "Two collection folders share the same ID; the first folder found keeps the ID.");

    /// <summary>Gets the <c>CollectionFolderInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode CollectionFolderInvalid { get; } = new(nameof(CollectionFolderInvalid), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13",
        "A collection folder dictionary is malformed or has an invalid entry; the folder is read as far as it is valid.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        FileSpecificationInvalid,
        EmbeddedFileMissing,
        EmbeddedFileSubtypeMissing,
        EmbeddedFileParamsInvalid,
        RelatedFilesInvalid,
        AssociatedFilesInvalid,
        CollectionInvalid,
        CollectionFolderCycle,
        CollectionFolderIdDuplicate,
        CollectionFolderInvalid,
    ];
}
