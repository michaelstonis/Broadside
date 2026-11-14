namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about file specifications, embedded files, collections and associated files.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.11, §12.3.5, §14.13. Each code is reported under <see cref="DiagnosticArea.FileSpecification"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class FileSpecificationDiagnostics
{
    /// <summary>Gets the <c>FileSpecificationInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode FileSpecificationInvalid { get; } = new(nameof(FileSpecificationInvalid), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13");

    /// <summary>Gets the <c>EmbeddedFileMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode EmbeddedFileMissing { get; } = new(nameof(EmbeddedFileMissing), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13");

    /// <summary>Gets the <c>EmbeddedFileSubtypeMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode EmbeddedFileSubtypeMissing { get; } = new(nameof(EmbeddedFileSubtypeMissing), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13");

    /// <summary>Gets the <c>EmbeddedFileParamsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode EmbeddedFileParamsInvalid { get; } = new(nameof(EmbeddedFileParamsInvalid), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13");

    /// <summary>Gets the <c>RelatedFilesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode RelatedFilesInvalid { get; } = new(nameof(RelatedFilesInvalid), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13");

    /// <summary>Gets the <c>AssociatedFilesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode AssociatedFilesInvalid { get; } = new(nameof(AssociatedFilesInvalid), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13");

    /// <summary>Gets the <c>CollectionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode CollectionInvalid { get; } = new(nameof(CollectionInvalid), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13");

    /// <summary>Gets the <c>CollectionFolderCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode CollectionFolderCycle { get; } = new(nameof(CollectionFolderCycle), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13");

    /// <summary>Gets the <c>CollectionFolderIdDuplicate</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode CollectionFolderIdDuplicate { get; } = new(nameof(CollectionFolderIdDuplicate), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13");

    /// <summary>Gets the <c>CollectionFolderInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.11, §12.3.5, §14.13</remarks>
    public static DiagnosticCode CollectionFolderInvalid { get; } = new(nameof(CollectionFolderInvalid), DiagnosticArea.FileSpecification, DiagnosticSeverity.Warning, "ISO 32000-2 §7.11, §12.3.5, §14.13");

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
