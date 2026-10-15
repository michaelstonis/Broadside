using Broadside.Annotations;
using Broadside.Filters;
using Broadside.Images;
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
/// <item><see cref="VersionedFeatures.Jbig2Compression"/> and <see cref="VersionedFeatures.JpxCompression"/>: a stream whose
/// <c>Filter</c> names <c>JBIG2Decode</c> or <c>JPXDecode</c> (§7.4.7, §7.4.9; Table 6).</item>
/// <item><see cref="VersionedFeatures.SoftMasks"/>: an <c>SMask</c> entry (a graphics state parameter dictionary's, Table 57, or an
/// image's, Table 87) and a soft-mask dictionary (<c>Type</c> <c>Mask</c>, Table 142).</item>
/// <item><see cref="VersionedFeatures.ImageMasks"/> and <see cref="VersionedFeatures.SixteenBitImages"/>: an image stream with a
/// <c>Mask</c> entry, or with <c>BitsPerComponent</c> 16 (Table 87); <see cref="VersionedFeatures.ProgressiveDct"/>: a stream
/// whose lone <c>DCTDecode</c> filter holds a progressive JPEG (§7.4.8), found from the stream data.</item>
/// <item><see cref="VersionedFeatures.TransparencyGroups"/>: a group attributes dictionary of subtype <c>Transparency</c> (§11.6.6,
/// Table 145), which a form's or a page's <c>Group</c> holds.</item>
/// <item><see cref="VersionedFeatures.TransparencyParameters"/>: a numeric <c>CA</c> or <c>ca</c>, or a <c>BM</c>, <c>AIS</c> or
/// <c>TK</c> entry, outside an annotation (Table 57; the appearance characteristics' <c>CA</c> is a text string and is not one),
/// and a markup annotation's <c>CA</c> (Table 166).</item>
/// <item><see cref="VersionedFeatures.OptionalContent"/>: an optional content group or membership dictionary (<c>Type</c>
/// <c>OCG</c> or <c>OCMD</c>, Tables 96 and 98) and an <c>OC</c> entry (Tables 87, 93 and 166).</item>
/// <item><see cref="VersionedFeatures.Pdf20AnnotationEntries"/>: an annotation (<c>Type</c> <c>Annot</c>, or untyped with
/// <c>Subtype</c> and <c>Rect</c>) with <c>ca</c>, <c>BM</c> or <c>Lang</c>, a non-markup annotation with <c>CA</c> (Table 166),
/// and the <c>Projection</c> and <c>RichMedia</c> types (Table 171).</item>
/// <item><see cref="VersionedFeatures.SampledFunction"/> and <see cref="VersionedFeatures.CalculatedFunction"/>: a function
/// dictionary or stream (<c>FunctionType</c> 0, or 2, 3 and 4; §7.10, Table 38).</item>
/// <item><see cref="VersionedFeatures.Shadings"/>: a shading dictionary or stream (<c>ShadingType</c> 1 to 7, Table 77) and a shading
/// pattern (<c>PatternType</c> 2, Table 76).</item>
/// <item><see cref="VersionedFeatures.PatternColorSpace"/>: a tiling pattern (<c>PatternType</c> 1, Table 74) and a Pattern colour
/// space array <c>[/Pattern underlying]</c> (§8.6.6.2).</item>
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
    private static readonly CosName Group = new("Group");
    private static readonly CosName Transparency = new("Transparency");
    private static readonly CosName Mask = new("Mask");
    private static readonly CosName S = new("S");
    private static readonly CosName CA = new("CA");
    private static readonly CosName LowerCA = new("ca");
    private static readonly CosName BM = new("BM");
    private static readonly CosName AIS = new("AIS");
    private static readonly CosName TK = new("TK");
    private static readonly CosName FunctionType = new("FunctionType");
    private static readonly CosName ShadingType = new("ShadingType");
    private static readonly CosName PatternType = new("PatternType");
    private static readonly CosName Pattern = new("Pattern");

    /// <summary>The annotation types Table 171 marks as not markup annotations, whose <c>CA</c> is PDF 2.0 (Table 166).</summary>
    private static readonly HashSet<string> NonMarkupTypes =
        ["Link", "Popup", "Movie", "Screen", "Widget", "PrinterMark", "TrapNet", "Watermark", "3D", "RichMedia"];

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

        if (dictionary.ContainsKey(ImageNames.SMask))
        {
            found(VersionedFeatures.SoftMasks);
        }

        if (dictionary.ContainsKey(FileAndLayerNames.OC))
        {
            found(VersionedFeatures.OptionalContent);
        }

        Graphics(document, dictionary, found);
        CosObject? type = EntryReader.Get(document, dictionary, KnownNames.Type);
        if (isStream)
        {
            if (FileAndLayerNames.EmbeddedFile.Equals(type))
            {
                found(VersionedFeatures.EmbeddedFileStream);
            }

            Filters(document, EntryReader.Get(document, dictionary, FilterNames.Filter), found);
            if (ImageNames.Image.Equals(EntryReader.Get(document, dictionary, ImageNames.Subtype)))
            {
                if (dictionary.ContainsKey(ImageNames.Mask))
                {
                    found(VersionedFeatures.ImageMasks);
                }

                if (EntryReader.Get(document, dictionary, ImageNames.BitsPerComponent) is CosInteger { Value: 16 })
                {
                    found(VersionedFeatures.SixteenBitImages);
                }
            }

            return;
        }

        if (FileAndLayerNames.OCG.Equals(type) || FileAndLayerNames.OCMD.Equals(type))
        {
            found(VersionedFeatures.OptionalContent);
        }
        else if (Mask.Equals(type))
        {
            found(VersionedFeatures.SoftMasks);
        }
        else if ((type is null || Group.Equals(type)) && Transparency.Equals(EntryReader.Get(document, dictionary, S))
            && !dictionary.ContainsKey(StructureNames.P))
        {
            found(VersionedFeatures.TransparencyGroups);
        }

        if (AnnotationNames.Annot.Equals(type)
            || (type is null && dictionary.ContainsKey(AnnotationNames.Rect) && dictionary.ContainsKey(AnnotationNames.Subtype)))
        {
            Annotation(document, dictionary, found);
        }
        else if (dictionary.ContainsKey(BM) || dictionary.ContainsKey(AIS) || dictionary.ContainsKey(TK)
            || EntryReader.Get(document, dictionary, CA) is CosInteger or CosReal
            || EntryReader.Get(document, dictionary, LowerCA) is CosInteger or CosReal)
        {
            found(VersionedFeatures.TransparencyParameters);
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

    /// <summary>Reports each feature a stream's data uses: a progressive JPEG behind a lone <c>DCTDecode</c> filter (§7.4.8).</summary>
    /// <param name="document">The document the stream belongs to.</param>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <param name="data">The stream's encoded (decrypted) data.</param>
    /// <param name="found">Receives each feature found.</param>
    public static void InStreamData(PdfDocument document, CosDictionary dictionary, ReadOnlySpan<byte> data, Action<VersionedFeature> found)
    {
        CosObject? filter = EntryReader.Get(document, dictionary, FilterNames.Filter);
        bool dct = FilterNames.DctDecode.Equals(filter) || (filter is CosArray { Count: 1 } array && FilterNames.DctDecode.Equals(EntryReader.Resolve(document, array[0])));
        if (dct && JpegInfo.Read(data, readProfile: false, out _) is { Progressive: true })
        {
            found(VersionedFeatures.ProgressiveDct);
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

        if (array.Count == 2 && Pattern.Equals(array[0]))
        {
            found(VersionedFeatures.PatternColorSpace);
        }
    }

    /// <summary>Reports a function's, a shading's or a pattern's version (§7.10, Table 38; §8.7, Tables 74, 76 and 77).</summary>
    private static void Graphics(PdfDocument document, CosDictionary dictionary, Action<VersionedFeature> found)
    {
        if (EntryReader.Get(document, dictionary, FunctionType) is CosInteger { Value: var function and (0 or 2 or 3 or 4) })
        {
            found(function == 0 ? VersionedFeatures.SampledFunction : VersionedFeatures.CalculatedFunction);
        }

        if (EntryReader.Get(document, dictionary, ShadingType) is CosInteger { Value: >= 1 and <= 7 })
        {
            found(VersionedFeatures.Shadings);
        }

        if (EntryReader.Get(document, dictionary, PatternType) is CosInteger { Value: var pattern and (1 or 2) })
        {
            found(pattern == 1 ? VersionedFeatures.PatternColorSpace : VersionedFeatures.Shadings);
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

    /// <summary>Reports the PDF 2.0 entries an annotation uses (Tables 166 and 171), and a markup annotation's <c>CA</c> (PDF 1.4).</summary>
    private static void Annotation(PdfDocument document, CosDictionary annotation, Action<VersionedFeature> found)
    {
        string? subtype = (EntryReader.Get(document, annotation, AnnotationNames.Subtype) as CosName)?.Value;
        if (annotation.ContainsKey(LowerCA) || annotation.ContainsKey(BM) || annotation.ContainsKey(AnnotationNames.Lang)
            || subtype is "Projection" or "RichMedia")
        {
            found(VersionedFeatures.Pdf20AnnotationEntries);
        }

        if (annotation.ContainsKey(CA))
        {
            found(subtype is not null && NonMarkupTypes.Contains(subtype)
                ? VersionedFeatures.Pdf20AnnotationEntries
                : VersionedFeatures.TransparencyParameters);
        }
    }

    /// <summary>Reports the feature of each filter a <c>Filter</c> value names that has a version row (§7.4.1, Table 6).</summary>
    private static void Filters(PdfDocument document, CosObject? filter, Action<VersionedFeature> found)
    {
        if (filter is CosName name)
        {
            Filter(name, found);
        }
        else if (filter is CosArray filters)
        {
            foreach (CosObject item in filters)
            {
                if (document.Resolve(item) is CosName each)
                {
                    Filter(each, found);
                }
            }
        }
    }

    private static void Filter(CosName name, Action<VersionedFeature> found)
    {
        if (VersionedFeatures.Filter(name) is { } feature)
        {
            found(feature);
        }
    }
}
