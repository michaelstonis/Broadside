using System.Runtime.CompilerServices;
using Broadside.Caching;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Annotations;

/// <summary>
/// The document's annotation views: one view per annotation dictionary (so a pop-up reached from its parent is the instance the page
/// lists), the per-page lists, and a lazily built map from annotation dictionary to the page that holds it.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.5.2 and §7.7.3.3. Thread-safe: views are created and published with first-publisher-wins semantics; the page map
/// is built on first use and rebuilt when a page dictionary or an <c>Annots</c> array it read has changed (ADR 0004).
/// </remarks>
internal sealed class AnnotationIndex
{
    private readonly PdfDocument _document;
    private readonly ConditionalWeakTable<CosDictionary, PdfAnnotation> _views = [];
    private OwnerIndex? _owners;

    public AnnotationIndex(PdfDocument document) => _document = document;

    /// <summary>Reads the annotations of <paramref name="page"/> from its <c>Annots</c> array, recording what the array gets wrong.</summary>
    public IReadOnlyList<PdfAnnotation> Read(PdfPage page, CosObject? annots, bool inherited)
    {
        CosReference? pageReference = page.Reference;
        if (inherited)
        {
            Report(DiagnosticCodes.AnnotsInherited, "The page has no Annots entry but a page tree node above it has one, which is not inheritable (Table 31); it is used, as viewers do.", pageReference);
        }

        switch (_document.Resolve(annots))
        {
            case CosNull:
                return [];
            case CosArray array:
                return ReadArray(page, array, inherited);
            default:
                Report(DiagnosticCodes.AnnotsInvalid, "The page's Annots entry is not an array; the page has no annotations.", pageReference);
                return [];
        }
    }

    /// <summary>Returns the view over <paramref name="dictionary"/>, creating it for <paramref name="page"/> when there is none or its subtype changed.</summary>
    public PdfAnnotation GetOrCreate(CosDictionary dictionary, CosReference? reference, PdfPage? page)
    {
        CosName? subtype = PdfAnnotation.ReadSubtype(_document, dictionary, out _);
        if (_views.TryGetValue(dictionary, out PdfAnnotation? existing) && Equals(existing.CreatedSubtype, subtype))
        {
            return existing;
        }

        PdfAnnotation created = PdfAnnotation.Create(_document, dictionary, reference, page);
        if (existing is not null)
        {
            _views.AddOrUpdate(dictionary, created);
            return created;
        }

        return _views.TryAdd(dictionary, created) || !_views.TryGetValue(dictionary, out PdfAnnotation? winner) ? created : winner;
    }

    /// <summary>
    /// Returns the annotation <paramref name="value"/> names, as the page that holds it lists it: the page of <paramref name="from"/>
    /// first (when given), then any page; an annotation no page holds gets a view without a page. <see langword="null"/> when the value
    /// is not a dictionary.
    /// </summary>
    public PdfAnnotation? Find(CosObject? value, PdfAnnotation? from)
    {
        if (_document.Resolve(value) is not CosDictionary dictionary)
        {
            return null;
        }

        if (TryGetCurrent(dictionary) is { } known)
        {
            return known;
        }

        if (from?.Page is { } page && FindOnPage(page, dictionary) is { } onPage)
        {
            return onPage;
        }

        if (Owners().TryGetValue(dictionary, out PdfPage? owner) && FindOnPage(owner, dictionary) is { } onOwner)
        {
            return onOwner;
        }

        return GetOrCreate(dictionary, value as CosReference, page: null);
    }

    /// <summary>The view <paramref name="page"/> lists for <paramref name="dictionary"/>, or <see langword="null"/>.</summary>
    private static PdfAnnotation? FindOnPage(PdfPage page, CosDictionary dictionary)
    {
        foreach (PdfAnnotation annotation in page.Annotations)
        {
            if (ReferenceEquals(annotation.Dictionary, dictionary))
            {
                return annotation;
            }
        }

        return null;
    }

