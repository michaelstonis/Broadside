using Broadside.Diagnostics;
using Broadside.Graphics;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics;

/// <summary>
/// Shadings (ISO 32000-2 §8.7.4, spec D20–D22) through their CoreGraphics form (<see cref="ShadingBuilder"/>), painted under the
/// shading's transform: by <c>sh</c> within the clip, and as a shading pattern within the filled path or the stroke's outline, over
/// its Background.
/// </summary>
internal sealed partial class CoreGraphicsBackend
{
    private static readonly Diagnostic ShadingSkipped = new(
        "BackendShadingUnsupported",
        DiagnosticSeverity.Warning,
        "The CoreGraphics backend met a shading of a type it does not know (ISO 32000-2 §8.7.4.5); those paints were left out of this render.");

    private static readonly Diagnostic MeshRasterized = new(
        "BackendMeshShadingRasterized",
        DiagnosticSeverity.Information,
        "CoreGraphics has no Gouraud-shaded triangles; the page's mesh shadings (ISO 32000-2 §8.7.4.5.5 to §8.7.4.5.8) were " +
        "rasterized by the backend at the device's pixels, without anti-aliasing at the mesh's outer edges, and drawn as images.");

    private readonly nfloat[] _background = new nfloat[4];
    private readonly ShadingCache _shadings;

    /// <summary>The device pixels the replay's initial clip covers, which bound a mesh's rasterization.</summary>
    private PixelRect _deviceBounds;

    /// <summary>Paints the shading within the current clip and its BBox, without its Background (ISO 32000-2 §8.7.4.2, §8.7.4.3).</summary>
    public void DrawShading(in DisplayShading shading)
    {
        if (_compositing.FillAlpha == 0 || GetShading(shading) is not { PaintsNothing: false } entry || entry.Gradient is null && entry.Mesh is null && entry.Texture is null)
        {
            return;
        }

        CGContext context = _context!;
        context.SaveState();
        context.SetAlpha((nfloat)(_compositing.FillAlpha / 255.0));
        PaintShading(entry, shading);
        context.RestoreState();
    }

    /// <summary>Fills the path with the shading pattern's shading over its Background (ISO 32000-2 §8.7.4.1, §8.7.4.3).</summary>
    public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
    {
        if (path.IsEmpty || _compositing.FillAlpha == 0 || GetShading(shading) is not { PaintsNothing: false } entry)
        {
            return;
        }

        CGContext context = _context!;
        context.SaveState();
        ClipToPath(context, path, rule);
        PaintWithBackground(entry, shading, _compositing.FillAlpha);
        context.RestoreState();
    }

    /// <summary>Paints the area the stroke covers with the shading pattern's shading over its Background (ISO 32000-2 §8.7.4.1, §8.5.3.2).</summary>
    public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
    {
        if (path.IsEmpty || _compositing.StrokeAlpha == 0 || GetShading(shading) is not { PaintsNothing: false } entry)
        {
            return;
        }

        CGContext context = _context!;
        context.SaveState();
        if (ClipToStroke(context, path, style))
        {
            PaintWithBackground(entry, shading, _compositing.StrokeAlpha);
        }

        context.RestoreState();
    }

    /// <summary>
    /// Paints the Background (when the paint asks for it) and the shading as one paint with <paramref name="alpha"/>: in a
    /// transparency layer composited with the alpha and blend mode when both are drawn and either is not plain.
    /// </summary>
    private void PaintWithBackground(ShadingCache.Entry entry, in DisplayShading shading, byte alpha)
    {
        CGContext context = _context!;
        context.SetAlpha((nfloat)(alpha / 255.0));
        if (!shading.PaintsBackground || entry.Background is not (byte r, byte g, byte b))
        {
            PaintShading(entry, shading);
            return;
        }

        bool layered = alpha != 255 || _compositing.BlendMode != BlendMode.Normal;
        CGRect area = context.GetClipBoundingBox();
        if (layered)
        {
            context.BeginTransparencyLayer(area, null);
        }

        _background[0] = (nfloat)(r / 255.0);
        _background[1] = (nfloat)(g / 255.0);
        _background[2] = (nfloat)(b / 255.0);
        _background[3] = 1;
        context.SetFillColor(_background);
        context.FillRect(area);
        PaintShading(entry, shading);
        if (layered)
        {
            context.EndTransparencyLayer();
        }
    }

