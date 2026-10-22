using System.Globalization;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Writing;

namespace Broadside.Annotations;

/// <summary>
/// Adds, edits and removes annotations (issue #165): the dictionaries of §12.5.2 and §12.5.6, the page's <c>Annots</c>, pop-ups,
/// replies and the structure tree's references. Every change of one call is recorded in a <see cref="CosChangeLog"/> and undone
/// when the call fails part way.
/// </summary>
internal static class AnnotationEditing
{
    private static readonly CosName Note = new("Note");
    private static readonly CosName Uri = new("URI");
    private static readonly CosName Fit = new("Fit");
    private static readonly CosName Solid = new("S");
    private static readonly CosName Action = new("Action");

    /// <summary>Runs <paramref name="change"/>, undoing every change it made to the document when it throws (mutation is single-threaded).</summary>
    public static T Atomically<T>(Func<T> change)
    {
        var log = new CosChangeLog();
        try
        {
            using (log.Record())
            {
                return change();
            }
        }
        catch
        {
            log.Revert();
            throw;
        }
    }

    public static CosArray Rectangle(PdfRectangle rect) =>
        new([Number(rect.Left), Number(rect.Bottom), Number(rect.Right), Number(rect.Top)]);

    public static CosNumber Number(double value) => TextFieldAppearance.Number(value);

    public static PdfRectangle Checked(PdfRectangle rect, string parameter)
    {
        if (!double.IsFinite(rect.Left) || !double.IsFinite(rect.Bottom) || !double.IsFinite(rect.Right) || !double.IsFinite(rect.Top))
        {
            throw new ArgumentOutOfRangeException(parameter, rect, "A rectangle's coordinates are finite numbers.");
        }

        return new PdfRectangle(Math.Min(rect.Left, rect.Right), Math.Min(rect.Bottom, rect.Top), Math.Max(rect.Left, rect.Right), Math.Max(rect.Bottom, rect.Top));
    }

    public static void SetText(CosDictionary dictionary, CosName key, string? value)
    {
        if (value is null)
        {
            dictionary.Remove(key);
        }
        else
        {
            dictionary[key] = new CosString(TextStringEncoder.Encode(value));
        }
    }

    public static void SetColor(CosDictionary dictionary, CosName key, PdfDeviceColor? color)
    {
        if (color is null)
        {
            dictionary.Remove(key);
        }
        else
        {
            dictionary[key] = new CosArray(color.Components.Select(component => (CosObject)Number(component)));
        }
    }

