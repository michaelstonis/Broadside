using Broadside.Graphics;
using Broadside.Images;

namespace Broadside.Rendering;

/// <summary>The asynchronous boundary before a replay: platform image decoders decode the images a region reaches (issue #372).</summary>
public sealed partial class DisplayList
{
    /// <summary>
    /// Decodes, with the engine's platform image decoders (<see cref="PdfOptions.UsePlatformImageDecoder"/>), the images that a
    /// replay of <paramref name="region"/> under <paramref name="deviceTransform"/> will draw, at the reductions it will ask for, so
    /// that the replay finds them in the document's image cache.
    /// </summary>
    /// <param name="deviceTransform">The transform from the list's default user space to device pixels, as given to the backend.</param>
    /// <param name="region">The region the replay draws, in default user space (as for <see cref="Replay(IRenderingBackend, PdfRectangle)"/>); <see langword="null"/> for the whole list.</param>
    /// <param name="samplesPerPixel">
    /// The backend's samples per device pixel (<see cref="DisplayImage.GetReduction"/>), or 0 when it draws images at their own
    /// resolution.
    /// </param>
    /// <param name="cancellationToken">Cancels the decodes in flight that only this call waits for; nothing partial is kept.</param>
    /// <returns>A task that completes when every image offered is decoded, or has failed and been left to the managed codec.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §8.9.4 (the image unit square under the transform in force) and §7.4.8. The asynchronous half of a synchronous
    /// replay, as fetching a remote file's ranges is for building: a viewer awaits it before it replays a tile, and the replay itself
    /// never waits on a platform decoder. An image the replay does not find (one this call did not offer, one evicted meanwhile, or
    /// one a platform could not decode) is decoded with the managed codec, as without this call.
    /// </para>
    /// <para>
    /// Completes at once, allocating nothing, when the engine has no platform image decoder or the list keeps no image encoded.
    /// Images inside soft masks and tiling-pattern cells are not offered. Diagnostics: <c>ImageDecodedManaged</c> when a platform
    /// decode fails; <c>PlatformImageVerified</c> or <c>PlatformImageMismatch</c> with <see cref="PdfOptions.VerifyPlatformImageDecoder"/>.
    /// </para>
    /// </remarks>
    public ValueTask PrefetchImagesAsync(in Matrix deviceTransform, PdfRectangle? region, double samplesPerPixel, CancellationToken cancellationToken)
    {
        if (Document.Configuration.PlatformImages is not { } platform || !HasPlatformCandidates())
        {
            return ValueTask.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        ImagePrefetchCollector collector = ImagePrefetchCollector.Rent(Document, deviceTransform, samplesPerPixel);
        List<Task>? pending = null;
        try
        {
            if (region is { } area)
            {
                Replay(collector, area);
            }
            else
            {
                Replay(collector);
            }

            foreach ((DisplayImage image, int reduction) in collector.Images)
            {
                if (image.Prefetch(reduction, platform, cancellationToken) is { IsCompletedSuccessfully: false } task)
                {
                    (pending ??= []).Add(task);
                }
            }
        }
        finally
        {
            ImagePrefetchCollector.Return(collector);
        }

        return pending is null ? ValueTask.CompletedTask : new ValueTask(WaitAsync(pending, cancellationToken));
    }

    private bool HasPlatformCandidates()
    {
        foreach (DisplayImage image in Images)
        {
            if (image.IsPlatformCandidate)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WaitAsync(List<Task> pending, CancellationToken cancellationToken)
    {
        foreach (Task task in pending)
        {
            try
            {
                await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Another prefetch's decode, cancelled by its own caller: this replay decodes the image with the managed codec.
            }
        }
    }

    /// <summary>
    /// A backend that paints nothing and collects the images a replay draws, each with the reduction the drawing backend will ask
    /// for (the finest when one image is drawn at several scales).
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.4.2 (the state stack), §8.3.2 (transforms) and §8.9.4 (the image unit square). One per thread, reused, so that
    /// a prefetch of images already decoded allocates nothing.
    /// </remarks>
    private sealed class ImagePrefetchCollector : IRenderingBackend
    {
        [ThreadStatic]
        private static ImagePrefetchCollector? _cached;

        private readonly Stack<Matrix> _saved = new();
        private PdfDocument? _document;
        private Matrix _device;
        private double _samplesPerPixel;
        private Matrix _transform = Matrix.Identity;

        public List<(DisplayImage Image, int Reduction)> Images { get; } = [];

        public static ImagePrefetchCollector Rent(PdfDocument document, in Matrix device, double samplesPerPixel)
        {
            ImagePrefetchCollector collector = _cached ?? new ImagePrefetchCollector();
            _cached = null;
            collector._document = document;
            collector._device = device;
            collector._samplesPerPixel = samplesPerPixel;
            collector._transform = Matrix.Identity;
            return collector;
        }

        public static void Return(ImagePrefetchCollector collector)
        {
            collector.Images.Clear();
            collector._saved.Clear();
            collector._document = null;
            _cached = collector;
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
            if (!image.IsPlatformCandidate)
            {
                return;
            }

            int reduction = 0;
            if (_samplesPerPixel > 0)
            {
                PdfColorConverter converter = _document!.GetColorConverter(image.ColorSpace, conversion with { Target = DeviceColorModel.Rgb }, defaultColorSpaces);
                reduction = image.GetReduction(_transform * _device, _samplesPerPixel, converter);
            }

            for (int i = 0; i < Images.Count; i++)
            {
                if (ReferenceEquals(Images[i].Image, image))
                {
                    Images[i] = (image, Math.Min(reduction, Images[i].Reduction));
                    return;
                }
            }

            Images.Add((image, reduction));
        }

        public void DrawGroup(in DisplayGroup group) => group.Replay(this);

        public void BeginAnnotation(in DisplayAnnotation annotation) => SaveState();

        public void EndAnnotation() => RestoreState();

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