    /// <summary>Intersects the clip with the path (the area <see cref="FillPath"/> would fill), anti-aliased.</summary>
    private void ClipToPath(CGContext context, PathView path, FillRule rule)
    {
        context.BeginPath();
        if (!AddMappedPath(context, path))
        {
            context.ClipToRect(CGRect.Empty);
        }
        else if (rule == FillRule.EvenOdd)
        {
            context.EOClip();
        }
        else
        {
            context.Clip();
        }
    }

    /// <summary>Intersects the clip with the area the stroke covers; false, with the clip emptied, when the stroke paints nothing.</summary>
    private bool ClipToStroke(CGContext context, PathView path, in StrokeStyle style)
    {
        context.BeginPath();
        if (!_stroker.Outline(context, path, style, _toDevice))
        {
            context.ClipToRect(CGRect.Empty);
            return false;
        }

        context.Clip();
        return true;
    }

    /// <summary>The CoreGraphics form of the shading, built on first use for its conversion; null for a type this backend does not know (reported).</summary>
    private ShadingCache.Entry? GetShading(in DisplayShading shading)
    {
        if (_shadings.Find(shading.Shading, shading.Conversion, shading.DefaultColorSpaces) is { } entry)
        {
            return entry;
        }

        ShadingCache.Entry? built = ShadingBuilder.Build(_document!, shading);
        if (built is null)
        {
            Report(ReportedKinds.ShadingUnsupported, ShadingSkipped);
            return null;
        }

        _shadings.Add(shading.Shading, built);
        return built;
    }

    /// <summary>Paints the shading over the current clip under its transform, clipped to its BBox (§8.7.4.3); the caller saves and restores.</summary>
    private void PaintShading(ShadingCache.Entry entry, in DisplayShading shading)
    {
        CGContext context = _context!;
        Matrix toTarget = shading.Matrix * _device;
        if (entry.Mesh is { } mesh)
        {
            PaintMesh(entry, mesh, shading, toTarget);
            return;
        }

        Concat(context, toTarget);
        if (shading.Shading.BoundingBox is PdfRectangle box)
        {
            context.ClipToRect(Rectangle(box));
        }

        if (entry.Gradient is { } gradient)
        {
            context.DrawShading(gradient);
        }
        else if (entry.Texture is { } texture)
        {
            PdfRectangle area = entry.TextureArea;
            Concat(context, entry.TextureMatrix);
            context.ClipToRect(Rectangle(area));
            double width = area.Right - area.Left;
            double height = area.Top - area.Bottom;

            // The texture's first row is the domain's smaller y.
            Concat(context, new Matrix(width, 0, 0, -height, area.Left, area.Bottom + height));
            context.InterpolationQuality = CGInterpolationQuality.Low;
            context.DrawImage(new CGRect(0, 0, 1, 1), texture.Image);
        }
    }

