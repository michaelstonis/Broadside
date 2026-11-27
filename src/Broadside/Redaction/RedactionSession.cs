using System.Text;
using Broadside.Annotations;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Fonts;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Redaction;

/// <summary>What one area's overlay draws once the content under it is removed (Table 195).</summary>
/// <param name="Quads">The areas the overlay covers, in default user space.</param>
/// <param name="Rect">The rectangle an <c>RO</c> form and the overlay text are fitted to.</param>
/// <param name="Form">The <c>RO</c> form, drawn instead of the fill when present.</param>
/// <param name="Fill">The <c>IC</c> fill, or <see langword="null"/> for none.</param>
/// <param name="Text">The <c>OverlayText</c>, or <see langword="null"/>.</param>
/// <param name="Appearance">The <c>DA</c> string the text is drawn with, or <see langword="null"/>.</param>
/// <param name="Justification">The <c>Q</c> justification.</param>
/// <param name="Repeat">The <c>Repeat</c> flag.</param>
internal sealed record RedactionOverlay(
    IReadOnlyList<Quad> Quads,
    PdfRectangle Rect,
    PdfFormXObject? Form,
    PdfDeviceColor? Fill,
    string? Text,
    string? Appearance,
    PdfTextJustification Justification,
    bool Repeat);

/// <summary>
/// One application of redaction to a document: runs the editor over each page, then removes or clears what lies under the areas
/// outside the content (annotations, form field values, the thumbnail, structure replacement text) and draws the overlays. Every
/// change is made under one change log, so a failure, or a fallback in strict mode, leaves the document as it was.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.5.6.23 (Table 195), §12.5.2 (annotations), §12.7.4 (fields), §12.3.4 (thumbnails), §14.7.5 and §14.9.3 to
/// §14.9.4 (structure and replacement text), §14.8.2.2.2 (the overlay as a pagination artifact of subtype Redaction).
/// </remarks>
internal sealed class RedactionSession
{
    private static readonly CosName ActualText = new("ActualText");
    private static readonly CosName Alt = new("Alt");
    private static readonly CosName Expansion = new("E");
    private static readonly CosName[] EncodingEntries = [FilterNames.Filter, FilterNames.DecodeParms, FilterNames.F, new("FFilter"), new("FDecodeParms"), FilterNames.DL, KnownNames.Length];

    private readonly List<Diagnostic> _diagnostics = [];
    private readonly HashSet<(int Key, int Mcid)> _mcids = [];
    private readonly HashSet<CosDictionary> _clearedFields = new(ReferenceEqualityComparer.Instance);
    private RedactionEditor? _pageEditor;

    public RedactionSession(PdfDocument document, PdfRedactionOptions options)
    {
        Document = document;
        Options = options;
    }

    public PdfDocument Document { get; }

    public PdfRedactionOptions Options { get; }

    public ContentOptions ContentOptions { get; } = new();

    public int GlyphsRemoved { get; set; }

    public int ImagesChanged { get; set; }

    public int PathsRemoved { get; set; }

    public int AnnotationsRemoved { get; set; }

    public int FieldsCleared { get; set; }

    /// <summary>Gets the depth of form copies being rewritten, bounded by <see cref="ContentOptions.MaxNestingDepth"/>.</summary>
    public int FormDepth { get; private set; }

