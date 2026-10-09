using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>
/// Reads <c>AF</c> arrays and object-level <c>Metadata</c> entries, and enumerates them over the document (issue #76). Every read is
/// lenient: malformed entries are repaired or skipped with a diagnostic, nothing is written.
/// </summary>
internal static class AssociatedFileReader
{
    /// <summary>Reads an <c>AF</c> value (ISO 32000-2 §14.13.2): an array of file specification dictionaries.</summary>
    public static IReadOnlyList<PdfFileSpecification> Read(PdfDocument document, CosObject? value, CosReference? owner, bool requireRelationship = false)
    {
        var files = new List<PdfFileSpecification>();
        switch (document.Resolve(value))
        {
            case CosNull:
                break;
            case CosDictionary:
                Warn("An AF entry is a single file specification, not an array; it is read as an array of one.");
                Add(value!, wrapped: true);
                break;
            case CosArray array:
                foreach (CosObject entry in array)
                {
                    Add(entry, wrapped: false);
                }

                break;
            default:
                Warn("An AF entry is neither an array nor a file specification; it is ignored.");
                break;
        }

        return files;

        void Add(CosObject entry, bool wrapped)
        {
            CosObject resolved = document.Resolve(entry);
            if (resolved is CosNull)
            {
                return;
            }

            if (PdfFileSpecification.Create(document, entry) is not { } file)
            {
                Warn("An AF array entry is not a file specification; it is skipped.");
                return;
            }

            if (resolved is CosString)
            {
                Warn("An AF array entry is a file specification string; associated files shall be file specification dictionaries. It is read as one.");
            }
            else if (!wrapped)
            {
                Check(document, file, owner, requireRelationship);
            }

            files.Add(file);
        }

        void Warn(string message) => ViewReading.Warn(document, DiagnosticCodes.AssociatedFilesInvalid, message, owner);
    }

    /// <summary>Reads the associated files of a marked-content property list (§14.13.5): an <c>MCAF</c> entry, or the array itself.</summary>
    public static IReadOnlyList<PdfFileSpecification> ReadMarkedContent(PdfDocument document, CosObject? properties, CosReference? owner) =>
        document.Resolve(properties) switch
        {
            CosArray array => Read(document, array, owner),
            CosDictionary dictionary when dictionary.TryGetValue(FileAndLayerNames.MCAF, out CosObject? files) => Read(document, files, owner, requireRelationship: true),
            _ => [],
        };

    /// <summary>Walks the known locations (and, when <paramref name="deep"/>, every object) for <c>AF</c> entries.</summary>
    public static IEnumerable<PdfAssociatedFile> Enumerate(PdfDocument document, bool deep)
    {
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        foreach (ObjectVisit visit in new DocumentObjectWalker(document).Walk())
        {
            seen.Add(visit.Value);
            if (visit.Role == ObjectRole.PropertyList)
            {
                foreach (PdfFileSpecification file in ReadMarkedContent(document, visit.Value, visit.Reference))
                {
                    yield return new PdfAssociatedFile(file, PdfAssociatedFileLocation.MarkedContent, visit.Value, visit.Reference, visit.PageIndex, visit.PropertyName);
                }
            }
            else
            {
                foreach (PdfAssociatedFile file in FromOwner(document, visit.Value, visit.Dictionary!, visit.Reference, LocationOf(visit.Role), visit.PageIndex))
                {
                    yield return file;
                }
            }

            if (visit.Dictionary is { } owner && MetadataOf(document, owner, visit.Reference) is { } metadata && seen.Add(metadata.Stream))
            {
                foreach (PdfAssociatedFile file in FromOwner(document, metadata.Stream, metadata.Stream.Dictionary, metadata.Reference, PdfAssociatedFileLocation.MetadataStream, visit.PageIndex))
                {
                    yield return file;
                }
            }
        }

        if (!deep)
        {
            yield break;
        }

        foreach (CosReference reference in document.EnumerateObjectReferences())
        {
            CosObject value = document.Resolve(reference);
            CosDictionary? dictionary = value switch
            {
                CosStream stream => stream.Dictionary,
                CosDictionary plain => plain,
                _ => null,
            };
            if (dictionary is null || !seen.Add(value))
            {
                continue;
            }

            PdfAssociatedFileLocation location = ViewReading.HasType(document, dictionary, FileAndLayerNames.Metadata)
                ? PdfAssociatedFileLocation.MetadataStream
                : PdfAssociatedFileLocation.Other;
            foreach (PdfAssociatedFile file in FromOwner(document, value, dictionary, reference, location, pageIndex: null))
            {
                yield return file;
            }
        }
    }

    /// <summary>Walks the known locations (and, when <paramref name="deep"/>, every object) for <c>Metadata</c> entries.</summary>
    public static IEnumerable<PdfObjectMetadata> EnumerateMetadata(PdfDocument document, bool deep)
    {
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        foreach (ObjectVisit visit in new DocumentObjectWalker(document).Walk())
        {
            seen.Add(visit.Value);
            if (visit.Dictionary is { } owner && MetadataOf(document, owner, visit.Reference) is { } metadata)
            {
                seen.Add(metadata.Stream);
                yield return new PdfObjectMetadata(document, metadata.Stream, metadata.Reference, MetadataLocationOf(visit.Role), visit.Value, visit.Reference, visit.PageIndex);
            }
        }

        if (!deep)
        {
            yield break;
        }

        foreach (CosReference reference in document.EnumerateObjectReferences())
        {
            CosObject value = document.Resolve(reference);
            CosDictionary? dictionary = value switch
            {
                CosStream stream => stream.Dictionary,
                CosDictionary plain => plain,
                _ => null,
            };
            if (dictionary is not null && seen.Add(value) && MetadataOf(document, dictionary, reference) is { } metadata)
            {
                yield return new PdfObjectMetadata(document, metadata.Stream, metadata.Reference, PdfMetadataLocation.Other, value, reference, null);
            }
        }
    }

