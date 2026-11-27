using Broadside.Diagnostics;

namespace Broadside.Graphics.Svg;

/// <summary>Clipping paths and masks.</summary>
/// <remarks>SVG 1.1 §14.3 (clipping paths) and §14.4 (masking); ISO 32000-2 §8.5.4 (clipping path operators) and §11.6.5 (soft masks).</remarks>
internal sealed partial class SvgImporter
{
    /// <summary>Intersects the clip with the clipping path <paramref name="reference"/> names.</summary>
    /// <returns><see langword="false"/> when the clip is empty, so the element draws nothing.</returns>
    private bool ApplyClipPath(SvgElement element, string reference, Func<SvgRect> bounds)
    {
        if (SvgReference.FromUrl(reference) is not { } id || _svg.Find(id) is not { Name: "clipPath" } clip)
        {
            Report(element, SvgDiagnostics.SvgReferenceMissing, $"The clip-path '{reference}' of {element} names no clipPath element; it is ignored.");
            return true;
        }

        if (_active.Contains(clip) || _active.Count >= MaxReferenceDepth)
        {
            Report(element, SvgDiagnostics.SvgReferenceCycle, $"The clip-path '{reference}' of {element} refers back to itself; it is ignored.");
            return true;
        }

        bool visible = true;
        Follow(clip, () =>
        {
            SvgStyle clipStyle = AncestorStyle(clip);
            Matrix units = Matrix.Identity;
            if (clip.Attribute("clipPathUnits") == "objectBoundingBox")
            {
                SvgRect box = bounds();
                if (!(box.Width > 0 && box.Height > 0))
                {
                    visible = false;
                    return;
                }

                units = new Matrix(box.Width, 0, 0, box.Height, box.X, box.Y);
            }

            Matrix transform = clip.Attribute("transform") is { } text && SvgTransformParser.TryParse(text, out Matrix parsed) ? parsed : Matrix.Identity;
            Matrix toUser = transform * units;

            // A clipping path may itself be clipped: the clips intersect.
            if (clipStyle.ClipPath is { } nested && !ApplyClipPath(clip, nested, bounds))
            {
                visible = false;
                return;
            }

            var combined = new SvgPath();
            FillRule rule = FillRule.NonZero;
            int shapes = 0;
            foreach (SvgElement child in clip.Children)
            {
                SvgStyle childStyle = StyleOf(child, clipStyle);
                if (!childStyle.Displayed || !childStyle.Visible)
                {
                    continue;
                }

                SvgElement shape = child;
                Matrix childTransform = child.Attribute("transform") is { } childText && SvgTransformParser.TryParse(childText, out Matrix childParsed) ? childParsed : Matrix.Identity;
                if (child.Name == "use")
                {
                    if (child.Attribute("href") is not { } href || SvgReference.Id(href) is not { } targetId || _svg.Find(targetId) is not { } target)
                    {
                        continue;
                    }

                    childTransform = Matrix.CreateTranslation(LengthX(child, "x", 0, childStyle), LengthY(child, "y", 0, childStyle)) * childTransform;
                    shape = target;
                    childStyle = StyleOf(target, childStyle);
                    if (target.Attribute("transform") is { } targetText && SvgTransformParser.TryParse(targetText, out Matrix targetTransform))
                    {
                        childTransform = targetTransform * childTransform;
                    }
                }

                if (shape.Name == "text")
                {
                    Report(child, SvgDiagnostics.SvgFeatureUnsupported, $"Text in the clipPath '#{id}' is not supported as a clip; it is left out.");
                    continue;
                }

                if (Geometry(shape, childStyle, report: true) is not { } path || path.IsEmpty)
                {
                    continue;
                }

                Append(combined, path, childTransform * toUser);
                rule = childStyle.ClipRule;
                shapes++;
            }

            if (shapes == 0)
            {
                visible = false;
                return;
            }

            combined.Emit(Canvas, Matrix.Identity);
            Canvas.Clip(shapes == 1 ? rule : FillRule.NonZero);
            Canvas.EndPath();
        });

        return visible;
    }