    private PdfAnnotation? TryGetCurrent(CosDictionary dictionary) =>
        _views.TryGetValue(dictionary, out PdfAnnotation? view) && Equals(view.CreatedSubtype, PdfAnnotation.ReadSubtype(_document, dictionary, out _))
            ? view
            : null;

    private PdfAnnotation[] ReadArray(PdfPage page, CosArray array, bool inherited)
    {
        CosReference? pageReference = page.Reference;
        var annotations = new List<PdfAnnotation>(array.Count);
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        int trapNets = 0;
        for (int index = 0; index < array.Count; index++)
        {
            CosObject element = array[index];
            var reference = element as CosReference;
            if (_document.Resolve(element) is not CosDictionary dictionary)
            {
                Report(DiagnosticCodes.AnnotationEntryInvalid, $"Element {index} of the page's Annots array is not an annotation dictionary; it is skipped.", reference ?? pageReference);
                continue;
            }

            if (reference is null)
            {
                Report(DiagnosticCodes.AnnotationNotIndirect, $"Element {index} of the page's Annots array is a direct dictionary; Table 31 requires indirect references. It is read anyway.", pageReference);
            }

            if (!seen.Add(dictionary))
            {
                Report(DiagnosticCodes.AnnotationDuplicate, $"The page's Annots array lists the same annotation more than once (again at element {index}); each listing is kept.", reference ?? pageReference);
            }

            PdfAnnotation annotation = GetOrCreate(dictionary, reference, page);
            if (!inherited)
            {
                if (annotation.Page is not null && !ReferenceEquals(annotation.Page, page))
                {
                    Report(DiagnosticCodes.AnnotationSharedAcrossPages, "The annotation is listed by the Annots arrays of more than one page; §12.5.2 allows only one. Its page is the first that lists it.", reference ?? pageReference);
                }

                if (dictionary.TryGetValue(AnnotationNames.P, out CosObject? p) && !ReferenceEquals(_document.Resolve(p), page.Dictionary))
                {
                    Report(DiagnosticCodes.AnnotationPageMismatch, "The annotation's P entry does not name the page whose Annots array holds it; that page is used.", reference ?? pageReference);
                }
            }

            if (annotation.Kind == PdfAnnotationKind.TrapNet)
            {
                trapNets++;
                if (trapNets > 1 || index != array.Count - 1)
                {
                    Report(DiagnosticCodes.TrapNetPlacementInvalid, "A page shall have at most one trap network annotation, the last element of its Annots array (§12.5.6.21).", reference ?? pageReference);
                }
            }

            annotations.Add(annotation);
        }

        return [.. annotations];
    }

    /// <summary>The map from annotation dictionary to the first page that lists it, rebuilt when anything it read has changed.</summary>
    private Dictionary<CosDictionary, PdfPage> Owners()
    {
        OwnerIndex? cached = Volatile.Read(ref _owners);
        if (cached is not null && cached.Stamps.IsCurrent)
        {
            return cached.Pages;
        }

        var owners = new Dictionary<CosDictionary, PdfPage>(ReferenceEqualityComparer.Instance);
        var stamps = new ContainerStamps();
        foreach (PdfPage page in _document.Pages)
        {
            stamps.Add(page.Dictionary);
            stamps.Add(_document.Resolve(page.Dictionary.TryGetValue(AnnotationNames.Annots, out CosObject? annots) ? annots : null));
            foreach (PdfAnnotation annotation in page.Annotations)
            {
                owners.TryAdd(annotation.Dictionary, page);
            }
        }

        Volatile.Write(ref _owners, new OwnerIndex(owners, stamps));
        return owners;
    }

    private void Report(string code, string message, CosReference? reference) =>
        _document.DiagnosticSink.Report(code, DiagnosticSeverity.Warning, message, objectReference: reference);

    /// <summary>A page map and the containers it was built from.</summary>
    private sealed record OwnerIndex(Dictionary<CosDictionary, PdfPage> Pages, ContainerStamps Stamps);
}
