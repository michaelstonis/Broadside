using Broadside.Diagnostics;
using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

public sealed partial class SkiaBackend
{
    /// <summary>
    /// Images and stencil masks (spec D15, D16): each interned display-list image becomes one Skia image per backend, drawn into the
    /// unit square under the current transform with the sampling its scale and Interpolate entry ask for.
    /// </summary>
    private sealed partial class Replayer
    {
        private static readonly Diagnostic ImageTooLarge = new(
            "BackendImageTooLarge",
            DiagnosticSeverity.Warning,
            "An image or mask has more pixels than the Skia backend can convert in memory (ISO 32000-2 §8.9.5); it was left out of this render.");

        private readonly SKPaint _image = new() { IsAntialias = true };
        private readonly SKPaint _mask = new() { IsAntialias = true };
        private readonly SKPaint _plain = new() { IsAntialias = true };
        private readonly SKPaint _maskIn = new() { IsAntialias = true, BlendMode = SKBlendMode.DstIn, Color = SKColors.Black };

        private ImageCache Images => (_owner ?? this)._imageCache!;

        /// <summary>
        /// Draws an image (ISO 32000-2 §8.9.4, §8.9.5): image space maps to the unit square through [1/w 0 0 −1/h 0 1], then the
        /// current transform. A mask of the image's size is folded into the pixels; a soft mask (§11.6.5.2) or explicit mask (§8.9.6.3)
        /// of another size is drawn at its own resolution onto the image in a layer, so each is resampled from its own grid.
        /// </summary>
        private void DrawImageCore(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
        {
            if (_compositing.FillAlpha == 0)
            {
                return;
            }

            ImageCache cache = Images;
            ImageCache.Entry entry = cache.Find(image);
            ColorConversion rgb = conversion with { Target = DeviceColorModel.Rgb };
            SKImage? pixels = entry.Matches(rgb, defaultColorSpaces) ? entry.Image : null;
            bool owned = false;
            if (pixels is null)
            {
                PdfColorConverter converter = _document!.GetColorConverter(image.ColorSpace, rgb, defaultColorSpaces);
                if (converter.PaintsNothing)
                {
                    return;
                }

                PdfColorConverter? baseConverter = image.ColorSpace is PdfIndexedColorSpace indexed && !image.Matte.IsEmpty
                    ? _document.GetColorConverter(indexed.Base, rgb, defaultColorSpaces)
                    : null;
                pixels = ImageConverter.Convert(image, converter, baseConverter);
                if (pixels is null)
                {
                    Report(128, ImageTooLarge);
                    return;
                }

                cache.Created++;
                owned = !cache.Keep(entry, pixels, (long)image.Width * image.Height * 4, rgb, defaultColorSpaces);
            }

            try
            {
                object? maskKey = SeparateMask(image);
                Prepare(_image, _compositing.FillAlpha, PaintBlend);
                if (maskKey is null)
                {
                    DrawSamples(pixels, entry, image.Width, image.Height, image.Interpolate, _image, clipEdges: true);
                    return;
                }

                _canvas!.SaveLayer(_image);
                DrawSamples(pixels, entry, image.Width, image.Height, image.Interpolate, _plain, clipEdges: true);
                DrawMask(maskKey, _maskIn, clipEdges: false);
                _canvas.Restore();
            }
            finally
            {
                if (owned)
                {
                    pixels.Dispose();
                }
            }
        }

        /// <summary>
        /// Fills a stencil mask with the colour (ISO 32000-2 §8.9.6.2): the mask maps onto the unit square like an image and is drawn
        /// as an alpha-only image, which Skia paints in the paint's colour.
        /// </summary>
        private void FillImageMaskCore(DisplayImageMask mask, in DisplayColor color)
        {
            if (_compositing.FillAlpha == 0 || !TryGetColor(color, out SKColor paint))
            {
                return;
            }

            _mask.Color = paint.WithAlpha(_compositing.FillAlpha);
            _mask.BlendMode = PaintBlend;
            DrawMask(mask, _mask, clipEdges: true);
        }

        /// <summary>The image's soft mask, else its explicit mask, when it is not folded into the pixels (another size); otherwise null.</summary>
        private static object? SeparateMask(DisplayImage image)
        {
            if (image.SoftMask is { } soft)
            {
                return ImageConverter.FoldsSoftMask(image) ? null : soft;
            }

            if (image.Mask is { } mask)
            {
                return ImageConverter.FoldsMask(image) ? null : mask;
            }

            return null;
        }

        private static Matrix UnitSquare(int width, int height) => new(1.0 / width, 0, 0, -1.0 / height, 0, 1);

        /// <summary>
        /// The sampling of an image Skia resamples itself: reduced on either axis (rotated, or enlarged on the other axis), a
        /// mipmapped bilinear filter that averages the samples under a pixel; enlarged, square samples unless the image asks for
        /// interpolation (§8.9.5.3).
        /// </summary>
        private static SKSamplingOptions Sampling(in Matrix imageToDevice, bool interpolate)
        {
            double x = Math.Sqrt((imageToDevice.A * imageToDevice.A) + (imageToDevice.B * imageToDevice.B));
            double y = Math.Sqrt((imageToDevice.C * imageToDevice.C) + (imageToDevice.D * imageToDevice.D));
            if (x < 1 || y < 1)
            {
                return new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
            }

            return interpolate ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None) : new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
        }

