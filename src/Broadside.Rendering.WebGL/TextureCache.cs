using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Broadside.Graphics;

namespace Broadside.Rendering.WebGL;

/// <summary>
/// The images and stencil masks uploaded as textures, shared by every renderer (one WebGL context per page): converted once, reused
/// by every tile and zoom level that draws them (mipmapped), released when their display list is collected or, least recently used
/// first, when the textures pass <see cref="Budget"/>.
/// </summary>
/// <remarks>ISO 32000-2 §8.9.4 to §8.9.6 (images, stencil masks, folded explicit and soft masks).</remarks>
[SupportedOSPlatform("browser")]
internal static class TextureCache
{
    private static readonly ConditionalWeakTable<object, Entry> Table = [];
    private static readonly ConditionalWeakTable<DisplayImage, BitmapKey> Bitmaps = [];
    private static readonly List<Entry> Entries = [];
    private static int _next;
    private static long _clock;

    /// <summary>Gets or sets the most bytes of texture memory kept (mipmaps included) before older textures are released.</summary>
    public static long Budget { get; set; } = 192L << 20;

    /// <summary>Gets the bytes of texture memory held (mipmaps included).</summary>
    public static long Bytes { get; private set; }

    /// <summary>Gets the largest texture side the context allows.</summary>
    public static int MaxSize { get; set; } = 4096;

    /// <summary>Starts a render: textures used from now on are not released before it ends.</summary>
    public static void BeginRender() => _clock++;

    /// <summary>
    /// Gets a value indicating whether the image can be drawn without its samples: its texture came from the browser's own
    /// <c>ImageBitmap</c> ("mode B", #373) or such a bitmap waits to be uploaded.
    /// </summary>
    public static bool HasNativeImage(DisplayImage image) =>
        Bitmaps.TryGetValue(image, out _) || (Table.TryGetValue(image, out Entry? entry) && entry.Native);

    /// <summary>Gets a value indicating whether the image has a texture or a bitmap waiting for one.</summary>
    public static bool HasTextureOrBitmap(DisplayImage image) => Bitmaps.TryGetValue(image, out _) || Table.TryGetValue(image, out _);

    /// <summary>Gets a value indicating whether the image has a converted texture for this conversion at <paramref name="reduction"/> or a finer one.</summary>
    public static bool HasConverted(DisplayImage image, PdfDefaultColorSpaces defaults, ColorConversion conversion, int reduction) =>
        Table.TryGetValue(image, out Entry? entry) && !entry.Native && ReferenceEquals(entry.Defaults, defaults) && entry.Conversion == conversion
        && entry.Grid.Reduction <= reduction;

    /// <summary>Remembers the browser's <c>ImageBitmap</c> of an image, uploaded by its next <see cref="GetImage"/> instead of converted samples.</summary>
    public static void AttachBitmap(DisplayImage image, int key) => Bitmaps.AddOrUpdate(image, new BitmapKey(key));

