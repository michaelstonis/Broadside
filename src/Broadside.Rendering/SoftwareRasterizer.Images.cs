using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Images (ISO 32000-2 §8.9): the unit-square mapping, colour conversion of the samples, masks (§8.9.6, §11.6.5) and resampling to
/// the device.
/// </summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The backend's image half.</summary>
    private sealed partial class Backend
    {
        private readonly ImageSource _imageSource = new();
        private readonly MaskSource _maskSource = new();
        private ImageFootprints? _footprints;
        private PdfColorSpace? _imageSpace;
        private PdfDefaultColorSpaces? _imageDefaults;
        private ColorConversion _imageConversion;
        private PdfColorConverter? _imageConverter;
        private PdfColorConverter? _imageBaseConverter;

        // The fewest decoded samples per device pixel a reduced image keeps (#362), or 0 to draw images at their own resolution.
        private double _imageSamplesPerPixel;

        // The render's token: a decode at replay (#362) stops with the render.
        private CancellationToken _cancellationToken;

        // Issue #365: the deadline of the current slice of a progressive render, the image paint left for the next slice, and how
        // deep the replay is inside groups (where a paint is never split).
        private long _paintDeadline = long.MaxValue;
        private SlicedPaint? _slicedPaint;
        private SlicedPaint? _paintState;
        private int _nested;

        /// <summary>
        /// Draws the image's samples into the parallelogram the unit square maps to: the outline is scan converted like a filled path
        /// (so its edges are anti-aliased and the clipping path cuts it as it cuts fills and strokes, §8.5.4), and each covered pixel
        /// takes its colour from the samples under it, its opacity from the image's mask.
        /// </summary>
        /// <remarks>
        /// ISO 32000-2 §8.9.4: image space maps to the unit square through <c>[1/w 0 0 −1/h 0 1]</c>, then through the CTM.
        /// §8.9.5.2 and §8.9.5.3 for the Decode mapping and Interpolate (see <see cref="ImageSource"/> and <see cref="ImagePainter"/>).
        /// Masks (§8.9.6.3, §8.9.6.4, §11.6.5.2, §7.4.9): one of the image's size is folded into the converted pixels; one of
        /// another size is resampled from its own grid (<see cref="MaskSource"/>). An image whose transform is singular covers no
        /// area and paints nothing.
        /// </remarks>
        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
        {
            if (_clip.IsEmpty || _fillCompositor.PaintsNothing)
            {
                return;
            }

            if (!TryCoverUnitSquare(image.Geometry.Width, image.Geometry.Height, out Matrix inverse))
            {
                return;
            }

            PdfColorConverter converter = ImageConverter(image.ColorSpace, defaultColorSpaces, conversion);
            if (converter.PaintsNothing)
            {
                _scan.Reset(_width, _height);
                return;
            }

            int reduction = _imageSamplesPerPixel > 0 ? image.GetReduction(_toDevice, _imageSamplesPerPixel, converter) : 0;
            if (_previewing)
            {
                PreviewImage(image, defaultColorSpaces, conversion, inverse);
                return;
            }

            if (image.GetSamplesAt(reduction, _cancellationToken) is not { } samples || (long)samples.Width * samples.Height > Array.MaxLength)
            {
                // ISO 32000-2 §8.9.5: an image that cannot be decoded paints nothing (its reason is a diagnostic of the document).
                _scan.Reset(_width, _height);
                return;
            }

            PdfColorConverter? baseConverter = image.ColorSpace is PdfIndexedColorSpace indexed && !image.Matte.IsEmpty
                ? _imageBaseConverter ??= _document!.GetColorConverter(indexed.Base, _pixelModel.Convert(conversion), defaultColorSpaces)
                : null;
            DisplayImageSamples? soft = FoldedSoftMask(image, samples);
            if (_imageSource.SharedFor(_document!) is { } shared)
            {
                // The rasterizers of a viewer's pool share the conversion (issue #432), leased while it is painted.
                SharedPixels leased = _imageSource.Lease(shared, image, samples, soft, converter, baseConverter, _pixelModel);
                if (PaintsInSlices)
                {
                    BeginSlicedPaint(image, samples, soft, inverse, leased.Pixels, leased, shared);
                    return;
                }

                try
                {
                    PaintConverted(image, samples, soft, inverse, leased.Pixels);
                }
                finally
                {
                    shared.Release(leased);
                }

                return;
            }

            _imageSource.Prepare(image, samples, soft, converter, baseConverter, _pixelModel);
            if (PaintsInSlices)
            {
                BeginSlicedPaint(image, samples, soft, inverse, _imageSource.Buffer, null, null);
                return;
            }

            PaintConverted(image, samples, soft, inverse, _imageSource.Buffer);
        }

        /// <summary>
        /// Whether an image paint may stop at the slice's deadline and finish in the next slice (issue #365): only at the top level of a
        /// progressive render's replay, where nothing else is drawn before the next slice; never inside a group, an annotation or an
        /// overprint preview, whose content goes on within the same operation.
        /// </summary>
        private bool PaintsInSlices => _paintDeadline != long.MaxValue && _nested == 0 && _annotationDepth == 0 && !_previewing && _target is not null;

        /// <summary>Gets a value indicating whether an image paint stopped at a slice's deadline and is still to be finished.</summary>
        public bool HasSlicedPaint => _slicedPaint is not null;

        /// <summary>Sets the deadline of the current slice for image paints (<see cref="long.MaxValue"/>: none).</summary>
        public void SetPaintDeadline(long deadline) => _paintDeadline = deadline;

        /// <summary>Starts a paint of the converted pixels that <see cref="ContinueSlicedPaint"/> may stop between rows.</summary>
        private void BeginSlicedPaint(DisplayImage image, DisplayImageSamples samples, DisplayImageSamples? soft, in Matrix inverse, uint[] pixels, SharedPixels? leased, SharedImageCache<SharedPixels>? shared)
        {
            ImageMask mask = PrepareImageMask(image, samples, soft);
            SlicedPaint paint = _paintState ??= new SlicedPaint();
            paint.Image = image;
            paint.Samples = samples;
            paint.ToSamples = ToSamples(inverse, samples);
            paint.Pixels = pixels;
            paint.Mask = mask;
            paint.Leased = leased;
            paint.Shared = shared;
            paint.Next = 0;
            _slicedPaint = paint;
            ContinueSlicedPaint();
        }

        /// <summary>
        /// Paints rows of the image paint begun last until it is done or, after at least one row, the slice's deadline has passed; the
        /// pixels equal one whole paint's (rows are painted independently).
        /// </summary>
        /// <returns><see langword="true"/> when nothing is left to paint.</returns>
        /// <remarks>ISO 32000-2 §8.9.4 and §8.9.5.3; issue #365.</remarks>
        public bool ContinueSlicedPaint()
        {
            if (_slicedPaint is not { } paint)
            {
                return true;
            }

            Bitmap target = _target!;
            ImageMask mask = paint.Mask;
            var painter = new ImagePainter(
                target.Pixels,
                target.Stride,
                paint.Pixels,
                paint.Samples.Width,
                paint.Samples.Height,
                paint.ToSamples,
                paint.Image!.Interpolate,
                mask.Opacity,
                mask.Width,
                mask.Height,
                mask.Interpolate,
                _fillCompositor,
                Averages,
                mask.ScaleU,
                mask.ScaleV,
                _footprints ??= new ImageFootprints());
            _scan.Rule = _imageRule;
            bool done;
            try
            {
                int next = paint.Next;
                done = _scan.PaintRows(FillRule.NonZero, ref painter, ref next, _paintDeadline);
                paint.Next = next;
            }
            finally
            {
                _scan.Rule = _pathRule;
            }

            if (!done)
            {
                return false;
            }

            if (_colorFilter != ColorFilter.None)
            {
                CoverImage();
            }
            else
            {
                _scan.Reset(_width, _height);
            }

            EndSlicedPaint();
            return true;
        }

        /// <summary>Lets go of an image paint begun in slices: done, or abandoned with its render.</summary>
        private void EndSlicedPaint()
        {
            if (_slicedPaint is { } paint)
            {
                if (paint is { Leased: { } leased, Shared: { } shared })
                {
                    shared.Release(leased);
                }

                paint.Image = null;
                paint.Pixels = [];
                paint.Leased = null;
                paint.Shared = null;
                paint.Mask = default;
                _slicedPaint = null;
            }
        }

        /// <summary>
        /// Gets the converter of this backend's images, which shares them with the other rasterizers drawing the same document when
        /// told to (issue #432).
        /// </summary>
        public ImageSource Images => _imageSource;

        /// <summary>Paints an image's converted pixels through its mask, when not folded into them, in the unit square's parallelogram.</summary>
        private void PaintConverted(DisplayImage image, DisplayImageSamples samples, DisplayImageSamples? soft, in Matrix inverse, uint[] pixels)
        {
            ImageMask mask = PrepareImageMask(image, samples, soft);
            Bitmap target = _target!;
            var painter = new ImagePainter(
                target.Pixels,
                target.Stride,
                pixels,
                samples.Width,
                samples.Height,
                ToSamples(inverse, samples),
                image.Interpolate,
                mask.Opacity,
                mask.Width,
                mask.Height,
                mask.Interpolate,
                _fillCompositor,
                Averages,
                mask.ScaleU,
                mask.ScaleV,
                _footprints ??= new ImageFootprints());
            PaintImage(ref painter, coversImage: true);
        }

        /// <summary>
        /// Returns the transform from device pixels to the grid of <paramref name="samples"/>, from the one to the image's own grid: a
        /// reduction by s divides positions by s, so reduced sample i covers source samples i·s to (i + 1)·s, and the reduced grid,
        /// ⌈W / s⌉·s source samples wide, overhangs the unit square, which the outline clips (#362).
        /// </summary>
        private static Matrix ToSamples(in Matrix inverse, DisplayImageSamples samples)
        {
            if (samples.Reduction == 0)
            {
                return inverse;
            }

            double scale = 1.0 / (1 << samples.Reduction);
            return inverse * new Matrix(scale, 0, 0, scale, 0, 0);
        }

        /// <summary>
        /// Returns the samples of the image's soft mask that fold into its converted pixels: a soft mask of the image's own size, at the
        /// reduction the image's samples have (§11.6.5.2; a Matte needs it, and the grids then align exactly); otherwise null.
        /// </summary>
        private DisplayImageSamples? FoldedSoftMask(DisplayImage image, DisplayImageSamples samples)
        {
            if (image.SoftMask is not { } mask || mask.Geometry.Width != image.Geometry.Width || mask.Geometry.Height != image.Geometry.Height)
            {
                return null;
            }

            return mask.GetSamplesAt(samples.Reduction, _cancellationToken) is { } soft && soft.Reduction == samples.Reduction ? soft : null;
        }

        /// <summary>
        /// Prepares the image's mask when it is not folded into the converted pixels (another size than the samples painted, §8.9.6.3,
        /// §11.6.5.2): its opacities, grid and the factors from the samples' grid to its own; or none. A viewer reduces it by its own
        /// rule at its own size (#362), a soft mask through its samples, an image mask to 8-bit coverage.
        /// </summary>
        private ImageMask PrepareImageMask(DisplayImage image, DisplayImageSamples samples, DisplayImageSamples? folded)
        {
            double scale = 1 << samples.Reduction;
            if (image.SoftMask is { } soft && folded is null && (long)soft.Geometry.Width * soft.Geometry.Height <= Array.MaxLength)
            {
                int reduction = _imageSamplesPerPixel > 0 ? soft.GetReduction(_toDevice, _imageSamplesPerPixel, null) : 0;
                if (soft.GetSamplesAt(reduction, _cancellationToken) is { } opacity)
                {
                    _maskSource.Prepare(soft, opacity);
                    double factor = scale / (1 << opacity.Reduction);
                    return new ImageMask(_maskSource.Buffer, _maskSource.Width, _maskSource.Height, soft.Interpolate, factor * soft.Geometry.Width / image.Geometry.Width, factor * soft.Geometry.Height / image.Geometry.Height);
                }
            }

            if (image.Mask is { } mask && !ImageSource.FoldsMask(image, samples) && (long)mask.Width * mask.Height <= Array.MaxLength)
            {
                int reduction = _imageSamplesPerPixel > 0 ? mask.GetReduction(_toDevice, _imageSamplesPerPixel) : 0;
                _maskSource.Prepare(mask, reduction);
                double factor = scale / (1 << reduction);
                return new ImageMask(_maskSource.Buffer, _maskSource.Width, _maskSource.Height, mask.Interpolate, factor * mask.Width / image.Geometry.Width, factor * mask.Height / image.Geometry.Height);
            }

            return new ImageMask(null, 1, 1, false, 0, 0);
        }

        /// <summary>Paints the colour through the stencil mask in the parallelogram the unit square maps to.</summary>
        /// <remarks>
        /// ISO 32000-2 §8.9.6.2: where the mask paints, the colour is painted; elsewhere the backdrop shows. The mask is resampled
        /// like an image (§8.9.5.3; see <see cref="ImagePainter"/>), its outline anti-aliased and clipped like a fill.
        /// </remarks>
        public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
        {
            if ((long)mask.Width * mask.Height > Array.MaxLength || _clip.IsEmpty || _fillCompositor.PaintsNothing)
            {
                return;
            }

            PdfColorConverter converter = Converter(color);
            if (converter.PaintsNothing || !TryCoverUnitSquare(mask.Width, mask.Height, out Matrix inverse))
            {
                return;
            }

            if (_previewing)
            {
                PreviewImageMask(mask, color, inverse);
                return;
            }

            Span<float> rgb = stackalloc float[4];
            converter.Convert(color.Components, rgb);

            // A viewer resamples a large mask drawn small from its 8-bit coverage at a reduction (#362), not from every bit.
            int reduction = _imageSamplesPerPixel > 0 ? mask.GetReduction(_toDevice, _imageSamplesPerPixel) : 0;
            _maskSource.Prepare(mask, reduction);
            Matrix toMask = reduction == 0 ? inverse : inverse * new Matrix(1.0 / (1 << reduction), 0, 0, 1.0 / (1 << reduction), 0, 0);
            Bitmap target = _target!;
            var painter = new ImagePainter(target.Pixels, target.Stride, Pack(rgb), _maskSource.Buffer, _maskSource.Width, _maskSource.Height, toMask, mask.Interpolate, _fillCompositor, Averages);
            PaintImage(ref painter);
        }

        /// <summary>Whether reduced images average the samples under each pixel; without anti-aliasing the sample under its centre is taken (§10.7.4).</summary>
        private bool Averages => _imageRule == CoverageRule.Exact;

        /// <summary>Paints the unit square's outline with the image coverage rule (§10.7.4: pixel centres inside, without anti-aliasing).</summary>
        /// <remarks>
        /// With a color filter (<see cref="ColorFilter.DarkMode"/>) an image's shape is also recorded as covered by an image, which
        /// the filter leaves as it is; a stencil mask (<paramref name="coversImage"/> false) paints a color and is filtered.
        /// </remarks>
        private void PaintImage(ref ImagePainter painter, bool coversImage = false)
        {
            _scan.Rule = _imageRule;
            try
            {
                bool covers = coversImage && _colorFilter != ColorFilter.None;
                _scan.Paint(FillRule.NonZero, ref painter, keep: covers);
                if (covers)
                {
                    CoverImage();
                }
            }
            finally
            {
                _scan.Rule = _pathRule;
            }
        }

        /// <summary>An image paint that stopped at a slice's deadline (issue #365): what it paints and the first cell left.</summary>
        /// <remarks>One per backend, reused, so that a paint in slices allocates nothing.</remarks>
        private sealed class SlicedPaint
        {
            public DisplayImage? Image { get; set; }

            public DisplayImageSamples Samples { get; set; }

            public Matrix ToSamples { get; set; }

            public uint[] Pixels { get; set; } = [];

            public ImageMask Mask { get; set; }

            public SharedPixels? Leased { get; set; }

            public SharedImageCache<SharedPixels>? Shared { get; set; }

            public int Next { get; set; }
        }

        /// <summary>A mask resampled on its own grid: its opacities and size, and the factors from the painted samples' positions to its own.</summary>
        private readonly record struct ImageMask(byte[]? Opacity, int Width, int Height, bool Interpolate, double ScaleU, double ScaleV);

        private static bool IsFinite(in Matrix m) =>
            double.IsFinite(m.A) && double.IsFinite(m.B) && double.IsFinite(m.C) && double.IsFinite(m.D) && double.IsFinite(m.E) && double.IsFinite(m.F);

        /// <summary>
        /// Adds the outline of the unit square under the current transform to the scan converter and returns the transform from device
        /// pixels back to the sample grid of a <paramref name="width"/> × <paramref name="height"/> image; false when nothing is covered.
        /// </summary>
        private bool TryCoverUnitSquare(int width, int height, out Matrix inverse)
        {
            Matrix toDevice = new Matrix(1.0 / width, 0, 0, -1.0 / height, 0, 1) * _toDevice;
            if (!toDevice.TryInvert(out inverse) || !IsFinite(inverse))
            {
                return false;
            }

            _scan.Reset(_width, _height);
            Device p00 = Map(new PathPoint(0, 0));
            Device p10 = Map(new PathPoint(1, 0));
            Device p11 = Map(new PathPoint(1, 1));
            Device p01 = Map(new PathPoint(0, 1));
            _scan.AddLine(p00.X, p00.Y, p10.X, p10.Y);
            _scan.AddLine(p10.X, p10.Y, p11.X, p11.Y);
            _scan.AddLine(p11.X, p11.Y, p01.X, p01.Y);
            _scan.AddLine(p01.X, p01.Y, p00.X, p00.Y);
            return !_scan.IsEmpty;
        }

        private PdfColorConverter ImageConverter(PdfColorSpace space, PdfDefaultColorSpaces defaults, ColorConversion conversion)
        {
            conversion = _pixelModel.Convert(conversion);
            if (_imageConverter is null || !ReferenceEquals(_imageSpace, space) || !ReferenceEquals(_imageDefaults, defaults) || _imageConversion != conversion)
            {
                _imageSpace = space;
                _imageDefaults = defaults;
                _imageConversion = conversion;
                _imageConverter = _document!.GetColorConverter(space, conversion, defaults);
                _imageBaseConverter = null;
            }

            return _imageConverter;
        }

        /// <summary>
        /// Lets go of the render's converters and document; the pooled buffers stay for the next render, and so do the image and
        /// mask converted last, so that the tiles of one display list (spec #33 story 14) convert an image once, not once per tile.
        /// They are released by the next image or by <see cref="Dispose"/>.
        /// </summary>
        private void EndImages()
        {
            EndSlicedPaint();
            _paintDeadline = long.MaxValue;
            _imageSpace = null;
            _imageDefaults = null;
            _imageConverter = null;
            _imageBaseConverter = null;
        }

        private void DisposeImages()
        {
            _imageSource.Dispose();
            _maskSource.Dispose();
            _footprints?.Dispose();
        }
    }
}
