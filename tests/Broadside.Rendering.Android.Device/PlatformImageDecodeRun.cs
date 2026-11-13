using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Android.Content;
using Android.OS;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;
using AndroidBitmap = Android.Graphics.Bitmap;
using AndroidCanvas = Android.Graphics.Canvas;
using BitmapFactory = Android.Graphics.BitmapFactory;
using Matrix = Broadside.Graphics.Matrix;

namespace Broadside.Rendering.Android.Device;

/// <summary>
/// The platform image decoder cases of issue #376, run with the scaled phase (<see cref="DeviceProtocol.ScaledArgument"/>): each
/// document is opened with <see cref="AndroidImageDecoder"/> and the verification mode, its images are prefetched through
/// <see cref="AndroidCanvasBackend.PrefetchImagesAsync"/> at a scale that asks for one reduction, and the decoder's diagnostics are
/// reported (<see cref="DeviceProtocol.PlatformKey"/>); then what <c>BitmapFactory</c> does with an ICC-tagged JPEG, and the decode
/// and render times against the managed decoder.
/// </summary>
internal static class PlatformImageDecodeRun
{
    /// <summary>The scales at which a 1320 × 948 image across the 612 × 792 page asks for reductions 0 to 3 (two samples per pixel, its height decides).</summary>
    private static readonly double[] PhotoScales = [1, 0.25, 0.125, 0.06];

    public static void Run(Context context, Bundle results)
    {
        foreach ((string name, byte[] file) in Photos())
        {
            for (int reduction = 0; reduction < PhotoScales.Length; reduction++)
            {
                Verify(results, DeviceProtocol.PlatformCase(name, reduction), file, PhotoScales[reduction]);
            }
        }

        foreach (string file in DeviceProtocol.ScaledDctFiles)
        {
            byte[] bytes = ReadAsset(context, "corpus/" + file);
            Verify(results, DeviceProtocol.PlatformCase(file, 0), bytes, 1);
            Verify(results, DeviceProtocol.PlatformCase(file, 2), bytes, 0.25);
        }

        Concurrent(results);
        Fidelity(results);
        Verify(results, DeviceProtocol.PlatformCase(DeviceProtocol.ExifIccPhoto, 0), Page(WithExifAndIcc(CatalogueSamples.Jpeg(640, 400, DctChromaSubsampling.None), SwappedPrimaries())), 1);
        Profiles(results);
        Times(results);
    }

    /// <summary>The generated photographs: 4:4:4, 4:2:2, 4:2:0 and gray, 1320 × 948 across the page.</summary>
    private static IEnumerable<(string Name, byte[] File)> Photos()
    {
        yield return ("photo-444", Page(CatalogueSamples.Jpeg(1320, 948, DctChromaSubsampling.None)));
        yield return ("photo-422", Page(CatalogueSamples.Jpeg(1320, 948, DctChromaSubsampling.Horizontal)));
        yield return ("photo-420", Page(CatalogueSamples.Jpeg(1320, 948, DctChromaSubsampling.HorizontalAndVertical)));
        yield return ("photo-gray", Page(GrayJpeg(1320, 948)));
    }

