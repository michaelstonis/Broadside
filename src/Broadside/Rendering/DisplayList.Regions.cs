using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Region replay: the bounds of every paint, and the replay that skips paints outside a region.</summary>
public sealed partial class DisplayList
{
    /// <summary>The square root of 2: how far a projecting square cap's corner reaches, in half line widths.</summary>
    private const double SquareCapReach = 1.4142135623730951;

    // Computed on the first region replay and kept: one record per operation.
    private OperationExtent[]? _extents;

    /// <summary>
    /// Calls <paramref name="backend"/> for the operations of the list that can change pixels inside <paramref name="region"/>, in
    /// order: every paint whose bounds meet the region, and every state change. A saved state whose paints all lie outside the
    /// region is skipped with its <see cref="IRenderingBackend.SaveState"/> and <see cref="IRenderingBackend.RestoreState"/>.
    /// </summary>
    /// <param name="backend">The backend that draws.</param>
    /// <param name="region">
    /// The region in the list's default user space (the page's coordinates, as its crop box is given). A backend that paints beyond a
    /// shape's exact outline (a minimum line width in device pixels, stroke adjustment, anti-aliasing) passes the region it draws
    /// widened by that much; <see cref="Replay(IRenderingBackend)"/> is the same as a region that holds everything.
    /// </param>
    /// <remarks>
    /// <para>
    /// Spec #33 story 14 (viewer capability 1: render tiles at many zoom levels from one cached list). Each paint's bounds are
    /// in default user space (ISO 32000-2 §8.3.2.3): its path's points (curve control points included, which bound the curve) under
    /// the transform of the paint (a fill or stroke in a shading or tiling pattern (§8.7.3) is bounded by its path like any other); a
    /// stroke's path widened by half the line width times the miter limit or √2 for square caps
    /// (§8.4.3.3–§8.4.3.5), in user space where the pen is; an image's or stencil mask's unit square (§8.9.4); a glyph run's outlines
    /// under their glyph transforms (§9.4.4); a shading's <c>BBox</c> (§8.7.4.3), or no bound without one. Every paint's bounds are
    /// cut by the clipping path's bounds at that point (§8.5.4), so the content of a clip outside the region is skipped too. An
    /// annotation (§12.5.5) is skipped whole like a saved state; one drawn with <c>NoZoom</c> or <c>NoRotate</c> (§12.5.3), which the
    /// backend places by its own mapping, is never skipped. Alpha constants and blend modes act per pixel (§11.3), so skipping a paint
    /// that changes no pixel of the region changes nothing in it.
    /// </para>
    /// <para>
    /// The bounds are computed once per list, on the first region replay (one array of an operation's size per list), and replaying a
    /// region allocates nothing after that. Safe to call on several threads at once with a backend per thread.
    /// </para>
    /// </remarks>
    public void Replay(IRenderingBackend backend, PdfRectangle region)
    {
        ArgumentNullException.ThrowIfNull(backend);
        OperationExtent[] extents = GetExtents();
        ReadOnlySpan<DisplayOperation> operations = _operations;
        for (int index = 0; index < operations.Length; index++)
        {
            ref readonly DisplayOperation operation = ref operations[index];
            ref readonly OperationExtent extent = ref extents[index];
            if (extent.IsCullable && !extent.Meets(region))
            {
                // A paint ends where it is; a saved state or an annotation at the operation that restores it.
                index = extent.End;
                continue;
            }

            Execute(backend, in operation);
            if (operation.Kind == DisplayOperationKind.DrawGroup)
            {
                // A group's content reaches the backend through its DisplayGroup, whole.
                index = Groups[operation.First].End - 1;
            }
        }
    }

    /// <summary>Returns the bounds of every operation, computing them on the first call.</summary>
    private OperationExtent[] GetExtents()
    {
        OperationExtent[]? extents = Volatile.Read(ref _extents);
        if (extents is null)
        {
            // Two threads may compute them at once; both results are equal and the first one stored wins.
            Interlocked.CompareExchange(ref _extents, ComputeExtents(), null);
            extents = _extents!;
        }

        return extents;
    }

