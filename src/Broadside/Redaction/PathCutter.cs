using Broadside.Content;
using Broadside.Graphics;

namespace Broadside.Redaction;

/// <summary>
/// Cuts a painted path by redaction boxes in user space and writes what is left, with its painting operators. A subpath that no box
/// meets is written as it was; one whose region holds a box but whose outline does not meet it (a page background, a table cell) is
/// such a subpath too. Of a subpath a box meets, a filled rectangle keeps its parts outside the boxes and a stroked polyline its
/// segments' parts outside them; any other is removed whole.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.5.6.23 (what applying a redaction removes), §8.5.2 (Table 58: <c>m</c>, <c>l</c>, <c>c</c>, <c>h</c>, <c>re</c>), §8.5.3 (Table 59: painting; a fill closes open
/// subpaths, a stroke reaches half the line width past its outline). Bézier curves are not cut exactly: a subpath with a curve that a
/// box meets is removed whole, the conservative default.
/// </remarks>
internal ref struct PathCutter
{
    /// <summary>The number of line segments a cubic Bézier curve is flattened into for the hit test.</summary>
    private const int CurveSteps = 16;

    private readonly PathView _path;
    private readonly PathPaint _paint;
    private readonly FillRule _rule;
    private readonly ReadOnlySpan<PdfRectangle> _boxes;
    private readonly double _margin;
    private readonly ContentWriter _writer;

    public PathCutter(PathView path, PathPaint paint, FillRule rule, ReadOnlySpan<PdfRectangle> boxes, double margin, ContentWriter writer)
    {
        _path = path;
        _paint = paint;
        _rule = rule;
        _boxes = boxes;
        _margin = margin;
        _writer = writer;
    }

    private readonly bool Fills => _paint is PathPaint.Fill or PathPaint.FillAndStroke;

    private readonly bool Strokes => _paint is PathPaint.Stroke or PathPaint.FillAndStroke;

    /// <summary>Writes the cut path into the writer (cleared first); false, writing nothing, when no box meets it.</summary>
    /// <param name="removed">The number of subpaths removed or cut.</param>
    public readonly bool Cut(out int removed)
    {
        removed = 0;
        int subpath = 0, point = 0;
        while (Next(ref subpath, ref point, out Subpath part))
        {
            if ((Fills && Hits(part, 0, closed: true)) || (Strokes && Hits(part, _margin, part.Closed)))
            {
                removed++;
            }
        }

        if (removed == 0)
        {
            return false;
        }

        _writer.Clear();
        if (Fills)
        {
            int start = _writer.Length;
            subpath = 0;
            point = 0;
            while (Next(ref subpath, ref point, out Subpath part))
            {
                if (!Hits(part, 0, closed: true))
                {
                    WriteVerbatim(part);
                }
                else if (Rectangle(part, out PdfRectangle rect, out bool clockwise))
                {
                    WritePieces(rect, clockwise);
                }
            }

            if (_writer.Length > start)
            {
                _writer.Operator(_rule == FillRule.EvenOdd ? "f*" : "f");
            }
        }

        if (Strokes)
        {
            int start = _writer.Length;
            subpath = 0;
            point = 0;
            while (Next(ref subpath, ref point, out Subpath part))
            {
                if (!Hits(part, _margin, part.Closed))
                {
                    WriteVerbatim(part);
                }
                else if (!part.HasCurves)
                {
                    WriteStrokeParts(part);
                }
            }

            if (_writer.Length > start)
            {
                _writer.Operator("S");
            }
        }

        return true;
    }

    /// <summary>Reads the subpath that starts at verb <paramref name="verb"/>; false past the last.</summary>
    private readonly bool Next(ref int verb, ref int point, out Subpath part)
    {
        ReadOnlySpan<PathVerb> verbs = _path.Verbs;
        part = default;
        if (verb >= verbs.Length)
        {
            return false;
        }

        int first = verb;
        int firstPoint = point;
        bool curves = false;
        bool closed = false;
        point += PointCount(verbs[verb]);
        verb++;
        while (verb < verbs.Length && verbs[verb] != PathVerb.MoveTo)
        {
            curves |= verbs[verb] is PathVerb.CubicTo or PathVerb.QuadTo;
            closed |= verbs[verb] == PathVerb.Close;
            point += PointCount(verbs[verb]);
            verb++;
        }

        part = new Subpath(first, verb, firstPoint, point, curves, closed);
        return true;
    }

    private static int PointCount(PathVerb verb) => verb switch
    {
        PathVerb.MoveTo or PathVerb.LineTo => 1,
        PathVerb.QuadTo => 2,
        PathVerb.CubicTo => 3,
        _ => 0,
    };

    /// <summary>Returns whether a box, grown by <paramref name="margin"/>, meets the subpath's outline (closed for a fill).</summary>
    private readonly bool Hits(in Subpath part, double margin, bool closed)
    {
        ReadOnlySpan<PathVerb> verbs = _path.Verbs;
        ReadOnlySpan<PathPoint> points = _path.Points;
        foreach (PdfRectangle area in _boxes)
        {
            PdfRectangle box = RedactionGeometry.Inflate(area, margin);
            int point = part.FirstPoint;
            PathPoint start = points[point];
            PathPoint current = start;
            point++;
            bool any = false;
            for (int verb = part.FirstVerb + 1; verb < part.EndVerb; verb++)
            {
                switch (verbs[verb])
                {
                    case PathVerb.LineTo:
                        any = true;
                        if (Meets(current, points[point], box))
                        {
                            return true;
                        }

                        current = points[point++];
                        break;
                    case PathVerb.QuadTo:
                        any = true;
                        if (MeetsCurve(current, Lerp(current, points[point], 2.0 / 3), Lerp(points[point + 1], points[point], 2.0 / 3), points[point + 1], box))
                        {
                            return true;
                        }

                        current = points[point + 1];
                        point += 2;
                        break;
                    case PathVerb.CubicTo:
                        any = true;
                        if (MeetsCurve(current, points[point], points[point + 1], points[point + 2], box))
                        {
                            return true;
                        }

                        current = points[point + 2];
                        point += 3;
                        break;
                }
            }

            if ((closed && any && Meets(current, start, box)) || (!any && RedactionGeometry.Inside(box, start)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Meets(PathPoint start, PathPoint end, in PdfRectangle box) =>
        RedactionGeometry.ClipSegment(start, end, box, out _, out _);

    private static bool MeetsCurve(PathPoint p0, PathPoint p1, PathPoint p2, PathPoint p3, in PdfRectangle box)
    {
        PathPoint previous = p0;
        for (int step = 1; step <= CurveSteps; step++)
        {
            double t = (double)step / CurveSteps;
            double u = 1 - t;
            var point = new PathPoint(
                (u * u * u * p0.X) + (3 * u * u * t * p1.X) + (3 * u * t * t * p2.X) + (t * t * t * p3.X),
                (u * u * u * p0.Y) + (3 * u * u * t * p1.Y) + (3 * u * t * t * p2.Y) + (t * t * t * p3.Y));
            if (Meets(previous, point, box))
            {
                return true;
            }

            previous = point;
        }

        return false;
    }

    private static PathPoint Lerp(PathPoint from, PathPoint to, double t) => new(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));

    /// <summary>Returns whether the subpath is an axis-aligned rectangle (as <c>re</c> builds one), and its orientation.</summary>
    private readonly bool Rectangle(in Subpath part, out PdfRectangle rect, out bool clockwise)
    {
        rect = default;
        clockwise = false;
        ReadOnlySpan<PathVerb> verbs = _path.Verbs[part.FirstVerb..part.EndVerb];
        ReadOnlySpan<PathPoint> points = _path.Points[part.FirstPoint..part.EndPoint];
        bool shape = verbs is [PathVerb.MoveTo, PathVerb.LineTo, PathVerb.LineTo, PathVerb.LineTo, PathVerb.Close]
            || (verbs is [PathVerb.MoveTo, PathVerb.LineTo, PathVerb.LineTo, PathVerb.LineTo, PathVerb.LineTo, PathVerb.Close] && points[4] == points[0]);
        if (!shape)
        {
            return false;
        }

        PathPoint a = points[0], b = points[1], c = points[2], d = points[3];
        bool horizontalFirst = a.Y == b.Y && b.X == c.X && c.Y == d.Y && d.X == a.X;
        bool verticalFirst = a.X == b.X && b.Y == c.Y && c.X == d.X && d.Y == a.Y;
        if (!horizontalFirst && !verticalFirst)
        {
            return false;
        }

        rect = new PdfRectangle(a.X, a.Y, c.X, c.Y);
        double area = ((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y));
        clockwise = area < 0;
        return rect.Width > 0 && rect.Height > 0;
    }

    private readonly void WritePieces(PdfRectangle rect, bool clockwise)
    {
        List<PdfRectangle> pieces = [rect];
        Span<PdfRectangle> cut = stackalloc PdfRectangle[4];
        foreach (PdfRectangle box in _boxes)
        {
            List<PdfRectangle> next = [];
            foreach (PdfRectangle piece in pieces)
            {
                int count = RedactionGeometry.Subtract(piece, box, cut);
                for (int index = 0; index < count; index++)
                {
                    if (cut[index].Width > 0 && cut[index].Height > 0)
                    {
                        next.Add(cut[index]);
                    }
                }
            }

            pieces = next;
        }

        foreach (PdfRectangle piece in pieces)
        {
            if (clockwise)
            {
                Precise(_writer, piece.Left).Then(piece.Top).Then(piece.Width).Then(-piece.Height).Operator("re");
            }
            else
            {
                Precise(_writer, piece.Left).Then(piece.Bottom).Then(piece.Width).Then(piece.Height).Operator("re");
            }
        }
    }

    /// <summary>Writes the parts of a stroked polyline's segments outside every box grown by half the line width, joined where they meet.</summary>
    private readonly void WriteStrokeParts(in Subpath part)
    {
        ReadOnlySpan<PathVerb> verbs = _path.Verbs;
        ReadOnlySpan<PathPoint> points = _path.Points;
        List<(PathPoint Start, PathPoint End)> segments = [];
        int point = part.FirstPoint;
        PathPoint start = points[point++];
        PathPoint current = start;
        for (int verb = part.FirstVerb + 1; verb < part.EndVerb; verb++)
        {
            if (verbs[verb] == PathVerb.LineTo)
            {
                segments.Add((current, points[point]));
                current = points[point++];
            }
            else if (verbs[verb] == PathVerb.Close)
            {
                segments.Add((current, start));
                current = start;
            }
        }

        foreach (PdfRectangle area in _boxes)
        {
            PdfRectangle box = RedactionGeometry.Inflate(area, _margin);
            List<(PathPoint Start, PathPoint End)> kept = [];
            foreach ((PathPoint a, PathPoint b) in segments)
            {
                if (!RedactionGeometry.ClipSegment(a, b, box, out double enter, out double leave))
                {
                    kept.Add((a, b));
                    continue;
                }

                if (enter > 1e-9)
                {
                    kept.Add((a, Lerp(a, b, enter)));
                }

                if (leave < 1 - 1e-9)
                {
                    kept.Add((Lerp(a, b, leave), b));
                }
            }

            segments = kept;
        }

        bool open = false;
        PathPoint last = default;
        foreach ((PathPoint a, PathPoint b) in segments)
        {
            if (!open || a != last)
            {
                Precise(_writer, a.X).Then(a.Y).Operator("m");
                open = true;
            }

            Precise(_writer, b.X).Then(b.Y).Operator("l");
            last = b;
        }
    }

    private readonly void WriteVerbatim(in Subpath part)
    {
        ReadOnlySpan<PathVerb> verbs = _path.Verbs;
        ReadOnlySpan<PathPoint> points = _path.Points;
        int point = part.FirstPoint;
        PathPoint current = points[point];
        for (int verb = part.FirstVerb; verb < part.EndVerb; verb++)
        {
            switch (verbs[verb])
            {
                case PathVerb.MoveTo:
                    Precise(_writer, points[point].X).Then(points[point].Y).Operator("m");
                    current = points[point++];
                    break;
                case PathVerb.LineTo:
                    Precise(_writer, points[point].X).Then(points[point].Y).Operator("l");
                    current = points[point++];
                    break;
                case PathVerb.QuadTo:
                    // A quadratic segment as the cubic with the same curve: controls two thirds of the way to the quadratic's.
                    WriteCurve(Lerp(current, points[point], 2.0 / 3), Lerp(points[point + 1], points[point], 2.0 / 3), points[point + 1]);
                    current = points[point + 1];
                    point += 2;
                    break;
                case PathVerb.CubicTo:
                    WriteCurve(points[point], points[point + 1], points[point + 2]);
                    current = points[point + 2];
                    point += 3;
                    break;
                case PathVerb.Close:
                    _writer.Operator("h");
                    break;
            }
        }
    }

    private readonly void WriteCurve(PathPoint first, PathPoint second, PathPoint end) =>
        Precise(_writer, first.X).Then(first.Y).Then(second.X).Then(second.Y).Then(end.X).Then(end.Y).Operator("c");

    private static PreciseWriter Precise(ContentWriter writer, double value) => new PreciseWriter(writer).Then(value);

    /// <param name="FirstVerb">The subpath's moveto.</param>
    /// <param name="EndVerb">One past its last verb.</param>
    /// <param name="FirstPoint">Its moveto's point.</param>
    /// <param name="EndPoint">One past its last point.</param>
    /// <param name="HasCurves">Whether it has a curve.</param>
    /// <param name="Closed">Whether it ends with a closepath.</param>
    private readonly record struct Subpath(int FirstVerb, int EndVerb, int FirstPoint, int EndPoint, bool HasCurves, bool Closed);

    /// <summary>
    /// Writes coordinates with ten decimals rather than the content writer's four: a cut edge must stay where it was cut even under a
    /// CTM that magnifies user space many thousand times.
    /// </summary>
    private readonly ref struct PreciseWriter(ContentWriter writer)
    {
        public PreciseWriter Then(double value)
        {
            double rounded = double.IsFinite(value) ? Math.Round(value, 10) : 0;
            if (rounded == 0)
            {
                rounded = 0;
            }

            int written;
            for (int size = 48; !rounded.TryFormat(writer.GetSpan(size), out written, "0.##########", System.Globalization.CultureInfo.InvariantCulture); size *= 2)
            {
            }

            writer.Advance(written);
            writer.Write((byte)' ');
            return this;
        }

        public ContentWriter Operator(string name) => writer.Operator(name);
    }
}
