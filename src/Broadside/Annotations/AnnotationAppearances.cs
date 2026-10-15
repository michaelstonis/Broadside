using System.Text;
using Broadside.Fonts;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>
/// Generates the normal appearance stream (§12.5.5) of the annotation subtypes the library creates (issue #165), from the
/// annotation dictionary's own entries, so that every processor draws the annotation the same way.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.5.5 and the subtype clauses of §12.5.6. The form's <c>BBox</c> is the annotation rectangle and its
/// <c>Matrix</c> the identity, so the appearance is drawn in default user space and the algorithm of §12.5.5 places it unscaled.
/// Geometric subtypes (Line, Polygon, PolyLine, Ink and the text markups) first get a rectangle that bounds their points and
/// strokes. Drawn: notes as a coloured page icon; links' borders; free text wrapped in Helvetica (the document's embedded
/// stand-in when Helvetica cannot encode it) from the size and colour of <c>DA</c>, aligned by <c>Q</c>; lines, polygons,
/// polylines, ink paths, squares and circles stroked in <c>C</c> at the border width, filled in <c>IC</c>; highlights filled in
/// <c>C</c> under the Multiply blend mode; underlines, strike-outs and squiggles along each quadrilateral, taking its points
/// in the order most writers use (top left, top right, bottom left, bottom right); stamps as their name in a rounded frame.
/// Line endings (<c>LE</c>), border styles other than solid and border effects are not drawn.
/// </para>
/// <para>
/// The constant opacity (<c>CA</c>) is not written into the stream: §12.5.5 applies the annotation's own to its appearance.
/// </para>
/// </remarks>
internal static class AnnotationAppearances
{
    private const double Kappa = 0.5522847498307936;

    private static readonly PdfDeviceColor Yellow = new(1, 1, 0);
    private static readonly PdfDeviceColor Red = new(1, 0, 0);
    private static readonly PdfDeviceColor Black = new(0);

    /// <summary>Returns whether appearances of <paramref name="kind"/> can be generated.</summary>
    public static bool CanGenerate(PdfAnnotationKind kind) => kind is PdfAnnotationKind.Text or PdfAnnotationKind.Link
        or PdfAnnotationKind.FreeText or PdfAnnotationKind.Line or PdfAnnotationKind.Square or PdfAnnotationKind.Circle
        or PdfAnnotationKind.Polygon or PdfAnnotationKind.PolyLine or PdfAnnotationKind.Ink or PdfAnnotationKind.Highlight
        or PdfAnnotationKind.Underline or PdfAnnotationKind.Squiggly or PdfAnnotationKind.StrikeOut or PdfAnnotationKind.Stamp;

    /// <summary>The colour a new annotation of <paramref name="kind"/> is given when the caller chooses none.</summary>
    public static PdfDeviceColor? DefaultColor(PdfAnnotationKind kind) => kind switch
    {
        PdfAnnotationKind.Text or PdfAnnotationKind.Highlight => Yellow,
        PdfAnnotationKind.Link or PdfAnnotationKind.FreeText => null,
        _ => Red,
    };

    /// <summary>
    /// Replaces the annotation's appearance dictionary with one normal appearance drawn from its entries, after fitting the
    /// rectangle of a geometric subtype to its points.
    /// </summary>
    /// <returns><see langword="false"/> when the subtype has no generator; the annotation is then unchanged.</returns>
    public static bool Generate(PdfAnnotation annotation)
    {
        if (!CanGenerate(annotation.Kind))
        {
            return false;
        }

        PdfDocument document = annotation.Document;
        double width = annotation.Border.Width;
        if (FittedRectangle(annotation, width) is { } fitted)
        {
            annotation.Dictionary[AnnotationNames.Rect] = AnnotationEditing.Rectangle(fitted);
        }

        PdfRectangle rect = annotation.Rect;
        if (!(rect.Width > 0) || !(rect.Height > 0))
        {
            throw new InvalidOperationException("The annotation's rectangle has no area, so it has no appearance to draw (ISO 32000-2 §12.5.5).");
        }

        PdfFormXObject form = document.CreateFormXObject(rect);
        using (PdfCanvas canvas = form.CreateCanvas())
        {
            Draw(annotation, canvas, rect, width);
        }

        annotation.Dictionary[AnnotationNames.AP] = new CosDictionary { [AnnotationNames.N] = form.Reference! };
        annotation.Dictionary.Remove(AnnotationNames.AS);
        return true;
    }

