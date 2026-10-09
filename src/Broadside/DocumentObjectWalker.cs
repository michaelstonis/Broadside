using Broadside.Objects;

namespace Broadside;

/// <summary>What a visited object is, by where the walk found it.</summary>
internal enum ObjectRole : byte
{
    Catalog,
    OptionalContentGroup,
    Thread,
    StructureTreeRoot,
    StructureElement,
    DocumentPart,
    EmbeddedFile,
    Page,
    FormXObject,
    ImageXObject,
    IccProfile,
    FontProgram,
    Type3Font,
    TilingPattern,
    Shading,
    PropertyList,
    Annotation,
    ThreeD,
}

/// <summary>One object the walk found.</summary>
/// <param name="Value">The dictionary or stream.</param>
/// <param name="Reference">Its indirect reference, when it was reached through one.</param>
/// <param name="Role">What it is.</param>
/// <param name="PageIndex">The page it was found under, or <see langword="null"/> for document-level objects.</param>
/// <param name="PropertyName">For a marked-content property list, its name in the <c>Properties</c> resource.</param>
internal readonly record struct ObjectVisit(CosObject Value, CosReference? Reference, ObjectRole Role, int? PageIndex, string? PropertyName = null)
{
    /// <summary>Gets the dictionary of the object (a stream's dictionary for a stream); <see langword="null"/> for a property list that is an array.</summary>
    public CosDictionary? Dictionary => Value switch
    {
        CosStream stream => stream.Dictionary,
        CosDictionary dictionary => dictionary,
        _ => null,
    };
}

/// <summary>
/// Walks the places of a document where associated files (ISO 32000-2 §14.13, PDF 2.0 Application Note 002) and object-level
/// metadata (§14.3.2, Application Note 003) can be: the catalog, optional content groups, threads, the structure tree, document
/// parts, embedded files, then each page with its resources (recursively through forms, patterns and Type 3 fonts) and annotations.
/// Every object is visited once (one visited set for the whole walk); recursion uses an explicit stack. Lazy; reads only.
/// </summary>
internal sealed class DocumentObjectWalker(PdfDocument document)
{
    private const int MaxColorSpaceDepth = 8;

    private readonly HashSet<CosObject> _visited = new(ReferenceEqualityComparer.Instance);

    public IEnumerable<ObjectVisit> Walk()
    {
        CosDictionary catalog = document.Catalog;
        _visited.Add(catalog);
        yield return new ObjectVisit(catalog, RootReference(), ObjectRole.Catalog, null);

        if (document.OptionalContent is { } optionalContent)
        {
            foreach (PdfOptionalContentGroup group in optionalContent.Groups)
            {
                if (_visited.Add(group.Dictionary))
                {
                    yield return new ObjectVisit(group.Dictionary, group.Reference, ObjectRole.OptionalContentGroup, null);
                }
            }
        }

        if (Get(catalog, Names.Threads) is CosArray threads)
        {
            foreach (CosObject thread in threads)
            {
                if (Visit(thread, ObjectRole.Thread, null) is { } visit)
                {
                    yield return visit;
                }
            }
        }

        foreach (ObjectVisit visit in WalkStructure(catalog))
        {
            yield return visit;
        }

        foreach (ObjectVisit visit in WalkDocumentParts(catalog))
        {
            yield return visit;
        }

        foreach (PdfEmbeddedFileEntry entry in document.EmbeddedFiles)
        {
            if (entry.File.EmbeddedFile is { } file && _visited.Add(file.Stream))
            {
                yield return new ObjectVisit(file.Stream, file.Reference, ObjectRole.EmbeddedFile, null);
            }
        }

        for (int index = 0; index < document.Pages.Count; index++)
        {
            PdfPage page = document.Pages[index];
            if (!_visited.Add(page.Dictionary))
            {
                continue;
            }

            yield return new ObjectVisit(page.Dictionary, page.Reference, ObjectRole.Page, index);
            foreach (ObjectVisit visit in WalkResources(page.Resources, index))
            {
                yield return visit;
            }

            if (Get(page.Dictionary, FileAndLayerNames.Annots) is CosArray annotations)
            {
                foreach (CosObject entry in annotations)
                {
                    if (Visit(entry, ObjectRole.Annotation, index) is not { } annotation)
                    {
                        continue;
                    }

                    yield return annotation;
                    foreach (ObjectVisit visit in WalkAnnotation(annotation.Dictionary!, index))
                    {
                        yield return visit;
                    }
                }
            }
        }
    }

