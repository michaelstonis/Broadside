using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

public sealed partial class SkiaBackend
{
    /// <summary>Text (ISO 32000-2 §9.3.6, §9.4.3): glyph runs filled and stroked as the text rendering mode says.</summary>
    private sealed partial class Replayer
    {
        // A glyph's outline mapped to user space, for stroking with a pen in user space.
        private PathPoint[] _glyphPoints = new PathPoint[64];

        /// <summary>Gets or sets a value indicating whether every glyph is drawn from its outline, never by the platform's font engine.</summary>
        public bool ForceOutlines { get; set; }

        /// <summary>
        /// Draws each glyph of the run, glyph by glyph: filled (§9.3.6, Table 104) under glyph space → user space → device, by Skia's
        /// font engine for a TrueType or OpenType program (unless outlines are forced) or else from its outline with the non-zero winding
        /// rule, then stroked from its outline with the run's line parameters in user space.
        /// </summary>
        /// <remarks>
        /// ISO 32000-2 §9.2.4 (glyph outlines, the font matrix), §9.3.6 and §8.4.3 (text strokes use the graphics state's line
        /// parameters in user space), §9.9 (embedded font programs). A run with neither fill nor stroke (mode 3) paints nothing.
        /// </remarks>
        private void DrawGlyphsCore(in GlyphRun run)
        {
            if (run.Glyphs.IsEmpty || !(run.Fills || run.Strokes))
            {
                return;
            }

            SKColor fill = default;
            bool fills = run.Fills && _compositing.FillAlpha != 0 && TryGetColor(run.FillColor, out fill);
            bool strokes = run.Strokes && _compositing.StrokeAlpha != 0;
            if (!fills && !strokes)
            {
                return;
            }

            if (fills)
            {
                _fill.Color = fill.WithAlpha(_compositing.FillAlpha);
                _fill.BlendMode = PaintBlend;
            }

            Face? face = fills ? GetFace(run.Program) : null;
            if (ReferenceEquals(face, Face.Unavailable))
            {
                Report(4, GlyphsOutlined);
            }

            // A run crosses many tiles: a filled glyph wholly outside the clip draws nothing, so it is neither recorded nor drawn.
            bool culls = fills && !strokes;
            SKRect clip = default;
            if (culls)
            {
                _canvas!.SetMatrix(in _base);
                clip = _canvas.LocalClipBounds;
            }

            for (int index = 0; index < run.Glyphs.Length; index++)
            {
                PathView outline = run.GetOutline(index);
                if (outline.IsEmpty)
                {
                    continue;
                }

                Matrix glyphToUser = run.GetGlyphTransform(index);
                if (culls && Misses(outline, glyphToUser * _toDevice, clip))
                {
                    continue;
                }

                if (fills)
                {
                    Matrix glyphToDevice = glyphToUser * _toDevice;
                    if (!TryFillNative(face, run.Glyphs[index].GlyphId, outline, glyphToDevice))
                    {
                        FillOutline(outline, glyphToDevice);
                    }
                }

                if (strokes)
                {
                    StrokePathCore(ToUserSpace(outline, glyphToUser), run.StrokeStyle, run.StrokeColor);
                }
            }
        }

        /// <summary>
        /// Whether the outline under <paramref name="glyphToDevice"/> lies wholly outside <paramref name="clip"/> (in the coordinates of
        /// the canvas under <c>_base</c>): the bounds of its points (a curve stays within its control points), with two pixels to spare.
        /// </summary>
        private static bool Misses(PathView outline, in Matrix glyphToDevice, SKRect clip)
        {
            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;
            foreach (PathPoint point in outline.Points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }

            PathPoint a = glyphToDevice.Transform(minX, minY);
            PathPoint b = glyphToDevice.Transform(maxX, minY);
            PathPoint c = glyphToDevice.Transform(maxX, maxY);
            PathPoint d = glyphToDevice.Transform(minX, maxY);

            // NaN compares false everywhere, so an outline that cannot be bounded is drawn as before.
            return Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) < clip.Left - 2
                || Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) > clip.Right + 2
                || Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) < clip.Top - 2
                || Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) > clip.Bottom + 2;
        }

        /// <summary>
        /// Fills a glyph outline, recorded in glyph space (so that the cached Skia path serves every show of the glyph), under the
        /// canvas matrix that maps glyph space to the device.
        /// </summary>
        private void FillOutline(PathView outline, in Matrix glyphToDevice)
        {
            _path.Clear();
            _path.FillType = SKPathFillType.Winding;
            ReadOnlySpan<PathPoint> points = outline.Points;
            int p = 0;
            foreach (PathVerb verb in outline.Verbs)
            {
                switch (verb)
                {
                    case PathVerb.MoveTo:
                        _path.MoveTo(ToSkia(points[p++]));
                        break;
                    case PathVerb.LineTo:
                        _path.LineTo(ToSkia(points[p++]));
                        break;
                    case PathVerb.QuadTo:
                        _path.QuadTo(ToSkia(points[p]), ToSkia(points[p + 1]));
                        p += 2;
                        break;
                    case PathVerb.CubicTo:
                        _path.CubicTo(ToSkia(points[p]), ToSkia(points[p + 1]), ToSkia(points[p + 2]));
                        p += 3;
                        break;
                    case PathVerb.Close:
                        _path.Close();
                        break;
                }
            }

            SKMatrix toDevice = ToSkia(glyphToDevice);
            SKCanvas canvas = _canvas!;
            canvas.SetMatrix(in _base);
            canvas.Concat(in toDevice);
            canvas.DrawPath(_paths.Get(_path), _fill);
        }

        /// <summary>Maps a glyph outline's points to user space, into a buffer kept for the next glyph.</summary>
        private PathView ToUserSpace(PathView outline, in Matrix glyphToUser)
        {
            ReadOnlySpan<PathPoint> points = outline.Points;
            Grow(ref _glyphPoints, points.Length);
            for (int index = 0; index < points.Length; index++)
            {
                _glyphPoints[index] = glyphToUser.Transform(points[index]);
            }

            return new PathView(outline.Verbs, _glyphPoints.AsSpan(0, points.Length));
        }
    }
}