    /// <summary>The rectangle that bounds a geometric annotation's points and strokes; null for the subtypes whose rectangle the caller gives.</summary>
    private static PdfRectangle? FittedRectangle(PdfAnnotation annotation, double width)
    {
        IEnumerable<PathPoint>? points = annotation switch
        {
            PdfLineAnnotation line => [line.Start, line.End],
            PdfPolygonAnnotation polygon => polygon.Vertices,
            PdfPolyLineAnnotation polyline => polyline.Vertices,
            PdfInkAnnotation ink => ink.InkList.SelectMany(stroke => stroke),
            PdfTextMarkupAnnotation markup => markup.QuadPoints.SelectMany(quad => (PathPoint[])[quad.Point1, quad.Point2, quad.Point3, quad.Point4]),
            _ => null,
        };
        if (points is null)
        {
            return null;
        }

        double left = double.PositiveInfinity, bottom = double.PositiveInfinity, right = double.NegativeInfinity, top = double.NegativeInfinity;
        foreach (PathPoint point in points)
        {
            left = Math.Min(left, point.X);
            bottom = Math.Min(bottom, point.Y);
            right = Math.Max(right, point.X);
            top = Math.Max(top, point.Y);
        }

        if (!double.IsFinite(left))
        {
            return null;
        }

        // A stroke reaches half its width past its points; a miter up to the default limit's ten widths, so lines get a full width
        // and round joins; the text markups draw inside their quadrilaterals.
        double margin = annotation is PdfTextMarkupAnnotation ? 0 : width + 1;
        return new PdfRectangle(left - margin, bottom - margin, right + margin, top + margin);
    }

    private static void Draw(PdfAnnotation annotation, PdfCanvas canvas, PdfRectangle rect, double width)
    {
        PdfDeviceColor? stroke = Usable(annotation.Color);
        switch (annotation)
        {
            case PdfTextAnnotation:
                DrawNote(canvas, rect, stroke ?? Yellow);
                break;
            case PdfLinkAnnotation:
                // A link border without a colour is black, as viewers draw it; an empty colour array is transparent.
                DrawFramed(canvas, rect, width, annotation.Color is null ? Black : stroke, fill: null, ellipse: false);
                break;
            case PdfSquareAnnotation:
                DrawFramed(canvas, rect, width, stroke, Interior(annotation), ellipse: false);
                break;
            case PdfCircleAnnotation:
                DrawFramed(canvas, rect, width, stroke, Interior(annotation), ellipse: true);
                break;
            case PdfFreeTextAnnotation freeText:
                DrawFreeText(annotation.Document, freeText, canvas, rect, width, stroke);
                break;
            case PdfLineAnnotation line:
                DrawPath(canvas, [line.Start, line.End], close: false, width, stroke, fill: null, round: false);
                break;
            case PdfPolygonAnnotation polygon:
                DrawPath(canvas, polygon.Vertices, close: true, width, stroke, Interior(annotation), round: false);
                break;
            case PdfPolyLineAnnotation polyline:
                DrawPath(canvas, polyline.Vertices, close: false, width, stroke, fill: null, round: false);
                break;
            case PdfInkAnnotation ink:
                foreach (IReadOnlyList<PathPoint> path in ink.InkList)
                {
                    DrawPath(canvas, path, close: false, width, stroke, fill: null, round: true);
                }

                break;
            case PdfTextMarkupAnnotation markup when stroke is not null:
                DrawMarkup(canvas, markup, stroke);
                break;
            case PdfStampAnnotation stamp:
                DrawStamp(annotation.Document, canvas, rect, Math.Max(width, 1), stroke ?? Red, stamp.Name);
                break;
        }
    }