    private CosReference? RootReference() =>
        document.Trailer.TryGetValue(KnownNames.Root, out CosObject? root) ? root as CosReference : null;

    private CosObject? Get(CosDictionary dictionary, CosName key) => ViewReading.Get(document, dictionary, key);

    /// <summary>Visits <paramref name="entry"/> when it resolves to a dictionary or stream not visited yet.</summary>
    private ObjectVisit? Visit(CosObject entry, ObjectRole role, int? page, string? propertyName = null)
    {
        CosObject resolved = document.Resolve(entry);
        return resolved is CosDictionary or CosStream && _visited.Add(resolved)
            ? new ObjectVisit(resolved, entry as CosReference, role, page, propertyName)
            : null;
    }

    private IEnumerable<ObjectVisit> WalkStructure(CosDictionary catalog)
    {
        if (!catalog.TryGetValue(Names.StructTreeRoot, out CosObject? rootEntry) || Visit(rootEntry, ObjectRole.StructureTreeRoot, null) is not { } root)
        {
            yield break;
        }

        yield return root;
        var pending = new Stack<CosObject>();
        PushChildren(root.Dictionary!, pending);
        while (pending.TryPop(out CosObject? entry))
        {
            CosObject resolved = document.Resolve(entry);
            if (resolved is CosArray array)
            {
                if (_visited.Add(array))
                {
                    for (int index = array.Count - 1; index >= 0; index--)
                    {
                        pending.Push(array[index]);
                    }
                }

                continue;
            }

            if (resolved is not CosDictionary element
                || ViewReading.Name(document, element, KnownNames.Type) is { Value: "MCR" or "OBJR" }
                || Visit(entry, ObjectRole.StructureElement, null) is not { } visit)
            {
                continue;
            }

            yield return visit;
            PushChildren(element, pending);
        }

        static void PushChildren(CosDictionary node, Stack<CosObject> pending)
        {
            if (node.TryGetValue(Names.K, out CosObject? kids))
            {
                pending.Push(kids);
            }
        }
    }

    private IEnumerable<ObjectVisit> WalkDocumentParts(CosDictionary catalog)
    {
        if (Get(catalog, Names.DPartRoot) is not CosDictionary partRoot || !partRoot.TryGetValue(Names.DPartRootNode, out CosObject? node))
        {
            yield break;
        }

        var pending = new Stack<CosObject>();
        pending.Push(node);
        while (pending.TryPop(out CosObject? entry))
        {
            if (Visit(entry, ObjectRole.DocumentPart, null) is not { } part)
            {
                continue;
            }

            yield return part;
            if (Get(part.Dictionary!, Names.DParts) is CosArray levels)
            {
                var children = new List<CosObject>();
                foreach (CosObject level in levels)
                {
                    if (document.Resolve(level) is CosArray parts)
                    {
                        children.AddRange(parts);
                    }
                }

                for (int index = children.Count - 1; index >= 0; index--)
                {
                    pending.Push(children[index]);
                }
            }
        }
    }

