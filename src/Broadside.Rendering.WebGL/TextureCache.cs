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

    /// <summary>Gets the texture of a colour image; 0 when it is larger than a texture can be.</summary>
    public static int GetImage(DisplayImage image, PdfDocument document, PdfDefaultColorSpaces defaults, ColorConversion conversion)
    {
        if (Table.TryGetValue(image, out Entry? entry) && ReferenceEquals(entry.Defaults, defaults) && entry.Conversion == conversion)
        {
            entry.LastUse = _clock;
            return entry.Id;
        }

        if (image.Width > MaxSize || image.Height > MaxSize)
        {
            return 0;
        }

        ColorConversion rgb = conversion with { Target = DeviceColorModel.Rgb };
        PdfColorConverter converter = document.GetColorConverter(image.ColorSpace, rgb, defaults);
        PdfColorConverter? baseConverter = image.ColorSpace is PdfIndexedColorSpace indexed ? document.GetColorConverter(indexed.Base, rgb, defaults) : null;
        byte[]? pixels = ImageConverter.Convert(image, converter, baseConverter);
        if (pixels is null)
        {
            return 0;
        }

        try
        {
            return Add(image, entry, image.Width, image.Height, 0, pixels, image.Interpolate, defaults, conversion);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    /// <summary>Gets the texture of a stencil mask (one channel, 255 where it paints); 0 when too large.</summary>
    public static int GetMask(DisplayImageMask mask)
    {
        if (Table.TryGetValue(mask, out Entry? entry))
        {
            entry.LastUse = _clock;
            return entry.Id;
        }

        if (mask.Width > MaxSize || mask.Height > MaxSize)
        {
            return 0;
        }

        byte[]? pixels = ImageConverter.ConvertStencil(mask);
        if (pixels is null)
        {
            return 0;
        }

        try
        {
            return Add(mask, null, mask.Width, mask.Height, 1, pixels, mask.Interpolate, null, default);
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

    private static int Add(object owner, Entry? stale, int width, int height, int format, byte[] pixels, bool interpolate, PdfDefaultColorSpaces? defaults, ColorConversion conversion)
    {
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
        var entry = new Entry(id, length * 4L / 3, new WeakReference<object>(owner), defaults, conversion) { LastUse = _clock };
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
    }
}
