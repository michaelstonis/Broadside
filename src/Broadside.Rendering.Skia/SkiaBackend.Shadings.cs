using Broadside.Diagnostics;
using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

public sealed partial class SkiaBackend
{
    /// <summary>
    /// Shadings (spec D20–D22) through their Skia form (<see cref="ShadingBuilder"/>), painted under the shading's transform: by
    /// <c>sh</c> within the clip, and as a shading pattern within the filled path or the stroke's outline, over its Background.
    /// </summary>
    private sealed partial class Replayer
    {
        private static readonly Diagnostic ShadingSkipped = new(
            "BackendShadingUnsupported",
            DiagnosticSeverity.Warning,
            "The Skia backend met a shading of a type it does not know (ISO 32000-2 §8.7.4.5); those paints were left out of this render.");

        // The paint of a shading or pattern, the paint of a Background, and the layers a stroke with a shading or pattern is drawn in.
        private readonly SKPaint _shade = new() { IsAntialias = true };
        private readonly SKPaint _backdrop = new() { IsAntialias = true };
        private readonly SKPaint _outer = new() { IsAntialias = true };
        private readonly SKPaint _inside = new() { IsAntialias = true, BlendMode = SKBlendMode.SrcIn };

        private ShadingCache Shadings => (_owner ?? this)._shadingCache!;

        /// <summary>Paints the shading within the current clip and its BBox, without its Background (ISO 32000-2 §8.7.4.2, §8.7.4.3).</summary>
        public void DrawShading(in DisplayShading shading)
        {
            if (_compositing.FillAlpha == 0 || GetShading(shading) is not { PaintsNothing: false } entry || entry.Gradient is null && entry.Mesh is null && entry.Texture is null)
            {
                return;
            }

            SKCanvas canvas = _canvas!;
            canvas.Save();
            Prepare(_shade, _compositing.FillAlpha, _compositing.BlendMode);
            PaintShading(entry, shading, _shade);
            canvas.Restore();
        }

        /// <summary>Fills the path with the shading pattern's shading over its Background (ISO 32000-2 §8.7.4.1, §8.7.4.3).</summary>
        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
        {
            if (path.IsEmpty || _compositing.FillAlpha == 0 || GetShading(shading) is not { PaintsNothing: false } entry)
            {
                return;
            }

            SKCanvas canvas = _canvas!;
            canvas.Save();
            ClipToPath(path, rule);
            SKColor? background = shading.PaintsBackground ? entry.Background : null;
            if (background is SKColor color && (_compositing.FillAlpha != 255 || _compositing.BlendMode != SKBlendMode.SrcOver))
            {
                // Background and shading composite as one paint: drawn opaque in a layer, the layer with the alpha and blend mode.
                Prepare(_outer, _compositing.FillAlpha, _compositing.BlendMode);
                canvas.SaveLayer(_outer);
                canvas.DrawColor(color, SKBlendMode.Src);
                Prepare(_shade, 255, SKBlendMode.SrcOver);
                PaintShading(entry, shading, _shade);
                canvas.Restore();
            }
            else
            {
                if (background is SKColor opaque)
                {
                    _backdrop.Color = opaque;
                    _backdrop.BlendMode = SKBlendMode.SrcOver;
                    canvas.DrawPaint(_backdrop);
                }

                Prepare(_shade, _compositing.FillAlpha, _compositing.BlendMode);
                PaintShading(entry, shading, _shade);
            }

            canvas.Restore();
        }

        /// <summary>Paints the area the stroke covers with the shading pattern's shading over its Background (ISO 32000-2 §8.7.4.1, §8.5.3.2).</summary>
        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
        {
            if (path.IsEmpty || _compositing.StrokeAlpha == 0 || GetShading(shading) is not { PaintsNothing: false } entry)
            {
                return;
            }

            if (BeginStrokeArea(path, style))
            {
                if (shading.PaintsBackground && entry.Background is SKColor background)
                {
                    _canvas!.DrawColor(background, SKBlendMode.Src);
                }

                Prepare(_shade, 255, SKBlendMode.SrcOver);
                PaintShading(entry, shading, _shade);
                EndStrokeArea();
            }
        }

