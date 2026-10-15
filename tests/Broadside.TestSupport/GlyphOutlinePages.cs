using System.Globalization;
using System.Text;
using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.TestSupport;

/// <summary>
/// Turns the text of a display list into a page of paths: every filled glyph becomes <c>q [glyph space → user space × CTM] cm
/// &lt;outline&gt; f Q</c> in its fill colour, so a backend's rendering of the page's glyphs with <see cref="RenderOptions.ForceOutlines"/>
/// can be compared with its own rendering of the same outlines as path objects (ISO 32000-2 §9.2.4: glyph outlines are filled by the
/// non-zero winding rule, §8.5.3.3).
/// </summary>
/// <remarks>
/// For text-only pages: fills in DeviceGray, DeviceRGB or DeviceCMYK at alpha 1 and the Normal blend mode. Any other paint, a clip,
/// an annotation or compositing state throws <see cref="NotSupportedException"/>, so nothing on the page is lost silently. Quadratic
/// segments become the cubic segments they are (degree elevation), since content streams have no quadratic operator.
/// </remarks>
public static class GlyphOutlinePages
{
    /// <summary>Returns a one-page file (612 × 792) whose content paints the glyph outlines <paramref name="list"/> fills, as paths.</summary>
    /// <param name="list">The display list of a text-only page.</param>
    /// <returns>The file.</returns>
    public static byte[] Create(DisplayList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        var writer = new Writer();
        list.Replay(writer);
        return ContentSamples.OnePage(Encoding.ASCII.GetBytes(writer.Content.ToString()));
    }

    private sealed class Writer : IRenderingBackend
    {
        private readonly Stack<Matrix> _saved = new();
        private Matrix _ctm = Matrix.Identity;

        public StringBuilder Content { get; } = new();

        public void SaveState() => _saved.Push(_ctm);

        public void RestoreState() => _ctm = _saved.Pop();

        public void SetTransform(in Matrix transform) => _ctm = transform;

        public void DrawGlyphs(in GlyphRun run)
        {
            if (run.Strokes)
            {
                throw new NotSupportedException("Stroked text is not written as paths.");
            }

            if (!run.Fills)
            {
                return;
            }

            string color = Color(run.FillColor);
            for (int index = 0; index < run.Glyphs.Length; index++)
            {
                PathView outline = run.GetOutline(index);
                if (outline.IsEmpty)
                {
                    continue;
                }

                Matrix m = run.GetGlyphTransform(index) * _ctm;
                Content.Append("q ").Append(color).Append(' ')
                    .Append(Number(m.A)).Append(' ').Append(Number(m.B)).Append(' ').Append(Number(m.C)).Append(' ')
                    .Append(Number(m.D)).Append(' ').Append(Number(m.E)).Append(' ').Append(Number(m.F)).Append(" cm\n");
                WritePath(outline);
                Content.Append("f Q\n");
            }
        }

        public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode)
        {
            if (fillAlpha != 1 || blendMode != BlendMode.Normal)
            {
                throw new NotSupportedException("Only opaque, Normal text is written as paths.");
            }
        }

        public void FillPath(PathView path, FillRule rule, in DisplayColor color) => throw Unsupported();

        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color) => throw Unsupported();

        public void IntersectClip(PathView path, FillRule rule) => throw Unsupported();

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion) => throw Unsupported();

        public void FillImageMask(DisplayImageMask mask, in DisplayColor color) => throw Unsupported();

        public void DrawShading(in DisplayShading shading) => throw Unsupported();

        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading) => throw Unsupported();

        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading) => throw Unsupported();

        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern) => throw Unsupported();

        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern) => throw Unsupported();

        public void BeginAnnotation(in DisplayAnnotation annotation) => throw Unsupported();

        public void EndAnnotation() => throw Unsupported();

        public void DrawGroup(in DisplayGroup group)
        {
            if (group.IsKnockout || group.Opacity < 1 || group.BlendMode != BlendMode.Normal || !group.SoftMask.IsNone)
            {
                throw new NotSupportedException("Only text-only pages without compositing groups are written as paths.");
            }

            group.Replay(this);
        }

        public void SetSoftMask(in DisplaySoftMask mask)
        {
            if (!mask.IsNone)
            {
                throw Unsupported();
            }
        }

        public void SetDeviceParameters(in DisplayDeviceParameters parameters)
        {
        }

        private static NotSupportedException Unsupported() => new("Only text-only pages are written as paths.");

        private static string Color(in DisplayColor color)
        {
            string op = color.ColorSpace.Family switch
            {
                PdfColorSpaceFamily.DeviceGray => "g",
                PdfColorSpaceFamily.DeviceRgb => "rg",
                PdfColorSpaceFamily.DeviceCmyk => "k",
                _ => throw new NotSupportedException($"Text in {color.ColorSpace.Family} is not written as paths."),
            };
            var text = new StringBuilder();
            foreach (float component in color.Components)
            {
                text.Append(Number(component)).Append(' ');
            }

            return text.Append(op).ToString();
        }

        private void WritePath(PathView path)
        {
            ReadOnlySpan<PathVerb> verbs = path.Verbs;
            ReadOnlySpan<PathPoint> points = path.Points;
            PathPoint current = default;
            int p = 0;
            foreach (PathVerb verb in verbs)
            {
                switch (verb)
                {
                    case PathVerb.MoveTo:
                        current = points[p++];
                        Point(current).Append("m\n");
                        break;
                    case PathVerb.LineTo:
                        current = points[p++];
                        Point(current).Append("l\n");
                        break;
                    case PathVerb.QuadTo:
                        PathPoint control = points[p];
                        PathPoint end = points[p + 1];
                        p += 2;
                        Point(new PathPoint(current.X + ((control.X - current.X) * 2 / 3), current.Y + ((control.Y - current.Y) * 2 / 3)));
                        Point(new PathPoint(end.X + ((control.X - end.X) * 2 / 3), end.Y + ((control.Y - end.Y) * 2 / 3)));
                        Point(end).Append("c\n");
                        current = end;
                        break;
                    case PathVerb.CubicTo:
                        Point(points[p]);
                        Point(points[p + 1]);
                        current = points[p + 2];
                        Point(current).Append("c\n");
                        p += 3;
                        break;
                    case PathVerb.Close:
                        Content.Append("h\n");
                        break;
                }
            }
        }

        private StringBuilder Point(PathPoint point) => Content.Append(Number(point.X)).Append(' ').Append(Number(point.Y)).Append(' ');

        private static string Number(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);
    }
}
