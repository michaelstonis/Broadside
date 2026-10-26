using Broadside.Graphics;
using Broadside.Images;

namespace Broadside.Rendering.WebGL;

/// <summary>"Mode B" (#373): JPEG images decoded by the browser into an <c>ImageBitmap</c> and uploaded straight to a texture.</summary>
public sealed partial class WebGLRenderer
{
    private static int _nextBitmap;

    /// <summary>
    /// Has the browser decode, with <c>createImageBitmap</c>, the JPEG images a render of <paramref name="region"/> under
    /// <paramref name="deviceTransform"/> will draw, so that the render uploads each bitmap to a texture instead of decoding and
    /// converting its samples, and reads nothing back. Only images whose colours our pipeline would not change are taken: gray or RGB
    /// (device or ICC-based), no Decode mapping, mask, soft mask, colour key, matte or transfer function, and chroma not subsampled.
    /// </summary>
    /// <param name="list">The display list to be rendered.</param>
    /// <param name="deviceTransform">The transform the render will use.</param>
    /// <param name="region">The region the render draws, in default user space; <see langword="null"/> for the whole list.</param>
    /// <param name="cancellationToken">Cancels the wait; bitmaps already made are kept for the next render.</param>
    /// <returns>A task that completes when the bitmaps are made (or failed: those images are drawn from their samples).</returns>
    /// <remarks>
    /// ISO 32000-2 §7.4.8, §8.9.4 and §8.9.5. For a browser without WebCodecs <c>ImageDecoder</c> (Safari), where reading a decoded
    /// bitmap back costs 60 to 141 ms on the page's thread: the GPU composites the bitmap as it is. The Exif and ICC payloads are
    /// zero-filled first (browsers rotate by Exif, and WebKit converts embedded profiles regardless of <c>colorSpaceConversion</c>).
    /// Within the platform decoders' tolerance of the managed decoder; no-op before <see cref="InitializeAsync"/> or without WebGL 2.
    /// </remarks>
    public static async Task PrefetchImageBitmapsAsync(DisplayList list, Matrix deviceTransform, PdfRectangle? region, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (!IsAvailable)
        {
            return;
        }

        var collector = new BitmapCollector();
        if (region is { } area)
        {
            list.Replay(collector, area);
        }
        else
        {
            list.Replay(collector);
        }

        List<(DisplayImage Image, int Key)>? started = null;
        foreach ((DisplayImage image, ColorConversion conversion) in collector.Images)
        {
            if (!IsNativeImagePaint(image, conversion) || TextureCache.HasTextureOrBitmap(image)
                || !image.TryGetPlatformData(PlatformImageFeatures.AppliesExifOrientation | PlatformImageFeatures.ConvertsEmbeddedProfiles, out PlatformImageRequest request, out ReadOnlyMemory<byte> data)
                || request.ChromaSubsampled || request.ArithmeticCoding || request.Width > TextureCache.MaxSize || request.Height > TextureCache.MaxSize)
            {
                continue;
            }

            int key = ++_nextBitmap;
            byte[] bytes = data.ToArray();
            WebGLInterop.BeginBitmap(key, bytes);
            (started ??= []).Add((image, key));
        }

        if (started is null)
        {
            return;
        }

        foreach ((DisplayImage image, int key) in started)
        {
            string status = await WebGLInterop.WaitBitmap(key).WaitAsync(cancellationToken).ConfigureAwait(true);
            if (status.StartsWith("ok ", StringComparison.Ordinal) && status == FormattableString.Invariant($"ok {image.Width} {image.Height}"))
            {
                TextureCache.AttachBitmap(image, key);
            }
            else
            {
                WebGLInterop.DropBitmap(key); // rejected, or another size (rotated): drawn from its samples
            }
        }
    }

    /// <summary>
    /// Returns whether the browser's own decode of an image may stand for its converted samples: the conversion would be the identity
    /// up to the decoders' tolerance.
    /// </summary>
    internal static bool IsNativeImagePaint(DisplayImage image, in ColorConversion conversion)
    {
        if (image.SoftMask is not null || image.Mask is not null || !image.ColorKey.IsEmpty || !image.Matte.IsEmpty || conversion.TransferFunction is not null)
        {
            return false;
        }

        PdfColorSpace space = image.ColorSpace;
        bool family = space.Family is PdfColorSpaceFamily.DeviceGray or PdfColorSpaceFamily.DeviceRgb
            || (space.Family == PdfColorSpaceFamily.IccBased && space.ComponentCount is 1 or 3);
        if (!family)
        {
            return false;
        }

        ImageDecodeMap map = image.DecodeMap;
        int top = (1 << map.BitsPerComponent) - 1;
        for (int c = 0; c < map.Components; c++)
        {
            if (map.Map(c, 0) != 0 || map.Map(c, top) != 1)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A backend that paints nothing and collects the images a replay draws.</summary>
    private sealed class BitmapCollector : IRenderingBackend
    {
        public List<(DisplayImage Image, ColorConversion Conversion)> Images { get; } = [];

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
        {
            foreach ((DisplayImage seen, _) in Images)
            {
                if (ReferenceEquals(seen, image))
                {
                    return;
                }
            }

            Images.Add((image, conversion));
        }

        public void DrawGroup(in DisplayGroup group) => group.Replay(this);

        public void SaveState()
        {
        }

        public void RestoreState()
        {
        }

        public void SetTransform(in Matrix transform)
        {
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

        public void BeginAnnotation(in DisplayAnnotation annotation)
        {
        }

        public void EndAnnotation()
        {
        }

        public void SetDeviceParameters(in DisplayDeviceParameters parameters)
        {
        }
    }
}