        /// <summary>
        /// Opens the two layers a paint within a stroke's outline is drawn in: the outer one holds the stroke's coverage and carries the
        /// stroke alpha and blend mode, the inner one receives the paint and is kept only where the stroke covers (source-in); false,
        /// with nothing opened, when the stroke paints nothing.
        /// </summary>
        private bool BeginStrokeArea(PathView path, in StrokeStyle style)
        {
            SKCanvas canvas = _canvas!;
            Prepare(_outer, _compositing.StrokeAlpha, _compositing.BlendMode);
            canvas.SaveLayer(_outer);
            if (!StrokeOutline(path, style, SKColors.Black, SKBlendMode.SrcOver))
            {
                canvas.Restore();
                return false;
            }

            canvas.SaveLayer(_inside);
            return true;
        }

        private void EndStrokeArea()
        {
            _canvas!.Restore();
            _canvas.Restore();
        }

        /// <summary>Intersects the clip with the path in device space, anti-aliased (the area <see cref="FillPath"/> would fill).</summary>
        private void ClipToPath(PathView path, FillRule rule)
        {
            AddDevicePath(path);
            _path.FillType = rule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            _canvas!.SetMatrix(in _base);
            _canvas.ClipPath(_paths.Get(_path), SKClipOperation.Intersect, antialias: true);
        }

        private static void Prepare(SKPaint paint, byte alpha, SKBlendMode mode)
        {
            paint.Color = SKColors.Black.WithAlpha(alpha);
            paint.BlendMode = mode;
        }

        /// <summary>The Skia form of the shading, built on first use for its conversion; null for a type this backend does not know (reported).</summary>
        private ShadingCache.Entry? GetShading(in DisplayShading shading)
        {
            ShadingCache cache = Shadings;
            if (cache.Find(shading.Shading, shading.Conversion, shading.DefaultColorSpaces) is { } entry)
            {
                return entry;
            }

            ShadingCache.Entry? built = ShadingBuilder.Build(_document!, shading);
            if (built is null)
            {
                Report(256, ShadingSkipped);
                return null;
            }

            cache.Add(shading.Shading, built);
            return built;
        }

        /// <summary>Paints the shading over the current clip under its transform, clipped to its BBox (§8.7.4.3); the caller saves and restores.</summary>
        private void PaintShading(ShadingCache.Entry entry, in DisplayShading shading, SKPaint paint)
        {
            SKCanvas canvas = _canvas!;
            canvas.SetMatrix(in _base);
            SKMatrix toDevice = ToSkia(shading.Matrix * _device);
            canvas.Concat(in toDevice);
            if (shading.Shading.BoundingBox is PdfRectangle box)
            {
                canvas.ClipRect(new SKRect((float)box.Left, (float)box.Bottom, (float)box.Right, (float)box.Top).Standardized, SKClipOperation.Intersect, antialias: true);
            }

            if (entry.Gradient is SKShader gradient)
            {
                paint.Shader = gradient;
                canvas.DrawPaint(paint);
                paint.Shader = null;
            }
            else if (entry.Mesh is SKVertices mesh)
            {
                paint.Shader = entry.MeshTable;
                canvas.DrawVertices(mesh, SKBlendMode.Dst, paint);
                paint.Shader = null;
            }
            else if (entry.Texture is SKImage texture)
            {
                SKMatrix domain = entry.TextureMatrix;
                canvas.Concat(in domain);
                canvas.ClipRect(entry.TextureArea, SKClipOperation.Intersect, antialias: true);
                canvas.DrawImage(texture, entry.TextureArea, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
            }
        }

        private void DisposeShadings()
        {
            _shade.Dispose();
            _backdrop.Dispose();
            _outer.Dispose();
            _inside.Dispose();
        }
    }
}
