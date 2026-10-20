using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Text (ISO 32000-2 §9.3.6, §9.4.3): glyph outlines filled and stroked as the text rendering mode says.</summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The backend's glyph half.</summary>
    private sealed partial class Backend
    {
        // A glyph's outline mapped to user space, for stroking with a pen in user space.
        private PathPoint[] _glyphPoints = new PathPoint[64];

        /// <summary>
        /// Draws each glyph of the run from its interned outline: filled with the non-zero winding rule under glyph space → user
        /// space → device, then stroked with the run's line parameters in user space, glyph by glyph.
        /// </summary>
        /// <remarks>
        /// ISO 32000-2 §9.2.4 (glyph outlines, the font matrix), §9.3.6 (Table 104: fill, stroke, fill then stroke, neither) and
        /// §8.4.3 (text strokes use the graphics state's line parameters in user space). Fills and strokes go through
        /// <see cref="ScanlineRasterizer.Fill"/>, so the clip applies (§8.5.4). A contour of a single point (a TrueType anchor) paints
        /// nothing: the degenerate-subpath rule of §8.5.3.3.1 is for path objects, not glyphs.
        /// </remarks>
        public void DrawGlyphs(in GlyphRun run)
        {
            if (_clip.IsEmpty || run.Glyphs.IsEmpty || !(run.Fills || run.Strokes))
            {
                return;
            }

            uint fill = 0;
            bool fills = run.Fills && !_fillCompositor.PaintsNothing && TryGetPixel(run.FillColor, out fill);
            uint stroke = 0;
            bool strokes = run.Strokes && !_strokeCompositor.PaintsNothing && TryGetPixel(run.StrokeColor, out stroke);
            if (!fills && !strokes)
            {
                return;
            }

            if (_previewing)
            {
                // The stroke's colour may add a spot colourant, which the fill's ink must then cover too (to erase it).
                int planes = _planes.Count;
                if (fills)
                {
                    PrepareInk(run.FillColor, _deviceParameters.FillOverprint, _fillInk);
                }

                if (strokes)
                {
                    PrepareInk(run.StrokeColor, _deviceParameters.StrokeOverprint, _strokeInk);
                }

                if (fills && _planes.Count != planes)
                {
                    PrepareInk(run.FillColor, _deviceParameters.FillOverprint, _fillInk);
                }
            }

            Matrix ctm = _toDevice;
            _scan.Rule = _textRule;
            try
            {
                for (int index = 0; index < run.Glyphs.Length; index++)
                {
                    PathView outline = run.GetOutline(index);
                    if (outline.IsEmpty)
                    {
                        continue;
                    }

                    Matrix glyphToUser = run.GetGlyphTransform(index);
                    if (fills && !strokes && !_previewing && MissesClip(outline, glyphToUser * ctm))
                    {
                        // A run crosses many tiles; a glyph wholly outside this one's clip paints nothing, so it is not scan converted.
                        continue;
                    }

                    if (fills)
                    {
                        _toDevice = glyphToUser * ctm;
                        _scan.Reset(_width, _height);
                        AddPath(outline, degenerateDots: false);
                        _toDevice = ctm;
                        PaintSolid(FillRule.NonZero, fill, _fillCompositor, _fillInk);
                    }

                    if (strokes)
                    {
                        _toDevice = ctm;
                        _scan.Reset(_width, _height);
                        _stroker.Stroke(ToUserSpace(outline, glyphToUser), run.StrokeStyle, ctm);
                        PaintSolid(FillRule.NonZero, stroke, _strokeCompositor, _strokeInk);
                    }
                }
            }
            finally
            {
                _toDevice = ctm;
                _scan.Rule = _pathRule;
            }
        }

        /// <summary>
        /// Whether the outline, under <paramref name="glyphToDevice"/>, lies wholly outside the clip's pixel bounds: the bounds of its
        /// points (a curve stays within its control points) mapped to device space, with a pixel to spare.
        /// </summary>
        private bool MissesClip(PathView outline, in Matrix glyphToDevice)
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
            _clip.GetBounds(out int left, out int top, out int right, out int bottom);

            // NaN compares false everywhere, so an outline that cannot be bounded is drawn as before.
            return Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) < left - 1
                || Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) > right + 1
                || Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) < top - 1
                || Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) > bottom + 1;
        }

        /// <summary>Converts a colour to the opaque pixel it paints, or returns false when it paints nothing.</summary>
        private bool TryGetPixel(in DisplayColor color, out uint pixel)
        {
            PdfColorConverter converter = Converter(color);
            if (converter.PaintsNothing)
            {
                pixel = 0;
                return false;
            }

            Span<float> rgb = stackalloc float[4];
            converter.Convert(color.Components, rgb);
            pixel = Pack(rgb);
            return true;
        }

        /// <summary>Maps a glyph outline's points to user space, into a buffer kept for the next glyph.</summary>
        private PathView ToUserSpace(PathView outline, in Matrix glyphToUser)
        {
            ReadOnlySpan<PathPoint> points = outline.Points;
            if (_glyphPoints.Length < points.Length)
            {
                _glyphPoints = new PathPoint[Math.Max(points.Length, _glyphPoints.Length * 2)];
            }

            for (int index = 0; index < points.Length; index++)
            {
                _glyphPoints[index] = glyphToUser.Transform(points[index]);
            }

            return new PathView(outline.Verbs, _glyphPoints.AsSpan(0, points.Length));
        }
    }
}
