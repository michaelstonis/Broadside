using Broadside.Content;
using Broadside.Graphics;

namespace Broadside.Layout;

/// <summary>
/// Collects, beside the text extractor in the same pass, the page's ruling lines: horizontal and vertical stroked segments, thin
/// filled rectangles, and the edges of other filled rectangles, in page space. Table detection reads tables' grids from them.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.5.2 (path construction: <c>m</c>, <c>l</c>, <c>re</c>, <c>h</c>) and §8.5.3 (path painting, through the CTM). A
/// segment is a ruling when it runs along an axis of the page within <see cref="Slack"/>; curves end a subpath's chance of being a
/// rectangle and are not rulings. A filled rectangle no thicker than <see cref="ThinRule"/> is one ruling along its middle; a
/// thicker one (a cell's background) gives its four edges; a white one gives nothing. Hidden content is skipped unless asked for.
/// At most <see cref="MaxRulings"/> are kept per page.
/// Figure detection (issue #190) reads the page's images (§8.9) and painted paths outside <c>Artifact</c> marked content
/// (§14.8.2.2) as <see cref="Graphic"/>s, at most <see cref="MaxGraphics"/>; Type 3 glyph procedures (§9.6.4) are glyphs, not
/// graphics, and are not entered.
/// </remarks>
internal sealed class RulingCollector(bool includeHidden) : ContentProcessor
{
    /// <summary>The most rulings kept for a page.</summary>
    internal const int MaxRulings = 4096;

    /// <summary>The most graphics kept for a page.</summary>
    internal const int MaxGraphics = 2048;

    /// <summary>The thickest filled rectangle, in points, read as a line rather than a box.</summary>
    internal const double ThinRule = 2.5;

    /// <summary>How far, in points, a segment's ends may be off an axis for it to be a ruling.</summary>
    internal const double Slack = 0.5;

    private readonly List<Ruling> _horizontal = [];
    private readonly List<Ruling> _vertical = [];
    private readonly List<Graphic> _graphics = [];
    private int _markedDepth;
    private int _artifactDepth;

    /// <inheritdoc/>
    public override ContentEvents Events =>
        ContentEvents.Paths | ContentEvents.Images | ContentEvents.MarkedContent | (includeHidden ? ContentEvents.HiddenContent : ContentEvents.None);

    /// <summary>Gets the horizontal rulings of the last run: <see cref="Ruling.Position"/> is their y, the span runs along x.</summary>
    public List<Ruling> Horizontal => _horizontal;

    /// <summary>Gets the vertical rulings of the last run: <see cref="Ruling.Position"/> is their x, the span runs along y.</summary>
    public List<Ruling> Vertical => _vertical;

    /// <summary>Gets the page's painted images and paths outside artifacts, for figure detection, in painting order.</summary>
    public List<Graphic> Graphics => _graphics;

    /// <inheritdoc/>
    public override void BeginRun(ContentContext context)
    {
        _horizontal.Clear();
        _vertical.Clear();
        _graphics.Clear();
        _markedDepth = 0;
        _artifactDepth = 0;
    }

    /// <inheritdoc/>
    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (path.Paint == PathPaint.None || path.Path.IsEmpty || (path.IsHidden && !includeHidden))
        {
            return;
        }

        Matrix ctm = context.State.Ctm;
        bool stroke = path.Paint is PathPaint.Stroke or PathPaint.FillAndStroke;
        bool fill = path.Paint is PathPaint.Fill or PathPaint.FillAndStroke;
        bool white = fill && IsWhite(context.State.FillColor);

        // Whether the path is more than lines and frames: a curve, a slanted segment, or a filled area thicker than a rule.
        bool shape = false;
        ReadOnlySpan<PathVerb> verbs = path.Path.Verbs;
        ReadOnlySpan<PathPoint> points = path.Path.Points;
        int point = 0;
        PathPoint start = default;
        PathPoint current = default;

        // The subpath's corners while it can still be an axis-aligned rectangle.
        int corners = 0;
        bool straight = true;
        double left = 0;
        double bottom = 0;
        double right = 0;
        double top = 0;
        for (int index = 0; index <= verbs.Length; index++)
        {
            PathVerb verb = index < verbs.Length ? verbs[index] : PathVerb.MoveTo;
            if (verb == PathVerb.MoveTo || verb == PathVerb.Close)
            {
                if (verb == PathVerb.Close && stroke)
                {
                    Segment(current, start);
                }

                shape |= fill && !white && right - left > ThinRule && top - bottom > ThinRule;
                if (fill && !white && straight && corners is 4 or 5)
                {
                    Box(left, bottom, right, top);
                }

                if (verb == PathVerb.Close)
                {
                    current = start;
                    corners = 0;
                    continue;
                }

                if (index == verbs.Length || point >= points.Length)
                {
                    break;
                }

                start = current = ctm.Transform(points[point++]);
                (left, bottom, right, top) = (current.X, current.Y, current.X, current.Y);
                corners = 1;
                straight = true;
                continue;
            }

            int count = verb switch
            {
                PathVerb.LineTo => 1,
                PathVerb.QuadTo => 2,
                _ => 3,
            };
            if (point + count > points.Length)
            {
                break;
            }

            PathPoint next = ctm.Transform(points[point + count - 1]);
            point += count;
            if (verb != PathVerb.LineTo)
            {
                straight = false;
                shape = true;
            }
            else
            {
                if (stroke)
                {
                    Segment(current, next);
                }

                bool aligned = Math.Abs(next.X - current.X) <= Slack || Math.Abs(next.Y - current.Y) <= Slack;
                straight &= aligned;
                shape |= !aligned;
                corners++;
                left = Math.Min(left, next.X);
                bottom = Math.Min(bottom, next.Y);
                right = Math.Max(right, next.X);
                top = Math.Max(top, next.Y);
            }

            current = next;
        }

