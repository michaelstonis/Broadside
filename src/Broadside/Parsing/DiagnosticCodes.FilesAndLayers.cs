namespace Broadside.Parsing;

/// <summary>Diagnostic codes of optional content, file specifications, collections, associated files and object metadata (issue #76).</summary>
internal static partial class DiagnosticCodes
{
    // Optional content (§8.11; issue #76).
    public const string OptionalContentConfigMissing = nameof(OptionalContentConfigMissing);
    public const string OptionalContentGroupsMissing = nameof(OptionalContentGroupsMissing);
    public const string OptionalContentGroupNotListed = nameof(OptionalContentGroupNotListed);
    public const string OptionalContentGroupInvalid = nameof(OptionalContentGroupInvalid);
    public const string OptionalContentBaseStateInvalid = nameof(OptionalContentBaseStateInvalid);
    public const string OptionalContentIntentInvalid = nameof(OptionalContentIntentInvalid);
    public const string OptionalContentGroupOnAndOff = nameof(OptionalContentGroupOnAndOff);
    public const string VisibilityExpressionInvalid = nameof(VisibilityExpressionInvalid);
    public const string VisibilityPolicyInvalid = nameof(VisibilityPolicyInvalid);
    public const string OptionalContentInlineProperties = nameof(OptionalContentInlineProperties);

    // File specifications, embedded files, collections and associated files (§7.11, §12.3.5, §14.13; issue #76).
    public const string FileSpecificationInvalid = nameof(FileSpecificationInvalid);
    public const string EmbeddedFileMissing = nameof(EmbeddedFileMissing);
    public const string EmbeddedFileSubtypeMissing = nameof(EmbeddedFileSubtypeMissing);
    public const string EmbeddedFileParamsInvalid = nameof(EmbeddedFileParamsInvalid);
    public const string RelatedFilesInvalid = nameof(RelatedFilesInvalid);
    public const string AssociatedFilesInvalid = nameof(AssociatedFilesInvalid);
    public const string CollectionInvalid = nameof(CollectionInvalid);
    public const string CollectionFolderCycle = nameof(CollectionFolderCycle);
    public const string CollectionFolderIdDuplicate = nameof(CollectionFolderIdDuplicate);

    // Object-level metadata (§14.3.2; issue #76) reports MetadataStreamInvalid from DiagnosticCodes.Catalog.cs.
}
