using Broadside.Diagnostics;
using Broadside.Graphics;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// Shadings (ISO 32000-2 §8.7.4.5) painted by <c>sh</c> over the clip and through shading patterns (§8.7.3.3) in fills and strokes:
/// axial and nested radial shadings as Direct2D gradient brushes in shading space, the Extend flags as clips; other radials and the
/// meshes rasterized per device pixel and function-based shadings as a texture, all three through a bitmap brush.
/// </summary>
internal sealed unsafe partial class Direct2DBackend
{
    /// <summary>How far, in device pixels, a circle's chords may stray from it: an extend clip decides whole pixels by their centres.</summary>
    private const double CircleTolerance = 0.002;

    private static readonly Diagnostic ShadingSkipped = new(
        "BackendShadingUnsupported",
        DiagnosticSeverity.Warning,
        "The Direct2D backend met a shading of a type it does not know (ISO 32000-2 §8.7.4.5); those paints were left out of this render.");

    private static readonly Diagnostic MeshRasterized = new(
        "BackendMeshShadingRasterized",
        DiagnosticSeverity.Information,
        "Direct2D has no Gouraud-shaded triangles; the page's mesh shadings (ISO 32000-2 §8.7.4.5.5 to §8.7.4.5.8) were " +
        "rasterized by the backend at the device's pixels, without anti-aliasing at the mesh's outer edges, and drawn as bitmaps.");

    private static readonly Diagnostic RadialRasterized = new(
        "BackendRadialShadingRasterized",
        DiagnosticSeverity.Information,
        "Direct2D's radial gradients have one circle inside the other; the page's radial shadings whose circles are not nested " +
        "(ISO 32000-2 §8.7.4.5.4) were evaluated by the backend at the device's pixels and drawn as bitmaps.");

    private readonly ShadingCache _shadings;

    /// <summary>Paints the shading over the current clip (ISO 32000-2 §8.7.4.2, <c>sh</c>): its Background is not painted.</summary>
    private void DrawShadingCore(in DisplayShading shading)
    {
        if (_compositing.FillAlpha == 0 || GetShading(shading) is not { PaintsNothing: false } entry || !Paints(entry))
        {
            return;
        }

        int mark = _clipCount;
        try
        {
            if (UseShading(entry, shading, _compositing.FillAlpha / 255f))
            {
                D2D_RECT_F area = ClipBounds;
                if (area.right > area.left && area.bottom > area.top)
                {
                    SetWorldTransform(Matrix.Identity);
                    _context->FillRectangle(&area, Paint(Matrix.Identity));
                }
            }
        }
        finally
        {
            EndPaint(mark);
        }
    }

    /// <summary>Fills the path with a shading pattern (ISO 32000-2 §8.7.3.3), its Background first where the pattern asks for it.</summary>
    private void FillPathWithShadingCore(PathView path, FillRule rule, in DisplayShading shading)
    {
        if (path.IsEmpty || _compositing.FillAlpha == 0 || GetShading(shading) is not { PaintsNothing: false } entry)
        {
            return;
        }

        PaintWithBackground(entry, shading, _compositing.FillAlpha, path, rule, default, fill: true);
    }

    /// <summary>Strokes the path with a shading pattern (ISO 32000-2 §8.7.3.3).</summary>
    private void StrokePathWithShadingCore(PathView path, in StrokeStyle style, in DisplayShading shading)
    {
        if (path.IsEmpty || _compositing.StrokeAlpha == 0 || GetShading(shading) is not { PaintsNothing: false } entry || !ConfigurePen(style))
        {
            return;
        }

        PaintWithBackground(entry, shading, _compositing.StrokeAlpha, path, default, style, fill: false);
    }

    private static bool Paints(ShadingCache.Entry entry) => entry.Gradient is not null || entry.Mesh is not null || entry.Texture is not null;

