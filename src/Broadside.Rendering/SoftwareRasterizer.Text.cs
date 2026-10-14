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

            Matrix ctm = _toDevice;
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
                    if (fills)
                    {
                        _toDevice = glyphToUser * ctm;
                        _scan.Reset(_width, _height);
                        AddPath(outline, degenerateDots: false);
                        _toDevice = ctm;
                        _scan.Fill(_target!, FillRule.NonZero, fill, _fillCompositor);
                    }

                    if (strokes)
                    {
                        _toDevice = ctm;
                        _scan.Reset(_width, _height);
                        _stroker.Stroke(ToUserSpace(outline, glyphToUser), run.StrokeStyle, ctm);
                        _scan.Fill(_target!, FillRule.NonZero, stroke, _strokeCompositor);
                    }
                }
            }
            finally
            {
                _toDevice = ctm;
            }
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
