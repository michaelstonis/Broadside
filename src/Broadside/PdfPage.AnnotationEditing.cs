using Broadside.Annotations;
using Broadside.Graphics;

namespace Broadside;

/// <summary>Adding and removing annotations (issue #165).</summary>
/// <remarks>
/// ISO 32000-2 §12.5. Each <c>Add…Annotation</c> method writes the annotation dictionary (§12.5.2 and the subtype's table in
/// §12.5.6), appends it to the page's <c>Annots</c> and draws its normal appearance stream (§12.5.5), so viewers that do not
/// generate appearances show it as Broadside does; the new annotation is returned as its typed view. A failure changes nothing.
/// Each call records the versions its entries need (ADR 0003), so a save raises an older document or refuses in strict mode.
/// </remarks>
public sealed partial class PdfPage
{
    /// <summary>Adds a text annotation: a note shown as an icon in <paramref name="rect"/>, whose text a viewer shows when it is opened.</summary>
    /// <param name="rect">The icon's rectangle in default user space.</param>
    /// <param name="text">The note's text (<c>Contents</c>).</param>
    /// <param name="options">Colours, flags and markup texts; <see langword="null"/> for the defaults (a yellow icon).</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object, or the rectangle has no area.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.4, Table 175 (<c>Name</c> <c>Note</c>).</remarks>
    public PdfTextAnnotation AddTextAnnotation(PdfRectangle rect, string text, PdfAnnotationOptions? options = null) =>
        AnnotationEditing.Add<PdfTextAnnotation>(this, PdfAnnotationKind.Text, Checked(rect), options, d => AnnotationEditing.FillText(d, text));

    /// <summary>Adds a link annotation to a URI, with no border.</summary>
    /// <param name="rect">The active area in default user space.</param>
    /// <param name="target">The absolute URI the link opens (a URI action).</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="target"/> is relative.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object, or the rectangle has no area.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.5, Table 176; §12.6.4.8 (URI actions, PDF 1.1).</remarks>
    public PdfLinkAnnotation AddLinkAnnotation(PdfRectangle rect, Uri target) =>
        AnnotationEditing.Add<PdfLinkAnnotation>(this, PdfAnnotationKind.Link, Checked(rect), null, d => AnnotationEditing.FillUriLink(d, target));

    /// <summary>Adds a link annotation to a page of the same document, shown whole (<c>/Fit</c>), with no border.</summary>
    /// <param name="rect">The active area in default user space.</param>
    /// <param name="target">The page the link goes to.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="target"/> is not an indirect page of this document.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object, or the rectangle has no area.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.5, Table 176 (<c>Dest</c>); §12.3.2.2, Table 149.</remarks>
    public PdfLinkAnnotation AddLinkAnnotation(PdfRectangle rect, PdfPage target) =>
        AnnotationEditing.Add<PdfLinkAnnotation>(this, PdfAnnotationKind.Link, Checked(rect), null, d => AnnotationEditing.FillPageLink(this, d, target));

    /// <summary>Adds a free text annotation: text drawn on the page, wrapped inside <paramref name="rect"/>.</summary>
    /// <param name="rect">The text box in default user space.</param>
    /// <param name="text">The text (<c>Contents</c>); line breaks start new lines.</param>
    /// <param name="options">Font size, text colour, a border colour, flags and markup texts; <see langword="null"/> for black 12-point text without a border.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object, the rectangle has no area, or the text has characters neither Helvetica nor a font found by the engine's resolvers can show.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.6, Table 177 (<c>DA</c> naming <c>/Helv</c>, PDF 1.3).</remarks>
    public PdfFreeTextAnnotation AddFreeTextAnnotation(PdfRectangle rect, string text, PdfAnnotationOptions? options = null) =>
        AnnotationEditing.Add<PdfFreeTextAnnotation>(this, PdfAnnotationKind.FreeText, Checked(rect), options, d => AnnotationEditing.FillFreeText(d, text, options));

    /// <summary>Adds a line annotation from <paramref name="start"/> to <paramref name="end"/>; its rectangle is fitted to the line.</summary>
    /// <param name="start">The start point in default user space.</param>
    /// <param name="end">The end point.</param>
    /// <param name="options">Colour, width, flags and markup texts; <see langword="null"/> for a one-point red line.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.7, Table 178 (<c>L</c>, PDF 1.3). Line endings are not drawn.</remarks>
    public PdfLineAnnotation AddLineAnnotation(PathPoint start, PathPoint end, PdfAnnotationOptions? options = null) =>
        AnnotationEditing.Add<PdfLineAnnotation>(this, PdfAnnotationKind.Line, default, options, d => AnnotationEditing.FillLine(d, start, end));