    private static void Append(SvgPath target, SvgPath source, Matrix matrix)
    {
        foreach (SvgSegment segment in source.Segments)
        {
            PathPoint end = matrix.Transform(segment.X, segment.Y);
            switch (segment.Kind)
            {
                case SvgSegmentKind.Move:
                    target.MoveTo(end.X, end.Y);
                    break;
                case SvgSegmentKind.Line:
                    target.LineTo(end.X, end.Y);
                    break;
                case SvgSegmentKind.Cubic:
                    PathPoint c1 = matrix.Transform(segment.X1, segment.Y1);
                    PathPoint c2 = matrix.Transform(segment.X2, segment.Y2);
                    target.CubicTo(c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
                    break;
                default:
                    target.Close();
                    break;
            }
        }
    }

    /// <summary>Builds the soft mask a <c>mask</c> reference names, in the current user space.</summary>
    /// <returns><see langword="false"/> when the mask hides everything (an empty region), so the element draws nothing.</returns>
    private bool TryBuildMask(SvgElement element, string reference, Func<SvgRect> bounds, SvgRect area, out PdfSoftMask? mask)
    {
        mask = null;
        if (SvgReference.FromUrl(reference) is not { } id || _svg.Find(id) is not { Name: "mask" } maskElement)
        {
            Report(element, SvgDiagnostics.SvgReferenceMissing, $"The mask '{reference}' of {element} names no mask element; it is ignored.");
            return true;
        }

        if (_active.Contains(maskElement) || _active.Count >= MaxReferenceDepth)
        {
            Report(element, SvgDiagnostics.SvgReferenceCycle, $"The mask '{reference}' of {element} refers back to itself; it is ignored.");
            return true;
        }

        SvgStyle maskStyle = AncestorStyle(maskElement);
        bool boxUnits = maskElement.Attribute("maskUnits") != "userSpaceOnUse";
        bool boxContent = maskElement.Attribute("maskContentUnits") == "objectBoundingBox";
        SvgRect box = boxUnits || boxContent ? bounds() : default;
        if ((boxUnits || boxContent) && !(box.Width > 0 && box.Height > 0))
        {
            return false;
        }

        SvgRect region = boxUnits
            ? new SvgRect(
                box.X + (Fraction(maskElement, "x", -0.1) * box.Width),
                box.Y + (Fraction(maskElement, "y", -0.1) * box.Height),
                Fraction(maskElement, "width", 1.2) * box.Width,
                Fraction(maskElement, "height", 1.2) * box.Height)
            : new SvgRect(
                Length(maskElement, "x", -0.1 * _viewportWidth, maskStyle, _viewportWidth, null),
                Length(maskElement, "y", -0.1 * _viewportHeight, maskStyle, _viewportHeight, null),
                Length(maskElement, "width", 1.2 * _viewportWidth, maskStyle, _viewportWidth, null),
                Length(maskElement, "height", 1.2 * _viewportHeight, maskStyle, _viewportHeight, null));
        if (!region.HasArea)
        {
            return false;
        }

        PdfFormXObject form = _document.CreateFormXObject(region.ToPdf());
        form.CreateTransparencyGroup().ColorSpace = PdfColor.FromRgb(0, 0, 0).ColorSpace;
        using (PdfCanvas canvas = form.CreateCanvas())
        {
            WithContext(canvas, region, () =>
            {
                Canvas.Rectangle(region.X, region.Y, region.Width, region.Height);
                Canvas.Clip();
                Canvas.EndPath();
                if (boxContent)
                {
                    Transform(new Matrix(box.Width, 0, 0, box.Height, box.X, box.Y));
                }

                Follow(maskElement, () => RenderChildren(maskElement, maskStyle));
            });
        }

        mask = _document.CreateSoftMask(maskStyle.AlphaMask ? PdfSoftMaskType.Alpha : PdfSoftMaskType.Luminosity, form);
        return true;
    }
}
