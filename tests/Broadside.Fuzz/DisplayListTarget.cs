using Broadside.Content;
using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>display-list</c> target (issue #94): runs the input as a page's decoded content through the display-list builder, then
/// replays the list twice into a checking backend. Invariants: nothing throws in lenient mode; saves and restores balance and never
/// go below zero; every path's verbs take exactly its points and every subpath starts with a moveto; every fill's and stroke's colour
/// space is not a Pattern space and has its components; every stroke's width, miter limit and dash pattern are in their ranges (issue
/// #99); every clip's path is well formed with a known rule (issue #105); every image (issue #102) has samples, a colour space and a
/// Decode map that agree on its component count; every glyph run (issue #100) has glyphs whose outlines are well formed, paints
/// in no Pattern colour, paints nothing in mode 3 and fills or strokes only in the modes that do; every shading (issue #109) is
/// valid, evaluates to finite colours and paints its Background only as a pattern; two replays of one list make the same calls.
/// The list's hit-test index (issue #101) gives every glyph a contiguous slice of its text, returns point matches in kind and content
/// order, and finds every glyph whose box has real size at its centre. Each call runs in a new empty document.
/// </summary>
/// <remarks>ADR 0006; ISO 32000-2 §8.4.2, §8.4.3, §8.5.3, §8.5.4, §8.6.</remarks>
internal static class DisplayListTarget
{
    // Inline images in the input are decoded into the list (issue #102); small limits keep a mutated /W /H from costing gigabytes.
    private static readonly PdfEngine Engine = new(new PdfOptions().WithMaxImagePixels(1 << 16).WithMaxDecodedStreamLength(1 << 20));

    public static void Target(ReadOnlySpan<byte> data)
    {
        using PdfDocument document = Engine.Create();
        var builder = new DisplayListBuilder();
        ContentInterpreter.RunBytes(data, document, builder, ContentInterpreter.DefaultOptions);
        DisplayList list = builder.ToDisplayList(document);

        var first = new CheckingBackend();
        list.Replay(first);
        var second = new CheckingBackend();
        list.Replay(second);
        Check(first.Depth == 0, "saves and restores do not balance");
        Check(first.Calls == second.Calls && first.Checksum.Equals(second.Checksum), "two replays of one list differ");
        CheckHitTest(list.CreateHitTestIndex());
    }

    private static void CheckHitTest(HitTestIndex index)
    {
        int text = 0;
        for (int glyph = 0; glyph < index.GlyphCount; glyph++)
        {
            HitTestGlyph entry = index.GetGlyph(glyph);
            Check(entry.TextStart == text && entry.TextLength >= 0, "a glyph's text does not follow the one before");
            text += entry.TextLength;
        }

        Check(text == index.Text.Length, "the glyphs' text does not cover the index's text");
        for (int glyph = 0; glyph < Math.Min(index.GlyphCount, 64); glyph++)
        {
            Quad quad = index.GetGlyph(glyph).Quad;
            double ux = quad.BottomRight.X - quad.BottomLeft.X;
            double uy = quad.BottomRight.Y - quad.BottomLeft.Y;
            double vx = quad.TopLeft.X - quad.BottomLeft.X;
            double vy = quad.TopLeft.Y - quad.BottomLeft.Y;
            var centre = new PathPoint(quad.BottomLeft.X + ((ux + vx) / 2), quad.BottomLeft.Y + ((uy + vy) / 2));
            PdfRectangle bounds = quad.Bounds;
            bool sized = Math.Abs((ux * vy) - (uy * vx)) > 1
                && Math.Max(Math.Max(Math.Abs(bounds.Left), Math.Abs(bounds.Right)), Math.Max(Math.Abs(bounds.Bottom), Math.Abs(bounds.Top))) < 1e6;
            bool found = false;
            int previousKind = 0;
            int previousIndex = -1;
            foreach (HitTestItem item in index.Find(centre, HitTestKinds.All))
            {
                Check((int)item.Kind > previousKind || ((int)item.Kind == previousKind && item.Index > previousIndex), "matches are out of order");
                previousKind = (int)item.Kind;
                previousIndex = item.Index;
                found |= item.Kind == HitTestKinds.Glyphs && item.Index == glyph;
            }

            Check(found || !sized, "a glyph is not found at its centre");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>Checks every call and folds what it received into a checksum.</summary>
    private sealed class CheckingBackend : IRenderingBackend
    {
        public int Depth { get; private set; }

        public int Calls { get; private set; }

        public double Checksum { get; private set; }

        public void SaveState()
        {
            Depth++;
            Calls++;
        }

        public void RestoreState()
        {
            Check(--Depth >= 0, "a restore without a save");
            Calls++;
        }

        public void SetTransform(in Matrix transform)
        {
            Calls++;
            Checksum += transform.A + transform.F;
        }

        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
            Calls++;
            Check(!path.IsEmpty && path.Verbs[0] == PathVerb.MoveTo, "a filled path does not start with a moveto");
            CheckPath(path, color);
            Checksum += (int)rule;
        }

        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
        {
            Calls++;
            Check(!path.IsEmpty && path.Verbs[0] == PathVerb.MoveTo, "a stroked path does not start with a moveto");
            Check(style.LineWidth >= 0, "a negative stroke width");
            Check(style.MiterLimit >= 1, "a miter limit below 1");
            Check(style.DashPhase >= 0 && (!style.DashArray.IsEmpty || style.DashPhase == 0), "a dash phase that is negative or set for a solid line");
            double sum = 0;
            foreach (double element in style.DashArray)
            {
                Check(element >= 0, "a negative dash element");
                sum += element;
            }

            Check(style.DashArray.IsEmpty || sum > 0, "a dash array of zeros");
            CheckPath(path, color);
            Checksum += style.LineWidth + style.DashArray.Length + (int)style.LineCap + (int)style.LineJoin;
        }

        /// <summary>Checks that the verbs take exactly the points, subpaths start with a moveto, and the colour is not a pattern.</summary>
        private void CheckPath(PathView path, in DisplayColor color)
        {
            int points = 0;
            for (int index = 0; index < path.Verbs.Length; index++)
            {
                PathVerb verb = path.Verbs[index];
                points += verb switch
                {
                    PathVerb.MoveTo or PathVerb.LineTo => 1,
                    PathVerb.QuadTo => 2,
                    PathVerb.CubicTo => 3,
                    _ => 0,
                };
                Check(verb != PathVerb.Close || index + 1 == path.Verbs.Length || path.Verbs[index + 1] is PathVerb.MoveTo or PathVerb.Close, "a segment after a close does not start a subpath");
            }

            Check(points == path.Points.Length, "path verbs and points disagree");
            Check(color.ColorSpace.Family != PdfColorSpaceFamily.Pattern, "a fill in a Pattern colour space");
            Check(color.Components.Length <= PdfColor.MaxComponents, "too many components");
            Checksum += points + color.Components.Length;
        }

        public void IntersectClip(PathView path, FillRule rule)
        {
            Calls++;
            Check(!path.IsEmpty && path.Verbs[0] == PathVerb.MoveTo, "a clipping path does not start with a moveto");
            Check(rule is FillRule.NonZero or FillRule.EvenOdd, "a clip with an unknown rule");
            int points = 0;
            foreach (PathVerb verb in path.Verbs)
            {
                points += verb switch
                {
                    PathVerb.MoveTo or PathVerb.LineTo => 1,
                    PathVerb.QuadTo => 2,
                    PathVerb.CubicTo => 3,
                    _ => 0,
                };
            }

            Check(points == path.Points.Length, "clipping path verbs and points disagree");
            Checksum += points + (2 * (int)rule) + 1;
        }

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
        {
            Calls++;
            Check(image.Width > 0 && image.Height > 0, "an image without samples");
            Check(image.Samples.Length == (long)image.Stride * image.Height, "the samples are not Stride x Height bytes");
            Check(image.ColorSpace.Family != PdfColorSpaceFamily.Pattern, "an image in a Pattern colour space");
            Check(image.ColorSpace.ComponentCount == image.Components && image.DecodeMap.Components == image.Components, "an image's colour space, samples and Decode disagree");
            Checksum += image.Width + image.Height + image.Components + (int)conversion.Intent;
            if (image.Mask is { } mask)
            {
                CheckMask(mask);
            }

            if (image.SoftMask is { } soft)
            {
                Check(soft.Components == 1 && soft.ColorSpace.ComponentCount == 1 && soft.DecodeMap.Components == 1, "a soft mask of more than one component");
                Check(soft.Samples.Length == (long)soft.Stride * soft.Height && soft.Width > 0 && soft.Height > 0, "the soft mask's samples are not Stride x Height bytes");
                Check(soft.SoftMask is null && soft.Mask is null && soft.ColorKey.IsEmpty, "a soft mask with a mask of its own");
                Checksum += soft.Width + soft.Height;
            }

            int baseComponents = image.ColorSpace is PdfIndexedColorSpace indexed ? indexed.Base.ComponentCount : image.Components;
            Check(image.Matte.IsEmpty || (image.SoftMask is not null && image.Matte.Length == baseComponents), "a Matte without a soft mask or of the wrong length");
            Check(image.ColorKey.Length is 0 || image.ColorKey.Length == 2 * image.Components, "colour key ranges of the wrong length");
            Check((image.Mask is null ? 0 : 1) + (image.SoftMask is null ? 0 : 1) + (image.ColorKey.IsEmpty ? 0 : 1) <= 1, "more than one mask on an image");
        }

        public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
        {
            Calls++;
            CheckMask(mask);
            Check(color.ColorSpace.Family != PdfColorSpaceFamily.Pattern, "a stencil mask in a Pattern colour space");
            Check(color.Components.Length <= PdfColor.MaxComponents, "too many components");
            Checksum += color.Components.Length;
        }

        private void CheckMask(DisplayImageMask mask)
        {
            Check(mask.Width > 0 && mask.Height > 0, "a mask without samples");
            Check(mask.Stride == (mask.Width + 7) / 8 && mask.Samples.Length == (long)mask.Stride * mask.Height, "the mask's samples are not one bit each");
            Checksum += mask.Width + mask.Height + (mask.IsInverted ? 1 : 0);
        }

        public void DrawGlyphs(in GlyphRun run)
        {
            Calls++;
            Check(!run.Glyphs.IsEmpty, "a glyph run without glyphs");
            TextRenderingMode mode = run.RenderingMode;
            Check(!run.Fills || mode is TextRenderingMode.Fill or TextRenderingMode.FillStroke or TextRenderingMode.FillClip or TextRenderingMode.FillStrokeClip, "a glyph run filled in a mode that does not fill");
            Check(!run.Strokes || mode is TextRenderingMode.Stroke or TextRenderingMode.FillStroke or TextRenderingMode.StrokeClip or TextRenderingMode.FillStrokeClip, "a glyph run stroked in a mode that does not stroke");
            Check(run.Program is not null || run.FontMatrix == Matrix.Identity, "a glyph run without a program has a font matrix");
            if (run.Fills)
            {
                Check(run.FillColor.ColorSpace.Family != PdfColorSpaceFamily.Pattern, "glyphs filled in a Pattern colour space");
                Checksum += run.FillColor.Components.Length;
            }

            if (run.Strokes)
            {
                Check(run.StrokeColor.ColorSpace.Family != PdfColorSpaceFamily.Pattern, "glyphs stroked in a Pattern colour space");
                Check(run.StrokeStyle.LineWidth >= 0 && run.StrokeStyle.MiterLimit >= 1, "glyphs stroked with a negative width or a miter limit below 1");
                Checksum += run.StrokeStyle.LineWidth;
            }

            for (int index = 0; index < run.Glyphs.Length; index++)
            {
                DisplayGlyph glyph = run.Glyphs[index];
                Check(glyph.GlyphId >= 0, "a negative glyph id");
                Check(glyph.HorizontalScale > 0 && double.IsFinite(glyph.HorizontalScale), "a glyph scale that is not a positive number");
                PathView outline = run.GetOutline(index);
                int points = 0;
                foreach (PathVerb verb in outline.Verbs)
                {
                    points += verb switch
                    {
                        PathVerb.MoveTo or PathVerb.LineTo => 1,
                        PathVerb.QuadTo => 2,
                        PathVerb.CubicTo => 3,
                        _ => 0,
                    };
                }

                Check(points == outline.Points.Length, "glyph outline verbs and points disagree");
                Check(outline.IsEmpty || outline.Verbs[0] == PathVerb.MoveTo, "a glyph outline does not start with a moveto");
                Checksum += glyph.GlyphId + points + (int)mode;
            }
        }

        public void DrawShading(in DisplayShading shading)
        {
            Calls++;
            Check(!shading.PaintsBackground, "sh paints a Background");
            CheckShading(shading);
        }

        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
        {
            Calls++;
            Check(!path.IsEmpty && path.Verbs[0] == PathVerb.MoveTo, "a shaded path does not start with a moveto");
            Check(shading.PaintsBackground, "a shading pattern does not paint its Background");
            CheckShading(shading);
            Checksum += (int)rule + path.Points.Length;
        }

        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
        {
            Calls++;
            Check(!path.IsEmpty && path.Verbs[0] == PathVerb.MoveTo, "a shaded stroke does not start with a moveto");
            Check(style.LineWidth >= 0, "a negative stroke width");
            Check(shading.PaintsBackground, "a shading pattern does not paint its Background");
            CheckShading(shading);
            Checksum += style.LineWidth + path.Points.Length;
        }

        /// <summary>Checks that a shading reaching the backend is usable and evaluates it like a backend would.</summary>
        private void CheckShading(in DisplayShading shading)
        {
            PdfShading model = shading.Shading;
            Check(model.IsValid, "an invalid shading reached the backend");
            Check(model.ColorSpace.Family != PdfColorSpaceFamily.Pattern, "a shading in a Pattern colour space");
            Check(model.ColorComponentCount <= PdfColor.MaxComponents, "too many shading components");
            if (model.HasFunction && model is PdfAxialShading or PdfRadialShading)
            {
                Span<float> color = stackalloc float[PdfColor.MaxComponents];
                model.EvaluateFunction([0.5f], color);
                foreach (float component in color[..model.ColorComponentCount])
                {
                    Check(float.IsFinite(component), "a shading function gave a non-finite component");
                }
            }

            Checksum += (int)model.ShadingType + shading.Matrix.A;
        }
    }
}
