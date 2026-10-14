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
        private PdfColorSpace? _imageSpace;
        private PdfDefaultColorSpaces? _imageDefaults;
        private ColorConversion _imageConversion;
        private PdfColorConverter? _imageConverter;
        private PdfColorConverter? _imageBaseConverter;

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
            if ((long)image.Width * image.Height > Array.MaxLength || _clip.IsEmpty || _fillCompositor.PaintsNothing)
            {
                return;
            }

            if (!TryCoverUnitSquare(image.Width, image.Height, out Matrix inverse))
            {
                return;
            }

            PdfColorConverter converter = ImageConverter(image.ColorSpace, defaultColorSpaces, conversion);
            if (converter.PaintsNothing)
            {
                return;
            }

            PdfColorConverter? baseConverter = image.ColorSpace is PdfIndexedColorSpace indexed && !image.Matte.IsEmpty
                ? _imageBaseConverter ??= _document!.GetColorConverter(indexed.Base, _pixelModel.Convert(conversion), defaultColorSpaces)
                : null;
            _imageSource.Prepare(image, converter, baseConverter, _pixelModel);
            byte[]? opacity = null;
            int maskWidth = 1;
            int maskHeight = 1;
            bool maskInterpolate = false;
            if (image.SoftMask is { } soft && !ImageSource.FoldsSoftMask(image) && (long)soft.Width * soft.Height <= Array.MaxLength)
            {
                _maskSource.Prepare(soft);
                (opacity, maskWidth, maskHeight, maskInterpolate) = (_maskSource.Buffer, soft.Width, soft.Height, soft.Interpolate);
            }
            else if (image.Mask is { } mask && !ImageSource.FoldsMask(image) && (long)mask.Width * mask.Height <= Array.MaxLength)
            {
                _maskSource.Prepare(mask);
                (opacity, maskWidth, maskHeight, maskInterpolate) = (_maskSource.Buffer, mask.Width, mask.Height, mask.Interpolate);
            }

            Bitmap target = _target!;
            var painter = new ImagePainter(
                target.Pixels, target.Stride, _imageSource.Buffer, image.Width, image.Height, inverse, image.Interpolate, opacity, maskWidth, maskHeight, maskInterpolate, _fillCompositor);
            _scan.Paint(FillRule.NonZero, ref painter);
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

            Span<float> rgb = stackalloc float[4];
            converter.Convert(color.Components, rgb);
            _maskSource.Prepare(mask);
            Bitmap target = _target!;
            var painter = new ImagePainter(target.Pixels, target.Stride, Pack(rgb), _maskSource.Buffer, mask.Width, mask.Height, inverse, mask.Interpolate, _fillCompositor);
            _scan.Paint(FillRule.NonZero, ref painter);
        }

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

        /// <summary>Lets go of the render's image, mask, converters and document; the pooled buffers stay for the next render.</summary>
        private void EndImages()
        {
            _imageSource.Forget();
            _maskSource.Forget();
            _imageSpace = null;
            _imageDefaults = null;
            _imageConverter = null;
            _imageBaseConverter = null;
        }

        private void DisposeImages()
        {
            _imageSource.Dispose();
            _maskSource.Dispose();
        }
    }
}