    /// <summary>Gets the texture of a colour image; 0 when it is larger than a texture can be.</summary>
    /// <param name="image">The image.</param>
    /// <param name="document">The document, for its colour converters.</param>
    /// <param name="defaults">The default colour spaces.</param>
    /// <param name="conversion">The conversion.</param>
    /// <param name="native">Whether the browser's own decode may stand for the converted samples (<see cref="WebGLRenderer.IsNativeImagePaint"/>).</param>
    /// <param name="samples">The samples to convert, at the reduction drawn (issue #362); null to take the image's own when it must be converted.</param>
    /// <param name="foldSoft">Whether the soft mask folds into the samples (<see cref="ImageConverter.FoldsSoftMask(DisplayImage, DisplayImageSamples, out DisplayImageSamples?)"/>).</param>
    /// <param name="softSamples">The soft mask's samples that fold, or null.</param>
    /// <param name="reduction">The reduction asked for: a texture at it or a finer one serves.</param>
    /// <param name="grid">The texture's samples: their size and reduction, for the texture coordinates of the unit square.</param>
    public static int GetImage(DisplayImage image, PdfDocument document, PdfDefaultColorSpaces defaults, ColorConversion conversion, bool native, DisplayImageSamples? samples, bool foldSoft, DisplayImageSamples? softSamples, int reduction, out TextureGrid grid)
    {
        if (Table.TryGetValue(image, out Entry? entry)
            && (entry.Native ? native : ReferenceEquals(entry.Defaults, defaults) && entry.Conversion == conversion && entry.Grid.Reduction <= reduction))
        {
            entry.LastUse = _clock;
            grid = entry.Grid;
            return entry.Id;
        }

        grid = new TextureGrid(image.Width, image.Height, 0);

        if (native && Bitmaps.TryGetValue(image, out BitmapKey? bitmap))
        {
            Bitmaps.Remove(image);
            int id = ++_next;
            if (WebGLInterop.UploadBitmap(id, bitmap.Key, image.Interpolate))
            {
                if (entry is not null)
                {
                    int index = Entries.IndexOf(entry);
                    if (index >= 0)
                    {
                        Remove(index);
                    }
                }

                var uploaded = new Entry(id, (long)image.Width * image.Height * 4 * 4 / 3, new WeakReference<object>(image), defaults, conversion) { LastUse = _clock, Native = true, Grid = grid };
                Table.AddOrUpdate(image, uploaded);
                Entries.Add(uploaded);
                Bytes += uploaded.Bytes;
                return id;
            }
        }

        if (samples is null)
        {
            // A bitmap that would not upload: the image's own samples (an image that cannot be decoded converts to nothing).
            samples = image.GetSamplesAt(0);
            foldSoft = ImageConverter.FoldsSoftMask(image);
            softSamples = foldSoft ? image.SoftMask!.GetSamplesAt(0) : null;
        }

        if (samples is not { } decoded || decoded.Width > MaxSize || decoded.Height > MaxSize)
        {
            return 0;
        }

        ColorConversion rgb = conversion with { Target = DeviceColorModel.Rgb };
        PdfColorConverter converter = document.GetColorConverter(image.ColorSpace, rgb, defaults);
        PdfColorConverter? baseConverter = image.ColorSpace is PdfIndexedColorSpace indexed ? document.GetColorConverter(indexed.Base, rgb, defaults) : null;
        byte[]? pixels = ImageConverter.Convert(image, decoded, foldSoft, softSamples, converter, baseConverter);
        if (pixels is null)
        {
            return 0;
        }

        try
        {
            grid = new TextureGrid(decoded.Width, decoded.Height, decoded.Reduction);
            return Add(image, entry, grid, 0, pixels, image.Interpolate, defaults, conversion);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    /// <summary>
    /// Gets the texture of a stencil mask (one channel, 255 where it paints), at <paramref name="reduction"/> or a finer one (8-bit
    /// coverage when reduced, issue #362); 0 when too large.
    /// </summary>
    public static int GetMask(DisplayImageMask mask, int reduction, out TextureGrid grid)
    {
        if (Table.TryGetValue(mask, out Entry? entry) && entry.Grid.Reduction <= reduction)
        {
            entry.LastUse = _clock;
            grid = entry.Grid;
            return entry.Id;
        }

        int scale = 1 << reduction;
        grid = new TextureGrid((int)(((long)mask.Width + scale - 1) >> reduction), (int)(((long)mask.Height + scale - 1) >> reduction), reduction);
        if (grid.Width > MaxSize || grid.Height > MaxSize)
        {
            return 0;
        }

        byte[]? pixels = ImageConverter.ConvertStencil(mask, reduction);
        if (pixels is null)
        {
            return 0;
        }

        try
        {
            return Add(mask, entry, grid, 1, pixels, mask.Interpolate, null, default);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    /// <summary>Uploads premultiplied RGBA pixels for one render; the caller releases the texture with <see cref="Release"/>.</summary>
    public static int UploadTemporary(Span<byte> pixels, int width, int height)
    {
        int id = ++_next;
        WebGLInterop.Upload(id, width, height, 0, pixels, interpolate: true);
        return id;
    }

    public static void Release(int id) => WebGLInterop.Release(id);

    /// <summary>Releases the textures whose owners were collected, then the least recently used ones over the budget.</summary>
    public static void Trim()
    {
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            Entry entry = Entries[i];
            if (!entry.Owner.TryGetTarget(out _))
            {
                Remove(i);
            }
        }

        while (Bytes > Budget)
        {
            int oldest = -1;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].LastUse < _clock && (oldest < 0 || Entries[i].LastUse < Entries[oldest].LastUse))
                {
                    oldest = i;
                }
            }

            if (oldest < 0)
            {
                break;
            }

            if (Entries[oldest].Owner.TryGetTarget(out object? owner))
            {
                Table.Remove(owner);
            }

            Remove(oldest);
        }
    }