    private static PdfDeviceColor? Interior(PdfAnnotation annotation) => Usable(annotation switch
    {
        PdfSquareAnnotation square => square.InteriorColor,
        PdfCircleAnnotation circle => circle.InteriorColor,
        PdfPolygonAnnotation polygon => polygon.InteriorColor,
        _ => null,
    });

    private static PdfDeviceColor? Usable(PdfDeviceColor? color) => color is { IsTransparent: false } ? color : null;

    internal static PdfColor ToColor(PdfDeviceColor color)
    {
        IReadOnlyList<double> c = color.Components;
        return c.Count switch
        {
            1 => PdfColor.FromGray((float)c[0]),
            3 => PdfColor.FromRgb((float)c[0], (float)c[1], (float)c[2]),
            _ => PdfColor.FromCmyk((float)c[0], (float)c[1], (float)c[2], (float)c[3]),
        };
    }

    /// <summary>A note icon: a page in the annotation's colour with a black outline and three lines of "text".</summary>
    private static void DrawNote(PdfCanvas canvas, PdfRectangle rect, PdfDeviceColor color)
    {
        canvas.SetFillColor(ToColor(color));
        canvas.SetStrokeColor(PdfColor.FromGray(0));
        canvas.SetLineWidth(1);
        canvas.Rectangle(rect.Left + 0.5, rect.Bottom + 0.5, rect.Width - 1, rect.Height - 1);
        canvas.FillAndStroke();
        for (int line = 1; line <= 3; line++)
        {
            // Whole units from the rectangle's edge, so a one-point line on a whole-unit rectangle covers whole pixels at 72 dpi.
            double y = rect.Bottom + Math.Floor(rect.Height * line / 4) + 0.5;
            canvas.MoveTo(rect.Left + (rect.Width * 0.2), y);
            canvas.LineTo(rect.Right - (rect.Width * 0.2), y);
        }

        canvas.Stroke();
    }

    /// <summary>A rectangle or ellipse inset by half the border width, so the border stays inside the rectangle (§12.5.6.8).</summary>
    private static void DrawFramed(PdfCanvas canvas, PdfRectangle rect, double width, PdfDeviceColor? stroke, PdfDeviceColor? fill, bool ellipse)
    {
        bool stroked = stroke is not null && width > 0;
        if (!stroked && fill is null)
        {
            return;
        }

        double inset = stroked ? width / 2 : 0;
        double left = rect.Left + inset, bottom = rect.Bottom + inset, w = rect.Width - (2 * inset), h = rect.Height - (2 * inset);
        if (!(w > 0) || !(h > 0))
        {
            return;
        }

        SetPaint(canvas, width, stroked ? stroke : null, fill);
        if (ellipse)
        {
            double rx = w / 2, ry = h / 2, cx = left + rx, cy = bottom + ry;
            canvas.MoveTo(cx + rx, cy);
            canvas.CurveTo(cx + rx, cy + (ry * Kappa), cx + (rx * Kappa), cy + ry, cx, cy + ry);
            canvas.CurveTo(cx - (rx * Kappa), cy + ry, cx - rx, cy + (ry * Kappa), cx - rx, cy);
            canvas.CurveTo(cx - rx, cy - (ry * Kappa), cx - (rx * Kappa), cy - ry, cx, cy - ry);
            canvas.CurveTo(cx + (rx * Kappa), cy - ry, cx + rx, cy - (ry * Kappa), cx + rx, cy);
            canvas.ClosePath();
        }
        else
        {
            canvas.Rectangle(left, bottom, w, h);
        }

        Finish(canvas, stroked ? stroke : null, fill);
    }