    /// <summary>Adds a square annotation: a rectangle drawn inside <paramref name="rect"/>.</summary>
    /// <param name="rect">The rectangle in default user space; the border is drawn inside it.</param>
    /// <param name="options">Border and interior colours, width, flags and markup texts; <see langword="null"/> for a one-point red border.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object, or the rectangle has no area.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.8, Table 180 (PDF 1.3).</remarks>
    public PdfSquareAnnotation AddSquareAnnotation(PdfRectangle rect, PdfAnnotationOptions? options = null) =>
        AnnotationEditing.Add<PdfSquareAnnotation>(this, PdfAnnotationKind.Square, Checked(rect), options, _ => { });

    /// <summary>Adds a circle annotation: the ellipse inscribed in <paramref name="rect"/>.</summary>
    /// <param name="rect">The bounding rectangle in default user space; the border is drawn inside it.</param>
    /// <param name="options">Border and interior colours, width, flags and markup texts; <see langword="null"/> for a one-point red border.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object, or the rectangle has no area.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.8, Table 180 (PDF 1.3).</remarks>
    public PdfCircleAnnotation AddCircleAnnotation(PdfRectangle rect, PdfAnnotationOptions? options = null) =>
        AnnotationEditing.Add<PdfCircleAnnotation>(this, PdfAnnotationKind.Circle, Checked(rect), options, _ => { });

    /// <summary>Adds a polygon annotation: the closed shape through <paramref name="vertices"/>; its rectangle is fitted to them.</summary>
    /// <param name="vertices">At least three vertices in default user space.</param>
    /// <param name="options">Border and interior colours, width, flags and markup texts; <see langword="null"/> for a one-point red border.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="vertices"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">There are fewer than three vertices.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.9, Table 181 (PDF 1.5).</remarks>
    public PdfPolygonAnnotation AddPolygonAnnotation(IReadOnlyList<PathPoint> vertices, PdfAnnotationOptions? options = null) =>
        AnnotationEditing.Add<PdfPolygonAnnotation>(this, PdfAnnotationKind.Polygon, default, options, d => AnnotationEditing.FillVertices(d, vertices, 3));

    /// <summary>Adds a polyline annotation: the open path through <paramref name="vertices"/>; its rectangle is fitted to them.</summary>
    /// <param name="vertices">At least two vertices in default user space.</param>
    /// <param name="options">Colour, width, flags and markup texts; <see langword="null"/> for a one-point red line.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="vertices"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">There are fewer than two vertices.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.9, Table 181 (PDF 1.5).</remarks>
    public PdfPolyLineAnnotation AddPolyLineAnnotation(IReadOnlyList<PathPoint> vertices, PdfAnnotationOptions? options = null) =>
        AnnotationEditing.Add<PdfPolyLineAnnotation>(this, PdfAnnotationKind.PolyLine, default, options, d => AnnotationEditing.FillVertices(d, vertices, 2));

    /// <summary>Adds an ink annotation: freehand strokes, each through its points with round caps and joins; its rectangle is fitted to them.</summary>
    /// <param name="strokes">One or more strokes, each of one or more points in default user space.</param>
    /// <param name="options">Colour, width, flags and markup texts; <see langword="null"/> for one-point red strokes.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="strokes"/> or a stroke is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">There is no stroke, or a stroke has no point.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.13, Table 185 (PDF 1.3).</remarks>
    public PdfInkAnnotation AddInkAnnotation(IReadOnlyList<IReadOnlyList<PathPoint>> strokes, PdfAnnotationOptions? options = null) =>
        AnnotationEditing.Add<PdfInkAnnotation>(this, PdfAnnotationKind.Ink, default, options, d => AnnotationEditing.FillInk(d, strokes));

    /// <summary>Adds a highlight annotation over the text in <paramref name="quadrilaterals"/>, filled in its colour under the Multiply blend mode.</summary>
    /// <param name="quadrilaterals">One quadrilateral per run of text, its points top left, top right, bottom left, bottom right of the text.</param>
    /// <param name="options">Colour, flags and markup texts; <see langword="null"/> for yellow.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="quadrilaterals"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">There is no quadrilateral.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.10, Table 182 (PDF 1.3).</remarks>
    public PdfHighlightAnnotation AddHighlightAnnotation(IReadOnlyList<PdfQuadrilateral> quadrilaterals, PdfAnnotationOptions? options = null) =>
        AnnotationEditing.Add<PdfHighlightAnnotation>(this, PdfAnnotationKind.Highlight, default, options, d => AnnotationEditing.FillQuadPoints(d, quadrilaterals));