    private static int Add(object owner, Entry? stale, TextureGrid grid, int format, byte[] pixels, bool interpolate, PdfDefaultColorSpaces? defaults, ColorConversion conversion)
    {
        int width = grid.Width;
        int height = grid.Height;
        if (stale is not null)
        {
            Table.Remove(owner);
            int index = Entries.IndexOf(stale);
            if (index >= 0)
            {
                Remove(index);
            }
        }

        int id = ++_next;
        int length = width * height * (format == 0 ? 4 : 1);
        WebGLInterop.Upload(id, width, height, format, pixels.AsSpan(0, length), interpolate);
        var entry = new Entry(id, length * 4L / 3, new WeakReference<object>(owner), defaults, conversion) { LastUse = _clock, Grid = grid };
        Table.AddOrUpdate(owner, entry);
        Entries.Add(entry);
        Bytes += entry.Bytes;
        return id;
    }

    private static void Remove(int index)
    {
        Entry entry = Entries[index];
        Entries.RemoveAt(index);
        Bytes -= entry.Bytes;
        WebGLInterop.Release(entry.Id);
    }

    private sealed class Entry(int id, long bytes, WeakReference<object> owner, PdfDefaultColorSpaces? defaults, ColorConversion conversion)
    {
        public int Id { get; } = id;

        public long Bytes { get; } = bytes;

        public WeakReference<object> Owner { get; } = owner;

        public PdfDefaultColorSpaces? Defaults { get; } = defaults;

        public ColorConversion Conversion { get; } = conversion;

        public long LastUse { get; set; }

        public bool Native { get; init; }

        /// <summary>Gets the texture's samples: their size and reduction (issue #362).</summary>
        public TextureGrid Grid { get; init; }
    }

    private sealed record BitmapKey(int Key);
}

/// <summary>
/// The samples a texture holds: <see cref="Width"/> × <see cref="Height"/>, reduced by 2^<see cref="Reduction"/> from the image's own
/// (issue #362). Reduced sample i covers source samples i·s to (i + 1)·s, so the unit square takes the first (W / s) / Width of the
/// texture across and (H / s) / Height down (ISO 32000-2 §8.9.4).
/// </summary>
internal readonly record struct TextureGrid(int Width, int Height, int Reduction)
{
    /// <summary>Returns the share of the texture's width the unit square covers for an image <paramref name="sourceWidth"/> samples wide.</summary>
    public float ExtentU(int sourceWidth) => Reduction == 0 ? 1f : (float)((double)sourceWidth / (1 << Reduction) / Width);

    /// <summary>Returns the share of the texture's height the unit square covers for an image <paramref name="sourceHeight"/> samples high.</summary>
    public float ExtentV(int sourceHeight) => Reduction == 0 ? 1f : (float)((double)sourceHeight / (1 << Reduction) / Height);
}
