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
        /// of another size is drawn at its own resolution onto the image in a layer, so each is resampled from its own grid. With scaled
        /// image decode (issue #362) the image is converted at the reduction <see cref="DisplayImage.GetReduction"/> gives for the
        /// canvas, and a mask not folded at the reduction of its own rule.
        /// </summary>
        private void DrawImageCore(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
        {
            if (_compositing.FillAlpha == 0)
            {
                return;
            }

            ColorConversion rgb = conversion with { Target = DeviceColorModel.Rgb };
            PdfColorConverter? converter = null;
            int reduction = 0;
            if (image.MaxReduction > 0 && TryGetImageSamplesPerPixel(out double samplesPerPixel, out Matrix toPixels))
            {
                // A codec averages before colour conversion, which the converter caps where the two orders differ.
                converter = _document!.GetColorConverter(image.ColorSpace, rgb, defaultColorSpaces);
                if (converter.PaintsNothing)
                {
                    return;
                }

                reduction = image.GetReduction(toPixels, samplesPerPixel, converter);
            }

            ImageCache cache = Images;
            ImageCache.Entry entry = cache.Find(image);
            SKImage? pixels = entry.Matches(rgb, defaultColorSpaces, reduction) ? entry.Image : null;

            // An image kept encoded that cannot be decoded paints nothing (ISO 32000-2 §8.9.5; its reason is a diagnostic). Pixels kept
            // at a finer reduction than asked need no samples (issue #362: zooming out decodes nothing again).
            DisplayImageSamples? decoded = pixels is null || reduction == 0 ? image.GetSamplesAt(reduction) : null;
            if (decoded is null && (pixels is null || reduction == 0))
            {
                return;
            }

            bool owned = false;
            int delivered;
            bool folded;
            if (pixels is null)
            {
                DisplayImageSamples samples = decoded!.Value;
                converter ??= _document!.GetColorConverter(image.ColorSpace, rgb, defaultColorSpaces);
                if (converter.PaintsNothing)
                {
                    return;
                }

                PdfColorConverter? baseConverter = image.ColorSpace is PdfIndexedColorSpace indexed && !image.Matte.IsEmpty
                    ? _document!.GetColorConverter(indexed.Base, rgb, defaultColorSpaces)
                    : null;
                bool foldSoft = ImageConverter.FoldsSoftMask(image, samples, out DisplayImageSamples? softSamples);
                bool foldMask = !foldSoft && ImageConverter.FoldsMask(image, samples);
                pixels = ImageConverter.Convert(image, samples, foldSoft, softSamples, foldMask, converter, baseConverter);
                if (pixels is null)
                {
                    Report(128, ImageTooLarge);
                    return;
                }

                cache.Created++;
                cache.PixelsConverted += (long)pixels.Width * pixels.Height;
                delivered = samples.Reduction;
                folded = foldSoft || foldMask;
                owned = !cache.Keep(entry, pixels, (long)samples.Width * samples.Height * 4, rgb, defaultColorSpaces, delivered);
                entry.Folded = folded;
            }
            else
            {
                delivered = entry.Delivered;
                folded = entry.Folded;
            }

            try
            {
                object? maskKey = folded ? null : (object?)image.SoftMask ?? image.Mask;
                var grid = new SampleGrid(pixels.Width, pixels.Height, image.Width, image.Height, delivered);
                Prepare(_image, _compositing.FillAlpha, PaintBlend);
                if (maskKey is null)
                {
                    DrawSamples(pixels, entry, grid, image.Interpolate, _image, clipEdges: true);
                    return;
                }

                _canvas!.SaveLayer(_image);
                DrawSamples(pixels, entry, grid, image.Interpolate, _plain, clipEdges: true);
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

        /// <summary>
        /// Gets the fewest samples per device pixel a reduced image keeps (issue #362), and the transform from user space to the canvas's
        /// pixels; false when images are drawn at their own resolution (scaled image decode off, or a canvas with perspective).
        /// </summary>
        private bool TryGetImageSamplesPerPixel(out double samplesPerPixel, out Matrix toPixels)
        {
            samplesPerPixel = (_owner ?? this).ImageSamplesPerPixel;
            toPixels = default;
            if (samplesPerPixel <= 0 || _base.Persp0 != 0 || _base.Persp1 != 0 || _base.Persp2 != 1)
            {
                return false;
            }

            toPixels = _toDevice * new Matrix(_base.ScaleX, _base.SkewY, _base.SkewX, _base.ScaleY, _base.TransX, _base.TransY);
            return true;
        }

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
        /// Draws the samples of an image or mask into the unit square under the current transform. Reduced on both axes without
        /// rotation, they are box-filtered to the device pixels (<see cref="ImageReducer"/>, kept with the image) and the outline clipped
        /// with anti-aliasing unless <paramref name="clipEdges"/> is off (a mask applied in a layer whose image already has its edges);
        /// otherwise Skia samples them under the transform (where a pixel centre falls exactly on a sample boundary, Skia's float
        /// arithmetic may take the other sample than the rasterizer's double arithmetic). Reduced samples (issue #362) overhang the unit
        /// square when the reduction does not divide the image's size, and are clipped to it.
        /// </summary>
        private void DrawSamples(SKImage samples, ImageCache.Entry entry, in SampleGrid grid, bool interpolate, SKPaint paint, bool clipEdges)
        {
            SKCanvas canvas = _canvas!;
            Matrix toDevice = grid.ToUnitSquare * _toDevice;
            if (IsPixelAligned(out int tx, out int ty))
            {
                Matrix toCanvas = toDevice * Matrix.CreateTranslation(tx, ty);
                if (ImageReducer.Applies(grid.Width, grid.Height, toCanvas, out SKRectI pixels, out bool upright))
                {
                    SKImage? reduced = null;
                    bool owned = false;
                    if (entry.Admit(toCanvas, pixels, out int slot))
                    {
                        reduced = entry.ReductionAt(slot);
                        // Made for the slot's own transform, even when this one is it moved by whole pixels, so that the pixels never depend
                        // on whether the resampling was kept (the budget).
                        if (reduced is null && ImageReducer.Reduce(samples, entry.TransformAt(slot), entry.PixelsAt(slot), interpolate) is { } made)
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
                        if (clipEdges || grid.Overhangs)
                        {
                            ClipToOutline(toCanvas, grid.OutlineWidth, grid.OutlineHeight, upright);
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
            if (grid.Overhangs)
            {
                // The reduced grid's last column and row reach past the unit square (§8.9.4): clip at its edge.
                canvas.Save();
                canvas.ClipRect(new SKRect(0, 0, (float)grid.OutlineWidth, (float)grid.OutlineHeight), SKClipOperation.Intersect, antialias: true);
                canvas.DrawImage(samples, new SKRect(0, 0, grid.Width, grid.Height), sampling, paint);
                canvas.Restore();
                return;
            }

            canvas.DrawImage(samples, new SKRect(0, 0, grid.Width, grid.Height), sampling, paint);
        }

        /// <summary>Intersects the clip with the outline of the image's unit square on the canvas, anti-aliased like a fill.</summary>
        /// <param name="toCanvas">The transform from the sample grid to the canvas.</param>
        /// <param name="width">The unit square's width on the grid: the image's width in samples over the reduction.</param>
        /// <param name="height">The unit square's height on the grid.</param>
        /// <param name="upright">Whether the transform has neither rotation nor skew.</param>
        private void ClipToOutline(in Matrix toCanvas, double width, double height, bool upright)
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

        /// <summary>
        /// Draws a mask (a soft mask image or a stencil) as an alpha-only image over the unit square: at its own resolution, or with
        /// scaled image decode at the reduction of its own rule (issue #362; a stencil as 8-bit coverage).
        /// </summary>
        private void DrawMask(object mask, SKPaint paint, bool clipEdges)
        {
            var soft = mask as DisplayImage;
            var stencil = mask as DisplayImageMask;
            (int width, int height, bool interpolate) = soft is not null ? (soft.Width, soft.Height, soft.Interpolate) : (stencil!.Width, stencil.Height, stencil.Interpolate);
            int reduction = 0;
            if (TryGetImageSamplesPerPixel(out double samplesPerPixel, out Matrix toPixels))
            {
                reduction = soft is not null ? soft.GetReduction(toPixels, samplesPerPixel, null) : stencil!.GetReduction(toPixels, samplesPerPixel);
            }

            ImageCache cache = Images;
            ImageCache.Entry entry = cache.Find(mask);
            SKImage? alpha = entry.Delivered <= reduction ? entry.Image : null;
            bool owned = false;
            int delivered = entry.Delivered;
            if (alpha is null)
            {
                if (soft is null)
                {
                    delivered = reduction;
                    alpha = ImageConverter.ConvertStencil(stencil!, reduction);
                }
                else if (reduction > 0 && soft.GetSamplesAt(reduction) is { } samples)
                {
                    delivered = samples.Reduction;
                    alpha = ImageConverter.ConvertSoftMask(soft, samples);
                }
                else
                {
                    delivered = 0;
                    alpha = ImageConverter.ConvertSoftMask(soft);
                }

                if (alpha is null)
                {
                    Report(128, ImageTooLarge);
                    return;
                }

                cache.Created++;
                cache.PixelsConverted += (long)alpha.Width * alpha.Height;
                owned = !cache.Keep(entry, alpha, (long)alpha.Width * alpha.Height, default, null, delivered);
            }

            try
            {
                // The mask's samples map onto the unit square at their own resolution (§8.9.6.2, §8.9.6.3, §11.6.5.2).
                DrawSamples(alpha, entry, new SampleGrid(alpha.Width, alpha.Height, width, height, delivered), interpolate, paint, clipEdges);
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

        /// <summary>
        /// The grid of samples drawn: <see cref="Width"/> × <see cref="Height"/> samples of an image of <see cref="SourceWidth"/> ×
        /// <see cref="SourceHeight"/>, reduced by 2^<see cref="Reduction"/> (issue #362). Reduced sample i covers source samples i·s to
        /// (i + 1)·s (ISO 32000-2 §8.9.4), so the grid maps to the unit square through s, then [1/W 0 0 −1/H 0 1], and overhangs it
        /// when s does not divide the size.
        /// </summary>
        private readonly record struct SampleGrid(int Width, int Height, int SourceWidth, int SourceHeight, int Reduction)
        {
            /// <summary>Gets the transform from the grid (y down from the first row) to the unit square.</summary>
            public Matrix ToUnitSquare => Reduction == 0
                ? new Matrix(1.0 / Width, 0, 0, -1.0 / Height, 0, 1)
                : new Matrix((double)(1 << Reduction) / SourceWidth, 0, 0, -(double)(1 << Reduction) / SourceHeight, 0, 1);

            /// <summary>Gets the unit square's width on the grid.</summary>
            public double OutlineWidth => Reduction == 0 ? Width : (double)SourceWidth / (1 << Reduction);

            /// <summary>Gets the unit square's height on the grid.</summary>
            public double OutlineHeight => Reduction == 0 ? Height : (double)SourceHeight / (1 << Reduction);

            /// <summary>Gets a value indicating whether the grid reaches past the unit square.</summary>
            public bool Overhangs => Reduction > 0 && (OutlineWidth != Width || OutlineHeight != Height);
        }
    }
}
