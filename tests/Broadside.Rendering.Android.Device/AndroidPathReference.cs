using Broadside.Graphics;
using AndroidBitmap = Android.Graphics.Bitmap;
using AndroidCanvas = Android.Graphics.Canvas;
using AndroidColor = Android.Graphics.Color;
using AndroidPath = Android.Graphics.Path;
using Matrix = Broadside.Graphics.Matrix;
using Paint = Android.Graphics.Paint;
using PaintFlags = Android.Graphics.PaintFlags;

namespace Broadside.Rendering.Android.Device;

/// <summary>
/// An independent reference for forced outlines (issue #135): replays a display list of filled paths, clips and filled glyph runs onto
/// an Android canvas, every glyph's outline mapped to the device in double precision and filled as an ordinary Android path with the
/// non-zero rule. Pages with anything else are refused (<see cref="NotSupportedException"/>), so the host compares only the pages the
/// reference can draw.
/// </summary>
internal sealed class AndroidPathReference : IRenderingBackend, IDisposable
{
    private readonly PdfDocument _document;
    private readonly AndroidCanvas _canvas;
    private readonly Matrix _device;
    private readonly Stack<Matrix> _states = new();
    private readonly Paint _paint = new(PaintFlags.AntiAlias);
    private Matrix _ctm = Matrix.Identity;

    private AndroidPathReference(PdfDocument document, AndroidCanvas canvas, Matrix device)
    {
        _document = document;
        _canvas = canvas;
        _device = device;
        _paint.SetStyle(Paint.Style.Fill);
    }

    /// <summary>Renders the page on white into a new bitmap of the options' pixel size.</summary>
    public static AndroidBitmap Render(PdfPage page, PdfDocument document, RenderOptions options)
    {
        PixelSize size = options.GetPixelSize(page);
        AndroidBitmap bitmap = AndroidBitmap.CreateBitmap(size.Width, size.Height, AndroidBitmap.Config.Argb8888!);
        try
        {
            using var canvas = new AndroidCanvas(bitmap);
            canvas.DrawColor(AndroidColor.White);
            using var reference = new AndroidPathReference(document, canvas, options.GetDeviceTransform(page));
            page.CreateDisplayList(options.Layers, options.Intent, new Content.ContentOptions()).Replay(reference);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

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
        SetColor(color);
        using AndroidPath android = Build(path, _ctm * _device, rule);
        _canvas.DrawPath(android, _paint);
    }

    public void IntersectClip(PathView path, FillRule rule)
    {
        using AndroidPath android = Build(path, _ctm * _device, rule);
        _canvas.ClipPath(android);
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

        SetColor(run.FillColor);
        for (int index = 0; index < run.Glyphs.Length; index++)
        {
            using AndroidPath android = Build(run.GetOutline(index), run.GetGlyphTransform(index) * _ctm * _device, FillRule.NonZero);
            _canvas.DrawPath(android, _paint);
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

    public void DrawGroup(in DisplayGroup group) => throw Unsupported();

    public void SetSoftMask(in DisplaySoftMask mask) => throw Unsupported();

    // Halftones, transfer functions and the like change nothing on a display (§10.1).
    public void SetDeviceParameters(in DisplayDeviceParameters parameters)
    {
    }

    private static NotSupportedException Unsupported() => new("Only fills, clips and filled text are part of the reference.");

    private void SetColor(in DisplayColor color)
    {
        PdfColorConverter converter = _document.GetColorConverter(color.ColorSpace, color.Conversion with { Target = DeviceColorModel.Rgb }, color.DefaultColorSpaces);
        Span<float> rgb = stackalloc float[4];
        converter.Convert(color.Components, rgb);
        _paint.SetARGB(255, Channel(rgb[0]), Channel(rgb[1]), Channel(rgb[2]));
    }

    private static int Channel(float value) => (int)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);

    private static AndroidPath Build(PathView path, in Matrix toDevice, FillRule rule)
    {
        var android = new AndroidPath();
        android.SetFillType(rule == FillRule.EvenOdd ? AndroidPath.FillType.EvenOdd! : AndroidPath.FillType.Winding!);
        ReadOnlySpan<PathPoint> points = path.Points;
        int p = 0;
        foreach (PathVerb verb in path.Verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    (float x, float y) = Map(points[p++], toDevice);
                    android.MoveTo(x, y);
                    break;
                case PathVerb.LineTo:
                    (x, y) = Map(points[p++], toDevice);
                    android.LineTo(x, y);
                    break;
                case PathVerb.QuadTo:
                    (float x1, float y1) = Map(points[p], toDevice);
                    (x, y) = Map(points[p + 1], toDevice);
                    android.QuadTo(x1, y1, x, y);
                    p += 2;
                    break;
                case PathVerb.CubicTo:
                    (x1, y1) = Map(points[p], toDevice);
                    (float x2, float y2) = Map(points[p + 1], toDevice);
                    (x, y) = Map(points[p + 2], toDevice);
                    android.CubicTo(x1, y1, x2, y2, x, y);
                    p += 3;
                    break;
                case PathVerb.Close:
                    android.Close();
                    break;
            }
        }

        return android;
    }

    private static (float X, float Y) Map(PathPoint point, in Matrix m) =>
        ((float)((m.A * point.X) + (m.C * point.Y) + m.E), (float)((m.B * point.X) + (m.D * point.Y) + m.F));
}
