using System.Diagnostics;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The image preparation of a render in slices (issue #365): before the first operation is drawn, the images the render will draw are
/// decoded at the reductions it will ask for, and converted into the shared cache, in steps no longer than the slice, so that no single
/// slice of a single-threaded viewer decodes or converts a whole photograph.
/// </summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The backend's preparation half.</summary>
    private sealed partial class Backend
    {
        private readonly PreparationCollector _collector = new();
        private DisplayList? _prepareList;
        private PdfRectangle? _prepareRegion;
        private bool _collected;
        private int _prepareIndex;

        /// <summary>Forgets the previous render's preparation; the next sliced render collects its own images.</summary>
        /// <param name="list">The list the render draws.</param>
        /// <param name="region">The region it draws, or <see langword="null"/> for the whole list.</param>
        public void BeginPreparation(DisplayList list, PdfRectangle? region)
        {
            _prepareList = list;
            _prepareRegion = region;
            _collected = false;
            _prepareIndex = 0;
            _collector.Clear();
        }

        /// <summary>Lets go of the list and the images collected.</summary>
        public void EndPreparation()
        {
            _prepareList = null;
            _collector.Clear();
        }

        /// <summary>
        /// Decodes and converts the images the render will draw, from where the last call stopped, until all are ready or, after at least
        /// one unit of work (a DCT MCU row, a JPEG 2000 code-block, a row of conversion), <paramref name="deadline"/> has passed.
        /// </summary>
        /// <returns><see langword="true"/> when every image is ready.</returns>
        /// <remarks>
        /// ISO 32000-2 §8.9.4 and §8.9.5: the same reductions and converters <see cref="DrawImage"/> will use, so the replay finds the
        /// samples in the document's image cache and the pixels in the shared cache, and paints what it would have painted. Images in
        /// a transparency group or an annotation are decoded only (their pixel model or device transform may differ); images in soft
        /// masks and pattern cells are left to the replay.
        /// </remarks>
        public bool ContinuePreparation(long deadline)
        {
            if (_prepareList is not { } list || _previewing)
            {
                return true;
            }

            if (!_collected)
            {
                _collector.Begin(_device);
                if (_prepareRegion is { } region)
                {
                    list.Replay(_collector, region);
                }
                else
                {
                    list.Replay(_collector);
                }

                _collected = true;
            }

            List<PreparedImage> images = _collector.Images;
            while (_prepareIndex < images.Count)
            {
                if (!Prepare(images[_prepareIndex], deadline))
                {
                    return false;
                }

                _prepareIndex++;
                if (_prepareIndex < images.Count && Stopwatch.GetTimestamp() >= deadline)
                {
                    return false;
                }
            }

            return true;
        }

        private bool Prepare(in PreparedImage item, long deadline)
        {
            DisplayImage image = item.Image;
            PdfColorConverter converter = ImageConverter(image.ColorSpace, item.Defaults, item.Conversion);
            if (converter.PaintsNothing)
            {
                return true;
            }

            int reduction = _imageSamplesPerPixel > 0 ? image.GetReduction(item.ToDevice, _imageSamplesPerPixel, converter) : 0;
            if (!image.ContinueDecode(reduction, Remaining(deadline), _cancellationToken))
            {
                return false;
            }

            if (image.GetSamplesAt(reduction, _cancellationToken) is not { } samples || (long)samples.Width * samples.Height > Array.MaxLength)
            {
                return true;
            }

            if (image.SoftMask is { } mask)
            {
                bool folds = mask.Geometry.Width == image.Geometry.Width && mask.Geometry.Height == image.Geometry.Height;
                int maskReduction = folds ? samples.Reduction : _imageSamplesPerPixel > 0 ? mask.GetReduction(item.ToDevice, _imageSamplesPerPixel, null) : 0;
                if (!mask.ContinueDecode(maskReduction, Remaining(deadline), _cancellationToken))
                {
                    return false;
                }
            }

            if (!item.TopLevel || _imageSource.SharedFor(_document!) is not { } shared)
            {
                return true;
            }

            PdfColorConverter? baseConverter = image.ColorSpace is PdfIndexedColorSpace indexed && !image.Matte.IsEmpty
                ? _document!.GetColorConverter(indexed.Base, _pixelModel.Convert(item.Conversion), item.Defaults)
                : null;
            return _imageSource.ContinueShared(shared, image, samples, FoldedSoftMask(image, samples), converter, baseConverter, _pixelModel, deadline);
        }

        /// <summary>The time left until <paramref name="deadline"/>, at least zero (one unit of work).</summary>
        private static TimeSpan Remaining(long deadline)
        {
            if (deadline == long.MaxValue)
            {
                return Timeout.InfiniteTimeSpan;
            }

            long left = deadline - Stopwatch.GetTimestamp();
            return left <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)(left * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
        }
    }

    /// <summary>An image a render will draw: the transform to the device and the colour parameters it is drawn with.</summary>
    private readonly record struct PreparedImage(DisplayImage Image, Matrix ToDevice, PdfDefaultColorSpaces Defaults, ColorConversion Conversion, bool TopLevel);

    /// <summary>
    /// A backend that paints nothing and collects the images a replay draws, each once, with the transform and colour parameters of its
    /// first paint, and whether that paint is outside transparency groups and annotations.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.4.2 (the state stack), §8.3.2 (transforms) and §8.9.4 (the image unit square).</remarks>
    private sealed class PreparationCollector : IRenderingBackend
    {
        private readonly Stack<Matrix> _saved = new();
        private Matrix _device;
        private Matrix _transform = Matrix.Identity;
        private int _nesting;

        public List<PreparedImage> Images { get; } = [];

        public void Begin(in Matrix device)
        {
            Clear();
            _device = device;
        }

        public void Clear()
        {
            Images.Clear();
            _saved.Clear();
            _transform = Matrix.Identity;
            _nesting = 0;
        }

        public void SaveState() => _saved.Push(_transform);

        public void RestoreState()
        {
            if (_saved.TryPop(out Matrix saved))
            {
                _transform = saved;
            }
        }

        public void SetTransform(in Matrix transform) => _transform = transform;

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
        {
            foreach (PreparedImage known in Images)
            {
                if (ReferenceEquals(known.Image, image))
                {
                    return;
                }
            }

            Images.Add(new PreparedImage(image, _transform * _device, defaultColorSpaces, conversion, _nesting == 0));
        }

        public void DrawGroup(in DisplayGroup group)
        {
            _nesting++;
            try
            {
                group.Replay(this);
            }
            finally
            {
                _nesting--;
            }
        }

        public void BeginAnnotation(in DisplayAnnotation annotation)
        {
            SaveState();
            _nesting++;
        }

        public void EndAnnotation()
        {
            _nesting--;
            RestoreState();
        }

        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
        }

        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
        {
        }

        public void IntersectClip(PathView path, FillRule rule)
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

        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern)
        {
        }

        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
        {
        }

        public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
        {
        }

        public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode)
        {
        }

        public void SetSoftMask(in DisplaySoftMask mask)
        {
        }

        public void SetDeviceParameters(in DisplayDeviceParameters parameters)
        {
        }
    }
}