    /// <summary>The metadata stream in <paramref name="owner"/>'s <c>Metadata</c> entry, checked against Table 347.</summary>
    public static (CosStream Stream, CosReference? Reference)? MetadataOf(PdfDocument document, CosDictionary owner, CosReference? ownerReference)
    {
        if (!owner.TryGetValue(FileAndLayerNames.Metadata, out CosObject? entry))
        {
            return null;
        }

        CosReference? reference = entry as CosReference;
        switch (document.Resolve(entry))
        {
            case CosNull:
                return null;
            case CosStream stream:
                if (!ViewReading.HasType(document, stream.Dictionary, FileAndLayerNames.Metadata)
                    || ViewReading.Name(document, stream.Dictionary, FileAndLayerNames.Subtype) is not { Value: "XML" })
                {
                    ViewReading.Warn(document, DiagnosticCodes.MetadataStreamInvalid, "A metadata stream shall have Type Metadata and Subtype XML; it is read as XMP anyway.", reference ?? ownerReference);
                }

                return (stream, reference);
            default:
                ViewReading.Warn(document, DiagnosticCodes.MetadataStreamInvalid, "A Metadata entry is not a stream; it is ignored.", ownerReference);
                return null;
        }
    }

    private static IEnumerable<PdfAssociatedFile> FromOwner(PdfDocument document, CosObject owner, CosDictionary dictionary, CosReference? reference, PdfAssociatedFileLocation location, int? pageIndex)
    {
        if (!dictionary.TryGetValue(FileAndLayerNames.AF, out CosObject? value))
        {
            return [];
        }

        return Read(document, value, reference).Select(file => new PdfAssociatedFile(file, location, owner, reference, pageIndex, null));
    }

    /// <summary>The checks §14.13.2 makes of an embedded associated file: <c>Subtype</c> shall be present; <c>Params</c> with <c>ModDate</c>.</summary>
    private static void Check(PdfDocument document, PdfFileSpecification file, CosReference? owner, bool requireRelationship)
    {
        CosReference? where = file.Reference ?? owner;
        if (requireRelationship && file.Dictionary is { } dictionary && !dictionary.ContainsKey(FileAndLayerNames.AFRelationship))
        {
            ViewReading.Warn(document, DiagnosticCodes.AssociatedFilesInvalid, "A file specification in a marked-content MCAF array shall have an AFRelationship entry; Unspecified is used.", where);
        }

        if (file.EmbeddedFile is not { } embedded)
        {
            return;
        }

        if (embedded.Subtype is null)
        {
            ViewReading.Warn(document, DiagnosticCodes.EmbeddedFileSubtypeMissing, "An embedded file used as an associated file shall have a Subtype (MIME type); application/octet-stream is assumed.", embedded.Reference ?? where);
        }

        if (embedded.Parameters is { } parameters && parameters.ModificationDate is null)
        {
            ViewReading.Warn(document, DiagnosticCodes.EmbeddedFileParamsInvalid, "The parameters of an embedded file used as an associated file shall have a ModDate.", embedded.Reference ?? where);
        }
    }

    private static PdfAssociatedFileLocation LocationOf(ObjectRole role) => role switch
    {
        ObjectRole.Catalog => PdfAssociatedFileLocation.Catalog,
        ObjectRole.Page => PdfAssociatedFileLocation.Page,
        ObjectRole.FormXObject => PdfAssociatedFileLocation.FormXObject,
        ObjectRole.ImageXObject => PdfAssociatedFileLocation.ImageXObject,
        ObjectRole.Annotation => PdfAssociatedFileLocation.Annotation,
        ObjectRole.StructureTreeRoot => PdfAssociatedFileLocation.StructureTreeRoot,
        ObjectRole.StructureElement => PdfAssociatedFileLocation.StructureElement,
        ObjectRole.DocumentPart => PdfAssociatedFileLocation.DocumentPart,
        ObjectRole.PropertyList => PdfAssociatedFileLocation.MarkedContent,
        _ => PdfAssociatedFileLocation.Other,
    };

    private static PdfMetadataLocation MetadataLocationOf(ObjectRole role) => role switch
    {
        ObjectRole.Catalog => PdfMetadataLocation.Document,
        ObjectRole.OptionalContentGroup => PdfMetadataLocation.OptionalContentGroup,
        ObjectRole.Thread => PdfMetadataLocation.Thread,
        ObjectRole.StructureElement => PdfMetadataLocation.StructureElement,
        ObjectRole.DocumentPart => PdfMetadataLocation.DocumentPart,
        ObjectRole.EmbeddedFile => PdfMetadataLocation.EmbeddedFile,
        ObjectRole.Page => PdfMetadataLocation.Page,
        ObjectRole.FormXObject => PdfMetadataLocation.FormXObject,
        ObjectRole.ImageXObject => PdfMetadataLocation.ImageXObject,
        ObjectRole.IccProfile => PdfMetadataLocation.IccProfile,
        ObjectRole.FontProgram => PdfMetadataLocation.FontProgram,
        ObjectRole.Type3Font => PdfMetadataLocation.Type3Font,
        ObjectRole.TilingPattern => PdfMetadataLocation.TilingPattern,
        ObjectRole.Shading => PdfMetadataLocation.Shading,
        ObjectRole.PropertyList => PdfMetadataLocation.MarkedContent,
        ObjectRole.Annotation => PdfMetadataLocation.Annotation,
        ObjectRole.ThreeD => PdfMetadataLocation.ThreeD,
        _ => PdfMetadataLocation.Other,
    };
}
