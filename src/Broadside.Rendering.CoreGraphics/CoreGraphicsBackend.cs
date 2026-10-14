using System.Runtime.InteropServices;
using Broadside.Graphics;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics;

/// <summary>
/// The backend <see cref="CoreGraphicsRenderer"/> replays display lists through: the PDF graphics state kept beside the context's,
/// paths handed to CoreGraphics, colours converted by the document's colour management.
/// </summary>
/// <remarks>
/// <para>
/// Transforms: <see cref="SetTransform"/> replaces the CTM, which CoreGraphics cannot do (its transform only concatenates), so the
/// CTM is kept here. Fills and clips are built from points mapped to the context's user space here, under the context's own
/// transform; strokes concatenate the CTM for the one stroke inside a saved state, because the pen is in the stroke's user space.
/// </para>
/// <para>
/// Nothing is allocated per operation: points go to the context one call at a time, colours through a buffer kept for the
/// backend's lifetime, strokes through <see cref="CoreGraphicsStroker"/>'s kept arrays.
/// </para>
/// </remarks>
internal sealed partial class CoreGraphicsBackend : IRenderingBackend
{
    /// <summary>Mapped coordinates are kept within ±2^40 units, as the software rasterizer keeps its device coordinates.</summary>
    internal const double CoordinateLimit = 1099511627776.0;

    private static readonly CGColorSpace Srgb = CGColorSpace.CreateSrgb() ?? throw new PlatformNotSupportedException("CoreGraphics has no sRGB colour space.");

    private readonly nfloat[] _color = new nfloat[4];
    private readonly CoreGraphicsStroker _stroker = new();
    private SavedState[] _stack = new SavedState[16];
    private int _depth;
    private Matrix _device;
    private Matrix _ctm;
    private Matrix _toDevice;
    private CGContext? _context;
    private PdfDocument? _document;

    private PdfColorSpace? _space;
    private ColorConversion _conversion;
    private PdfDefaultColorSpaces? _defaults;
    private PdfColorConverter? _converter;

    /// <summary>Replays <paramref name="list"/> onto <paramref name="context"/>, leaving its graphics state as it was.</summary>
    public void Replay(DisplayList list, CGContext context, in Matrix device)
    {
        _context = context;
        _document = list.Document;
        _device = device;
        _ctm = Matrix.Identity;
        _toDevice = device;
        _depth = 0;
        BeginCompositing();
        context.SaveState();
        try
        {
            context.SetFillColorSpace(Srgb);
            context.SetStrokeColorSpace(Srgb);
            context.SetShouldAntialias(true);
            list.Replay(this);
        }
        finally
        {
            for (; _depth > 0; _depth--)
            {
                EndLayerAt(_depth);
                context.RestoreState();
            }

            context.RestoreState();
            _context = null;
            _document = null;
            _space = null;
            _defaults = null;
            _converter = null;
        }
    }

    public void SaveState()
    {
        if (_depth == _stack.Length)
        {
            Array.Resize(ref _stack, _stack.Length * 2);
        }

        _stack[_depth++] = new SavedState(_ctm, _toDevice, _compositing);
        _context!.SaveState();
    }

    public void RestoreState()
    {
        if (_depth > 0)
        {
            SavedState saved = _stack[--_depth];
            _ctm = saved.Ctm;
            _toDevice = saved.ToDevice;
            _compositing = saved.Compositing;
            _context!.RestoreState();
        }
    }

    public void SetTransform(in Matrix transform)
    {
        _ctm = transform;
        _toDevice = transform * _device;
    }

    public void FillPath(PathView path, FillRule rule, in DisplayColor color)
    {
        if (path.IsEmpty || _compositing.FillAlpha == 0 || !TryLoadColor(color, _compositing.FillAlpha))
        {
            return;
        }

        CGContext context = _context!;
        context.SetFillColor(_color);
        context.BeginPath();
        AddMappedPath(context, path);
        if (rule == FillRule.EvenOdd)
        {
            context.EOFillPath();
        }
        else
        {
            context.FillPath();
        }
    }

    public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
    {
        if (path.IsEmpty || _compositing.StrokeAlpha == 0 || !TryLoadColor(color, _compositing.StrokeAlpha))
        {
            return;
        }

        _context!.SetStrokeColor(_color);
        _stroker.Stroke(_context, path, style, _toDevice);
    }

    public void IntersectClip(PathView path, FillRule rule)
    {
        CGContext context = _context!;
        context.BeginPath();
        if (!AddMappedPath(context, path))
        {
            // §8.5.4: a path that encloses nothing leaves nothing inside the clip.
            context.ClipToRect(CGRect.Empty);
            return;
        }

        if (rule == FillRule.EvenOdd)
        {
            context.EOClip();
        }
        else
        {
            context.Clip();
        }
    }

    // Images, shadings, glyph runs and stencil masks are drawn by the CoreGraphics tickets that follow this one (#126, #127).
    public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
    {
    }

    public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
    {
    }

    public void DrawGlyphs(in GlyphRun run)
    {
    }

    public void DrawShading(in DisplayShading shading)
    {
    }

    public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
    {
    }

    public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
    {
    }

