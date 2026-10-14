using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// A backend that paints nothing and collects where images and paths land: each paint's box in page space under the transform in
/// force, cut to the bounds of the clip in force (ADR 0006: hit-testing reads the display list like any backend).
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.4.2 (the state stack), §8.3.2 (transforms), §8.5.3 (paths), §8.5.4 (clips), §8.9.4 (the image unit square) and
/// §8.4.3 (line width, caps, joins, miter limit). Glyphs come from the list's text positions, not from its runs, so a code is one
/// glyph whatever draws it; shadings painted with <c>sh</c> fill the clip and are not collected.
/// </remarks>
internal sealed class HitTestCollector : IRenderingBackend
{
    private static readonly PdfRectangle Unclipped = new(double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity);

    private readonly List<HitTestShape> _images = [];
    private readonly List<HitTestShape> _paths = [];
    private readonly Stack<(Matrix Transform, PdfRectangle Clip, bool ClipEmpty)> _saved = new();
    private Matrix _transform = Matrix.Identity;
    private PdfRectangle _clip = Unclipped;
    private bool _clipEmpty;

    /// <summary>Gets the image and stencil-mask paints, in paint order.</summary>
    public HitTestShape[] Images => [.. _images];

    /// <summary>Gets the path fills and strokes, in paint order.</summary>
    public HitTestShape[] Paths => [.. _paths];

    /// <inheritdoc/>
    public void SaveState() => _saved.Push((_transform, _clip, _clipEmpty));

    /// <inheritdoc/>
    public void RestoreState()
    {
        if (_saved.TryPop(out (Matrix Transform, PdfRectangle Clip, bool ClipEmpty) saved))
        {
            (_transform, _clip, _clipEmpty) = saved;
        }
    }

    /// <inheritdoc/>
    public void SetTransform(in Matrix transform) => _transform = transform;

    /// <inheritdoc/>
    public void FillPath(PathView path, FillRule rule, in DisplayColor color) => AddPath(path, 0, 1);

    /// <inheritdoc/>
    public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color) => AddStroke(path, style);

    /// <inheritdoc/>
    public void IntersectClip(PathView path, FillRule rule)
    {
        if (_clipEmpty || !TryGetBounds(path, out PdfRectangle box))
        {
            _clipEmpty = true;
            return;
        }

        PdfRectangle mapped = Quad.FromRectangle(_transform, box.Left, box.Bottom, box.Right, box.Top).Bounds;
        if (!TryIntersect(_clip, mapped, out _clip))
        {
            _clipEmpty = true;
        }
    }

    /// <inheritdoc/>
    public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion) => AddUnitSquare();

    /// <inheritdoc/>
    public void DrawGlyphs(in GlyphRun run)
    {
    }

    /// <inheritdoc/>
    public void DrawShading(in DisplayShading shading)
    {
    }

    /// <inheritdoc/>
    public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading) => AddPath(path, 0, 1);

    /// <inheritdoc/>
    public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading) => AddStroke(path, style);

    /// <inheritdoc/>
    public void FillImageMask(DisplayImageMask mask, in DisplayColor color) => AddUnitSquare();

    /// <inheritdoc/>
    /// <remarks>Opacity does not change where a paint lands: a paint with alpha 0 is still indexed.</remarks>
    public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode)
    {
    }

    /// <inheritdoc/>
    /// <remarks>An appearance's paints are indexed where they land in default user space, as on a page shown at 100% unrotated.</remarks>
    public void BeginAnnotation(in DisplayAnnotation annotation) => SaveState();

    /// <inheritdoc/>
    public void EndAnnotation() => RestoreState();

    private void AddStroke(PathView path, in StrokeStyle style)
    {
        // Half the width reaches past the path's points on either side; a miter can reach the miter limit times that, a square cap
        // or a bevel at most √2 times it (§8.4.3.3 to §8.4.3.5).
        double factor = style.LineJoin == LineJoin.Miter ? Math.Max(Math.Sqrt(2), style.MiterLimit) : Math.Sqrt(2);
        AddPath(path, Math.Abs(style.LineWidth) / 2, factor);
    }

    private void AddPath(PathView path, double halfWidth, double factor)
    {
        if (!TryGetBounds(path, out PdfRectangle box))
        {
            return;
        }

        double grow = halfWidth * factor;
        Add(_paths, Quad.FromRectangle(_transform, box.Left - grow, box.Bottom - grow, box.Right + grow, box.Top + grow));
    }

    private void AddUnitSquare() => Add(_images, Quad.FromRectangle(_transform, 0, 0, 1, 1));

    private void Add(List<HitTestShape> shapes, in Quad quad)
    {
        if (!_clipEmpty && TryIntersect(_clip, quad.Bounds, out PdfRectangle bounds))
        {
            shapes.Add(new HitTestShape(quad, bounds));
        }
    }

    private static bool TryGetBounds(PathView path, out PdfRectangle bounds)
    {
        ReadOnlySpan<PathPoint> points = path.Points;
        if (points.IsEmpty)
        {
            bounds = default;
            return false;
        }

        double left = double.PositiveInfinity;
        double bottom = double.PositiveInfinity;
        double right = double.NegativeInfinity;
        double top = double.NegativeInfinity;
        foreach (PathPoint point in points)
        {
            left = Math.Min(left, point.X);
            bottom = Math.Min(bottom, point.Y);
            right = Math.Max(right, point.X);
            top = Math.Max(top, point.Y);
        }

        bounds = new PdfRectangle(left, bottom, right, top);
        return double.IsFinite(left) && double.IsFinite(bottom) && double.IsFinite(right) && double.IsFinite(top);
    }

    private static bool TryIntersect(in PdfRectangle first, in PdfRectangle second, out PdfRectangle intersection)
    {
        double left = Math.Max(first.Left, second.Left);
        double bottom = Math.Max(first.Bottom, second.Bottom);
        double right = Math.Min(first.Right, second.Right);
        double top = Math.Min(first.Top, second.Top);
        intersection = new PdfRectangle(left, bottom, right, top);
        return left <= right && bottom <= top;
    }
}
