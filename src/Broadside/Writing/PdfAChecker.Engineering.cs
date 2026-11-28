using Broadside.Objects;

namespace Broadside.Writing;

/// <content>
/// What PDF/A-4e (ISO 19005-4, Annex B; issue #650) allows that PDF/A-4 forbids, as veraPDF's PDF/A-4E profile checks it: 3D and
/// RichMedia annotations (rule 6.3.1-1), each 3D stream U3D or PRC (6.1.6.1-3), the SetOCGState and GoTo3DView actions (6.6.1-1),
/// and embedded files of any kind (no rule 6.9-3), including a RichMedia annotation's assets, each with <c>F</c>, <c>UF</c>, an
/// <c>AFRelationship</c> and a MIME type (6.9-1, 6.9-2, 6.9-4). The 3D and RichMedia content is kept as stored; nothing renders it.
/// </content>
/// <remarks>ISO 19005-4 §6.1.6.1, §6.3.1, §6.6.1, §6.9 and Annex B; ISO 32000-2 §13.6.2, §13.6.3 and §13.7.2.</remarks>
internal sealed partial class PdfAChecker
{
    private static readonly CosName ThreeDData = new("3DD");
    private static readonly CosName RichMediaContent = new("RichMediaContent");
    private static readonly CosName Assets = new("Assets");

    /// <summary>The annotation types PDF/A-4e adds to <see cref="AnnotationTypes"/> (veraPDF PDF/A-4E rule 6.3.1-1).</summary>
    private static readonly HashSet<string> EngineeringAnnotationTypes = ["3D", "RichMedia"];

    /// <summary>The action types PDF/A-4e adds to those of PDF/A-4 (veraPDF PDF/A-4E rule 6.6.1-1).</summary>
    private static readonly HashSet<string> EngineeringActionTypes = ["SetOCGState", "GoTo3DView"];

    /// <summary>The 3D stream subtypes PDF/A-4e allows (veraPDF PDF/A-4E rule 6.1.6.1-3; ISO 32000-2 §13.6.3, Table 311).</summary>
    private static readonly HashSet<string> ThreeDStreamTypes = ["U3D", "PRC"];

    /// <summary>Whether the level allows an annotation of the <paramref name="subtype"/> (rule 6.3.1-1).</summary>
    private bool IsAllowedAnnotation(string subtype) =>
        AnnotationTypes.Contains(subtype) || (_conformance.AllowsEngineeringContent() && EngineeringAnnotationTypes.Contains(subtype));

    /// <summary>Whether the level allows an action of the <paramref name="type"/> (rules 6.5.1-1 and, for PDF/A-4, 6.6.1-1).</summary>
    private bool IsAllowedAction(string type) =>
        ActionTypes.Contains(type)
        || (_conformance.Part() == 4 && PdfA4ActionTypes.Contains(type))
        || (_conformance.AllowsEngineeringContent() && EngineeringActionTypes.Contains(type));

    /// <summary>
    /// The PDF/A-4e checks of a 3D or RichMedia annotation: its 3D stream's subtype (6.1.6.1-3, the stream itself or a 3D reference
    /// dictionary's), and a RichMedia annotation's assets recorded as embedded files for <see cref="CheckEmbeddedFiles"/>.
    /// </summary>
    private void CheckEngineeringAnnotation(CosDictionary annotation, string subtype, CosReference? reference)
    {
        if (!_conformance.AllowsEngineeringContent())
        {
            return;
        }

        if (subtype == "3D")
        {
            CosObject data = Resolve(annotation.GetValueOrDefault(ThreeDData));
            CosStream? stream = data switch
            {
                CosStream direct => direct,
                CosDictionary threeDReference => Resolve(threeDReference.GetValueOrDefault(ThreeDData)) as CosStream,
                _ => null,
            };
            if (stream is not null && (NameOf(stream.Dictionary, Subtype) is not { } type || !ThreeDStreamTypes.Contains(type)))
            {
                Report("6.1.6.1-3", $"a 3D stream's Subtype is {NameOf(stream.Dictionary, Subtype) ?? "missing"}, not U3D or PRC", annotation.GetValueOrDefault(ThreeDData) as CosReference ?? reference, renumber: false);
            }
        }
        else if (subtype == "RichMedia" && Dictionary(annotation, RichMediaContent) is { } content && Dictionary(content, Assets) is { } assets)
        {
            var pending = new Stack<CosDictionary>([assets]);
            var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
            while (pending.TryPop(out CosDictionary? node))
            {
                if (!seen.Add(node))
                {
                    continue;
                }

                if (Resolve(node.GetValueOrDefault(Names)) is CosArray entries)
                {
                    for (int i = 1; i < entries.Count; i += 2)
                    {
                        AddEmbeddedFile(Resolve(entries[i]) as CosDictionary, entries[i] as CosReference);
                    }
                }

                if (Resolve(node.GetValueOrDefault(KnownNames.Kids)) is CosArray kids)
                {
                    foreach (CosObject kid in kids)
                    {
                        if (Resolve(kid) is CosDictionary child)
                        {
                            pending.Push(child);
                        }
                    }
                }
            }
        }
    }
}