    /// <summary>Applies the Redact annotations of every page, then removes them.</summary>
    public static PdfRedactionResult ApplyAnnotations(PdfDocument document, PdfRedactionOptions? options)
    {
        var session = new RedactionSession(document, options ?? new PdfRedactionOptions());
        return session.Run(() =>
        {
            foreach (PdfPage page in document.Pages)
            {
                List<PdfRedactAnnotation> marks = [.. page.Annotations.OfType<PdfRedactAnnotation>()];
                if (marks.Count == 0)
                {
                    continue;
                }

                List<Quad> areas = [];
                List<RedactionOverlay> overlays = [];
                foreach (PdfRedactAnnotation mark in marks)
                {
                    List<Quad> quads = [.. mark.QuadPoints.Select(static quad => new Quad(quad.Point1, quad.Point2, quad.Point3, quad.Point4))];
                    PdfRectangle rect = mark.Rect;
                    if (quads.Count == 0)
                    {
                        quads.Add(Quad.FromRectangle(Matrix.Identity, rect.Left, rect.Bottom, rect.Right, rect.Top));
                    }

                    areas.AddRange(quads);
                    overlays.Add(new RedactionOverlay(
                        quads, rect, mark.Overlay, mark.InteriorColor, mark.OverlayText, mark.Dictionary.ContainsKey(AnnotationNames.OverlayText) ? mark.DefaultAppearance : null,
                        mark.Justification, mark.RepeatsOverlayText));
                }

                foreach (PdfRedactAnnotation mark in marks)
                {
                    if (page.Annotations.Any(candidate => ReferenceEquals(candidate.Dictionary, mark.Dictionary)))
                    {
                        int before = page.Annotations.Count;
                        AnnotationEditing.Remove(page, mark);
                        session.AnnotationsRemoved += before - page.Annotations.Count;
                    }
                }

                session.RedactPage(page, areas, overlays);
            }
        });
    }

    /// <summary>Applies areas to one page, with the overlay the options describe over each.</summary>
    public static PdfRedactionResult ApplyAreas(PdfPage page, IReadOnlyList<Quad> areas, PdfRedactionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(areas);
        foreach (Quad area in areas)
        {
            if (!Finite(area.BottomLeft) || !Finite(area.BottomRight) || !Finite(area.TopRight) || !Finite(area.TopLeft))
            {
                throw new ArgumentOutOfRangeException(nameof(areas), "An area's coordinates are finite numbers.");
            }
        }

        options ??= new PdfRedactionOptions();
        var session = new RedactionSession(page.Document, options);
        if (areas.Count == 0)
        {
            return session.Result();
        }

        string? appearance = options.OverlayText is null ? null : DefaultAppearanceFor(options.FillColor);
        List<RedactionOverlay> overlays =
        [
            .. areas.Select(area => new RedactionOverlay([area], area.Bounds, null, options.FillColor, options.OverlayText, appearance, PdfTextJustification.Centered, options.RepeatOverlayText)),
        ];
        return session.Run(() => session.RedactPage(page, areas, overlays));
    }

    /// <summary>The <c>DA</c> of overlay text: Helvetica 10, white over a dark fill and black otherwise.</summary>
    public static string DefaultAppearanceFor(PdfDeviceColor? fill)
    {
        bool dark = fill is { IsTransparent: false } && Luminance(fill) < 0.5;
        return dark ? "/Helv 10 Tf 1 g" : "/Helv 10 Tf 0 g";
    }

    /// <summary>Records a diagnostic on the document and in the result; in strict mode a warning throws (and the run is undone).</summary>
    public void Report(DiagnosticCode code, string message, CosReference? reference)
    {
        var diagnostic = new Diagnostic(code, code.DefaultSeverity, message, objectReference: reference);
        _diagnostics.Add(diagnostic);
        Document.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Copies a form XObject painted over an area and rewrites the copy with the areas in its run's space; returns the copy's new
    /// reference, or <see langword="null"/> when nothing of it was under an area (the copy is then dropped).
    /// </summary>
    public CosReference? RewriteFormCopy(PdfFormXObject form, ReadOnlySpan<Quad> areas)
    {
        CosStream original = form.Stream;
        var dictionary = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> pair in original.Dictionary)
        {
            if (Array.IndexOf(EncodingEntries, pair.Key) < 0)
            {
                dictionary[pair.Key] = pair.Value;
            }
        }

        var copy = new CosStream(dictionary, Document.DecodeStream(original).ToArray());
        var view = new PdfFormXObject(Document, copy, reference: null);
        var editor = new RedactionEditor(this);
        editor.Configure(areas, dictionary, view.Resources);
        bool changed;
        FormDepth++;
        try
        {
            changed = ContentRewriting.RewriteForm(view, Document, editor, ContentOptions);
        }
        finally
        {
            FormDepth--;
        }

        if (!changed)
        {
            return null;
        }

        editor.PruneXObjects(editor.Output);

        if (view.StructParents is int key)
        {
            foreach (int mcid in editor.RemovedMcids)
            {
                _mcids.Add((key, mcid));
            }
        }

        CosReference reference = Document.AddObject(copy);
        Report(
            RedactionDiagnostics.RedactionFormCopied,
            "A form XObject painted over a redaction area is copied and the copy rewritten; whatever else paints the original keeps it.",
            form.Reference);
        return reference;
    }