    /// <summary>Walks the list as a backend would, tracking the transform and the clip's bounds, and bounds every paint.</summary>
    private OperationExtent[] ComputeExtents()
    {
        var extents = new OperationExtent[_operations.Length];
        var pathBounds = new Box[_paths.Length];
        var computed = new bool[_paths.Length];
        var stack = new Stack<Frame>();
        Matrix ctm = Matrix.Identity;
        Box clip = Box.Everything;
        Box group = Box.Empty;

        // Inside a NoZoom or NoRotate annotation the backend maps default user space differently (§12.5.3), so page bounds do not hold.
        int unbounded = 0;
        for (int index = 0; index < _operations.Length; index++)
        {
            CloseGroups(index, extents, stack, ref ctm, ref clip, ref group);
            ref readonly DisplayOperation operation = ref _operations[index];
            Box paint;
            switch (operation.Kind)
            {
                case DisplayOperationKind.DrawGroup:
                    // A transparency group (§11.4) is skipped whole, bounded by its content, which ends at the group's End.
                    stack.Push(new Frame(ctm, clip, index, group, KeepsSize: false, GroupEnd: Groups[operation.First].End));
                    group = Box.Empty;
                    extents[index] = OperationExtent.Everything(index);
                    continue;
                case DisplayOperationKind.SaveState or DisplayOperationKind.BeginAnnotation:
                    bool keepsSize = operation.Kind == DisplayOperationKind.BeginAnnotation
                        && Annotations[operation.First] is { NoZoom: true } or { NoRotate: true };
                    stack.Push(new Frame(ctm, clip, index, group, keepsSize, GroupEnd: -1));
                    unbounded += keepsSize ? 1 : 0;
                    group = Box.Empty;
                    extents[index] = OperationExtent.Everything(index);
                    continue;
                case DisplayOperationKind.RestoreState or DisplayOperationKind.EndAnnotation:
                    if (stack.TryPeek(out Frame top) && top.GroupEnd < 0 && stack.TryPop(out Frame frame))
                    {
                        unbounded -= frame.KeepsSize ? 1 : 0;
                        if (frame.KeepsSize)
                        {
                            group = Box.Everything;
                        }

                        extents[frame.SaveIndex] = new OperationExtent(group, cullable: true, index);
                        ctm = frame.Ctm;
                        clip = frame.Clip;
                        group = Box.Union(frame.Group, group);
                    }

                    extents[index] = OperationExtent.Everything(index);
                    continue;
                case DisplayOperationKind.SetCompositing or DisplayOperationKind.SetDeviceParameters
                    or DisplayOperationKind.SetSoftMask:
                    extents[index] = OperationExtent.Everything(index);
                    continue;
                case DisplayOperationKind.SetTransform:
                    ctm = _transforms[operation.First];
                    extents[index] = OperationExtent.Everything(index);
                    continue;
                case DisplayOperationKind.IntersectClip:
                    clip = Box.Intersect(clip, PathBounds(operation.First, 0, ctm, pathBounds, computed));
                    extents[index] = OperationExtent.Everything(index);
                    continue;
                case DisplayOperationKind.FillPath or DisplayOperationKind.FillPathWithShading or DisplayOperationKind.FillPathWithTilingPattern:
                    paint = PathBounds(operation.First, 0, ctm, pathBounds, computed);
                    break;
                case DisplayOperationKind.StrokePath or DisplayOperationKind.StrokePathWithShading or DisplayOperationKind.StrokePathWithTilingPattern:
                    paint = PathBounds(operation.First, StrokeReach(Strokes[operation.Second]), ctm, pathBounds, computed);
                    break;
                case DisplayOperationKind.DrawImage or DisplayOperationKind.FillImageMask:
                    paint = Box.Transform(new Box(0, 0, 1, 1), ctm);
                    break;
                case DisplayOperationKind.DrawGlyphs:
                    paint = GlyphBounds(GlyphRuns[operation.First], ctm, pathBounds, computed);
                    break;
                case DisplayOperationKind.DrawShading:
                    ref readonly DisplayShadingPaint shading = ref ShadingPaints[operation.First];
                    paint = shading.Shading.BoundingBox is { } box
                        ? Box.Transform(new Box(box.Left, box.Bottom, box.Right, box.Top), shading.Matrix)
                        : Box.Everything;
                    break;
                default:
                    // An operation this walk does not know is never skipped, nor is a saved state that holds one.
                    paint = Box.Everything;
                    break;
            }

            paint = unbounded > 0 ? Box.Everything : Box.Intersect(paint, clip);
            extents[index] = new OperationExtent(paint, cullable: true, index);
            group = Box.Union(group, paint);
        }

        CloseGroups(_operations.Length, extents, stack, ref ctm, ref clip, ref group);

        // A save never restored (the builder balances them, so this is defensive) is never skipped.
        while (stack.TryPop(out Frame open))
        {
            extents[open.SaveIndex] = OperationExtent.Everything(open.SaveIndex);
        }

        return extents;
    }

