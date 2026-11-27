using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.Redaction;

namespace Broadside;

/// <content>Marking areas for redaction with Redact annotations, and applying redaction to areas directly (issue #624).</content>
public sealed partial class PdfPage
{
    /// <summary>
    /// Marks areas for redaction with a Redact annotation that a reviewer can see and change; <see cref="PdfDocument.Redact()"/> applies
    /// it later.
    /// </summary>
    /// <param name="quadrilaterals">The areas, in default user space; at least one. The overlay is a black fill.</param>
    /// <returns>The new annotation.</returns>
    /// <exception cref="ArgumentException"><paramref name="quadrilaterals"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.5.6.23 (Table 195). The annotation is drawn as the outline of its areas in its color (red by default) until it
    /// is applied. Records PDF 1.7. A mutation.
    /// </remarks>
    public PdfRedactAnnotation AddRedactAnnotation(IReadOnlyList<PdfQuadrilateral> quadrilaterals) => AddRedactAnnotation(quadrilaterals, new PdfRedactionOptions());

    /// <summary>Marks areas for redaction with a Redact annotation whose overlay <paramref name="options"/> describe.</summary>
    /// <param name="quadrilaterals">The areas, in default user space; at least one.</param>
    /// <param name="options">The overlay drawn once the redaction is applied (<c>IC</c>, <c>OverlayText</c>, <c>Repeat</c>).</param>
    /// <returns>The new annotation.</returns>
    /// <exception cref="ArgumentException"><paramref name="quadrilaterals"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.23 (Table 195). Records PDF 1.7. A mutation.</remarks>
    public PdfRedactAnnotation AddRedactAnnotation(IReadOnlyList<PdfQuadrilateral> quadrilaterals, PdfRedactionOptions options)
    {
        ArgumentNullException.ThrowIfNull(quadrilaterals);
        ArgumentNullException.ThrowIfNull(options);
        if (quadrilaterals.Count == 0)
        {
            throw new ArgumentException("A Redact annotation marks at least one quadrilateral (ISO 32000-2 §12.5.6.23, Table 195).", nameof(quadrilaterals));
        }

        PdfRectangle bounds = Bounds(quadrilaterals);
        PdfRedactAnnotation annotation = AnnotationEditing.Add<PdfRedactAnnotation>(this, PdfAnnotationKind.Redact, bounds, null, d =>
        {
            AnnotationEditing.FillQuadPoints(d, quadrilaterals);
            FillRedaction(d, options);
        });
        _document.RequireFeature(Writing.VersionedFeatures.RedactAnnotation);
        return annotation;
    }

    /// <summary>Marks a rectangle for redaction with a Redact annotation, filled black once applied; <see cref="PdfDocument.Redact()"/> applies it later.</summary>
    /// <param name="rect">The area, in default user space.</param>
    /// <returns>The new annotation.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rect"/> has a coordinate that is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object, or the rectangle has no area.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.23 (Table 195): with no <c>QuadPoints</c> the rectangle is the area. Records PDF 1.7. A mutation.</remarks>
    public PdfRedactAnnotation AddRedactAnnotation(PdfRectangle rect) => AddRedactAnnotation(rect, new PdfRedactionOptions());

    /// <summary>Marks a rectangle for redaction with a Redact annotation whose overlay <paramref name="options"/> describe.</summary>
    /// <param name="rect">The area, in default user space.</param>
    /// <param name="options">The overlay drawn once the redaction is applied.</param>
    /// <returns>The new annotation.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rect"/> has a coordinate that is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object, or the rectangle has no area.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.23 (Table 195): with no <c>QuadPoints</c> the rectangle is the area. Records PDF 1.7. A mutation.</remarks>
    public PdfRedactAnnotation AddRedactAnnotation(PdfRectangle rect, PdfRedactionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        PdfRedactAnnotation annotation = AnnotationEditing.Add<PdfRedactAnnotation>(this, PdfAnnotationKind.Redact, Checked(rect), null, d => FillRedaction(d, options));
        _document.RequireFeature(Writing.VersionedFeatures.RedactAnnotation);
        return annotation;
    }

    /// <summary>Applies redaction to areas of this page with the default options: a black fill over each area once its content is removed.</summary>
    /// <param name="areas">The areas, in default user space; each is taken as the convex hull of its four points. Empty does nothing.</param>
    /// <returns>What was removed, and the diagnostics recorded.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An area has a coordinate that is not finite.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a fallback to a coarser removal; the page is then unchanged.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.23. As <see cref="Redact(IReadOnlyList{Quad}, PdfRedactionOptions?)"/>.</remarks>
    public PdfRedactionResult Redact(IReadOnlyList<Quad> areas) => Redact(areas, null);

    /// <summary>
    /// Applies redaction to areas of this page: removes the glyphs, images and subpaths under them from the page's content, removes
    /// the annotations and clears the form fields under them, and draws the overlay the options describe over each.
    /// </summary>
    /// <param name="areas">The areas, in default user space; each is taken as the convex hull of its four points. Empty does nothing.</param>
    /// <param name="options">The overlay and what to do with images, annotations and fields; <see langword="null"/> for the defaults.</param>
    /// <returns>What was removed, and the diagnostics recorded.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An area has a coordinate that is not finite.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a fallback to a coarser removal; the page is then unchanged.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.5.6.23. A glyph under an area leaves its text-showing operator, replaced by an adjustment of its advance so the
    /// rest do not move (§9.4.3, Table 107); an image or a subpath under an area is removed, a filled or stroked rectangle cut; forms
    /// painted over an area are copied and the copy rewritten; replacement text of what was removed goes too (§14.9.4).
    /// </para>
    /// <para>
    /// The removed content stays recoverable from the original bytes until the document is saved with
    /// <see cref="PdfRedactionResult.CreateSaveOptions"/>: never save a redacted document as an incremental update.
    /// </para>
    /// </remarks>
    public PdfRedactionResult Redact(IReadOnlyList<Quad> areas, PdfRedactionOptions? options) =>
        RedactionSession.ApplyAreas(this, areas, options);

    private static PdfRectangle Bounds(IReadOnlyList<PdfQuadrilateral> quadrilaterals)
    {
        double left = double.PositiveInfinity, bottom = double.PositiveInfinity, right = double.NegativeInfinity, top = double.NegativeInfinity;
        foreach (PdfQuadrilateral quad in quadrilaterals)
        {
            PdfRectangle bounds = quad.Bounds;
            left = Math.Min(left, bounds.Left);
            bottom = Math.Min(bottom, bounds.Bottom);
            right = Math.Max(right, bounds.Right);
            top = Math.Max(top, bounds.Top);
        }

        return AnnotationEditing.Checked(new PdfRectangle(left, bottom, right, top), "quadrilaterals");
    }

    private static void FillRedaction(Objects.CosDictionary dictionary, PdfRedactionOptions options)
    {
        AnnotationEditing.SetColor(dictionary, AnnotationNames.IC, options.FillColor);
        if (options.OverlayText is { } text)
        {
            AnnotationEditing.SetText(dictionary, AnnotationNames.OverlayText, text);
            dictionary[AnnotationNames.DA] = new Objects.CosString(System.Text.Encoding.ASCII.GetBytes(RedactionSession.DefaultAppearanceFor(options.FillColor)));
            dictionary[AnnotationNames.Q] = new Objects.CosInteger(1);
            if (options.RepeatOverlayText)
            {
                dictionary[AnnotationNames.Repeat] = Objects.CosBoolean.True;
            }
        }
    }
}
