using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// An independent reference for forced outlines (issue #123): replays a display list of filled paths and filled glyph runs onto a
/// Skia canvas, every glyph's outline mapped to the device in double precision and filled as an ordinary Skia path with the non-zero
/// rule, the way the backend fills any path. Pages with anything else are refused, so a test sees only the pages it can compare.
/// </summary>
internal sealed class SkiaPathReference : IRenderingBackend, IDisposable
{
    private readonly PdfDocument _document;
    private readonly SKCanvas _canvas;
    private readonly Matrix _device;
    private readonly Stack<Matrix> _states = new();
    private readonly SKPaint _paint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private Matrix _ctm = Matrix.Identity;

    private SkiaPathReference(PdfDocument document, SKCanvas canvas, Matrix device)
    {
        _document = document;
        _canvas = canvas;
        _device = device;
    }

    /// <summary>Renders the page at the golden images' resolution on white.</summary>
    public static SKBitmap Render(PdfPage page, PdfDocument document, RenderOptions options)
    {
        PixelSize size = options.GetPixelSize(page);
        var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var reference = new SkiaPathReference(document, canvas, options.GetDeviceTransform(page));
        page.CreateDisplayList(options.Layers, options.Intent, new Content.ContentOptions()).Replay(reference);
        return bitmap;
    }

    public void Dispose() => _paint.Dispose();

    public void SaveState()
    {
        _states.Push(_ctm);
        _canvas.Save();
    }

    public void RestoreState()
    {
        _ctm = _states.Pop();
        _canvas.Restore();
    }

    public void SetTransform(in Matrix transform) => _ctm = transform;

    public void FillPath(PathView path, FillRule rule, in DisplayColor color)
    {
        _paint.Color = Color(color);
        using SKPath skia = Build(path, _ctm * _device, rule);
        _canvas.DrawPath(skia, _paint);
    }

    public void IntersectClip(PathView path, FillRule rule)
    {
        using SKPath skia = Build(path, _ctm * _device, rule);
        _canvas.ClipPath(skia, SKClipOperation.Intersect, antialias: true);
    }

    public void DrawGlyphs(in GlyphRun run)
    {
        if (run.Strokes)
        {
            throw new NotSupportedException("Stroked text is not part of the reference.");
        }

        if (!run.Fills)
        {
            return;
        }

        _paint.Color = Color(run.FillColor);
        for (int index = 0; index < run.Glyphs.Length; index++)
        {
            using SKPath skia = Build(run.GetOutline(index), run.GetGlyphTransform(index) * _ctm * _device, FillRule.NonZero);
            _canvas.DrawPath(skia, _paint);
        }
    }

    public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode)
    {
        if (fillAlpha != 1 || blendMode != BlendMode.Normal)
        {
            throw new NotSupportedException("Compositing is not part of the reference.");
        }
    }

    public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color) => throw Unsupported();

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
            throw new NotSupportedException("Compositing groups are not part of the reference.");
        }

        group.Replay(this);
    }

    public void SetSoftMask(in DisplaySoftMask mask)
    {
        if (!mask.IsNone)
        {
            throw new NotSupportedException("Soft masks are not part of the reference.");
        }
    }

    public void SetDeviceParameters(in DisplayDeviceParameters parameters)
    {
    }

    private static NotSupportedException Unsupported() => new("Only fills, clips and filled text are part of the reference.");

    private static SKPath Build(PathView path, in Matrix toDevice, FillRule rule)
    {
        using var builder = new SKPathBuilder { FillType = rule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding };
        ReadOnlySpan<PathPoint> points = path.Points;
        int p = 0;
        foreach (PathVerb verb in path.Verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    builder.MoveTo(Map(points[p++], toDevice));
                    break;
                case PathVerb.LineTo:
                    builder.LineTo(Map(points[p++], toDevice));
                    break;
                case PathVerb.QuadTo:
                    builder.QuadTo(Map(points[p], toDevice), Map(points[p + 1], toDevice));
                    p += 2;
                    break;
                case PathVerb.CubicTo:
                    builder.CubicTo(Map(points[p], toDevice), Map(points[p + 1], toDevice), Map(points[p + 2], toDevice));
                    p += 3;
                    break;
                case PathVerb.Close:
                    builder.Close();
                    break;
            }
        }

        return builder.Detach();
    }

    private static SKPoint Map(PathPoint point, in Matrix toDevice)
    {
        PathPoint device = toDevice.Transform(point);
        return new SKPoint((float)device.X, (float)device.Y);
    }

    private SKColor Color(in DisplayColor color)
    {
        PdfColorConverter converter = _document.GetColorConverter(color.ColorSpace, color.Conversion with { Target = DeviceColorModel.Rgb }, color.DefaultColorSpaces);
        Span<float> rgb = stackalloc float[4];
        converter.Convert(color.Components, rgb);
        return new SKColor(Channel(rgb[0]), Channel(rgb[1]), Channel(rgb[2]));
    }

    private static byte Channel(float value) => (byte)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);
}