    /// <summary>
    /// Paints a mesh as its rasterization at the device's pixels (<see cref="MeshRasterizer"/>), kept with the shading for the first
    /// transforms it is painted at, placed on the pixels one to one and clipped to the BBox.
    /// </summary>
    private void PaintMesh(ShadingCache.Entry entry, MeshTriangles mesh, in DisplayShading shading, in Matrix toTarget)
    {
        Report(ReportedKinds.MeshRasterized, MeshRasterized);
        if (!_baseInvertible || _deviceBounds.Width <= 0 || _deviceBounds.Height <= 0)
        {
            return;
        }

        CGContext context = _context!;
        Matrix toPixels = toTarget * _pixels;
        if (shading.Shading.BoundingBox is PdfRectangle box)
        {
            Concat(context, toTarget);
            context.ClipToRect(Rectangle(box));
            if (toTarget.TryInvert(out Matrix back))
            {
                Concat(context, back);
            }
        }

        PixelRect bounds = Intersect(_deviceBounds, toPixels, mesh.Extent);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        int step = 1;
        while ((long)((bounds.Width + step - 1) / step) * ((bounds.Height + step - 1) / step) > MeshRasterizer.MaxPixels)
        {
            step++;
        }

        if (!entry.Admit(toPixels, bounds, out int slot))
        {
            PaintMeshTexture(entry, mesh, toTarget, toPixels);
            return;
        }

        PixelImage? raster = entry.RasterAt(slot);
        if (raster is null)
        {
            raster = MeshRasterizer.Rasterize(mesh, toPixels, bounds, step);
            entry.SetRaster(slot, raster);
        }

        int width = raster.Width * step;
        int height = raster.Height * step;
        Concat(context, _fromPixels);
        Concat(context, new Matrix(width, 0, 0, -height, bounds.Left, bounds.Top + height));
        context.InterpolationQuality = step == 1 ? CGInterpolationQuality.None : CGInterpolationQuality.Low;
        context.DrawImage(new CGRect(0, 0, 1, 1), raster.Image);
    }

    /// <summary>
    /// Paints a mesh at a transform beyond those it is rasterized for at the device: through one rasterization in shading space, at
    /// the scale of the first such transform, which CoreGraphics resamples under each (as an image painted at many positions is).
    /// </summary>
    private void PaintMeshTexture(ShadingCache.Entry entry, MeshTriangles mesh, in Matrix toTarget, in Matrix toPixels)
    {
        PdfRectangle extent = mesh.Extent;
        double left = Math.Min(extent.Left, extent.Right);
        double top = Math.Max(extent.Top, extent.Bottom);
        double width = Math.Abs(extent.Right - extent.Left);
        double height = Math.Abs(extent.Top - extent.Bottom);
        if (entry.Texture is null && entry.MeshTexture is null)
        {
            double scale = Math.Sqrt(Math.Abs(toPixels.Determinant));
            if (!(scale > 0) || !double.IsFinite(scale) || !(width > 0) || !(height > 0))
            {
                return;
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
        CGContext context = _context!;
        Concat(context, toTarget);
        Concat(context, new Matrix(texture.Width * side, 0, 0, texture.Height * side, left, top - (texture.Height * side)));
        context.InterpolationQuality = CGInterpolationQuality.Low;
        context.DrawImage(new CGRect(0, 0, 1, 1), texture.Image);
    }

    /// <summary>Reads the device pixels the context's clip covers at the start of a replay.</summary>
    private void BeginShadings(CGContext context)
    {
        CGRect clip = context.GetClipBoundingBox();
        if (!_baseInvertible || clip.IsInfinite() || clip.IsNull() || clip.IsEmpty)
        {
            _deviceBounds = default;
            return;
        }

        PathPoint a = _pixels.Transform(clip.Left, clip.Top);
        PathPoint b = _pixels.Transform(clip.Right, clip.Top);
        PathPoint c = _pixels.Transform(clip.Right, clip.Bottom);
        PathPoint d = _pixels.Transform(clip.Left, clip.Bottom);
        double left = Math.Floor(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)));
        double right = Math.Ceiling(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)));
        double top = Math.Floor(Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)));
        double bottom = Math.Ceiling(Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
        const double limit = 1 << 24;
        _deviceBounds = new PixelRect(
            (int)Math.Clamp(left, -limit, limit),
            (int)Math.Clamp(top, -limit, limit),
            (int)Math.Clamp(right, -limit, limit),
            (int)Math.Clamp(bottom, -limit, limit));
    }

    /// <summary>The pixels of <paramref name="pixels"/> that the image of <paramref name="extent"/> under <paramref name="m"/> touches.</summary>
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

    private static CGRect Rectangle(PdfRectangle box) =>
        new(Math.Min(box.Left, box.Right), Math.Min(box.Bottom, box.Top), Math.Abs(box.Right - box.Left), Math.Abs(box.Top - box.Bottom));
}
