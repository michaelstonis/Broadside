using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Text (ISO 32000-2 §9.3.6, §9.4.3): glyph outlines filled and stroked as the text rendering mode says.</summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The backend's glyph half.</summary>
    private sealed partial class Backend
    {
        /// <summary>The widest and tallest glyph mask, in pixels, that is scan converted on its own and cached; larger glyphs are drawn in place.</summary>
        private const int MaxMaskSize = 512;

        /// <summary>Glyph origins further than this from the bitmap, in pixels, are drawn in place (their pixel offset must fit an integer).</summary>
        private const double MaxOrigin = 1 << 30;

        // A glyph's outline mapped to user space, for stroking with a pen in user space.
        private PathPoint[] _glyphPoints = new PathPoint[64];

        // The cache of glyph masks: this backend's own, or its parent's for a nested render (a pattern cell, a group, a soft mask).
        private GlyphCache _glyphs = new(DefaultGlyphCacheBudget);
        private bool _ownsGlyphs = true;

        // The document the cache's masks were drawn for; held weakly, so that a rasterizer does not keep a closed document alive.
        private readonly WeakReference<PdfDocument> _glyphDocument = new(null!);

        /// <summary>Gets the glyph cache (issue #370).</summary>
        public GlyphCache Glyphs => _glyphs;

        /// <summary>Gets or sets how glyph masks are positioned: at the exact sub-pixel offset, or at the nearest quarter pixel.</summary>
        public GlyphPositioning GlyphPositioning { get; set; }

        /// <summary>Makes a nested backend draw glyphs from <paramref name="parent"/>'s cache, with its positioning.</summary>
        private void UseGlyphsOf(Backend parent)
        {
            if (_ownsGlyphs)
            {
                _glyphs.Dispose();
                _ownsGlyphs = false;
            }

            _glyphs = parent._glyphs;
            GlyphPositioning = parent.GlyphPositioning;
        }

        /// <summary>
        /// Empties the glyph cache when this backend owns it and the render is of another document than the last: its keys hold the
        /// last document's font programs, which another document never draws with, so they are released at the next render.
        /// </summary>
        private void BeginGlyphs(PdfDocument document)
        {
            if (!_ownsGlyphs)
            {
                return;
            }

            if (_glyphDocument.TryGetTarget(out PdfDocument? last) && ReferenceEquals(last, document))
            {
                return;
            }

            _glyphs.Clear();
            _glyphDocument.SetTarget(document);
        }

        /// <summary>Returns the glyph cache's buffers, when it is this backend's own.</summary>
        private void DisposeGlyphs()
        {
            if (_ownsGlyphs)
            {
                _glyphs.Dispose();
            }
        }

        /// <summary>
        /// Draws each glyph of the run from its interned outline: filled with the non-zero winding rule under glyph space → user
        /// space → device, then stroked with the run's line parameters in user space, glyph by glyph.
        /// </summary>
        /// <remarks>
        /// ISO 32000-2 §9.2.4 (glyph outlines, the font matrix), §9.3.6 (Table 104: fill, stroke, fill then stroke, neither) and
        /// §8.4.3 (text strokes use the graphics state's line parameters in user space). Fills and strokes go through
        /// <see cref="ScanlineRasterizer.Fill"/>, so the clip applies (§8.5.4). A contour of a single point (a TrueType anchor) paints
        /// nothing: the degenerate-subpath rule of §8.5.3.3.1 is for path objects, not glyphs. Fills are painted from the glyph cache
        /// (<see cref="FillFromMask"/>).
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

            // Text that clips (modes 4 to 7) is filled in place, as the issue scopes the cache to the plain modes.
            bool cached = run.Program is not null && run.RenderingMode < TextRenderingMode.FillClip;
            bool cull = fills && !strokes && !_previewing;
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
                    Matrix glyphToDevice = glyphToUser * ctm;
                    if (fills)
                    {
                        MaskFill result = cached ? FillFromMask(outline, run.Program!, run.Glyphs[index].GlyphId, glyphToDevice, fill, cull) : MaskFill.InPlace;
                        if (result == MaskFill.Culled)
                        {
                            continue;
                        }

                        if (result == MaskFill.InPlace)
                        {
                            if (cull && MissesClip(outline, glyphToDevice))
                            {
                                // A run crosses many tiles; a glyph wholly outside this one's clip paints nothing, so it is not scan converted.
                                continue;
                            }

                            _toDevice = glyphToDevice;
                            _scan.Reset(_width, _height);
                            AddPath(outline, degenerateDots: false);
                            _toDevice = ctm;
                            PaintSolid(FillRule.NonZero, fill, _fillCompositor, _fillInk);
                        }
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
        /// Fills a glyph from its cached mask (issue #370), scan converting the mask first when it is not cached: the outline under
        /// <paramref name="glyphToDevice"/> with the origin moved to its pixel's top-left corner (the offset inside the pixel kept
        /// exactly, or rounded to a quarter pixel for <see cref="GlyphPositioning.Quantized"/>), swept into runs of coverage, which
        /// are painted moved by the origin's whole pixels.
        /// </summary>
        /// <returns>
        /// Whether the glyph was painted, lies wholly outside the clip, or must be filled in place: a transform that is not finite or
        /// cannot be inverted, an origin too far away, or a mask larger than <see cref="MaxMaskSize"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The mask is a function of its key (<see cref="GlyphKey"/>) alone, and every glyph the cache takes is painted from one, whether
        /// it was stored or not, so the output never depends on the budget or on what was drawn before. With
        /// <see cref="GlyphPositioning.Exact"/> the glyph covers what it covers when filled in place, up to the last bits of the
        /// arithmetic (relative to its origin's pixel rather than to the page), which can move a coverage that falls on a rounding
        /// boundary by one level.
        /// </para>
        /// <para>ISO 32000-2 §9.2.4, §10.7.4 and §8.5.4.</para>
        /// </remarks>
        private MaskFill FillFromMask(PathView outline, object program, int glyphId, in Matrix glyphToDevice, uint fill, bool cull)
        {
            Matrix m = glyphToDevice;
            if (!(double.IsFinite(m.A) && double.IsFinite(m.B) && double.IsFinite(m.C) && double.IsFinite(m.D)
                && Math.Abs(m.E) < MaxOrigin && Math.Abs(m.F) < MaxOrigin) || (m.A * m.D) - (m.B * m.C) == 0)
            {
                return MaskFill.InPlace;
            }

            double originX = Math.Floor(m.E);
            double originY = Math.Floor(m.F);
            double x = m.E - originX;
            double y = m.F - originY;
            if (GlyphPositioning == GlyphPositioning.Quantized)
            {
                x = Quarter(x, ref originX);
                y = Quarter(y, ref originY);
            }

            var key = new GlyphKey(program, glyphId, m, x, y, _scan.Rule);
            bool hit = _glyphs.TryGet(key, out GlyphMask mask);
            if (!hit)
            {
                var local = new Matrix(m.A, m.B, m.C, m.D, x, y);
                if (!TryMeasure(outline, local, out int left, out int top, out int width, out int height))
                {
                    return MaskFill.InPlace;
                }

                if (cull && Misses(left + (int)originX, top + (int)originY, width, height))
                {
                    return MaskFill.Culled;
                }

                // The mask's own scan conversion: the outline with the mask's top-left corner at the origin, swept into runs.
                Matrix toDevice = _toDevice;
                _toDevice = new Matrix(m.A, m.B, m.C, m.D, x - left, y - top);
                _scan.Reset(width, height);
                AddPath(outline, degenerateDots: false);
                _toDevice = toDevice;
                _glyphs.BeginMask();
                var collector = new MaskCollector(_glyphs);
                _scan.Sweep(FillRule.NonZero, ref collector);
                _scan.Reset(_width, _height);
                mask = _glyphs.Add(key, left, top, width, height);
            }
            else if (Misses(mask.Left + (int)originX, mask.Top + (int)originY, mask.Width, mask.Height))
            {
                return MaskFill.Culled;
            }

            _glyphs.CountLookup(hit);
            PaintMask(_glyphs.Spans(mask), mask.Left + (int)originX, mask.Top + (int)originY, fill);
            return MaskFill.Painted;
        }

        /// <summary>Rounds an offset inside a pixel to the nearest quarter pixel, carrying a whole pixel into <paramref name="origin"/>.</summary>
        private static double Quarter(double offset, ref double origin)
        {
            double quarter = Math.Floor((offset * 4) + 0.5) / 4;
            if (quarter >= 1)
            {
                quarter -= 1;
                origin += 1;
            }

            return quarter;
        }

        /// <summary>
        /// Measures the pixels a glyph's mask needs under <paramref name="local"/> (the origin's offset inside its pixel): the box of
        /// its points (a curve stays within its control points) with a pixel to spare on the right and bottom. False when it is not
        /// finite or larger than <see cref="MaxMaskSize"/>.
        /// </summary>
        private static bool TryMeasure(PathView outline, in Matrix local, out int left, out int top, out int width, out int height)
        {
            double x0 = double.PositiveInfinity;
            double y0 = double.PositiveInfinity;
            double x1 = double.NegativeInfinity;
            double y1 = double.NegativeInfinity;
            foreach (PathPoint point in outline.Points)
            {
                x0 = Math.Min(x0, point.X);
                y0 = Math.Min(y0, point.Y);
                x1 = Math.Max(x1, point.X);
                y1 = Math.Max(y1, point.Y);
            }

            // The glyph-space box's corners mapped by the matrix bound the mapped outline.
            PathPoint a = local.Transform(x0, y0);
            PathPoint b = local.Transform(x1, y0);
            PathPoint c = local.Transform(x1, y1);
            PathPoint d = local.Transform(x0, y1);
            double minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
            double minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
            double maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
            double maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
            left = top = width = height = 0;
            if (!(maxX - minX <= MaxMaskSize - 2 && maxY - minY <= MaxMaskSize - 2))
            {
                return false; // Also NaN.
            }

            left = (int)Math.Floor(minX);
            top = (int)Math.Floor(minY);
            width = (int)Math.Ceiling(maxX) - left + 1;
            height = (int)Math.Ceiling(maxY) - top + 1;
            return true;
        }

        /// <summary>Whether a mask at these device pixels lies wholly outside the clip's pixel bounds, with a pixel to spare.</summary>
        private bool Misses(int left, int top, int width, int height)
        {
            _clip.GetBounds(out int clipLeft, out int clipTop, out int clipRight, out int clipBottom);
            return left + width < clipLeft - 1 || left > clipRight + 1 || top + height < clipTop - 1 || top > clipBottom + 1;
        }

        /// <summary>Paints a glyph mask's runs at <paramref name="left"/>, <paramref name="top"/> as <see cref="PaintSolid"/> paints a fill.</summary>
        private void PaintMask(ReadOnlySpan<CoverageSpan> spans, int left, int top, uint fill)
        {
            _scan.Reset(_width, _height); // The runs are cut to this render's bitmap, whatever the scan converter held last.
            if (!_previewing)
            {
                _scan.FillSpans(_target!, spans, left, top, fill, _fillCompositor);
                return;
            }

            var sink = new InkSink(_planes, _fillInk, _fillCompositor);
            _scan.PaintSpans(spans, left, top, ref sink);
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

        /// <summary>What <see cref="FillFromMask"/> did with a glyph.</summary>
        private enum MaskFill
        {
            /// <summary>Painted from its mask.</summary>
            Painted,

            /// <summary>Not painted: it lies wholly outside the clip.</summary>
            Culled,

            /// <summary>Not taken: fill it in place.</summary>
            InPlace,
        }

        /// <summary>Hands the runs of a mask's sweep, those that cover anything, to the glyph cache.</summary>
        private readonly ref struct MaskCollector(GlyphCache cache) : ICoverageSink
        {
            public void Span(int row, int x, int count, int coverage)
            {
                if (coverage > 0)
                {
                    cache.Append(row, x, count, coverage);
                }
            }
        }
    }
}