    private static PixelRect Intersect(PixelRect pixels, in Matrix m, PdfRectangle extent)
    {
        PathPoint a = m.Transform(extent.Left, extent.Bottom);
        PathPoint b = m.Transform(extent.Right, extent.Bottom);
        PathPoint c = m.Transform(extent.Right, extent.Top);
        PathPoint d = m.Transform(extent.Left, extent.Top);
        double left = Math.Floor(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)));
        double right = Math.Ceiling(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)));
        double top = Math.Floor(Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)));
        double bottom = Math.Ceiling(Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
        if (!(left < right) || !(top < bottom))
        {
            return default;
        }

        return new PixelRect(
            (int)Math.Max(pixels.Left, left),
            (int)Math.Max(pixels.Top, top),
            (int)Math.Min(pixels.Right, right),
            (int)Math.Min(pixels.Bottom, bottom));
    }

    private static int Step(PixelRect bounds, long maxPixels)
    {
        int step = 1;
        while ((long)((bounds.Width + step - 1) / step) * ((bounds.Height + step - 1) / step) > maxPixels)
        {
            step++;
        }

        return step;
    }

    private static D2D_RECT_F Rectangle(PixelRect pixels) => new() { left = pixels.Left, top = pixels.Top, right = pixels.Right, bottom = pixels.Bottom };

    /// <summary>
    /// Paints the path (a fill, or a stroke whose pen is configured) with the shading at <paramref name="alpha"/>; with a Background
    /// (§8.7.4.3), the background and the shading are composited together, as one group, at that alpha.
    /// </summary>
    private void PaintWithBackground(ShadingCache.Entry entry, in DisplayShading shading, byte alpha, PathView path, FillRule rule, in StrokeStyle style, bool fill)
    {
        int mark = _clipCount;
        try
        {
            float opacity = alpha / 255f;
            if (shading.PaintsBackground && entry.Background is (byte r, byte g, byte b))
            {
                if (alpha != 255)
                {
                    var parameters = new D2D1_LAYER_PARAMETERS1
                    {
                        contentBounds = Interop.InfiniteRect,
                        geometricMask = null,
                        maskAntialiasMode = D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
                        maskTransform = Interop.Matrix(Matrix.Identity),
                        opacity = opacity,
                        opacityBrush = null,
                        layerOptions = D2D1_LAYER_OPTIONS1.D2D1_LAYER_OPTIONS1_NONE,
                    };
                    SetWorldTransform(Matrix.Identity);
                    _context->PushLayer(&parameters, null);
                    AddClip(new Clip(null, isLayer: true), _area);
                    opacity = 1;
                }

                var color = new D2D1_COLOR_F { r = r / 255f, g = g / 255f, b = b / 255f, a = 1 };
                _brush->SetColor(&color);
                PaintPath(path, rule, style, fill);
            }

            if (Paints(entry) && UseShading(entry, shading, opacity))
            {
                PaintPath(path, rule, style, fill);
            }
        }
        finally
        {
            EndPaint(mark);
        }
    }

    private void PaintPath(PathView path, FillRule rule, in StrokeStyle style, bool fill)
    {
        if (fill)
        {
            FillWith(path, rule);
        }
        else
        {
            StrokeWith(path, style);
        }
    }

    /// <summary>Pops the clips a paint pushed and goes back to the solid colour brush.</summary>
    private void EndPaint(int mark)
    {
        PopClips(mark);
        _paint = (ID2D1Brush*)_brush;
    }

    private ShadingCache.Entry? GetShading(in DisplayShading shading)
    {
        if (_shadings.Find(shading.Shading, shading.Conversion, shading.DefaultColorSpaces) is { } entry)
        {
            return entry;
        }

        ShadingCache.Entry? built = ShadingBuilder.Build(_document!, shading);
        if (built is null)
        {
            Skip(SkippedOperations.ShadingUnsupported, ShadingSkipped);
            return null;
        }

        _shadings.Add(shading.Shading, built);
        return built;
    }

    /// <summary>
    /// Makes the shading the current paint at <paramref name="opacity"/>, pushing the clips it needs (its BBox, §8.7.4.3; the ends a
    /// gradient does not extend; a raster's block). Returns false when it paints nothing.
    /// </summary>
    private bool UseShading(ShadingCache.Entry entry, in DisplayShading shading, float opacity)
    {
        Matrix toDevice = shading.Matrix * _device;
        if (!toDevice.TryInvert(out Matrix fromDevice) || !double.IsFinite(fromDevice.A + fromDevice.B + fromDevice.C + fromDevice.D + fromDevice.E + fromDevice.F))
        {
            return false;
        }

        if (shading.Shading.BoundingBox is PdfRectangle box)
        {
            PushRectangleIn(box, toDevice);
        }

        if (entry.Mesh is { } mesh)
        {
            return UseMesh(entry, mesh, toDevice, opacity);
        }

        if (entry.Gradient is { } gradient)
        {
            return gradient.Native ? UseGradient(entry, gradient, toDevice, fromDevice, opacity) : UseRadialRaster(entry, gradient, toDevice, shading.Shading.BoundingBox, opacity);
        }

        if (entry.Texture is not { } texture)
        {
            return false;
        }

        // §8.7.4.5.2: the function's domain, mapped by the shading's Matrix; the texture's first row is the domain's smaller y.
        PdfRectangle area = entry.TextureArea;
        Matrix textureToDevice = entry.TextureMatrix * toDevice;
        PushRectangleIn(area, textureToDevice);
        var placement = new Matrix(
            (area.Right - area.Left) / texture.Width,
            0,
            0,
            (area.Top - area.Bottom) / texture.Height,
            area.Left,
            area.Bottom);
        UseBitmap(texture.Bitmap(_context), placement * textureToDevice, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_CLAMP, opacity);
        return true;
    }

    /// <summary>Makes <paramref name="bitmap"/>, mapped to the device by <paramref name="toDevice"/>, the current paint.</summary>
    private void UseBitmap(ID2D1Bitmap* bitmap, in Matrix toDevice, D2D1_INTERPOLATION_MODE interpolation, D2D1_EXTEND_MODE extend, float opacity)
    {
        ID2D1BitmapBrush1* brush = _surface!.BitmapBrush;
        brush->SetBitmap(bitmap);
        brush->SetInterpolationMode1(interpolation);
        brush->SetExtendModeX(extend);
        brush->SetExtendModeY(extend);
        brush->SetOpacity(opacity);
        _paint = (ID2D1Brush*)brush;
        _paintToDevice = toDevice;
    }

    private PixelRect AreaPixels => new(0, 0, (int)_area.right, (int)_area.bottom);

    /// <summary>
    /// A mesh rasterized at the device's pixels over the block it covers within the area drawn, kept for the first transforms; later
    /// transforms draw a shading-space rasterization through the transform.
    /// </summary>
    private bool UseMesh(ShadingCache.Entry entry, MeshTriangles mesh, in Matrix toDevice, float opacity)
    {
        Skip(SkippedOperations.MeshRasterized, MeshRasterized);
        PixelRect bounds = Intersect(AreaPixels, toDevice, mesh.Extent);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        int step = Step(bounds, MeshRasterizer.MaxPixels);
        if (!entry.Admit(toDevice, bounds, out int slot))
        {
            return UseMeshTexture(entry, mesh, toDevice, opacity);
        }

        PixelImage? raster = entry.RasterAt(slot);
        if (raster is null)
        {
            raster = MeshRasterizer.Rasterize(mesh, toDevice, bounds, step);
            entry.SetRaster(slot, raster);
        }

        UseRaster(raster, bounds, step, opacity);
        return true;
    }

    private void UseRaster(PixelImage raster, PixelRect bounds, int step, float opacity)
    {
        PushRectangle(Rectangle(bounds));
        UseBitmap(
            raster.Bitmap(_context),
            new Matrix(step, 0, 0, step, bounds.Left, bounds.Top),
            step == 1 ? D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_NEAREST_NEIGHBOR : D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR,
            D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_CLAMP,
            opacity);
    }

    private bool UseMeshTexture(ShadingCache.Entry entry, MeshTriangles mesh, in Matrix toDevice, float opacity)
    {
        PdfRectangle extent = mesh.Extent;
        double left = Math.Min(extent.Left, extent.Right);
        double top = Math.Max(extent.Top, extent.Bottom);
        double width = Math.Abs(extent.Right - extent.Left);
        double height = Math.Abs(extent.Top - extent.Bottom);
        if (entry.Texture is null && entry.MeshTexture is null)
        {
            double scale = Math.Sqrt(Math.Abs(toDevice.Determinant));
            if (!(scale > 0) || !double.IsFinite(scale) || !(width > 0) || !(height > 0))
            {
                return false;
            }

            scale = Math.Min(scale, Math.Sqrt(MeshRasterizer.MaxPixels / (width * height)));
            int pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
            var toTexture = new Matrix(scale, 0, 0, -scale, -left * scale, top * scale);
            entry.MeshTexture = MeshRasterizer.Rasterize(mesh, toTexture, new PixelRect(0, 0, pixelWidth, pixelHeight), 1);
            entry.MeshTextureScale = scale;
        }

        PixelImage texture = entry.MeshTexture!;
        double side = 1 / entry.MeshTextureScale;
        PushRectangleIn(new PdfRectangle(left, top - (texture.Height * side), left + (texture.Width * side), top), toDevice);
        UseBitmap(texture.Bitmap(_context), new Matrix(side, 0, 0, -side, left, top) * toDevice, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_CLAMP, opacity);
        return true;
    }

    /// <summary>
    /// A radial whose circles are not nested, evaluated at the device's pixels of the area drawn (within its BBox), kept for the first
    /// transforms. Later transforms draw a shading-space evaluation over the BBox through the transform; without a BBox, the clip's
    /// pixels are evaluated for this paint alone.
    /// </summary>
    private bool UseRadialRaster(ShadingCache.Entry entry, GradientShape gradient, in Matrix toDevice, PdfRectangle? box, float opacity)
    {
        Skip(SkippedOperations.RadialRasterized, RadialRasterized);
        PixelRect bounds = box is PdfRectangle extent ? Intersect(AreaPixels, toDevice, extent) : AreaPixels;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        if (entry.Admit(toDevice, bounds, out int slot))
        {
            int step = Step(bounds, MeshRasterizer.MaxPixels);
            PixelImage? raster = entry.RasterAt(slot);
            if (raster is null)
            {
#pragma warning disable CA2000 // Kept by the entry.
                raster = GradientRasterizer.Rasterize(gradient, toDevice, bounds, step);
#pragma warning restore CA2000
                if (raster is null)
                {
                    return false;
                }

                entry.SetRaster(slot, raster);
            }

            UseRaster(raster, bounds, step, opacity);
            return true;
        }

        if (box is PdfRectangle area)
        {
            return UseRadialTexture(entry, gradient, area, toDevice, opacity);
        }

        D2D_RECT_F clip = ClipBounds;
        var pixels = new PixelRect((int)Math.Floor(clip.left), (int)Math.Floor(clip.top), (int)Math.Ceiling(clip.right), (int)Math.Ceiling(clip.bottom));
        if (pixels.Width <= 0 || pixels.Height <= 0)
        {
            return false;
        }

        int clipStep = Step(pixels, MeshRasterizer.MaxPixels);
        using PixelImage? once = GradientRasterizer.Rasterize(gradient, toDevice, pixels, clipStep);
        if (once is null)
        {
            return false;
        }

        // The brush holds its own reference to the bitmap.
        UseRaster(once, pixels, clipStep, opacity);
        return true;
    }

    /// <summary>A radial evaluated once in shading space over its BBox, at the scale of the first transform it met there, drawn through the transform.</summary>
    private bool UseRadialTexture(ShadingCache.Entry entry, GradientShape gradient, PdfRectangle box, in Matrix toDevice, float opacity)
    {
        double left = Math.Min(box.Left, box.Right);
        double top = Math.Max(box.Top, box.Bottom);
        double width = Math.Abs(box.Right - box.Left);
        double height = Math.Abs(box.Top - box.Bottom);
        if (entry.MeshTexture is null)
        {
            double scale = Math.Sqrt(Math.Abs(toDevice.Determinant));
            if (!(scale > 0) || !double.IsFinite(scale) || !(width > 0) || !(height > 0))
            {
                return false;
            }

            scale = Math.Min(scale, Math.Sqrt(MeshRasterizer.MaxPixels / (width * height)));
            int pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
            var toTexture = new Matrix(scale, 0, 0, -scale, -left * scale, top * scale);
#pragma warning disable CA2000 // Kept by the entry.
            if (GradientRasterizer.Rasterize(gradient, toTexture, new PixelRect(0, 0, pixelWidth, pixelHeight), 1) is not { } texture)
#pragma warning restore CA2000
            {
                return false;
            }

            entry.MeshTexture = texture;
            entry.MeshTextureScale = scale;
        }

        double side = 1 / entry.MeshTextureScale;
        UseBitmap(entry.MeshTexture.Bitmap(_context), new Matrix(side, 0, 0, -side, left, top) * toDevice, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_CLAMP, opacity);
        return true;
    }

    /// <summary>
    /// An axial or nested radial gradient as a Direct2D gradient brush in shading space; the ends it does not extend are clipped away
    /// (Direct2D's gradients always extend their end colours).
    /// </summary>
    private bool UseGradient(ShadingCache.Entry entry, GradientShape gradient, in Matrix toDevice, in Matrix fromDevice, float opacity)
    {
        if (entry.Brush is null)
        {
            entry.Brush = CreateGradientBrush(gradient);
        }

        // A distance in shading space beyond every pixel of the area drawn, for clips that stand for unbounded regions.
        double far = Math.Abs(gradient.EndRadius) + Math.Abs(gradient.StartRadius)
            + Distance(gradient.Start, gradient.End);
        far += Math.Max(
            Math.Max(Distance(gradient.Start, fromDevice.Transform(_area.left, _area.top)), Distance(gradient.Start, fromDevice.Transform(_area.right, _area.top))),
            Math.Max(Distance(gradient.Start, fromDevice.Transform(_area.left, _area.bottom)), Distance(gradient.Start, fromDevice.Transform(_area.right, _area.bottom))));
        far = (2 * far) + 1;
        if (!double.IsFinite(far))
        {
            return false;
        }

        if (!gradient.Radial)
        {
            if (!gradient.ExtendStart || !gradient.ExtendEnd)
            {
                PushAxialStrip(gradient, far, toDevice);
            }
        }
        else
        {
            PushRadialClip(gradient, far, toDevice);
        }

        entry.Brush->SetOpacity(opacity);
        _paint = entry.Brush;
        _paintToDevice = toDevice;
        return true;
    }

    private static double Distance(PathPoint a, PathPoint b) => Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));

    /// <summary>The smaller and larger circles of a nested radial, and whether the larger is the start circle.</summary>
    private static (PathPoint Small, double SmallRadius, PathPoint Large, double LargeRadius, bool Swapped) Circles(GradientShape gradient) =>
        gradient.EndRadius >= gradient.StartRadius
            ? (gradient.Start, gradient.StartRadius, gradient.End, gradient.EndRadius, false)
            : (gradient.End, gradient.EndRadius, gradient.Start, gradient.StartRadius, true);

    /// <summary>
    /// Creates the brush of a gradient. Axial: a linear brush from the start to the end point with the colours at evenly spaced stops.
    /// Nested radial: the circles are homothetic about a focus F inside the larger circle (where the radius reaches 0), and a point's
    /// s is its distance from F as a fraction of the distance to the larger circle along the same ray, so Direct2D's focal radial
    /// brush (centre and radius of the larger circle, origin at F) draws it with the stops from r_small / r_large to 1.
    /// </summary>
    private ID2D1Brush* CreateGradientBrush(GradientShape gradient)
    {
        const int count = ShadingBuilder.Stops;
        D2D1_GRADIENT_STOP* stops = stackalloc D2D1_GRADIENT_STOP[count];
        byte[] colors = gradient.Colors;
        double first = 0;
        bool reversed = false;
        if (gradient.Radial)
        {
            var circles = Circles(gradient);
            first = circles.SmallRadius / circles.LargeRadius;
            reversed = circles.Swapped;
        }

        for (int i = 0; i < count; i++)
        {
            int c = 3 * (reversed ? count - 1 - i : i);
            stops[i] = new D2D1_GRADIENT_STOP
            {
                position = (float)(first + (i / (double)(count - 1) * (1 - first))),
                color = new D2D1_COLOR_F { r = colors[c] / 255f, g = colors[c + 1] / 255f, b = colors[c + 2] / 255f, a = 1 },
            };
        }

        ID2D1GradientStopCollection1* collection;
        _context->CreateGradientStopCollection(
            stops,
            count,
            D2D1_COLOR_SPACE.D2D1_COLOR_SPACE_SRGB,
            D2D1_COLOR_SPACE.D2D1_COLOR_SPACE_SRGB,
            D2D1_BUFFER_PRECISION.D2D1_BUFFER_PRECISION_8BPC_UNORM,
            D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_CLAMP,
            D2D1_COLOR_INTERPOLATION_MODE.D2D1_COLOR_INTERPOLATION_MODE_STRAIGHT,
            &collection);
        try
        {
            if (!gradient.Radial)
            {
                var linear = new D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES { startPoint = Interop.Point(gradient.Start), endPoint = Interop.Point(gradient.End) };
                ID2D1LinearGradientBrush* brush;
                _context->CreateLinearGradientBrush(&linear, null, (ID2D1GradientStopCollection*)collection, &brush);
                return (ID2D1Brush*)brush;
            }

            var (small, smallRadius, large, largeRadius, _) = Circles(gradient);
            double k = smallRadius / (largeRadius - smallRadius);
            var focus = new PathPoint(small.X - (k * (large.X - small.X)), small.Y - (k * (large.Y - small.Y)));
            var radial = new D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES
            {
                center = Interop.Point(large),
                gradientOriginOffset = Interop.Point(focus.X - large.X, focus.Y - large.Y),
                radiusX = (float)largeRadius,
                radiusY = (float)largeRadius,
            };
            ID2D1RadialGradientBrush* radialBrush;
            _context->CreateRadialGradientBrush(&radial, null, (ID2D1GradientStopCollection*)collection, &radialBrush);
            return (ID2D1Brush*)radialBrush;
        }
        finally
        {
            collection->Release();
        }
    }

    /// <summary>Clips to the part of the plane an axial shading paints: the strip between the lines through its ends, a side open when extended.</summary>
    private void PushAxialStrip(GradientShape gradient, double far, in Matrix toDevice)
    {
        double length = Distance(gradient.Start, gradient.End);
        double ux = (gradient.End.X - gradient.Start.X) / length;
        double uy = (gradient.End.Y - gradient.Start.Y) / length;
        double nx = -uy * far;
        double ny = ux * far;
        double s = gradient.ExtendStart ? far : 0;
        double e = gradient.ExtendEnd ? far : 0;
        var s0 = new PathPoint(gradient.Start.X - (ux * s), gradient.Start.Y - (uy * s));
        var s1 = new PathPoint(gradient.End.X + (ux * e), gradient.End.Y + (uy * e));
        ID2D1PathGeometry* geometry = OpenGeometry(out ID2D1GeometrySink* sink);
        try
        {
            sink->BeginFigure(Interop.Point(s0.X + nx, s0.Y + ny), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);
            sink->AddLine(Interop.Point(s1.X + nx, s1.Y + ny));
            sink->AddLine(Interop.Point(s1.X - nx, s1.Y - ny));
            sink->AddLine(Interop.Point(s0.X - nx, s0.Y - ny));
            sink->EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED);
            sink->Close();
        }
        finally
        {
            sink->Release();
        }

        PushGeometry(geometry, toDevice, aliased: true);
    }

    /// <summary>
    /// Clips to the part of the plane a nested radial paints: s &lt; 0 lies inside the smaller circle when it is the start circle,
    /// s &gt; 1 outside the larger when it is the end circle (and the other way round when the start circle is the larger).
    /// </summary>
    private void PushRadialClip(GradientShape gradient, double far, in Matrix toDevice)
    {
        var (small, smallRadius, large, largeRadius, swapped) = Circles(gradient);
        bool excludeInside = swapped ? !gradient.ExtendEnd : !gradient.ExtendStart;
        bool excludeOutside = swapped ? !gradient.ExtendStart : !gradient.ExtendEnd;
        if (!(excludeInside && smallRadius > 0) && !excludeOutside)
        {
            return;
        }

        ID2D1PathGeometry* geometry = OpenGeometry(out ID2D1GeometrySink* sink);
        try
        {
            sink->SetFillMode(D2D1_FILL_MODE.D2D1_FILL_MODE_ALTERNATE);
            if (excludeOutside)
            {
                Circle(sink, large, largeRadius, MaxScale(toDevice));
            }
            else
            {
                sink->BeginFigure(Interop.Point(large.X - far, large.Y - far), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);
                sink->AddLine(Interop.Point(large.X + far, large.Y - far));
                sink->AddLine(Interop.Point(large.X + far, large.Y + far));
                sink->AddLine(Interop.Point(large.X - far, large.Y + far));
                sink->EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED);
            }

            if (excludeInside && smallRadius > 0)
            {
                Circle(sink, small, smallRadius, MaxScale(toDevice));
            }

            sink->Close();
        }
        finally
        {
            sink->Release();
        }

        PushGeometry(geometry, toDevice, aliased: true);
    }

    /// <summary>
    /// Writes a circle as a polygon whose chords stay within <see cref="CircleTolerance"/> device pixels of it (Direct2D flattens curves
    /// more coarsely), so that the aliased clip keeps the pixels whose centres the rasterizer finds inside.
    /// </summary>
    private static void Circle(ID2D1GeometrySink* sink, PathPoint c, double r, double scale)
    {
        double pixels = r * scale;
        int segments = pixels > CircleTolerance && double.IsFinite(pixels)
            ? (int)Math.Clamp(Math.Ceiling(Math.PI / Math.Acos(1 - (CircleTolerance / pixels))), 16, 8192)
            : 16;
        sink->BeginFigure(Interop.Point(c.X + r, c.Y), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);
        for (int i = 1; i < segments; i++)
        {
            double angle = 2 * Math.PI * i / segments;
            sink->AddLine(Interop.Point(c.X + (r * Math.Cos(angle)), c.Y + (r * Math.Sin(angle))));
        }

        sink->EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED);
    }

    /// <summary>Clips to <paramref name="box"/> mapped to the device by <paramref name="toDevice"/>: axis-aligned when it stays so.</summary>
    private void PushRectangleIn(PdfRectangle box, in Matrix toDevice)
    {
        PathPoint p0 = toDevice.Transform(box.Left, box.Bottom);
        PathPoint p1 = toDevice.Transform(box.Right, box.Bottom);
        PathPoint p2 = toDevice.Transform(box.Right, box.Top);
        PathPoint p3 = toDevice.Transform(box.Left, box.Top);
        PushQuad(p0, p1, p2, p3, toDevice.B == 0 && toDevice.C == 0);
    }

    /// <summary>Clips to the device quadrilateral p0 p1 p2 p3, anti-aliased; <paramref name="upright"/> when its sides follow the pixel axes.</summary>
    private void PushQuad(PathPoint p0, PathPoint p1, PathPoint p2, PathPoint p3, bool upright)
    {
        if (!double.IsFinite(p0.X + p0.Y + p1.X + p1.Y + p2.X + p2.Y + p3.X + p3.Y))
        {
            PushRectangle(default);
            return;
        }

        if (upright)
        {
            PushRectangle(new D2D_RECT_F
            {
                left = Interop.Point(Math.Min(p0.X, p2.X), 0).x,
                top = Interop.Point(0, Math.Min(p0.Y, p2.Y)).y,
                right = Interop.Point(Math.Max(p0.X, p2.X), 0).x,
                bottom = Interop.Point(0, Math.Max(p0.Y, p2.Y)).y,
            });
            return;
        }

        ID2D1PathGeometry* geometry = OpenGeometry(out ID2D1GeometrySink* sink);
        try
        {
            sink->BeginFigure(Interop.Point(p0), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);
            sink->AddLine(Interop.Point(p1));
            sink->AddLine(Interop.Point(p2));
            sink->AddLine(Interop.Point(p3));
            sink->EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED);
            sink->Close();
        }
        finally
        {
            sink->Release();
        }

        PushGeometry(geometry, Matrix.Identity);
    }
}