    private IEnumerable<ObjectVisit> WalkAnnotation(CosDictionary annotation, int page)
    {
        if (Get(annotation, Names.FS) is { } fileSpecification
            && PdfFileSpecification.Create(document, fileSpecification) is { Dictionary: not null } spec
            && spec.EmbeddedFile is { } file
            && _visited.Add(file.Stream))
        {
            yield return new ObjectVisit(file.Stream, file.Reference, ObjectRole.EmbeddedFile, page);
        }

        if (annotation.TryGetValue(Names.ThreeDD, out CosObject? artwork) && Visit(artwork, ObjectRole.ThreeD, page) is { } threeD)
        {
            yield return threeD;
        }

        if (Get(annotation, Names.AP) is CosDictionary appearances)
        {
            foreach (KeyValuePair<CosName, CosObject> appearance in appearances)
            {
                IEnumerable<CosObject> streams = document.Resolve(appearance.Value) switch
                {
                    CosStream => [appearance.Value],
                    CosDictionary states => states.Values,
                    _ => [],
                };
                foreach (CosObject stream in streams)
                {
                    if (document.Resolve(stream) is CosStream && Visit(stream, ObjectRole.FormXObject, page) is { } form)
                    {
                        yield return form;
                        foreach (ObjectVisit visit in WalkResources(Get(form.Dictionary!, KnownNames.Resources) as CosDictionary, page))
                        {
                            yield return visit;
                        }
                    }
                }
            }
        }
    }

    /// <summary>Walks a resource dictionary and every resource dictionary it leads to, with an explicit stack.</summary>
    private IEnumerable<ObjectVisit> WalkResources(CosDictionary? resources, int page)
    {
        var pending = new Stack<CosDictionary>();
        if (resources is not null && _visited.Add(resources))
        {
            pending.Push(resources);
        }

        while (pending.TryPop(out CosDictionary? current))
        {
            var found = new List<ObjectVisit>();
            if (Get(current, FileAndLayerNames.XObject) is CosDictionary xobjects)
            {
                foreach (CosObject entry in xobjects.Values)
                {
                    if (document.Resolve(entry) is not CosStream stream)
                    {
                        continue;
                    }

                    CosName? subtype = ViewReading.Name(document, stream.Dictionary, FileAndLayerNames.Subtype);
                    ObjectRole? role = subtype?.Value switch
                    {
                        "Image" => ObjectRole.ImageXObject,
                        "Form" => ObjectRole.FormXObject,
                        _ => null,
                    };
                    if (role is { } known && Visit(entry, known, page) is { } visit)
                    {
                        found.Add(visit);
                        if (known == ObjectRole.ImageXObject)
                        {
                            AddColorSpace(Get(stream.Dictionary, Names.ColorSpace), page, found, 0);
                        }
                    }
                }
            }

            if (Get(current, Names.ColorSpace) is CosDictionary colorSpaces)
            {
                foreach (CosObject entry in colorSpaces.Values)
                {
                    AddColorSpace(entry, page, found, 0);
                }
            }

            if (Get(current, Names.Font) is CosDictionary fonts)
            {
                foreach (CosObject entry in fonts.Values)
                {
                    AddFont(entry, page, found);
                }
            }

            if (Get(current, Names.Pattern) is CosDictionary patterns)
            {
                foreach (CosObject entry in patterns.Values)
                {
                    switch (document.Resolve(entry))
                    {
                        case CosStream when Visit(entry, ObjectRole.TilingPattern, page) is { } tiling:
                            found.Add(tiling);
                            break;
                        case CosDictionary shadingPattern when _visited.Add(shadingPattern) && shadingPattern.TryGetValue(Names.Shading, out CosObject? shading):
                            if (Visit(shading, ObjectRole.Shading, page) is { } visit)
                            {
                                found.Add(visit);
                            }

                            break;
                    }
                }
            }

            if (Get(current, Names.Shading) is CosDictionary shadings)
            {
                foreach (CosObject entry in shadings.Values)
                {
                    if (Visit(entry, ObjectRole.Shading, page) is { } visit)
                    {
                        found.Add(visit);
                    }
                }
            }

            if (Get(current, FileAndLayerNames.Properties) is CosDictionary properties)
            {
                foreach (KeyValuePair<CosName, CosObject> entry in properties)
                {
                    CosObject resolved = document.Resolve(entry.Value);
                    if (resolved is CosArray array && _visited.Add(array))
                    {
                        found.Add(new ObjectVisit(array, entry.Value as CosReference, ObjectRole.PropertyList, page, entry.Key.Value));
                    }
                    else if (resolved is CosDictionary && Visit(entry.Value, ObjectRole.PropertyList, page, entry.Key.Value) is { } visit)
                    {
                        found.Add(visit);
                    }
                }
            }

            foreach (ObjectVisit visit in found)
            {
                yield return visit;
                if (visit.Role is ObjectRole.FormXObject or ObjectRole.TilingPattern or ObjectRole.Type3Font
                    && Get(visit.Dictionary!, KnownNames.Resources) is CosDictionary nested && _visited.Add(nested))
                {
                    pending.Push(nested);
                }
            }
        }
    }