    private static void DrawPath(PdfCanvas canvas, IReadOnlyList<PathPoint> points, bool close, double width, PdfDeviceColor? stroke, PdfDeviceColor? fill, bool round)
    {
        bool stroked = stroke is not null && width > 0;
        if (points.Count == 0 || (!stroked && fill is null))
        {
            return;
        }

        if (round)
        {
            canvas.SetLineCap(LineCap.Round);
            canvas.SetLineJoin(LineJoin.Round);
        }

        SetPaint(canvas, width, stroked ? stroke : null, fill);

        canvas.MoveTo(points[0].X, points[0].Y);
        for (int index = 1; index < points.Count; index++)
        {
            canvas.LineTo(points[index].X, points[index].Y);
        }

        if (close)
        {
            canvas.ClosePath();
        }

        Finish(canvas, stroked ? stroke : null, fill);
    }

    private static void SetPaint(PdfCanvas canvas, double width, PdfDeviceColor? stroke, PdfDeviceColor? fill)
    {
        if (stroke is not null)
        {
            canvas.SetStrokeColor(ToColor(stroke));
            canvas.SetLineWidth(width);
        }

        if (fill is not null)
        {
            canvas.SetFillColor(ToColor(fill));
        }
    }

    private static void Finish(PdfCanvas canvas, PdfDeviceColor? stroke, PdfDeviceColor? fill)
    {
        if (stroke is not null && fill is not null)
        {
            canvas.FillAndStroke();
        }
        else if (stroke is not null)
        {
            canvas.Stroke();
        }
        else
        {
            canvas.Fill();
        }
    }

    /// <summary>
    /// Text markups along each quadrilateral, in the frame whose origin is its third point, whose x axis runs to the fourth and
    /// whose y axis runs to the first: a highlight fills it under Multiply, so the text below shows through (§11.3.5.2); an
    /// underline is a band at its foot, a strike-out one across its middle, a squiggle a zigzag at its foot.
    /// </summary>
    private static void DrawMarkup(PdfCanvas canvas, PdfTextMarkupAnnotation markup, PdfDeviceColor color)
    {
        canvas.SetFillColor(ToColor(color));
        canvas.SetStrokeColor(ToColor(color));
        if (markup is PdfHighlightAnnotation)
        {
            canvas.SetBlendMode(BlendMode.Multiply);
        }

        foreach (PdfQuadrilateral quad in markup.QuadPoints)
        {
            PathPoint origin = quad.Point3;
            var across = new PathPoint(quad.Point4.X - origin.X, quad.Point4.Y - origin.Y);
            var up = new PathPoint(quad.Point1.X - origin.X, quad.Point1.Y - origin.Y);
            double height = Math.Sqrt((up.X * up.X) + (up.Y * up.Y));
            double length = Math.Sqrt((across.X * across.X) + (across.Y * across.Y));
            if (!(height > 0) || !(length > 0))
            {
                continue;
            }

            PathPoint At(double u, double v) => new(origin.X + (u * across.X) + (v * up.X), origin.Y + (u * across.Y) + (v * up.Y));
            // Bands of whole units from the quadrilateral's foot, so markups on whole-unit text boxes cover whole pixels at 72 dpi.
            double thickness = Math.Max(1, Math.Round(height / 14));
            double band = thickness / height;
            double strike = Math.Round((height * 0.45) - (thickness / 2)) / height;
            switch (markup)
            {
                case PdfHighlightAnnotation:
                    Quad(canvas, At(0, 0), At(1, 0), At(1, 1), At(0, 1));
                    break;
                case PdfUnderlineAnnotation:
                    Quad(canvas, At(0, 0), At(1, 0), At(1, band), At(0, band));
                    break;
                case PdfStrikeOutAnnotation:
                    Quad(canvas, At(0, strike), At(1, strike), At(1, strike + band), At(0, strike + band));
                    break;
                case PdfSquigglyAnnotation:
                    double amplitude = 0.15;
                    double step = height * amplitude / length;
                    int count = Math.Max(2, (int)Math.Ceiling(1 / step));
                    PathPoint first = At(0, band);
                    canvas.SetLineWidth(band * height);
                    canvas.MoveTo(first.X, first.Y);
                    for (int index = 1; index <= count; index++)
                    {
                        PathPoint next = At(Math.Min(1, index * step), (index % 2 == 1 ? amplitude : 0) + band);
                        canvas.LineTo(next.X, next.Y);
                    }

                    canvas.Stroke();
                    continue;
            }

            canvas.Fill();
        }
    }

