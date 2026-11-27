using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>
/// Maps the geometry of an annotation dictionary through an affine transformation (issue #647): its rectangle and every entry of
/// the subtype tables that holds coordinates in default user space, so the annotation still sits over the content it marks after
/// that content is moved, scaled or reshaped.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.5.2, Table 166 (<c>Rect</c>); §12.5.3, Table 167 (<c>NoZoom</c>); §12.5.6.4 (text annotations behave as
/// <c>NoZoom</c>); the coordinate entries of Tables 176 (<c>QuadPoints</c>), 177 (<c>CL</c>, <c>RD</c>), 178 (<c>L</c>),
/// 180 (<c>RD</c>), 181 (<c>Vertices</c>, <c>Path</c>), 182 (<c>QuadPoints</c>), 183 (<c>RD</c>), 185 (<c>InkList</c>) and
/// 195 (<c>QuadPoints</c>). The appearance stream needs no change: it is fitted to the mapped rectangle (§12.5.5).
/// </remarks>
internal static class AnnotationGeometry
{
    /// <summary>The entries that hold a flat list of x, y pairs.</summary>
    private static readonly CosName[] FlatPoints = [AnnotationNames.L, AnnotationNames.Vertices, AnnotationNames.QuadPoints, AnnotationNames.CL];

    /// <summary>The entries that hold a list of lists of x, y pairs.</summary>
    private static readonly CosName[] NestedPoints = [AnnotationNames.InkList, AnnotationNames.Path];

    private static readonly CosName Text = new("Text");
    private const int NoZoom = (int)PdfAnnotationFlags.NoZoom;

    /// <summary>
    /// Maps <paramref name="dictionary"/>'s geometry through <paramref name="matrix"/>. With <paramref name="honorNoZoom"/>, an
    /// annotation with the <c>NoZoom</c> flag (or a text annotation) keeps its size: its upper-left corner is mapped and every
    /// coordinate moves with it. Entries that are not coordinate arrays are kept as they are.
    /// </summary>
    public static void Map(PdfDocument document, CosDictionary dictionary, Matrix matrix, bool honorNoZoom)
    {
        PdfRectangle? rect = AnnotationValues.ReadRectangle(document, dictionary.GetValueOrDefault(AnnotationNames.Rect));
        if (honorNoZoom && rect is { } fixedRect && KeepsSize(document, dictionary))
        {
            PathPoint corner = matrix.Transform(fixedRect.Left, fixedRect.Top);
            matrix = Matrix.CreateTranslation(corner.X - fixedRect.Left, corner.Y - fixedRect.Top);
        }

        if (rect is { } r)
        {
            dictionary[AnnotationNames.Rect] = AnnotationEditing.Rectangle(Bounds(matrix, r));
        }

        foreach (CosName key in FlatPoints)
        {
            if (dictionary.TryGetValue(key, out CosObject? value) && document.Resolve(value) is CosArray points)
            {
                dictionary[key] = Mapped(document, points, matrix);
            }
        }

        foreach (CosName key in NestedPoints)
        {
            if (dictionary.TryGetValue(key, out CosObject? value) && document.Resolve(value) is CosArray lists)
            {
                dictionary[key] = new CosArray(lists.Select(list => document.Resolve(list) is CosArray inner ? Mapped(document, inner, matrix) : list));
            }
        }

        // RD holds distances, not points: they scale with an axis-aligned map (left, bottom, right, top per Table 180).
        if (matrix.B == 0 && matrix.C == 0 && (matrix.A != 1 || matrix.D != 1)
            && dictionary.TryGetValue(AnnotationNames.RD, out CosObject? differences) && document.Resolve(differences) is CosArray { Count: 4 } rd)
        {
            double sx = Math.Abs(matrix.A);
            double sy = Math.Abs(matrix.D);
            var scaled = new CosArray();
            for (int index = 0; index < 4; index++)
            {
                scaled.Add(document.Resolve(rd[index]) is CosNumber number ? AnnotationEditing.Number(number.ToDouble() * (index % 2 == 0 ? sx : sy)) : rd[index]);
            }

            dictionary[AnnotationNames.RD] = scaled;
        }
    }

    /// <summary>The bounding box of <paramref name="rect"/>'s corners mapped through <paramref name="matrix"/>.</summary>
    public static PdfRectangle Bounds(Matrix matrix, PdfRectangle rect)
    {
        PathPoint p0 = matrix.Transform(rect.Left, rect.Bottom);
        PathPoint p1 = matrix.Transform(rect.Right, rect.Bottom);
        PathPoint p2 = matrix.Transform(rect.Right, rect.Top);
        PathPoint p3 = matrix.Transform(rect.Left, rect.Top);
        return new PdfRectangle(
            Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X)),
            Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y)),
            Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X)),
            Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y)));
    }

    /// <summary>Whether the annotation keeps its size when the page is zoomed: the <c>NoZoom</c> flag, or a text annotation (§12.5.6.4). Read raw, so no diagnostic is recorded.</summary>
    private static bool KeepsSize(PdfDocument document, CosDictionary dictionary)
    {
        if (Text.Equals(document.Resolve(dictionary.GetValueOrDefault(AnnotationNames.Subtype))))
        {
            return true;
        }

        return document.Resolve(dictionary.GetValueOrDefault(AnnotationNames.F)) switch
        {
            CosInteger integer => (integer.Value & NoZoom) != 0,
            CosReal real when double.IsFinite(real.Value) && Math.Abs(real.Value) <= uint.MaxValue => ((long)real.Value & NoZoom) != 0,
            _ => false,
        };
    }

    /// <summary>A copy of a flat coordinate array with every whole x, y pair mapped; an element that is not a number, and an odd last one, is kept.</summary>
    private static CosArray Mapped(PdfDocument document, CosArray points, Matrix matrix)
    {
        var mapped = new CosArray();
        int index = 0;
        for (; index + 1 < points.Count; index += 2)
        {
            if (document.Resolve(points[index]) is CosNumber x && document.Resolve(points[index + 1]) is CosNumber y)
            {
                PathPoint point = matrix.Transform(x.ToDouble(), y.ToDouble());
                mapped.Add(AnnotationEditing.Number(point.X));
                mapped.Add(AnnotationEditing.Number(point.Y));
            }
            else
            {
                mapped.Add(points[index]);
                mapped.Add(points[index + 1]);
            }
        }

        for (; index < points.Count; index++)
        {
            mapped.Add(points[index]);
        }

        return mapped;
    }
}