    private static bool Finite(PathPoint point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static double Luminance(PdfDeviceColor color)
    {
        IReadOnlyList<double> c = color.Components;
        return c.Count switch
        {
            1 => c[0],
            3 => (0.3 * c[0]) + (0.59 * c[1]) + (0.11 * c[2]),
            4 => 1 - Math.Min(1, (0.3 * c[0]) + (0.59 * c[1]) + (0.11 * c[2]) + c[3]),
            _ => 1,
        };
    }

    private PdfRedactionResult Run(Action apply)
    {
        if (Document.AcroForm is { } form && form.TerminalFields.OfType<PdfSignatureField>().Any(static field => field.IsSigned))
        {
            Report(
                RedactionDiagnostics.RedactionInvalidatesSignatures,
                "The document is signed: applying the redaction changes the content the signatures cover, so they no longer verify (§12.8).",
                null);
        }

        AnnotationEditing.Atomically(() =>
        {
            apply();
            ClearStructureReplacements();
            return true;
        });
        return Result();
    }

    private PdfRedactionResult Result() =>
        new(GlyphsRemoved, ImagesChanged, PathsRemoved, AnnotationsRemoved, FieldsCleared, [.. _diagnostics]);

    private void RedactPage(PdfPage page, IReadOnlyList<Quad> requested, IReadOnlyList<RedactionOverlay> overlays)
    {
        PdfRectangle crop = page.CropBox;
        List<Quad> areas = [];
        foreach (Quad area in requested)
        {
            // An area wholly outside the crop box covers nothing that shows; the rest are applied whole.
            if (RedactionGeometry.Overlaps(area.Bounds, crop))
            {
                areas.Add(RedactionGeometry.Hull(area));
            }
        }

        if (areas.Count == 0)
        {
            return;
        }

        RedactionEditor editor = _pageEditor ??= new RedactionEditor(this);
        editor.Configure([.. areas], page.Dictionary, page.Resources);
        if (page.RewriteContent(editor, ContentOptions))
        {
            editor.PruneXObjects(editor.Output);
        }

        if (ReadInteger(page.Dictionary, StructureNames.StructParents) is int key)
        {
            foreach (int mcid in editor.RemovedMcids)
            {
                _mcids.Add((key, mcid));
            }
        }

        RemoveAnnotations(page, areas);
        page.Dictionary.Remove(FileAndLayerNames.Thumb);
        DrawOverlays(page, crop, overlays);
    }

    private int? ReadInteger(CosDictionary dictionary, CosName key) =>
        dictionary.TryGetValue(key, out CosObject? value) && Document.Resolve(value) is CosInteger integer && integer.Value is >= 0 and <= int.MaxValue
            ? (int)integer.Value
            : null;

    /// <summary>
    /// Removes the annotations under the areas (§12.5.2) through the same path as <see cref="PdfPage.RemoveAnnotation"/>, and clears
    /// the fields of the widgets under them (§12.7.4): a widget stays, its field loses its value and gets a new appearance.
    /// </summary>
    private void RemoveAnnotations(PdfPage page, List<Quad> areas)
    {
        foreach (PdfAnnotation annotation in page.Annotations.ToList())
        {
            PdfRectangle rect = annotation.Rect;
            Quad box = Quad.FromRectangle(Matrix.Identity, rect.Left, rect.Bottom, rect.Right, rect.Top);
            if (!areas.Any(area => RedactionGeometry.Intersects(area, box)))
            {
                continue;
            }

            if (annotation is PdfWidgetAnnotation widget)
            {
                if (Options.ClearIntersectingFields)
                {
                    ClearField(widget);
                }
            }
            else if (Options.RemoveIntersectingAnnotations && page.Annotations.Any(candidate => ReferenceEquals(candidate.Dictionary, annotation.Dictionary)))
            {
                int before = page.Annotations.Count;
                AnnotationEditing.Remove(page, annotation);
                AnnotationsRemoved += before - page.Annotations.Count;
            }
        }
    }

    private void ClearField(PdfWidgetAnnotation widget)
    {
        if (widget.Field is not { } field || !_clearedFields.Add(field.Dictionary))
        {
            return;
        }

        switch (field)
        {
            case PdfTextField text:
                text.SetValue(null);
                break;
            case PdfChoiceField choice:
                choice.SetValue(null);
                break;
            case PdfToggleButtonField toggle:
                toggle.SetValue(FormNames.Off);
                break;
            default:
                return;
        }

        FieldsCleared++;
    }

    /// <summary>
    /// Removes <c>ActualText</c>, <c>Alt</c> and <c>E</c> from every structure element (and its ancestors) any of whose marked content
    /// lost something: the replacement would still say what the content was (§14.9.3, §14.9.4).
    /// </summary>
    private void ClearStructureReplacements()
    {
        if (_mcids.Count == 0
            || Document.Resolve(Document.Catalog.TryGetValue(StructureNames.StructTreeRoot, out CosObject? root) ? root : null) is not CosDictionary tree
            || Document.GetNumberTreeReader(tree.TryGetValue(StructureNames.ParentTree, out CosObject? parents) ? parents : null) is not { } parentTree)
        {
            return;
        }

        var cleared = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        foreach ((int key, int mcid) in _mcids)
        {
            if (!parentTree.TryGetValue(key, out CosObject? value) || Document.Resolve(value) is not CosArray elements || mcid >= elements.Count)
            {
                continue;
            }

            CosDictionary? element = Document.Resolve(elements[mcid]) as CosDictionary;
            for (int depth = 0; element is not null && depth < 256 && cleared.Add(element); depth++)
            {
                if (ReferenceEquals(element, tree))
                {
                    break;
                }

                element.Remove(ActualText);
                element.Remove(Alt);
                element.Remove(Expansion);
                element = Document.Resolve(element.TryGetValue(StructureNames.P, out CosObject? parent) ? parent : null) as CosDictionary;
            }
        }
    }

    /// <summary>
    /// Draws each overlay after the removal, as a pagination artifact of subtype Redaction (§14.8.2.2.2, Table 363): the <c>RO</c> form
    /// fitted to the rectangle when present, otherwise the <c>IC</c> fill of each area; then the <c>OverlayText</c> in its <c>DA</c>,
    /// justified by <c>Q</c> and repeated to fill the area when <c>Repeat</c> is set (Table 195).
    /// </summary>
    private void DrawOverlays(PdfPage page, PdfRectangle crop, IReadOnlyList<RedactionOverlay> overlays)
    {
        if (!overlays.Any(static overlay => overlay.Form is not null || overlay.Fill is { IsTransparent: false } || !string.IsNullOrEmpty(overlay.Text)))
        {
            return;
        }

        using PdfCanvas canvas = page.CreateCanvas();

        // The PDF 2.0 subtype is a property-list value earlier readers ignore; applied redaction is ordinary content (issue #624).
        canvas.BeginMarkedContent("Artifact", new CosDictionary { [KnownNames.Type] = new CosName("Pagination"), [FileAndLayerNames.Subtype] = new CosName("Redaction") });
        canvas.SaveState();
        canvas.Rectangle(crop.Left, crop.Bottom, crop.Width, crop.Height);
        canvas.Clip();
        canvas.EndPath();
        foreach (RedactionOverlay overlay in overlays)
        {
            if (overlay.Form is { } form && form.Reference is not null && ReferenceEquals(form.Document, Document))
            {
                DrawForm(canvas, form, overlay.Rect);
            }
            else if (overlay.Fill is { IsTransparent: false } fill)
            {
                canvas.SetFillColor(AnnotationAppearances.ToColor(fill));
                foreach (Quad quad in overlay.Quads)
                {
                    Quad hull = RedactionGeometry.Hull(quad);
                    canvas.MoveTo(hull.BottomLeft.X, hull.BottomLeft.Y);
                    canvas.LineTo(hull.BottomRight.X, hull.BottomRight.Y);
                    canvas.LineTo(hull.TopRight.X, hull.TopRight.Y);
                    canvas.LineTo(hull.TopLeft.X, hull.TopLeft.Y);
                    canvas.ClosePath();
                }

                canvas.Fill();
            }

            if (!string.IsNullOrEmpty(overlay.Text))
            {
                DrawText(canvas, overlay);
            }
        }

        canvas.RestoreState();
        canvas.EndMarkedContent();
    }

    /// <summary>Draws an <c>RO</c> form with its bounding box (through its matrix) fitted to the rectangle, as an appearance is (§12.5.5).</summary>
    private static void DrawForm(PdfCanvas canvas, PdfFormXObject form, PdfRectangle rect)
    {
        PdfRectangle box = form.BoundingBox ?? rect;
        PdfRectangle mapped = Quad.FromRectangle(form.Matrix, box.Left, box.Bottom, box.Right, box.Top).Bounds;
        double sx = mapped.Width > 0 ? rect.Width / mapped.Width : 1;
        double sy = mapped.Height > 0 ? rect.Height / mapped.Height : 1;
        canvas.SaveState();
        canvas.Transform(new Matrix(sx, 0, 0, sy, rect.Left - (mapped.Left * sx), rect.Bottom - (mapped.Bottom * sy)));
        canvas.DrawForm(form);
        canvas.RestoreState();
    }

    private void DrawText(PdfCanvas canvas, RedactionOverlay overlay)
    {
        string text = overlay.Text!;
        PdfDefaultAppearance? appearance = overlay.Appearance is { } da ? PdfDefaultAppearance.Parse(Encoding.Latin1.GetBytes(da)) : null;
        PdfRectangle rect = overlay.Rect;
        double size = appearance is { FontSize: > 0 and <= 1_000 } ? appearance.FontSize : Math.Clamp(rect.Height * 0.7, 1, 12);
        PdfCanvasFont font = AnnotationAppearances.TextFont(Document, Fonts.Standard14Font.Helvetica, text);
        double width = font.MeasureWidth(text, size);
        double ascent = font.Ascent / 1000 * size;
        double leading = size * 1.2;
        canvas.SaveState();
        canvas.Rectangle(rect.Left, rect.Bottom, rect.Width, rect.Height);
        canvas.Clip();
        canvas.EndPath();
        canvas.SetFillColor(appearance?.Color ?? PdfColor.FromGray(overlay.Fill is { } fill && Luminance(fill) < 0.5 ? 1 : 0));
        canvas.BeginText();
        canvas.SetFont(font, size);
        double previousX = 0, previousY = 0;
        double baseline = overlay.Repeat ? rect.Top - ascent : rect.Bottom + ((rect.Height - size) / 2) + (size * 0.2);
        int rows = overlay.Repeat ? Math.Max(1, (int)Math.Ceiling(rect.Height / leading)) : 1;
        int columns = overlay.Repeat && width > 0 ? Math.Max(1, (int)Math.Ceiling(rect.Width / width)) : 1;
        string line = columns == 1 ? text : string.Concat(Enumerable.Repeat(text, columns));
        double lineWidth = columns == 1 ? width : width * columns;
        for (int row = 0; row < Math.Min(rows, 500); row++)
        {
            double x = overlay.Repeat ? rect.Left : overlay.Justification switch
            {
                PdfTextJustification.Centered => rect.Left + ((rect.Width - lineWidth) / 2),
                PdfTextJustification.Right => rect.Right - lineWidth,
                _ => rect.Left,
            };
            canvas.MoveText(x - previousX, baseline - previousY);
            canvas.ShowText(line);
            (previousX, previousY) = (x, baseline);
            baseline -= leading;
        }

        canvas.EndText();
        canvas.RestoreState();
    }
}