    private static void Quad(PdfCanvas canvas, PathPoint a, PathPoint b, PathPoint c, PathPoint d)
    {
        canvas.MoveTo(a.X, a.Y);
        canvas.LineTo(b.X, b.Y);
        canvas.LineTo(c.X, c.Y);
        canvas.LineTo(d.X, d.Y);
        canvas.ClosePath();
    }

    /// <summary>Free text: an optional border in <c>C</c>, then the contents wrapped at word boundaries inside a two-point padding.</summary>
    private static void DrawFreeText(PdfDocument document, PdfFreeTextAnnotation annotation, PdfCanvas canvas, PdfRectangle rect, double width, PdfDeviceColor? stroke)
    {
        DrawFramed(canvas, rect, width, stroke, fill: null, ellipse: false);
        string text = annotation.Contents ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        PdfDefaultAppearance? appearance = annotation.DefaultAppearance is { } da ? PdfDefaultAppearance.Parse(Encoding.Latin1.GetBytes(da)) : null;
        double size = appearance is { FontSize: > 0 and <= 1_000 } ? appearance.FontSize : 12;
        PdfCanvasFont font = TextFont(document, Standard14Font.Helvetica, text);
        double padding = (stroke is not null ? width : 0) + 2;
        double left = rect.Left + padding, right = rect.Right - padding, top = rect.Top - padding;
        if (!(right > left))
        {
            return;
        }

        List<string> lines = Wrap(text, font, size, right - left);
        canvas.SaveState();
        canvas.Rectangle(left, rect.Bottom + padding, right - left, Math.Max(0, top - rect.Bottom - padding));
        canvas.Clip();
        canvas.EndPath();
        canvas.SetFillColor(appearance?.Color ?? PdfColor.FromGray(0));
        canvas.BeginText();
        canvas.SetFont(font, size);
        double leading = size * 1.2;
        double baseline = top - (font.Ascent / 1000 * size);
        double previousX = 0, previousY = 0;
        foreach (string line in lines)
        {
            double lineWidth = font.MeasureWidth(line, size);
            double x = annotation.Justification switch
            {
                PdfTextJustification.Centered => left + ((right - left - lineWidth) / 2),
                PdfTextJustification.Right => right - lineWidth,
                _ => left,
            };
            canvas.MoveText(x - previousX, baseline - previousY);
            canvas.ShowText(line);
            (previousX, previousY) = (x, baseline);
            baseline -= leading;
        }

        canvas.EndText();
        canvas.RestoreState();
    }

    /// <summary>Breaks <paramref name="text"/> at its line breaks and, greedily, at spaces so each line fits <paramref name="width"/>; a word wider than a line stands alone.</summary>
    private static List<string> Wrap(string text, PdfCanvasFont font, double size, double width)
    {
        List<string> lines = [];
        foreach (string paragraph in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = new StringBuilder();
            foreach (string word in paragraph.Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && font.MeasureWidth(candidate, size) > width)
                {
                    lines.Add(line.ToString());
                    line.Clear().Append(word);
                }
                else
                {
                    line.Clear().Append(candidate);
                }
            }

            lines.Add(line.ToString());
        }

