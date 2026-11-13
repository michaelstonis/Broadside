using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes of optional content, file specifications, collections, associated files and object metadata (issue #76).</summary>
internal static partial class DiagnosticCodes
{
    // Optional content (§8.11; issue #76).
    public static string OptionalContentConfigMissing => OptionalContentDiagnostics.OptionalContentConfigMissing.Name;
    public static string OptionalContentGroupsMissing => OptionalContentDiagnostics.OptionalContentGroupsMissing.Name;
    public static string OptionalContentGroupNotListed => OptionalContentDiagnostics.OptionalContentGroupNotListed.Name;
    public static string OptionalContentGroupInvalid => OptionalContentDiagnostics.OptionalContentGroupInvalid.Name;
    public static string OptionalContentBaseStateInvalid => OptionalContentDiagnostics.OptionalContentBaseStateInvalid.Name;
    public static string OptionalContentIntentInvalid => OptionalContentDiagnostics.OptionalContentIntentInvalid.Name;
    public static string OptionalContentGroupOnAndOff => OptionalContentDiagnostics.OptionalContentGroupOnAndOff.Name;
    public static string VisibilityExpressionInvalid => OptionalContentDiagnostics.VisibilityExpressionInvalid.Name;
    public static string VisibilityPolicyInvalid => OptionalContentDiagnostics.VisibilityPolicyInvalid.Name;
    public static string OptionalContentInlineProperties => OptionalContentDiagnostics.OptionalContentInlineProperties.Name;

    // File specifications, embedded files, collections and associated files (§7.11, §12.3.5, §14.13; issue #76).
    public static string FileSpecificationInvalid => FileSpecificationDiagnostics.FileSpecificationInvalid.Name;
    public static string EmbeddedFileMissing => FileSpecificationDiagnostics.EmbeddedFileMissing.Name;
    public static string EmbeddedFileSubtypeMissing => FileSpecificationDiagnostics.EmbeddedFileSubtypeMissing.Name;
    public static string EmbeddedFileParamsInvalid => FileSpecificationDiagnostics.EmbeddedFileParamsInvalid.Name;
    public static string RelatedFilesInvalid => FileSpecificationDiagnostics.RelatedFilesInvalid.Name;
    public static string AssociatedFilesInvalid => FileSpecificationDiagnostics.AssociatedFilesInvalid.Name;
    public static string CollectionInvalid => FileSpecificationDiagnostics.CollectionInvalid.Name;
    public static string CollectionFolderCycle => FileSpecificationDiagnostics.CollectionFolderCycle.Name;
    public static string CollectionFolderIdDuplicate => FileSpecificationDiagnostics.CollectionFolderIdDuplicate.Name;
    public static string CollectionFolderInvalid => FileSpecificationDiagnostics.CollectionFolderInvalid.Name;

    // Unencrypted wrapper documents (§7.6.7; issue #234).
    public static string UnencryptedWrapperInvalid => SecurityDiagnostics.UnencryptedWrapperInvalid.Name;
    public static string EncryptedPayloadFilterUnknown => SecurityDiagnostics.EncryptedPayloadFilterUnknown.Name;

    // Object-level metadata (§14.3.2; issue #76) reports MetadataStreamInvalid from DiagnosticCodes.Catalog.cs.
}