    private void AddColorSpace(CosObject? entry, int page, List<ObjectVisit> found, int depth)
    {
        if (depth >= MaxColorSpaceDepth || document.Resolve(entry) is not CosArray array || array.Count == 0)
        {
            return;
        }

        if (document.Resolve(array[0]) is CosName { Value: "ICCBased" } && array.Count > 1)
        {
            if (document.Resolve(array[1]) is CosStream && Visit(array[1], ObjectRole.IccProfile, page) is { } visit)
            {
                found.Add(visit);
            }

            return;
        }

        for (int index = 1; index < array.Count; index++)
        {
            AddColorSpace(array[index], page, found, depth + 1);
        }
    }

    private void AddFont(CosObject entry, int page, List<ObjectVisit> found)
    {
        if (document.Resolve(entry) is not CosDictionary font || _visited.Contains(font))
        {
            return;
        }

        CosName? subtype = ViewReading.Name(document, font, FileAndLayerNames.Subtype);
        if (subtype is { Value: "Type3" })
        {
            if (Visit(entry, ObjectRole.Type3Font, page) is { } visit)
            {
                found.Add(visit);
            }

            return;
        }

        _visited.Add(font);
        AddFontProgram(font);
        if (subtype is { Value: "Type0" } && Get(font, Names.DescendantFonts) is CosArray descendants)
        {
            foreach (CosObject descendant in descendants)
            {
                if (document.Resolve(descendant) is CosDictionary cidFont && _visited.Add(cidFont))
                {
                    AddFontProgram(cidFont);
                }
            }
        }

        void AddFontProgram(CosDictionary dictionary)
        {
            if (Get(dictionary, Names.FontDescriptor) is not CosDictionary descriptor)
            {
                return;
            }

            foreach (CosName key in (ReadOnlySpan<CosName>)[Names.FontFile, Names.FontFile2, Names.FontFile3])
            {
                if (descriptor.TryGetValue(key, out CosObject? program) && document.Resolve(program) is CosStream
                    && Visit(program, ObjectRole.FontProgram, page) is { } visit)
                {
                    found.Add(visit);
                }
            }
        }
    }

    /// <summary>Names only this walk looks up.</summary>
    private static class Names
    {
        public static readonly CosName Threads = new("Threads");
        public static readonly CosName StructTreeRoot = new("StructTreeRoot");
        public static readonly CosName K = new("K");
        public static readonly CosName DPartRoot = new("DPartRoot");
        public static readonly CosName DPartRootNode = new("DPartRootNode");
        public static readonly CosName DParts = new("DParts");
        public static readonly CosName FS = new("FS");
        public static readonly CosName ThreeDD = new("3DD");
        public static readonly CosName AP = new("AP");
        public static readonly CosName ColorSpace = new("ColorSpace");
        public static readonly CosName Font = new("Font");
        public static readonly CosName Pattern = new("Pattern");
        public static readonly CosName Shading = new("Shading");
        public static readonly CosName DescendantFonts = new("DescendantFonts");
        public static readonly CosName FontDescriptor = new("FontDescriptor");
        public static readonly CosName FontFile = new("FontFile");
        public static readonly CosName FontFile2 = new("FontFile2");
        public static readonly CosName FontFile3 = new("FontFile3");
    }
}