    /// <summary>Adds an underline annotation under the text in <paramref name="quadrilaterals"/>.</summary>
    /// <param name="quadrilaterals">One quadrilateral per run of text, its points top left, top right, bottom left, bottom right of the text.</param>
    /// <param name="options">Colour, flags and markup texts; <see langword="null"/> for red.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="quadrilaterals"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">There is no quadrilateral.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.10, Table 182 (PDF 1.3).</remarks>
    public PdfUnderlineAnnotation AddUnderlineAnnotation(IReadOnlyList<PdfQuadrilateral> quadrilaterals, PdfAnnotationOptions? options = null) =>
        AnnotationEditing.Add<PdfUnderlineAnnotation>(this, PdfAnnotationKind.Underline, default, options, d => AnnotationEditing.FillQuadPoints(d, quadrilaterals));

    /// <summary>Adds a squiggly underline annotation under the text in <paramref name="quadrilaterals"/>.</summary>
    /// <param name="quadrilaterals">One quadrilateral per run of text, its points top left, top right, bottom left, bottom right of the text.</param>
    /// <param name="options">Colour, flags and markup texts; <see langword="null"/> for red.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="quadrilaterals"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">There is no quadrilateral.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.10, Table 182 (PDF 1.4).</remarks>
    public PdfSquigglyAnnotation AddSquigglyAnnotation(IReadOnlyList<PdfQuadrilateral> quadrilaterals, PdfAnnotationOptions? options = null) =>
        AnnotationEditing.Add<PdfSquigglyAnnotation>(this, PdfAnnotationKind.Squiggly, default, options, d => AnnotationEditing.FillQuadPoints(d, quadrilaterals));

    /// <summary>Adds a strikeout annotation through the text in <paramref name="quadrilaterals"/>.</summary>
    /// <param name="quadrilaterals">One quadrilateral per run of text, its points top left, top right, bottom left, bottom right of the text.</param>
    /// <param name="options">Colour, flags and markup texts; <see langword="null"/> for red.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="quadrilaterals"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">There is no quadrilateral.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.10, Table 182 (PDF 1.3).</remarks>
    public PdfStrikeOutAnnotation AddStrikeOutAnnotation(IReadOnlyList<PdfQuadrilateral> quadrilaterals, PdfAnnotationOptions? options = null) =>
        AnnotationEditing.Add<PdfStrikeOutAnnotation>(this, PdfAnnotationKind.StrikeOut, default, options, d => AnnotationEditing.FillQuadPoints(d, quadrilaterals));

    /// <summary>Adds a rubber stamp annotation: <paramref name="name"/> in capitals in a rounded frame, filling <paramref name="rect"/>.</summary>
    /// <param name="rect">The stamp's rectangle in default user space.</param>
    /// <param name="name">The icon name (<c>Name</c>), one of Table 184's (such as <c>Approved</c>, <c>NotApproved</c>, <c>Draft</c>) or another; its words are the stamp's text.</param>
    /// <param name="options">Colour, width, flags and markup texts; <see langword="null"/> for red.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <exception cref="InvalidOperationException">The page is not an indirect object, or the rectangle has no area.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.12, Table 184 (PDF 1.3).</remarks>
    public PdfStampAnnotation AddStampAnnotation(PdfRectangle rect, string name, PdfAnnotationOptions? options = null) =>
        AnnotationEditing.Add<PdfStampAnnotation>(this, PdfAnnotationKind.Stamp, Checked(rect), options, d => AnnotationEditing.FillStamp(d, name));

    /// <summary>
    /// Removes an annotation from the page with its pop-up, the replies to it (<c>IRT</c>) on the page and their pop-ups, and the
    /// structure tree's references to each; a pop-up removed on its own is removed from its parent's <c>Popup</c>.
    /// </summary>
    /// <param name="annotation">One of <see cref="Annotations"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="annotation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The annotation is not on this page.</exception>
    /// <exception cref="InvalidOperationException">The annotation is a widget, which belongs to a form field.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.5.2 (<c>Annots</c>), §12.5.6.2 (<c>Popup</c>, <c>IRT</c>), §12.5.6.14, and §14.7.5.3 and §14.7.5.4 (object
    /// references and the parent tree). Only the page's <c>Annots</c> array (or the page, when it lists none of its own), the parent
    /// of a removed pop-up and the structure tree change, so an incremental save writes just those; the removed dictionaries stay
    /// in the file, unreferenced.
    /// </remarks>
    public void RemoveAnnotation(PdfAnnotation annotation) => AnnotationEditing.Remove(this, annotation);

    private static PdfRectangle Checked(PdfRectangle rect) => AnnotationEditing.Checked(rect, nameof(rect));
}
