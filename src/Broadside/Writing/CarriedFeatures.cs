using Broadside.Filters;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Writing;

/// <summary>
/// Finds the rows of <see cref="VersionedFeatures"/> an existing object uses, from its entries: what an object copied from another
/// document (<see cref="PdfObjectCopier"/>) carries into the target, which records it so the target's next save raises its version
/// or refuses in strict mode (issue #290).
/// </summary>
/// <remarks>
/// <para>ADR 0003; ISO 32000-2 §7.5.2. Each object is looked at on its own, by its keys and type, so a walk over a graph finds what
/// the whole graph uses. Found:</para>
/// <list type="bullet">
/// <item><see cref="VersionedFeatures.AssociatedFiles"/>: an <c>AF</c> or <c>AFRelationship</c> entry in any dictionary (§14.13).</item>
/// <item><see cref="VersionedFeatures.UnicodeFileName"/> and <see cref="VersionedFeatures.FileDescription"/>: <c>UF</c>, and
/// <c>Desc</c> in a file specification dictionary (§7.11.3, Table 43).</item>
/// <item><see cref="VersionedFeatures.EmbeddedFileStream"/>: a stream of type <c>EmbeddedFile</c> (§7.11.4).</item>
/// <item><see cref="VersionedFeatures.FlateCompression"/>: a stream whose <c>Filter</c> names <c>FlateDecode</c> (§7.4.4).</item>
/// <item><see cref="VersionedFeatures.StructureTree"/>: a structure element (§14.7.2, Table 355), at the version of what it uses
/// (<c>Lang</c> or <c>ActualText</c> PDF 1.4, <c>E</c> PDF 1.5); <see cref="VersionedFeatures.StructureNamespaces"/> for a namespace
/// dictionary (Table 356), a structure element with <c>NS</c>, <c>Ref</c>, <c>Phoneme</c> or <c>PhoneticAlphabet</c>, and a
/// structure tree root with <c>Namespaces</c> or <c>PronunciationLexicon</c> (Table 354).</item>
/// <item><see cref="VersionedFeatures.OutlineItemStyle"/>: an outline item (<c>Title</c> and <c>Parent</c>) with <c>C</c> or
/// <c>F</c> (§12.3.3, Table 151).</item>
/// <item><see cref="VersionedFeatures.BoundingBoxDestinations"/>: an explicit destination array whose view is <c>FitB</c>,
/// <c>FitBH</c> or <c>FitBV</c> (§12.3.2.2, Table 149).</item>
/// </list>
/// <para>
/// The catalog-level rows (page labels, the name trees, developer extensions) are not found here: a copy never carries the source's
/// catalog, whose page tree does not copy.
/// </para>
/// </remarks>
internal static class CarriedFeatures
{
    private static readonly CosName FitB = new("FitB");
    private static readonly CosName FitBH = new("FitBH");
    private static readonly CosName FitBV = new("FitBV");

    /// <summary>Reports each feature <paramref name="dictionary"/>'s own entries use.</summary>
    /// <param name="document">The document the dictionary belongs to, which resolves its entries.</param>
    /// <param name="dictionary">A dictionary, or a stream's dictionary.</param>
    /// <param name="isStream">Whether <paramref name="dictionary"/> is a stream's.</param>
    /// <param name="found">Receives each feature found; a feature may be reported more than once.</param>
    public static void InDictionary(PdfDocument document, CosDictionary dictionary, bool isStream, Action<VersionedFeature> found)
    {
        if (dictionary.ContainsKey(FileAndLayerNames.AF) || dictionary.ContainsKey(FileAndLayerNames.AFRelationship))
        {
            found(VersionedFeatures.AssociatedFiles);
        }

        if (dictionary.ContainsKey(FileAndLayerNames.UF))
        {
            found(VersionedFeatures.UnicodeFileName);
        }

        CosObject? type = EntryReader.Get(document, dictionary, KnownNames.Type);
        if (isStream)
        {
            if (FileAndLayerNames.EmbeddedFile.Equals(type))
            {
                found(VersionedFeatures.EmbeddedFileStream);
            }

            if (NamesFlate(document, EntryReader.Get(document, dictionary, FilterNames.Filter)))
            {
                found(VersionedFeatures.FlateCompression);
            }

            return;
        }

        if (dictionary.ContainsKey(FileAndLayerNames.Desc)
            && (FileAndLayerNames.Filespec.Equals(type) || dictionary.ContainsKey(FileAndLayerNames.EF) || dictionary.ContainsKey(FileAndLayerNames.UF)))
        {
            found(VersionedFeatures.FileDescription);
        }

        if (StructureNames.Namespace.Equals(type))
        {
            found(VersionedFeatures.StructureNamespaces);
        }
        else if (StructureNames.StructTreeRoot.Equals(type))
        {
            if (dictionary.ContainsKey(StructureNames.Namespaces) || dictionary.ContainsKey(StructureNames.PronunciationLexicon))
            {
                found(VersionedFeatures.StructureNamespaces);
            }
        }
        else if (StructureNames.StructElem.Equals(type)
            || (type is null && dictionary.ContainsKey(StructureNames.S) && dictionary.ContainsKey(StructureNames.P)))
        {
            found(StructureElement(dictionary));
        }
        else if (type is null && dictionary.ContainsKey(NavigationNames.Title) && dictionary.ContainsKey(KnownNames.Parent)
            && (dictionary.ContainsKey(NavigationNames.C) || dictionary.ContainsKey(NavigationNames.F)))
        {
            found(VersionedFeatures.OutlineItemStyle);
        }
    }

    /// <summary>Reports each feature <paramref name="array"/> itself uses: an explicit destination's view.</summary>
    /// <param name="array">An array.</param>
    /// <param name="found">Receives each feature found.</param>
    public static void InArray(CosArray array, Action<VersionedFeature> found)
    {
        if (array.Count >= 2 && array[1] is CosName view && (view.Equals(FitB) || view.Equals(FitBH) || view.Equals(FitBV)))
        {
            found(VersionedFeatures.BoundingBoxDestinations);
        }
    }

    /// <summary>The structure feature a structure element stands for, as <see cref="PdfStructureTreeRoot.MinimumVersion"/> reads it.</summary>
    private static VersionedFeature StructureElement(CosDictionary element)
    {
        if (element.ContainsKey(StructureNames.NS) || element.ContainsKey(StructureNames.Ref)
            || element.ContainsKey(StructureNames.Phoneme) || element.ContainsKey(StructureNames.PhoneticAlphabet))
        {
            return VersionedFeatures.StructureNamespaces;
        }

        PdfVersion version = element.ContainsKey(StructureNames.E) ? new PdfVersion(1, 5)
            : element.ContainsKey(StructureNames.Lang) || element.ContainsKey(StructureNames.ActualText) ? new PdfVersion(1, 4)
            : new PdfVersion(1, 3);
        return VersionedFeatures.StructureTree(version);
    }

    /// <summary>Whether a <c>Filter</c> value is or lists <c>FlateDecode</c>.</summary>
    private static bool NamesFlate(PdfDocument document, CosObject? filter) => filter switch
    {
        CosName name => FilterNames.FlateDecode.Equals(name),
        CosArray filters => filters.Any(item => FilterNames.FlateDecode.Equals(document.Resolve(item))),
        _ => false,
    };
}