        if (_artifactDepth == 0 && (stroke || !white))
        {
            PdfRectangle box = path.Path.Bounds;
            AddGraphic(Quad.FromRectangle(ctm, box.Left, box.Bottom, box.Right, box.Top).Bounds, shape ? GraphicKind.Shape : GraphicKind.Rule);
        }
    }

    public override void PaintImage(in ImageEvent image, ContentContext context)
    {
        if (_artifactDepth == 0 && (!image.IsHidden || includeHidden))
        {
            AddGraphic(Quad.FromRectangle(image.Ctm, 0, 0, 1, 1).Bounds, GraphicKind.Image);
        }
    }

    public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        _markedDepth++;
        if (_artifactDepth == 0 && mc.Tag.SequenceEqual("Artifact"u8))
        {
            _artifactDepth = _markedDepth;
        }
    }

    public override void EndMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        if (_markedDepth == 0)
        {
            return;
        }

        if (_markedDepth == _artifactDepth)
        {
            _artifactDepth = 0;
        }

        _markedDepth--;
    }

    private void AddGraphic(PdfRectangle box, GraphicKind kind)
    {
        if (_graphics.Count < MaxGraphics && double.IsFinite(box.Left) && double.IsFinite(box.Bottom) && double.IsFinite(box.Right) && double.IsFinite(box.Top))
        {
            _graphics.Add(new Graphic(box, kind));
        }
    }

    private static bool IsWhite(in PdfColor color)
    {
        ReadOnlySpan<float> components = color.Components;
        if (color.PatternName is not null || components.IsEmpty)
        {
            return false;
        }

        bool cmyk = components.Length == 4;
        foreach (float component in components)
        {
            if (cmyk ? component > 0.05f : component < 0.95f)
            {
                return false;
            }
        }

        return components.Length is 1 or 3 or 4;
    }

    private void Segment(PathPoint from, PathPoint to)
    {
        double dx = Math.Abs(to.X - from.X);
        double dy = Math.Abs(to.Y - from.Y);
        if (dy <= Slack && dx > Slack)
        {
            Add(_horizontal, new Ruling((from.Y + to.Y) / 2, Math.Min(from.X, to.X), Math.Max(from.X, to.X)));
        }
        else if (dx <= Slack && dy > Slack)
        {
            Add(_vertical, new Ruling((from.X + to.X) / 2, Math.Min(from.Y, to.Y), Math.Max(from.Y, to.Y)));
        }
    }

    private void Box(double left, double bottom, double right, double top)
    {
        double width = right - left;
        double height = top - bottom;
        if (width <= ThinRule && height <= ThinRule)
        {
            return;
        }

        if (height <= ThinRule)
        {
            Add(_horizontal, new Ruling((bottom + top) / 2, left, right));
        }
        else if (width <= ThinRule)
        {
            Add(_vertical, new Ruling((left + right) / 2, bottom, top));
        }
        else
        {
            Add(_horizontal, new Ruling(bottom, left, right));
            Add(_horizontal, new Ruling(top, left, right));
            Add(_vertical, new Ruling(left, bottom, top));
            Add(_vertical, new Ruling(right, bottom, top));
        }
    }

    private void Add(List<Ruling> rulings, Ruling ruling)
    {
        if (_horizontal.Count + _vertical.Count < MaxRulings && double.IsFinite(ruling.Position) && double.IsFinite(ruling.Low) && double.IsFinite(ruling.High))
        {
            rulings.Add(ruling);
        }
    }
}

/// <summary>A horizontal or vertical line on a page: where it is across its axis, and the span it covers along it.</summary>
/// <param name="Position">The line's y (horizontal) or x (vertical).</param>
/// <param name="Low">Where the line starts along its axis.</param>
/// <param name="High">Where the line ends along its axis.</param>
internal readonly record struct Ruling(double Position, double Low, double High);

/// <summary>What a painted graphic is to figure detection.</summary>
internal enum GraphicKind : byte
{
    /// <summary>Axis-aligned lines, frames and thin filled rectangles: they join a figure but do not make one.</summary>
    Rule,

    /// <summary>A path with a curve, a slanted segment or a filled area thicker than a rule.</summary>
    Shape,

    /// <summary>An image (§8.9), placed by the unit square through its CTM.</summary>
    Image,
}

/// <summary>A painted image or path: its bounds in page space and its kind.</summary>
/// <param name="Box">The bounds.</param>
/// <param name="Kind">The kind.</param>
internal readonly record struct Graphic(PdfRectangle Box, GraphicKind Kind);