    /// <summary>Ends the groups whose content ends at <paramref name="index"/>: each is bounded by what its content paints.</summary>
    private static void CloseGroups(int index, OperationExtent[] extents, Stack<Frame> stack, ref Matrix ctm, ref Box clip, ref Box group)
    {
        while (stack.TryPeek(out Frame frame) && frame.GroupEnd == index)
        {
            stack.Pop();
            extents[frame.SaveIndex] = new OperationExtent(group, cullable: true, index - 1);
            ctm = frame.Ctm;
            clip = frame.Clip;
            group = Box.Union(frame.Group, group);
        }
    }

    /// <summary>How far a stroke reaches beyond its path in user space: half the line width, times the miter limit or √2 for square caps.</summary>
    private static double StrokeReach(in DisplayStroke stroke)
    {
        double factor = 1;
        if (stroke.LineCap == LineCap.ProjectingSquare)
        {
            factor = SquareCapReach;
        }

        if (stroke.LineJoin == LineJoin.Miter)
        {
            factor = Math.Max(factor, stroke.MiterLimit);
        }

        return Math.Abs(stroke.LineWidth) / 2 * factor;
    }

    /// <summary>Returns the bounds of a path widened by <paramref name="reach"/> in user space, under <paramref name="ctm"/>.</summary>
    private Box PathBounds(int path, double reach, in Matrix ctm, Box[] cache, bool[] computed)
    {
        Box user = UserBounds(path, cache, computed);
        if (user.IsEmpty)
        {
            return Box.Empty;
        }

        return Box.Transform(new Box(user.Left - reach, user.Bottom - reach, user.Right + reach, user.Top + reach), ctm);
    }

    /// <summary>Returns the bounds of a glyph run: each glyph's outline under its glyph transform, widened by a stroke's reach, under the CTM.</summary>
    private Box GlyphBounds(in DisplayGlyphRun run, in Matrix ctm, Box[] cache, bool[] computed)
    {
        Box user = Box.Empty;
        foreach (ref readonly DisplayGlyph glyph in Glyphs.AsSpan(run.GlyphStart, run.GlyphCount))
        {
            Box outline = UserBounds(glyph.Outline, cache, computed);
            if (outline.IsEmpty)
            {
                continue;
            }

            Matrix font = run.FontMatrix;
            if (glyph.HorizontalScale != 1)
            {
                font *= Matrix.CreateScale(glyph.HorizontalScale, 1);
            }

            user = Box.Union(user, Box.Transform(outline, font * glyph.TextMatrix));
        }

        if (user.IsEmpty)
        {
            return Box.Empty;
        }

        double reach = run.Stroke >= 0 ? StrokeReach(Strokes[run.Stroke]) : 0;
        return Box.Transform(new Box(user.Left - reach, user.Bottom - reach, user.Right + reach, user.Top + reach), ctm);
    }

    /// <summary>Returns the bounds of a path's points in its own space, once per path.</summary>
    private Box UserBounds(int path, Box[] cache, bool[] computed)
    {
        if (computed[path])
        {
            return cache[path];
        }

        ref readonly DisplayPath slice = ref _paths[path];
        Box bounds = Box.Empty;
        foreach (PathPoint point in _points.AsSpan(slice.PointStart, slice.PointCount))
        {
            bounds = new Box(Math.Min(bounds.Left, point.X), Math.Min(bounds.Bottom, point.Y), Math.Max(bounds.Right, point.X), Math.Max(bounds.Top, point.Y));
        }

        if (!bounds.IsEmpty && !bounds.IsFinite)
        {
            bounds = Box.Everything;
        }

        cache[path] = bounds;
        computed[path] = true;
        return bounds;
    }