    /// <summary>
    /// Prefetches the first page drawn at <paramref name="scale"/> with two samples per pixel and reports the platform decoder's
    /// diagnostics, <c>code|message</c> separated by <see cref="DeviceProtocol.LineSeparator"/> (empty when no image was offered), and the prefetch's milliseconds.
    /// </summary>
    private static void Verify(Bundle results, string name, byte[] file, double scale)
    {
        using PdfDocument document = new PdfEngine(new PdfOptions().UseAndroidImageDecoder().WithPlatformImageVerification(true).WithDisplayListCacheBudget(0)).Open(file);
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        using var backend = new AndroidCanvasBackend { ScaledImageDecode = true };
        long start = Stopwatch.GetTimestamp();
        backend.PrefetchImagesAsync(list, options.GetDeviceTransform(page), size.Width, size.Height, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var report = new StringBuilder();
        foreach (Diagnostics.Diagnostic diagnostic in document.Diagnostics)
        {
            if (diagnostic.Code.StartsWith("Platform", StringComparison.Ordinal) || diagnostic.Code == "ImageDecodedManaged")
            {
                report.Append(diagnostic.Code).Append('|').Append(diagnostic.Message.Replace('\n', ' ')).Append(DeviceProtocol.LineSeparator);
            }
        }

        results.PutString(DeviceProtocol.PlatformKey(name, DeviceProtocol.Diagnostics), report.ToString());
        Put(results, DeviceProtocol.PlatformKey(name, DeviceProtocol.Milliseconds), milliseconds);
    }

    /// <summary>
    /// Prefetches one photograph from eight pool threads at once, where the decoder decodes on the calling thread and returns a
    /// completed task, and reports how many times it was called (issue #484: once).
    /// </summary>
    private static void Concurrent(Bundle results)
    {
        const int count = 8;
        var decoder = new CountingDecoder(AndroidImageDecoder.Shared);
        using PdfDocument document = new PdfEngine(new PdfOptions().UsePlatformImageDecoder(decoder).WithDisplayListCacheBudget(0)).Open(Page(CatalogueSamples.Jpeg(1320, 948, DctChromaSubsampling.None)));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithScale(1);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var start = new Barrier(count);
        Task[] prefetches = [.. Enumerable.Range(0, count).Select(_ => Task.Run(() =>
        {
            start.SignalAndWait();
            using var backend = new AndroidCanvasBackend { ScaledImageDecode = true };
            return backend.PrefetchImagesAsync(list, device, size.Width, size.Height, CancellationToken.None).AsTask();
        }))];
        Task.WaitAll(prefetches);
        results.PutString(DeviceProtocol.PlatformKey(DeviceProtocol.ConcurrentPhoto, DeviceProtocol.Decodes), decoder.Calls.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The decoder's samples against the managed decoder's at every reduction, decoded directly (not through the seam's rule): the
    /// photographs, and small pictures with sharp edges and partial MCUs, at each chroma subsampling. Reported as
    /// <c>delivered max psnr over2</c>.
    /// </summary>
    private static void Fidelity(Bundle results)
    {
        (string Name, byte[] Jpeg, int Width, int Height, int Components, DctChromaSubsampling Subsampling)[] cases =
        [
            ("photo-444", CatalogueSamples.Jpeg(1320, 948, DctChromaSubsampling.None), 1320, 948, 3, DctChromaSubsampling.None),
            ("photo-422", CatalogueSamples.Jpeg(1320, 948, DctChromaSubsampling.Horizontal), 1320, 948, 3, DctChromaSubsampling.Horizontal),
            ("photo-420", CatalogueSamples.Jpeg(1320, 948, DctChromaSubsampling.HorizontalAndVertical), 1320, 948, 3, DctChromaSubsampling.HorizontalAndVertical),
            ("photo-gray", GrayJpeg(1320, 948), 1320, 948, 1, DctChromaSubsampling.None),
            ("edges-444", EdgesJpeg(45, 29, DctChromaSubsampling.None), 45, 29, 3, DctChromaSubsampling.None),
            ("edges-422", EdgesJpeg(45, 29, DctChromaSubsampling.Horizontal), 45, 29, 3, DctChromaSubsampling.Horizontal),
            ("edges-420", EdgesJpeg(45, 29, DctChromaSubsampling.HorizontalAndVertical), 45, 29, 3, DctChromaSubsampling.HorizontalAndVertical),
            ("edges-420-large", EdgesJpeg(203, 131, DctChromaSubsampling.HorizontalAndVertical), 203, 131, 3, DctChromaSubsampling.HorizontalAndVertical),
        ];
        foreach ((string name, byte[] jpeg, int width, int height, int components, DctChromaSubsampling subsampling) in cases)
        {
            for (int reduction = 0; reduction <= 3; reduction++)
            {
                var request = new PlatformImageRequest
                {
                    Filter = new CosName("DCTDecode"),
                    Width = width,
                    Height = height,
                    Components = components,
                    BitsPerComponent = 8,
                    ColorTransform = components == 3,
                    ChromaSubsampled = subsampling != DctChromaSubsampling.None,
                    Reduction = reduction,
                };
                using DecodedImage platform = AndroidImageDecoder.Decode(jpeg, request) ?? throw new InvalidOperationException(name + ": BitmapFactory decoded nothing.");
                using DecodedImage managed = Managed(jpeg, width, height, components, platform.Reduction);
                (int max, double psnr, double over) = Difference(platform, managed);
                results.PutString(
                    DeviceProtocol.PlatformKey(DeviceProtocol.PlatformCase(name, reduction), DeviceProtocol.Fidelity),
                    string.Create(CultureInfo.InvariantCulture, $"{platform.Reduction} {max} {(double.IsPositiveInfinity(psnr) ? "inf" : psnr.ToString("0.0", CultureInfo.InvariantCulture))} {over * 100:0.###}"));
            }
        }
    }

    /// <summary>The largest difference, the PSNR and the share of pixels more than 2 levels apart (the verification mode's measures).</summary>
    private static (int Max, double Psnr, double Over) Difference(DecodedImage a, DecodedImage b)
    {
        int components = a.Components;
        int max = 0;
        long over = 0;
        double squares = 0;
        for (int y = 0; y < a.Height; y++)
        {
            ReadOnlySpan<byte> rowA = a.GetRow(y)[..(a.Width * components)];
            ReadOnlySpan<byte> rowB = b.GetRow(y)[..(a.Width * components)];
            for (int x = 0; x < rowA.Length; x += components)
            {
                int pixel = 0;
                for (int c = 0; c < components; c++)
                {
                    int d = Math.Abs(rowA[x + c] - rowB[x + c]);
                    squares += d * d;
                    pixel = Math.Max(pixel, d);
                }

                max = Math.Max(max, pixel);
                over += pixel > 2 ? 1 : 0;
            }
        }

        long pixels = (long)a.Width * a.Height;
        double mse = squares / (pixels * components);
        return (max, mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse), over / (double)pixels);
    }

    /// <summary>The corpus's DCT test picture (a ramp, a dark disc and a white bar: sharp chroma edges) as a JPEG of the given size.</summary>
    private static byte[] EdgesJpeg(int width, int height, DctChromaSubsampling subsampling)
    {
        byte[] rgb = new byte[width * height * 3];
        int radius = height * 3 / 5;
        for (int y = 0, i = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++, i += 3)
            {
                int r = 255 * x / (width - 1);
                int g = 255 * y / (height - 1);
                int b = 255 - (127 * (x + y) / (width + height - 2));
                if ((((2 * x) - width) * ((2 * x) - width)) + (((2 * y) - height) * ((2 * y) - height)) <= radius * radius)
                {
                    (r, g, b) = (r / 4, g / 4, 64);
                }

                if (height / 3 <= y && y < (height / 3) + 3)
                {
                    r = g = b = 255;
                }

                (rgb[i], rgb[i + 1], rgb[i + 2]) = ((byte)r, (byte)g, (byte)b);
            }
        }

        var output = new ArrayBufferWriter<byte>();
        var dictionary = new CosDictionary { [new CosName("Width")] = new CosInteger(width), [new CosName("Height")] = new CosInteger(height) };
        new DctEncoder(90, subsampling).Encode(rgb, output, new StreamEncoderContext { StreamDictionary = dictionary });
        return output.WrittenSpan.ToArray();
    }

    /// <summary>
    /// What <c>BitmapFactory</c> does with a JPEG whose ICC profile swaps red and blue, against the managed decode: decoded as it is,
    /// with <c>inPreferredColorSpace</c> sRGB, and with the profile's payload zero-filled as the core hands it over. The largest
    /// difference of each is reported, and the bitmap's colour space.
    /// </summary>
    private static void Profiles(Bundle results)
    {
        byte[] plain = CatalogueSamples.Jpeg(640, 400, DctChromaSubsampling.None);
        byte[] tagged = WithExifAndIcc(plain, SwappedPrimaries());
        byte[] cleared = WithExifAndIcc(plain, new byte[SwappedPrimaries().Length]);
        using DecodedImage managed = Managed(plain, 640, 400, 3, 0);
        Profile(results, DeviceProtocol.ProfileAsIs, tagged, managed, srgb: false);
        Profile(results, DeviceProtocol.ProfileSrgb, tagged, managed, srgb: true);
        Profile(results, DeviceProtocol.ProfileCleared, cleared, managed, srgb: false);
    }

    private static void Profile(Bundle results, string variant, byte[] jpeg, DecodedImage managed, bool srgb)
    {
        using var options = new BitmapFactory.Options { InPreferredConfig = AndroidBitmap.Config.Argb8888, InScaled = false };
        if (srgb && OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            options.InPreferredColorSpace = global::Android.Graphics.ColorSpace.Get(global::Android.Graphics.ColorSpace.Named.Srgb!);
        }

        using AndroidBitmap bitmap = BitmapFactory.DecodeByteArray(jpeg, 0, jpeg.Length, options)!;
        byte[] rgba = new byte[bitmap.ByteCount];
        using (var buffer = Java.Nio.ByteBuffer.Wrap(rgba))
        {
            bitmap.CopyPixelsToBuffer(buffer);
            buffer.Rewind();
            buffer.Get(rgba);
        }

        int max = 0;
        for (int y = 0; y < managed.Height; y++)
        {
            ReadOnlySpan<byte> row = managed.GetRow(y);
            for (int x = 0; x < managed.Width; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    max = Math.Max(max, Math.Abs(row[(x * 3) + c] - rgba[(y * bitmap.RowBytes) + (x * 4) + c]));
                }
            }
        }

        string space = OperatingSystem.IsAndroidVersionAtLeast(26) ? bitmap.ColorSpace?.Name ?? "none" : "n/a";
        results.PutString(DeviceProtocol.PlatformKey(variant, DeviceProtocol.MaxDifference), max.ToString(CultureInfo.InvariantCulture));
        results.PutString(DeviceProtocol.PlatformKey(variant, DeviceProtocol.ColorSpaceName), space);
    }

    /// <summary>
    /// The decode times of the 2550 × 3150 catalogue photograph (4:4:4 and 4:2:0) at each reduction, managed and with
    /// <c>BitmapFactory</c>, the median of five after one warm-up; and the time to draw the catalogue cover 390 pixels wide from a
    /// fresh document (list, prefetch, render), managed and with the decoder, the median of three.
    /// </summary>
    private static void Times(Bundle results)
    {
        foreach ((string name, DctChromaSubsampling subsampling) in (ReadOnlySpan<(string, DctChromaSubsampling)>)[("444", DctChromaSubsampling.None), ("420", DctChromaSubsampling.HorizontalAndVertical)])
        {
            byte[] jpeg = CatalogueSamples.Jpeg(2550, 3150, subsampling);
            for (int reduction = 0; reduction <= 3; reduction++)
            {
                var request = new PlatformImageRequest
                {
                    Filter = new CosName("DCTDecode"),
                    Width = 2550,
                    Height = 3150,
                    Components = 3,
                    BitsPerComponent = 8,
                    ColorTransform = true,
                    ChromaSubsampled = subsampling != DctChromaSubsampling.None,
                    Reduction = reduction,
                };
                int r = reduction;
                Put(results, DeviceProtocol.TimeKey($"decode-{name}-r{r}", DeviceProtocol.Managed), Median(5, () => Managed(jpeg, 2550, 3150, 3, r).Dispose()));
                Put(results, DeviceProtocol.TimeKey($"decode-{name}-r{r}", DeviceProtocol.Platform), Median(5, () => AndroidImageDecoder.Decode(jpeg, request)!.Dispose()));
            }

            byte[] cover = CatalogueSamples.Cover(2550, 3150, subsampling);
            foreach (int width in (ReadOnlySpan<int>)[390, 1080])
            {
                foreach (bool platform in (ReadOnlySpan<bool>)[false, true])
                {
                    string variant = platform ? DeviceProtocol.Platform : DeviceProtocol.Managed;
                    Draw(cover, width, platform);
                    double[] prefetch = new double[5];
                    double[] render = new double[5];
                    double[] total = new double[5];
                    for (int i = 0; i < 5; i++)
                    {
                        (prefetch[i], render[i]) = Draw(cover, width, platform);
                        total[i] = prefetch[i] + render[i];
                    }

                    Put(results, DeviceProtocol.TimeKey($"cover-{name}-{width}", variant), Middle(total));
                    Put(results, DeviceProtocol.TimeKey($"cover-{name}-{width}-prefetch", variant), Middle(prefetch));
                    Put(results, DeviceProtocol.TimeKey($"cover-{name}-{width}-render", variant), Middle(render));
                }
            }
        }
    }

    /// <summary>
    /// Draws the cover <paramref name="width"/> pixels wide from a fresh document with scaled decode, as a viewer's first paint: the
    /// prefetch (with the decoder when asked; nothing to do without one), then the render; returns the milliseconds of each.
    /// </summary>
    private static (double Prefetch, double Render) Draw(byte[] file, int width, bool platform)
    {
        PdfOptions options = new PdfOptions().WithDisplayListCacheBudget(0);
        using PdfDocument document = new PdfEngine(platform ? options.UseAndroidImageDecoder() : options).Open(file);
        PdfPage page = document.Pages[0];
        RenderOptions render = new RenderOptions().WithScale(width / 612.0);
        PixelSize size = render.GetPixelSize(page);
        Matrix device = render.GetDeviceTransform(page);
        DisplayList list = page.CreateDisplayList();
        using var backend = new AndroidCanvasBackend { ScaledImageDecode = true };
        using AndroidBitmap bitmap = AndroidBitmap.CreateBitmap(size.Width, size.Height, AndroidBitmap.Config.Argb8888!);
        using var canvas = new AndroidCanvas(bitmap);
        long start = Stopwatch.GetTimestamp();
        backend.PrefetchImagesAsync(list, device, size.Width, size.Height, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        double prefetch = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        backend.Render(list, canvas, device);
        return (prefetch, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    private static double Middle(double[] times)
    {
        Array.Sort(times);
        return times[times.Length / 2];
    }

    private static double Median(int runs, Action action)
    {
        action();
        double[] times = new double[runs];
        for (int i = 0; i < runs; i++)
        {
            long start = Stopwatch.GetTimestamp();
            action();
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(times);
        return times[runs / 2];
    }

    private static DecodedImage Managed(byte[] jpeg, int width, int height, int components, int reduction) =>
        new DctDecodeFilter().DecodeImage(jpeg, new ImageFilterContext(new FilterContext())
        {
            Width = width,
            Height = height,
            BitsPerComponent = 8,
            ColorComponents = components,
            Reduction = reduction,
        }) ?? throw new InvalidOperationException("The managed decoder decoded nothing.");

    /// <summary>A gray baseline JPEG of the photograph's green samples.</summary>
    private static byte[] GrayJpeg(int width, int height)
    {
        byte[] rgb = CatalogueSamples.Photo(width, height);
        byte[] gray = new byte[width * height];
        for (int i = 0; i < gray.Length; i++)
        {
            gray[i] = rgb[(i * 3) + 1];
        }

        var output = new ArrayBufferWriter<byte>();
        var dictionary = new CosDictionary { [new CosName("Width")] = new CosInteger(width), [new CosName("Height")] = new CosInteger(height) };
        new DctEncoder(90).Encode(gray, output, new StreamEncoderContext { StreamDictionary = dictionary });
        return output.WrittenSpan.ToArray();
    }

    /// <summary>A letter page filled by the JPEG, placed byte for byte.</summary>
    private static byte[] Page(byte[] jpeg)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImageFromJpeg(jpeg);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(image, 0, 0, 612, 792);
        }

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    /// <summary>The sRGB profile with its red and blue colorants swapped (their tag table offsets exchanged): a converting decoder would swap the channels.</summary>
    private static byte[] SwappedPrimaries()
    {
        byte[] profile = PdfIccBasedColorSpace.CreateSrgbProfileData();
        int tags = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(profile.AsSpan(128));
        int red = -1;
        int blue = -1;
        for (int i = 0; i < tags; i++)
        {
            int entry = 132 + (i * 12);
            string signature = Encoding.ASCII.GetString(profile, entry, 4);
            red = signature == "rXYZ" ? entry : red;
            blue = signature == "bXYZ" ? entry : blue;
        }

        Span<byte> temporary = stackalloc byte[4];
        profile.AsSpan(red + 4, 4).CopyTo(temporary);
        profile.AsSpan(blue + 4, 4).CopyTo(profile.AsSpan(red + 4));
        temporary.CopyTo(profile.AsSpan(blue + 4));
        return profile;
    }

    /// <summary>Inserts an APP1 Exif segment with orientation 6 and an APP2 ICC profile segment after SOI.</summary>
    private static byte[] WithExifAndIcc(byte[] jpeg, byte[] profile)
    {
        byte[] exif = [.. "Exif\0\0MM\0*\0\0\0\b\0\u0001\u0001\u0012\0\u0003\0\0\0\u0001\0\u0006\0\0\0\0\0\0"u8];
        byte[] icc = [.. "ICC_PROFILE\0\u0001\u0001"u8, .. profile];
        return [0xFF, 0xD8, .. Segment(0xE1, exif), .. Segment(0xE2, icc), .. jpeg.AsSpan(2)];

        static byte[] Segment(byte marker, byte[] payload) =>
            [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];
    }

    private static void Put(Bundle results, string key, double value) => results.PutString(key, value.ToString("0.0##", CultureInfo.InvariantCulture));

    private static byte[] ReadAsset(Context context, string name)
    {
        using Stream asset = context.Assets!.Open(name);
        using var memory = new MemoryStream();
        asset.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>Counts the decodes it passes on to <paramref name="inner"/>.</summary>
    private sealed class CountingDecoder(IPlatformImageDecoder inner) : IPlatformImageDecoder
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string Name => inner.Name;

        public PlatformImageFidelity Fidelity => inner.Fidelity;

        public PlatformImageFeatures Features => inner.Features;

        public bool CanDecode(in PlatformImageRequest request) => inner.CanDecode(request);

        public ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return inner.DecodeAsync(encoded, request, cancellationToken);
        }
    }
}