    public static void SetOpacity(PdfAnnotation annotation, double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "An opacity is a number from 0 to 1.");
        }

        if (value == 1)
        {
            annotation.Dictionary.Remove(AnnotationNames.StrokingOpacity);
            return;
        }

        annotation.Dictionary[AnnotationNames.StrokingOpacity] = Number(value);
        annotation.Document.RequireFeature(annotation is PdfMarkupAnnotation ? VersionedFeatures.TransparencyParameters : VersionedFeatures.Pdf20AnnotationEntries);
    }

    public static void SetBorderWidth(PdfAnnotation annotation, double width)
    {
        if (!double.IsFinite(width) || width < 0 || width > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "A border width is a finite number from 0 to 1 000.");
        }

        annotation.Dictionary[AnnotationNames.BS] = new CosDictionary { [AnnotationNames.W] = Number(width), [AnnotationNames.S] = Solid };
        annotation.Document.RequireFeature(VersionedFeatures.AnnotationBorderStyle);
    }

    /// <summary>
    /// Creates an annotation of <paramref name="kind"/> on <paramref name="page"/>: the common entries from the options, the
    /// subtype's own from <paramref name="fill"/>, then its appearance; returns its view.
    /// </summary>
    public static T Add<T>(PdfPage page, PdfAnnotationKind kind, PdfRectangle rect, PdfAnnotationOptions? options, Action<CosDictionary> fill)
        where T : PdfAnnotation
    {
        PdfDocument document = page.Document;
        if (page.Reference is not { } pageReference)
        {
            throw new InvalidOperationException("The page is not an indirect object, so an annotation cannot name it in P (ISO 32000-2 §12.5.2, Table 166).");
        }

        options ??= new PdfAnnotationOptions();
        bool markup = kind is not PdfAnnotationKind.Link;
        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = AnnotationNames.Annot,
            [AnnotationNames.Subtype] = new CosName(Subtype(kind)),
            [AnnotationNames.Rect] = Rectangle(rect),
            [AnnotationNames.P] = pageReference,
            [AnnotationNames.F] = new CosInteger((uint)options.Flags),
        };
        SetColor(dictionary, AnnotationNames.C, options.Color ?? AnnotationAppearances.DefaultColor(kind));
        if (kind is not (PdfAnnotationKind.Text or PdfAnnotationKind.Link) && markup)
        {
            dictionary[AnnotationNames.BS] = new CosDictionary { [AnnotationNames.W] = Number(options.BorderWidth), [AnnotationNames.S] = Solid };
        }

        if (options.InteriorColor is { } interior && kind is PdfAnnotationKind.Square or PdfAnnotationKind.Circle or PdfAnnotationKind.Polygon or PdfAnnotationKind.Line)
        {
            SetColor(dictionary, AnnotationNames.IC, interior);
        }

        if (options.Opacity < 1)
        {
            dictionary[AnnotationNames.StrokingOpacity] = Number(options.Opacity);
        }

        if (options.Contents is { } contents)
        {
            SetText(dictionary, AnnotationNames.Contents, contents);
        }

        if (options.Name is { } name)
        {
            SetText(dictionary, AnnotationNames.NM, name);
        }

        if (markup && options.Title is { } title)
        {
            SetText(dictionary, AnnotationNames.T, title);
        }

        if (markup && options.Subject is { } subject)
        {
            SetText(dictionary, AnnotationNames.Subj, subject);
        }

        fill(dictionary);
        T annotation = Atomically(() =>
        {
            CosReference reference = document.AddObject(dictionary);
            AddToPage(page, reference);
            var view = (T)document.AnnotationIndex.GetOrCreate(dictionary, reference, page);
            AnnotationAppearances.Generate(view);
            return view;
        });

        RequireFeatures(document, dictionary, kind);
        return annotation;
    }

    /// <summary>Records the versioned features the new annotation's entries use, as <see cref="CarriedFeatures"/> finds them in a copy.</summary>
    private static void RequireFeatures(PdfDocument document, CosDictionary dictionary, PdfAnnotationKind kind)
    {
        document.RequireFeature(VersionedFeatures.AnnotationEntries11);
        if (kind is PdfAnnotationKind.Squiggly || dictionary.ContainsKey(AnnotationNames.IC) || dictionary.ContainsKey(AnnotationNames.NM))
        {
            document.RequireFeature(VersionedFeatures.AnnotationTypes14);
        }

        if (kind is PdfAnnotationKind.Polygon or PdfAnnotationKind.PolyLine || dictionary.ContainsKey(AnnotationNames.Subj))
        {
            document.RequireFeature(VersionedFeatures.AnnotationTypes15);
        }

        if (kind is not (PdfAnnotationKind.Text or PdfAnnotationKind.Link or PdfAnnotationKind.Polygon or PdfAnnotationKind.PolyLine or PdfAnnotationKind.Squiggly))
        {
            document.RequireFeature(VersionedFeatures.AnnotationTypes13);
        }

        if (dictionary.ContainsKey(AnnotationNames.BS))
        {
            document.RequireFeature(VersionedFeatures.AnnotationBorderStyle);
        }

        if (dictionary.ContainsKey(AnnotationNames.StrokingOpacity))
        {
            document.RequireFeature(VersionedFeatures.TransparencyParameters);
        }

        if (kind is PdfAnnotationKind.Highlight)
        {
            // The appearance draws through a graphics state with BM Multiply (§11.3.5, Table 57).
            document.RequireFeature(VersionedFeatures.TransparencyParameters);
        }
    }

    private static string Subtype(PdfAnnotationKind kind) => kind switch
    {
        PdfAnnotationKind.PolyLine => "PolyLine",
        PdfAnnotationKind.StrikeOut => "StrikeOut",
        PdfAnnotationKind.FreeText => "FreeText",
        _ => kind.ToString(),
    };

    public static void FillText(CosDictionary dictionary, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        SetText(dictionary, AnnotationNames.Contents, text);
        dictionary[AnnotationNames.Name] = Note;
    }

    public static void FillUriLink(CosDictionary dictionary, Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsAbsoluteUri)
        {
            throw new ArgumentException("A link's URI is absolute (ISO 32000-2 §12.6.4.8).", nameof(target));
        }

        LinkBorder(dictionary);
        dictionary[AnnotationNames.A] = new CosDictionary
        {
            [KnownNames.Type] = Action,
            [AnnotationNames.S] = Uri,
            [Uri] = new CosString(System.Text.Encoding.ASCII.GetBytes(target.AbsoluteUri)),
        };
    }

    public static void FillPageLink(PdfPage page, CosDictionary dictionary, PdfPage target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!ReferenceEquals(target.Document, page.Document) || target.Reference is not { } reference)
        {
            throw new ArgumentException("The target page is not an indirect page of the annotation's document (ISO 32000-2 §12.3.2.2).", nameof(target));
        }

        LinkBorder(dictionary);
        dictionary[AnnotationNames.Dest] = new CosArray([reference, Fit]);
    }

    private static void LinkBorder(CosDictionary dictionary) =>
        dictionary[AnnotationNames.Border] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(0)]);

    public static void FillFreeText(CosDictionary dictionary, string text, PdfAnnotationOptions? options)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= new PdfAnnotationOptions();
        SetText(dictionary, AnnotationNames.Contents, text);
        IReadOnlyList<double> c = options.TextColor.Components;
        string color = string.Join(' ', c.Select(Format)) + (c.Count switch { 1 => " g", 3 => " rg", _ => " k" });
        dictionary[AnnotationNames.DA] = new CosString(System.Text.Encoding.ASCII.GetBytes($"/Helv {Format(options.FontSize)} Tf {color}"));
    }

    public static void FillLine(CosDictionary dictionary, PathPoint start, PathPoint end) =>
        dictionary[AnnotationNames.L] = Points([start, end], 2, nameof(start));

    public static void FillVertices(CosDictionary dictionary, IReadOnlyList<PathPoint> vertices, int minimum) =>
        dictionary[AnnotationNames.Vertices] = Points(vertices, minimum, nameof(vertices));

    public static void FillInk(CosDictionary dictionary, IReadOnlyList<IReadOnlyList<PathPoint>> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        if (strokes.Count == 0)
        {
            throw new ArgumentException("An ink annotation has at least one path (ISO 32000-2 §12.5.6.13, Table 185).", nameof(strokes));
        }

        dictionary[AnnotationNames.InkList] = new CosArray(strokes.Select(stroke => (CosObject)Points(stroke, 1, nameof(strokes))));
    }

    public static void FillQuadPoints(CosDictionary dictionary, IReadOnlyList<PdfQuadrilateral> quads)
    {
        ArgumentNullException.ThrowIfNull(quads);
        if (quads.Count == 0)
        {
            throw new ArgumentException("A text markup annotation has at least one quadrilateral (ISO 32000-2 §12.5.6.10, Table 182).", nameof(quads));
        }

        dictionary[AnnotationNames.QuadPoints] = Points([.. quads.SelectMany(quad => (PathPoint[])[quad.Point1, quad.Point2, quad.Point3, quad.Point4])], 4, nameof(quads));
    }

    public static void FillStamp(CosDictionary dictionary, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        dictionary[AnnotationNames.Name] = new CosName(name);
    }

    private static CosArray Points(IReadOnlyList<PathPoint> points, int minimum, string parameter)
    {
        ArgumentNullException.ThrowIfNull(points, parameter);
        if (points.Count < minimum)
        {
            throw new ArgumentException($"At least {minimum} points are needed.", parameter);
        }

        var array = new CosArray();
        foreach (PathPoint point in points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                throw new ArgumentOutOfRangeException(parameter, "A point's coordinates are finite numbers.");
            }

            array.Add(Number(point.X));
            array.Add(Number(point.Y));
        }

        return array;
    }

    private static string Format(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Adds the annotation to the page's <c>Annots</c>, keeping the annotations it reads from a page tree node above it.</summary>
    private static void AddToPage(PdfPage page, CosReference annotation)
    {
        PdfDocument document = page.Document;
        if (document.Resolve(page.Dictionary.TryGetValue(AnnotationNames.Annots, out CosObject? own) ? own : null) is CosArray array)
        {
            array.Add(annotation);
            return;
        }

        CosArray annotations = InheritedCopy(page, own);
        annotations.Add(annotation);
        page.Dictionary[AnnotationNames.Annots] = annotations;
    }

    private static CosArray InheritedCopy(PdfPage page, CosObject? own)
    {
        var annotations = new CosArray();
        if (own is null && page.FindInheritedAnnots() is { } inherited && page.Document.Resolve(inherited) is CosArray kept)
        {
            foreach (CosObject element in kept)
            {
                annotations.Add(element);
            }
        }

        return annotations;
    }

    /// <summary>
    /// Removes the annotation from the page with what belongs to it: its pop-up, the replies to it (§12.5.6.2, <c>IRT</c>) and
    /// theirs, the structure tree's object references to each and their parent-tree entries (§14.7.5.3, §14.7.5.4).
    /// </summary>
    public static void Remove(PdfPage page, PdfAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        IReadOnlyList<PdfAnnotation> annotations = page.Annotations;
        if (!annotations.Any(candidate => ReferenceEquals(candidate.Dictionary, annotation.Dictionary)))
        {
            throw new ArgumentException("The annotation is not one of the page's annotations.", nameof(annotation));
        }

        if (annotation.Kind == PdfAnnotationKind.Widget)
        {
            throw new InvalidOperationException("A widget annotation belongs to a form field; remove it through the form, not as an annotation (ISO 32000-2 §12.7.4).");
        }

        var removed = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance) { annotation.Dictionary };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (PdfAnnotation candidate in annotations)
            {
                if (removed.Contains(candidate.Dictionary))
                {
                    continue;
                }

                bool belongs = candidate switch
                {
                    PdfPopupAnnotation popup => popup.Parent is { } parent && removed.Contains(parent.Dictionary),
                    PdfMarkupAnnotation reply => reply.InReplyTo is { } target && removed.Contains(target.Dictionary),
                    _ => false,
                };
                belongs |= annotations.Any(owner => removed.Contains(owner.Dictionary) && owner is PdfMarkupAnnotation { Popup: { } own } && ReferenceEquals(own.Dictionary, candidate.Dictionary));
                if (belongs)
                {
                    removed.Add(candidate.Dictionary);
                    grew = true;
                }
            }
        }

        PdfDocument document = page.Document;
        Atomically(() =>
        {
            CosObject? own = page.Dictionary.TryGetValue(AnnotationNames.Annots, out CosObject? value) ? value : null;
            CosArray array = document.Resolve(own) as CosArray ?? InheritedCopy(page, own);
            for (int index = array.Count - 1; index >= 0; index--)
            {
                if (document.Resolve(array[index]) is CosDictionary entry && removed.Contains(entry))
                {
                    array.RemoveAt(index);
                }
            }

            if (document.Resolve(own) is not CosArray)
            {
                page.Dictionary[AnnotationNames.Annots] = array;
            }

            // A pop-up removed on its own leaves its parent without one.
            foreach (PdfAnnotation owner in annotations)
            {
                if (!removed.Contains(owner.Dictionary) && owner is PdfMarkupAnnotation { Popup: { } popup } && removed.Contains(popup.Dictionary))
                {
                    owner.Dictionary.Remove(AnnotationNames.Popup);
                }
            }

            RemoveFromStructure(document, removed);
            return true;
        });
    }

    private static void RemoveFromStructure(PdfDocument document, HashSet<CosDictionary> removed)
    {
        if (!document.Catalog.ContainsKey(StructureNames.StructTreeRoot) || document.StructureTree is not { } tree)
        {
            return;
        }

        StructureContext context = tree.Context;
        foreach (List<PdfObjectReference> references in StructureWriter.FindObjectReferences(context, removed).Values)
        {
            foreach (PdfObjectReference reference in references)
            {
                StructureWriter.RemoveObjectReference(context, reference);
            }
        }

        foreach (CosDictionary annotation in removed)
        {
            StructureWriter.UnlinkRemovedObject(context, annotation);
        }
    }

    public static void Move(PdfAnnotation annotation, double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy))
        {
            throw new ArgumentOutOfRangeException(double.IsFinite(dx) ? nameof(dy) : nameof(dx), "An offset is a finite number.");
        }

        PdfDocument document = annotation.Document;
        CosDictionary dictionary = annotation.Dictionary;
        Atomically(() =>
        {
            PdfRectangle rect = annotation.Rect;
            dictionary[AnnotationNames.Rect] = Rectangle(new PdfRectangle(rect.Left + dx, rect.Bottom + dy, rect.Right + dx, rect.Top + dy));
            foreach (CosName key in (CosName[])[AnnotationNames.L, AnnotationNames.Vertices, AnnotationNames.QuadPoints, AnnotationNames.CL])
            {
                if (dictionary.TryGetValue(key, out CosObject? value) && document.Resolve(value) is CosArray points)
                {
                    dictionary[key] = Translated(document, points, dx, dy);
                }
            }

            foreach (CosName key in (CosName[])[AnnotationNames.InkList, AnnotationNames.Path])
            {
                if (dictionary.TryGetValue(key, out CosObject? value) && document.Resolve(value) is CosArray lists)
                {
                    dictionary[key] = new CosArray(lists.Select(list => document.Resolve(list) is CosArray inner ? Translated(document, inner, dx, dy) : list));
                }
            }

            return true;
        });
    }

    /// <summary>A copy of a flat coordinate array with every pair moved; an element that is not a number is kept.</summary>
    private static CosArray Translated(PdfDocument document, CosArray points, double dx, double dy)
    {
        var moved = new CosArray();
        for (int index = 0; index < points.Count; index++)
        {
            CosObject element = points[index];
            moved.Add(document.Resolve(element) is CosNumber number ? Number(number.ToDouble() + (index % 2 == 0 ? dx : dy)) : element);
        }

        return moved;
    }
}