        return lines;
    }

    /// <summary>A stamp: its name in capitals, Helvetica Bold, centred in a rounded frame, in the annotation's colour.</summary>
    private static void DrawStamp(PdfDocument document, PdfCanvas canvas, PdfRectangle rect, double width, PdfDeviceColor color, string? name)
    {
        PdfColor ink = ToColor(color);
        double inset = width / 2;
        double left = rect.Left + inset, bottom = rect.Bottom + inset, right = rect.Right - inset, top = rect.Top - inset;
        double radius = Math.Min(Math.Min(right - left, top - bottom) / 4, 8);
        if (right - left > 0 && top - bottom > 0)
        {
            double k = radius * (1 - Kappa);
            canvas.SetStrokeColor(ink);
            canvas.SetLineWidth(width);
            canvas.MoveTo(left + radius, bottom);
            canvas.LineTo(right - radius, bottom);
            canvas.CurveTo(right - k, bottom, right, bottom + k, right, bottom + radius);
            canvas.LineTo(right, top - radius);
            canvas.CurveTo(right, top - k, right - k, top, right - radius, top);
            canvas.LineTo(left + radius, top);
            canvas.CurveTo(left + k, top, left, top - k, left, top - radius);
            canvas.LineTo(left, bottom + radius);
            canvas.CurveTo(left, bottom + k, left + k, bottom, left + radius, bottom);
            canvas.ClosePath();
            canvas.Stroke();
        }

        string text = StampText(name);
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.HelveticaBold);
        if (text.Length == 0 || !font.CanEncode(text))
        {
            return;
        }

        double available = rect.Width - (2 * width) - 12;
        double unit = font.MeasureWidth(text, 1);
        double size = Math.Min(rect.Height * 0.55, unit > 0 ? available / unit : 0);
        if (!(size > 0))
        {
            return;
        }

        double x = rect.Left + ((rect.Width - (unit * size)) / 2);
        double y = rect.Bottom + ((rect.Height - (font.CapHeight / 1000 * size)) / 2);
        canvas.SetFillColor(ink);
        canvas.BeginText();
        canvas.SetFont(font, size);
        canvas.MoveText(x, y);
        canvas.ShowText(text);
        canvas.EndText();
    }

    /// <summary>The words of a stamp name in capitals: <c>NotApproved</c> reads "NOT APPROVED" (Table 184).</summary>
    internal static string StampText(string? name)
    {
        var text = new StringBuilder();
        foreach (char c in name ?? "Draft")
        {
            if (char.IsUpper(c) && text.Length > 0 && text[^1] != ' ')
            {
                text.Append(' ');
            }

            text.Append(char.ToUpperInvariant(c));
        }

        return text.ToString();
    }

    /// <summary>A Standard 14 font when it encodes the text, else the document's embedded stand-in found through its font resolvers.</summary>
    internal static PdfCanvasFont TextFont(PdfDocument document, Standard14Font standard, string text)
    {
        PdfCanvasFont font = document.GetStandard14Font(standard);
        if (font.CanEncode(text))
        {
            return font;
        }

        string? missing = null;
        if (document.FontResolvers.ResolveFont(new FontQuery("Helvetica") { FontType = PdfFontType.TrueType, Standard14 = standard }) is { } resolution
            && EmbeddedCanvasFont.Get(document, resolution, [text], out missing, out _) is { } embedded)
        {
            return embedded;
        }

        throw new InvalidOperationException(missing is not null
            ? $"No font program found by the engine's font resolvers has a glyph for the character '{missing}' (U+{char.ConvertToUtf32(missing, 0):X4})."
            : "The text has characters Helvetica cannot show, and no TrueType font program to embed for them was found. Configure the Standard 14 fonts package (PdfOptions.UseStandard14Fonts()) or a font resolver (PdfOptions.UseFontResolver).");
    }
}