    /// <summary>
    /// A saved state (or an annotation) during the walk: what <c>RestoreState</c> brings back, where the save is, the bounds of the
    /// enclosing group so far, and whether it is an annotation that keeps its size or orientation.
    /// </summary>
    /// <remarks>A transparency group's frame has the index its content ends at (<see cref="GroupEnd"/>), −1 for the others.</remarks>
    private readonly record struct Frame(Matrix Ctm, Box Clip, int SaveIndex, Box Group, bool KeepsSize, int GroupEnd);

    /// <summary>
    /// A rectangle in default user space with double coordinates; empty when <see cref="Left"/> &gt; <see cref="Right"/>, unbounded
    /// with infinite coordinates.
    /// </summary>
    private readonly record struct Box(double Left, double Bottom, double Right, double Top)
    {
        public static Box Empty { get; } = new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);

        public static Box Everything { get; } = new(double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity);

        public bool IsEmpty => !(Left <= Right && Bottom <= Top);

        public bool IsFinite => double.IsFinite(Left) && double.IsFinite(Bottom) && double.IsFinite(Right) && double.IsFinite(Top);

        public static Box Union(in Box a, in Box b) =>
            a.IsEmpty ? b : b.IsEmpty ? a : new(Math.Min(a.Left, b.Left), Math.Min(a.Bottom, b.Bottom), Math.Max(a.Right, b.Right), Math.Max(a.Top, b.Top));

        public static Box Intersect(in Box a, in Box b)
        {
            var box = new Box(Math.Max(a.Left, b.Left), Math.Max(a.Bottom, b.Bottom), Math.Min(a.Right, b.Right), Math.Min(a.Top, b.Top));
            return box.IsEmpty ? Empty : box;
        }

        /// <summary>Returns the bounds of the box's four corners under <paramref name="matrix"/>; unbounded when that is not finite.</summary>
        public static Box Transform(in Box box, in Matrix matrix)
        {
            if (box.IsEmpty)
            {
                return Empty;
            }

            if (!box.IsFinite)
            {
                return Everything;
            }

            PathPoint a = matrix.Transform(box.Left, box.Bottom);
            PathPoint b = matrix.Transform(box.Right, box.Bottom);
            PathPoint c = matrix.Transform(box.Right, box.Top);
            PathPoint d = matrix.Transform(box.Left, box.Top);
            var result = new Box(
                Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)),
                Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)),
                Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)),
                Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
            return result.IsFinite ? result : Everything;
        }
    }

    /// <summary>
    /// The bounds of one operation in single precision, rounded outwards, and for a saved state the index of its restore; 20 bytes.
    /// </summary>
    private readonly struct OperationExtent
    {
        private readonly float _left;
        private readonly float _bottom;
        private readonly float _right;
        private readonly float _top;

        public OperationExtent(in Box box, bool cullable, int end)
        {
            if (box.IsEmpty)
            {
                (_left, _bottom, _right, _top) = (float.PositiveInfinity, float.PositiveInfinity, float.NegativeInfinity, float.NegativeInfinity);
            }
            else
            {
                _left = Down(box.Left);
                _bottom = Down(box.Bottom);
                _right = Up(box.Right);
                _top = Up(box.Top);
            }

            IsCullable = cullable;
            End = end;
        }

        /// <summary>Gets a value indicating whether the operation is a paint or a saved state, which a region replay may skip.</summary>
        public bool IsCullable { get; }

        /// <summary>Gets, for a saved state, the index of the operation that restores it.</summary>
        public int End { get; }

        public static OperationExtent Everything(int index) => new(Box.Everything, cullable: false, index);

        /// <summary>Whether the bounds meet <paramref name="region"/>, edges included.</summary>
        public bool Meets(in PdfRectangle region) =>
            _left <= region.Right && _right >= region.Left && _bottom <= region.Top && _top >= region.Bottom;

        private static float Down(double value)
        {
            float single = (float)value;
            return single > value ? MathF.BitDecrement(single) : single;
        }

        private static float Up(double value)
        {
            float single = (float)value;
            return single < value ? MathF.BitIncrement(single) : single;
        }
    }
}