        /// <summary>Whether the caller's canvas transform is a whole-pixel translation, so that device pixels are canvas pixels.</summary>
        private bool IsPixelAligned(out int x, out int y)
        {
            x = (int)_base.TransX;
            y = (int)_base.TransY;
            return _base.ScaleX == 1 && _base.ScaleY == 1 && _base.SkewX == 0 && _base.SkewY == 0 && _base.Persp0 == 0 && _base.Persp1 == 0
                && _base.Persp2 == 1 && _base.TransX == x && _base.TransY == y;
        }

        /// <summary>
        /// Draws an image or mask of <paramref name="width"/> × <paramref name="height"/> samples into the unit square under the
        /// current transform. Reduced on both axes without rotation, it is box-filtered to the device pixels (<see cref="ImageReducer"/>,
        /// kept with the image) and its outline clipped with anti-aliasing unless <paramref name="clipEdges"/> is off (a mask applied in
        /// a layer whose image already has its edges); otherwise Skia samples it under the transform (where a pixel centre falls
        /// exactly on a sample boundary, Skia's float arithmetic may take the other sample than the rasterizer's double arithmetic).
        /// </summary>
        private void DrawSamples(SKImage samples, ImageCache.Entry entry, int width, int height, bool interpolate, SKPaint paint, bool clipEdges)
        {
            SKCanvas canvas = _canvas!;
            Matrix toDevice = UnitSquare(width, height) * _toDevice;
            if (IsPixelAligned(out int tx, out int ty))
            {
                Matrix toCanvas = toDevice * Matrix.CreateTranslation(tx, ty);
                if (ImageReducer.Applies(width, height, toCanvas, out SKRectI pixels, out bool upright))
                {
                    SKImage? reduced = null;
                    bool owned = false;
                    if (entry.Admit(toCanvas, out int slot))
                    {
                        reduced = entry.ReductionAt(slot);
                        if (reduced is null && ImageReducer.Reduce(samples, toCanvas, pixels, interpolate) is { } made)
                        {
                            reduced = made;
                            long bytes = (long)pixels.Width * pixels.Height * (samples.ColorType == SKColorType.Alpha8 ? 1 : 4);
                            owned = !Images.KeepReduction(entry, slot, made, bytes);
                        }
                    }

                    if (reduced is not null)
                    {
                        canvas.Save();
                        canvas.ResetMatrix();
                        if (clipEdges)
                        {
                            ClipToOutline(toCanvas, width, height, upright);
                        }

                        canvas.DrawImage(reduced, pixels.Left, pixels.Top, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), paint);
                        canvas.Restore();
                        if (owned)
                        {
                            reduced.Dispose();
                        }

                        return;
                    }
                }
            }

            SKSamplingOptions sampling = Sampling(toDevice, interpolate);
            SKMatrix matrix = ToSkia(toDevice);
            canvas.SetMatrix(in _base);
            canvas.Concat(in matrix);
            canvas.DrawImage(samples, new SKRect(0, 0, width, height), sampling, paint);
        }

        /// <summary>Intersects the clip with the outline of the image's samples on the canvas (its unit square), anti-aliased like a fill.</summary>
        private void ClipToOutline(in Matrix toCanvas, int width, int height, bool upright)
        {
            SKCanvas canvas = _canvas!;
            PathPoint p0 = toCanvas.Transform(0, 0);
            PathPoint p2 = toCanvas.Transform(width, height);
            if (upright)
            {
                canvas.ClipRect(new SKRect((float)p0.X, (float)p0.Y, (float)p2.X, (float)p2.Y).Standardized, SKClipOperation.Intersect, antialias: true);
                return;
            }

            PathPoint p1 = toCanvas.Transform(width, 0);
            PathPoint p3 = toCanvas.Transform(0, height);
            _path.Clear();
            _path.FillType = SKPathFillType.Winding;
            _path.MoveTo(new SKPoint((float)p0.X, (float)p0.Y));
            _path.LineTo(new SKPoint((float)p1.X, (float)p1.Y));
            _path.LineTo(new SKPoint((float)p2.X, (float)p2.Y));
            _path.LineTo(new SKPoint((float)p3.X, (float)p3.Y));
            _path.Close();
            canvas.ClipPath(_paths.Get(_path), SKClipOperation.Intersect, antialias: true);
        }

        /// <summary>Draws a mask (a soft mask image or a stencil) as an alpha-only image over the unit square.</summary>
        private void DrawMask(object mask, SKPaint paint, bool clipEdges)
        {
            ImageCache cache = Images;
            ImageCache.Entry entry = cache.Find(mask);
            SKImage? alpha = entry.Image;
            bool owned = false;
            int width;
            int height;
            bool interpolate;
            if (mask is DisplayImage soft)
            {
                (width, height, interpolate) = (soft.Width, soft.Height, soft.Interpolate);
            }
            else
            {
                var stencil = (DisplayImageMask)mask;
                (width, height, interpolate) = (stencil.Width, stencil.Height, stencil.Interpolate);
            }

            if (alpha is null)
            {
                alpha = mask is DisplayImage image ? ImageConverter.ConvertSoftMask(image) : ImageConverter.ConvertStencil((DisplayImageMask)mask);
                if (alpha is null)
                {
                    Report(128, ImageTooLarge);
                    return;
                }

                cache.Created++;
                owned = !cache.Keep(entry, alpha, (long)width * height, default, null);
            }

            try
            {
                // The mask's samples map onto the unit square at their own resolution (§8.9.6.2, §8.9.6.3, §11.6.5.2).
                DrawSamples(alpha, entry, width, height, interpolate, paint, clipEdges);
            }
            finally
            {
                if (owned)
                {
                    alpha.Dispose();
                }
            }
        }

        private void DisposeImages()
        {
            _image.Dispose();
            _mask.Dispose();
            _plain.Dispose();
            _maskIn.Dispose();
        }
    }
}