    private static nfloat Quantize(float value) => (nfloat)(MathF.Round(Math.Clamp(value, 0f, 1f) * 255f) / 255f);

    /// <summary>
    /// Converts <paramref name="color"/> to sRGB into the colour buffer, with <paramref name="alpha"/> (the constant alpha, 0 to 255) as
    /// its alpha (§11.6.4.4); false when it paints nothing (Separation None).
    /// </summary>
    private bool TryLoadColor(in DisplayColor color, byte alpha)
    {
        if (_converter is null || !ReferenceEquals(_space, color.ColorSpace) || !ReferenceEquals(_defaults, color.DefaultColorSpaces) || _conversion != color.Conversion)
        {
            _space = color.ColorSpace;
            _defaults = color.DefaultColorSpaces;
            _conversion = color.Conversion;
            _converter = _document!.GetColorConverter(color.ColorSpace, color.Conversion with { Target = DeviceColorModel.Rgb }, color.DefaultColorSpaces);
        }

        if (_converter.PaintsNothing)
        {
            return false;
        }

        Span<float> rgb = stackalloc float[4];
        _converter.Convert(color.Components, rgb);

        // The golden images hold 8-bit colours; giving CoreGraphics the same 8-bit values keeps its rounding out of the comparison.
        _color[0] = Quantize(rgb[0]);
        _color[1] = Quantize(rgb[1]);
        _color[2] = Quantize(rgb[2]);
        _color[3] = (nfloat)(alpha / 255.0);
        return true;
    }

    /// <summary>
    /// Adds <paramref name="path"/> to the context's current path for a fill or clip, mapped by the current transform to the context's
    /// user space. A lone moveto adds nothing; a degenerate subpath (one point, with at least one segment or a close) adds the device
    /// pixel under that point instead (§8.5.3.3.1).
    /// </summary>
    /// <returns>Whether anything was added.</returns>
    private bool AddMappedPath(CGContext context, PathView path)
    {
        ReadOnlySpan<PathVerb> verbs = path.Verbs;
        ReadOnlySpan<PathPoint> points = path.Points;
        Matrix m = _toDevice;
        bool added = false;
        int verb = 0;
        int p = 0;
        while (verb < verbs.Length)
        {
            if (verbs[verb] != PathVerb.MoveTo)
            {
                // A path begins with a moveto; anything before one is skipped.
                p += PointCount(verbs[verb++]);
                continue;
            }

            int start = p;
            int end = verb + 1;
            int q = p + 1;
            bool degenerate = true;
            while (end < verbs.Length && verbs[end] != PathVerb.MoveTo)
            {
                int count = PointCount(verbs[end]);
                for (int k = 0; k < count; k++)
                {
                    degenerate &= points[q + k] == points[start];
                }

                q += count;
                end++;
            }

            bool segments = end > verb + 1;
            if (!degenerate)
            {
                PathPoint first = Map(m, points[start]);
                context.MoveTo((nfloat)first.X, (nfloat)first.Y);
                for (int index = verb + 1, r = p + 1; index < end; r += PointCount(verbs[index]), index++)
                {
                    Emit(context, verbs[index], points, r, m);
                }

                added = true;
            }
            else if (segments)
            {
                PathPoint point = Map(m, points[start]);
                context.AddRect(new CGRect(Math.Floor(point.X), Math.Floor(point.Y), 1, 1));
                added = true;
            }

            verb = end;
            p = q;
        }

        return added;
    }

    /// <summary>Maps a point to the context's space, clamped to <see cref="CoordinateLimit"/> (NaN, from infinite input, to 0).</summary>
    internal static PathPoint Map(in Matrix m, PathPoint point)
    {
        PathPoint mapped = m.Transform(point);
        return new PathPoint(Clamp(mapped.X), Clamp(mapped.Y));
    }

    private static double Clamp(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, -CoordinateLimit, CoordinateLimit);

    private static int PointCount(PathVerb verb) => verb switch
    {
        PathVerb.MoveTo or PathVerb.LineTo => 1,
        PathVerb.QuadTo => 2,
        PathVerb.CubicTo => 3,
        _ => 0,
    };

    private static void Emit(CGContext context, PathVerb verb, ReadOnlySpan<PathPoint> points, int p, in Matrix m)
    {
        switch (verb)
        {
            case PathVerb.LineTo:
                PathPoint point = Map(m, points[p]);
                context.AddLineToPoint((nfloat)point.X, (nfloat)point.Y);
                break;
            case PathVerb.QuadTo:
                PathPoint control = Map(m, points[p]);
                PathPoint end = Map(m, points[p + 1]);
                context.AddQuadCurveToPoint((nfloat)control.X, (nfloat)control.Y, (nfloat)end.X, (nfloat)end.Y);
                break;
            case PathVerb.CubicTo:
                PathPoint c1 = Map(m, points[p]);
                PathPoint c2 = Map(m, points[p + 1]);
                PathPoint to = Map(m, points[p + 2]);
                context.AddCurveToPoint((nfloat)c1.X, (nfloat)c1.Y, (nfloat)c2.X, (nfloat)c2.Y, (nfloat)to.X, (nfloat)to.Y);
                break;
            case PathVerb.Close:
                context.ClosePath();
                break;
        }
    }
}
