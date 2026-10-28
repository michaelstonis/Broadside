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

        private SharedImageCache<ImageCache.Entry>? Shared => (_owner ?? this)._shared;

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

            if (Shared is { } shared)
            {
                DrawSharedImage(shared, image, defaultColorSpaces, rgb, converter, reduction);
                return;
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

                pixels = ConvertImage(image, samples, rgb, defaultColorSpaces, converter, out folded);
                if (pixels is null)
                {
                    return;
                }

                delivered = samples.Reduction;
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
                DrawConverted(image, pixels, entry, delivered, folded);
            }
            finally
            {
                if (owned)
                {
                    pixels.Dispose();
                }
            }
        }

        /// <summary>Draws an image's converted pixels, with its mask when it is not folded into them.</summary>
        private void DrawConverted(DisplayImage image, SKImage pixels, ImageCache.Entry entry, int delivered, bool folded)
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

        /// <summary>
        /// Draws an image from the cache the backends drawing its document share (issue #432): its conversion is leased while drawn,
        /// and made by one backend at a time, under its key's lock, when no matching one is kept (a finer one serves, as in the
        /// backend's own cache); the others wait and draw it.
        /// </summary>
        private void DrawSharedImage(SharedImageCache<ImageCache.Entry> shared, DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion rgb, PdfColorConverter? converter, int reduction)
        {
            SharedImageCache<ImageCache.Entry>.Slot slot = shared.SlotOf(image);
            ImageCache.Entry? entry = LeaseMatching(shared, slot, rgb, defaultColorSpaces, reduction);
            if (entry is null)
            {
                lock (slot.Converting)
                {
                    entry = LeaseMatching(shared, slot, rgb, defaultColorSpaces, reduction);
                    if (entry is null)
                    {
                        if (image.GetSamplesAt(reduction) is not { } samples)
                        {
                            return;
                        }

                        converter ??= _document!.GetColorConverter(image.ColorSpace, rgb, defaultColorSpaces);
                        if (converter.PaintsNothing || ConvertImage(image, samples, rgb, defaultColorSpaces, converter, out bool folded) is not { } pixels)
                        {
                            return;
                        }

                        entry = new ImageCache.Entry
                        {
                            Image = pixels,
                            Bytes = (long)samples.Width * samples.Height * 4,
                            Conversion = rgb,
                            Defaults = defaultColorSpaces,
                            Delivered = samples.Reduction,
                            Folded = folded,
                            Gate = new Lock(),
                        };
                        shared.Publish(slot, entry, entry.Bytes);
                    }
                }
            }
            else if (reduction == 0 && image.GetSamplesAt(0) is null)
            {
                // As with the backend's own cache: an image that cannot be decoded paints nothing (§8.9.5).
                shared.Release(entry);
                return;
            }

            try
            {
                DrawConverted(image, entry.Image!, entry, entry.Delivered, entry.Folded);
            }
            finally
            {
                shared.Release(entry);
            }
        }

        /// <summary>Leases the slot's image when it was converted this way at <paramref name="reduction"/> or finer; else null.</summary>
        private static ImageCache.Entry? LeaseMatching(SharedImageCache<ImageCache.Entry> shared, SharedImageCache<ImageCache.Entry>.Slot slot, ColorConversion rgb, PdfDefaultColorSpaces? defaults, int reduction)
        {
            ImageCache.Entry? entry = shared.Lease(slot);
            if (entry is not null && !entry.Matches(rgb, defaults, reduction))
            {
                shared.Release(entry);
                return null;
            }

            return entry;
        }

        /// <summary>Converts an image's samples to a Skia image, its mask folded in when it has their size; null (reported) when too large.</summary>
        private SKImage? ConvertImage(DisplayImage image, DisplayImageSamples samples, ColorConversion rgb, PdfDefaultColorSpaces defaultColorSpaces, PdfColorConverter converter, out bool folded)
        {
            PdfColorConverter? baseConverter = image.ColorSpace is PdfIndexedColorSpace indexed && !image.Matte.IsEmpty
                ? _document!.GetColorConverter(indexed.Base, rgb, defaultColorSpaces)
                : null;
            bool foldSoft = ImageConverter.FoldsSoftMask(image, samples, out DisplayImageSamples? softSamples);
            bool foldMask = !foldSoft && ImageConverter.FoldsMask(image, samples);
            folded = foldSoft || foldMask;
            SKImage? pixels = ImageConverter.Convert(image, samples, foldSoft, softSamples, foldMask, converter, baseConverter);
            if (pixels is null)
            {
                Report(128, ImageTooLarge);
                return null;
            }

            ImageCache cache = Images;
            cache.Created++;
            cache.PixelsConverted += (long)pixels.Width * pixels.Height;
            return pixels;
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
                    SKImage? reduced;
                    bool owned;
                    if (entry.Gate is { } gate)
                    {
                        // A shared conversion (issue #432): its resampling is made once, under its lock, for every backend that draws it.
                        lock (gate)
                        {
                            reduced = Resample(samples, entry, toCanvas, pixels, interpolate, out owned);
                        }
                    }
                    else
                    {
                        reduced = Resample(samples, entry, toCanvas, pixels, interpolate, out owned);
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

        /// <summary>
        /// Returns the entry's resampling for <paramref name="toCanvas"/>, made and kept when it fits the budget beside the image
        /// (<paramref name="owned"/> when not: the caller disposes it after drawing); null when the entry's slots are taken.
        /// </summary>
        private SKImage? Resample(SKImage samples, ImageCache.Entry entry, in Matrix toCanvas, SKRectI pixels, bool interpolate, out bool owned)
        {
            owned = false;
            if (!entry.Admit(toCanvas, pixels, out int slot))
            {
                return null;
            }

            SKImage? reduced = entry.ReductionAt(slot);

            // Made for the slot's own transform, even when this one is it moved by whole pixels, so that the pixels never depend on
            // whether the resampling was kept (the budget).
            if (reduced is null && ImageReducer.Reduce(samples, entry.TransformAt(slot), entry.PixelsAt(slot), interpolate) is { } made)
            {
                reduced = made;
                long bytes = (long)pixels.Width * pixels.Height * (samples.ColorType == SKColorType.Alpha8 ? 1 : 4);
                if (entry.Gate is null)
                {
                    owned = !Images.KeepReduction(entry, slot, made, bytes);
                }
                else if (Shared is { } shared && shared.Grow(entry, bytes))
                {
                    entry.SetReduction(slot, made, bytes);
                }
                else
                {
                    owned = true;
                }
            }

            return reduced;
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

            if (Shared is { } shared)
            {
                DrawSharedMask(shared, mask, soft, stencil, reduction, width, height, interpolate, paint, clipEdges);
                return;
            }

            ImageCache cache = Images;
            ImageCache.Entry entry = cache.Find(mask);
            SKImage? alpha = entry.Delivered <= reduction ? entry.Image : null;
            bool owned = false;
            int delivered = entry.Delivered;
            if (alpha is null)
            {
                alpha = ConvertMask(soft, stencil, reduction, out delivered);
                if (alpha is null)
                {
                    return;
                }

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

        /// <summary>Draws a mask from the cache the backends drawing its document share (issue #432), converted once (see <see cref="DrawSharedImage"/>).</summary>
        private void DrawSharedMask(SharedImageCache<ImageCache.Entry> shared, object mask, DisplayImage? soft, DisplayImageMask? stencil, int reduction, int width, int height, bool interpolate, SKPaint paint, bool clipEdges)
        {
            SharedImageCache<ImageCache.Entry>.Slot slot = shared.SlotOf(mask);
            ImageCache.Entry? entry = LeaseMask(shared, slot, reduction);
            if (entry is null)
            {
                lock (slot.Converting)
                {
                    entry = LeaseMask(shared, slot, reduction);
                    if (entry is null)
                    {
                        if (ConvertMask(soft, stencil, reduction, out int delivered) is not { } alpha)
                        {
                            return;
                        }

                        entry = new ImageCache.Entry { Image = alpha, Bytes = (long)alpha.Width * alpha.Height, Delivered = delivered, Gate = new Lock() };
                        shared.Publish(slot, entry, entry.Bytes);
                    }
                }
            }

            try
            {
                SKImage alpha = entry.Image!;
                DrawSamples(alpha, entry, new SampleGrid(alpha.Width, alpha.Height, width, height, entry.Delivered), interpolate, paint, clipEdges);
            }
            finally
            {
                shared.Release(entry);
            }
        }

        /// <summary>Leases the slot's mask when it was converted at <paramref name="reduction"/> or finer; else null.</summary>
        private static ImageCache.Entry? LeaseMask(SharedImageCache<ImageCache.Entry> shared, SharedImageCache<ImageCache.Entry>.Slot slot, int reduction)
        {
            ImageCache.Entry? entry = shared.Lease(slot);
            if (entry is not null && entry.Delivered > reduction)
            {
                shared.Release(entry);
                return null;
            }

            return entry;
        }

        /// <summary>Converts a mask to an alpha-only Skia image at <paramref name="reduction"/> (or what its samples deliver); null (reported) when too large.</summary>
        private SKImage? ConvertMask(DisplayImage? soft, DisplayImageMask? stencil, int reduction, out int delivered)
        {
            SKImage? alpha;
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
                return null;
            }

            ImageCache cache = Images;
            cache.Created++;
            cache.PixelsConverted += (long)alpha.Width * alpha.Height;
            return alpha;
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
