using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Images (ISO 32000-2 §8.9): the unit-square mapping, colour conversion of the samples and resampling to the device.</summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The backend's image half.</summary>
    private sealed partial class Backend
    {
        private readonly ImageSource _imageSource = new();
        private PdfColorSpace? _imageSpace;
        private PdfDefaultColorSpaces? _imageDefaults;
        private ColorConversion _imageConversion;
        private PdfColorConverter? _imageConverter;

        /// <summary>
        /// Draws the image's samples into the parallelogram the unit square maps to: the outline is scan converted like a filled path
        /// (so its edges are anti-aliased and anything that modulates coverage applies), and each covered pixel takes its colour from
        /// the samples under it.
        /// </summary>
        /// <remarks>
        /// ISO 32000-2 §8.9.4: image space maps to the unit square through <c>[1/w 0 0 −1/h 0 1]</c>, then through the CTM.
        /// §8.9.5.2 and §8.9.5.3 for the Decode mapping and Interpolate (see <see cref="ImageSource"/> and <see cref="ImagePainter"/>).
        /// An image whose transform is singular covers no area and paints nothing.
        /// </remarks>
        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
        {
            if ((long)image.Width * image.Height > Array.MaxLength)
            {
                return;
            }

            Matrix toDevice = new Matrix(1.0 / image.Width, 0, 0, -1.0 / image.Height, 0, 1) * _toDevice;
            if (!toDevice.TryInvert(out Matrix inverse) || !IsFinite(inverse))
            {
                return;
            }

            PdfColorConverter converter = ImageConverter(image.ColorSpace, defaultColorSpaces, conversion);
            if (converter.PaintsNothing)
            {
                return;
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
            if (_scan.IsEmpty)
            {
                return;
            }

            _imageSource.Prepare(image, converter);
            var painter = new ImagePainter(_imageSource.Buffer, image.Width, image.Height, inverse, image.Interpolate);
            _scan.Fill(_target!, FillRule.NonZero, ref painter);
        }

        private static bool IsFinite(in Matrix m) =>
            double.IsFinite(m.A) && double.IsFinite(m.B) && double.IsFinite(m.C) && double.IsFinite(m.D) && double.IsFinite(m.E) && double.IsFinite(m.F);

        private PdfColorConverter ImageConverter(PdfColorSpace space, PdfDefaultColorSpaces defaults, ColorConversion conversion)
        {
            if (_imageConverter is null || !ReferenceEquals(_imageSpace, space) || !ReferenceEquals(_imageDefaults, defaults) || _imageConversion != conversion)
            {
                _imageSpace = space;
                _imageDefaults = defaults;
                _imageConversion = conversion;
                _imageConverter = _document!.GetColorConverter(space, conversion with { Target = DeviceColorModel.Rgb }, defaults);
            }

            return _imageConverter;
        }

        /// <summary>Lets go of the render's image, converter and document; the pooled buffers stay for the next render.</summary>
        private void EndImages()
        {
            _imageSource.Forget();
            _imageSpace = null;
            _imageDefaults = null;
            _imageConverter = null;
        }

        private void DisposeImages() => _imageSource.Dispose();
    }
}
