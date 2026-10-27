using System.Globalization;
using System.Runtime.InteropServices;
using Android.Content;
using Android.OS;
using Broadside.Graphics;
using Broadside.TestSupport;
using AndroidBitmap = Android.Graphics.Bitmap;
using AndroidCanvas = Android.Graphics.Canvas;
using AndroidColor = Android.Graphics.Color;
using Matrix = Broadside.Graphics.Matrix;

namespace Broadside.Rendering.Android.Device;

/// <summary>
/// The scaled image decode cases of issue #428 (the Android half of #362), run on the device when the host asks
/// (<see cref="DeviceProtocol.ScaledArgument"/>): each renders a page through <see cref="AndroidCanvasBackend"/> with and without
/// scaled image decode and reports the comparison (<see cref="ScaledImagePages.Compare"/>) and the pixels the backend converted, so
/// that the host asserts on figures rather than pulling large renders back.
/// </summary>
internal static class ScaledImageDecodeRun
{
    /// <summary>The catalogue cover's phone widths, in pixels.</summary>
    private static readonly int[] CoverWidths = [390, 300, 150];

    public static void Run(Context context, Bundle results)
    {
        foreach (int width in CoverWidths)
        {
            Compare(results, DeviceProtocol.CoverCase(width), CatalogueSamples.Cover(), width / 612.0);
        }

        var fonts = new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps().UseSystemFontResolver(null).WithDisplayListCacheBudget(0);
        foreach (string file in DeviceProtocol.ScaledDctFiles)
        {
            using PdfDocument document = new PdfEngine(fonts).Open(ReadAsset(context, "corpus/" + file));
            Compare(results, file, document, 0.25);
        }

        foreach (string page in ScaledImagePages.Names)
        {
            Compare(results, page, ScaledImagePages.Page(page), 0.25);
        }

        // A strongly curved tint transform: the codec may average at most 1/2 before conversion; the canvas resamples the rest.
        const string space = "[/Separation /Spot /DeviceRGB << /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0.2 0.6 0.9] /N 2 >>]";
        using (PdfDocument curved = PdfDocument.Open(
            ReducingImageFilter.Page(ReducingImageFilter.Photo(1024, 1024, 1), 1024, 1024, space, ScaledImagePages.Paint),
            new PdfOptions().UseFilter(new ReducingImageFilter()).WithDisplayListCacheBudget(0)))
        {
            Compare(results, DeviceProtocol.CurvedCase, curved, 0.0625);
        }

        // Each case opens the cover anew: the document keeps the samples it decoded, and a finer decode serves a coarser request.
        OneToOne(results);
        PageRenders(results);
        OneSamplePerPixel(results);
        Zoom(results);
        Allocation(results);
    }

    private static PdfOptions Uncached() => new PdfOptions().WithDisplayListCacheBudget(0);

    /// <summary>Opens the file without a display-list cache and compares its first page (see the other overload).</summary>
    private static void Compare(Bundle results, string name, byte[] file, double scale)
    {
        using PdfDocument document = PdfDocument.Open(file, Uncached());
        Compare(results, name, document, scale);
    }

    /// <summary>Renders the page reduced, then in full (the reduced first: a finer decode serves a coarser request), and reports the comparison.</summary>
    private static void Compare(Bundle results, string name, PdfDocument document, double scale)
    {
        (byte[] reduced, long reducedPixels, long kept) = Render(document, scale, scaled: true);
        (byte[] full, long fullPixels, _) = Render(document, scale, scaled: false);
        (double psnr, double over8, int max) = ScaledImagePages.Compare(full, reduced);
        Put(results, DeviceProtocol.ScaledKey(name, DeviceProtocol.Psnr), psnr);
        Put(results, DeviceProtocol.ScaledKey(name, DeviceProtocol.Over8), over8);
        Put(results, DeviceProtocol.ScaledKey(name, DeviceProtocol.MaxDifference), max);
        Put(results, DeviceProtocol.ScaledKey(name, DeviceProtocol.ReducedPixels), reducedPixels);
        Put(results, DeviceProtocol.ScaledKey(name, DeviceProtocol.FullPixels), fullPixels);
        Put(results, DeviceProtocol.ScaledKey(name, DeviceProtocol.KeptBytes), kept);
    }

    /// <summary>At one device pixel per sample nothing is reduced: the page renders are identical with and without scaled decode.</summary>
    private static void OneToOne(Bundle results)
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), Uncached());
        RenderOptions options = new RenderOptions().WithScale(2550 / 612.0).WithRegion(new PdfRectangle(100, 100, 160, 160));
        byte[] full = Page(document, options.WithScaledImageDecode(false), out _);
        byte[] reduced = Page(document, options.WithScaledImageDecode(true), out _);
        results.PutString(DeviceProtocol.ScaledKey(DeviceProtocol.OneToOneCase, DeviceProtocol.Identical), full.AsSpan().SequenceEqual(reduced) ? "yes" : "no");
    }

    /// <summary>Page renders convert every sample unless the options ask; a new backend's list renders do too.</summary>
    private static void PageRenders(Bundle results)
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), Uncached());
        Page(document, new RenderOptions().WithScale(0.25).WithScaledImageDecode(true), out long viewer);
        Page(document, new RenderOptions().WithScale(0.25), out long byDefault);
        using var backend = new AndroidCanvasBackend();
        Put(results, DeviceProtocol.ScaledKey(DeviceProtocol.PageCase, DeviceProtocol.ReducedPixels), viewer);
        Put(results, DeviceProtocol.ScaledKey(DeviceProtocol.PageCase, DeviceProtocol.FullPixels), byDefault);
        results.PutString(
            DeviceProtocol.ScaledKey(DeviceProtocol.PageCase, DeviceProtocol.Defaults),
            string.Create(CultureInfo.InvariantCulture, $"{new RenderOptions().ScaledImageDecode} {backend.ScaledImageDecode} {backend.ImageSamplesPerPixel}"));
    }

    /// <summary>One sample per pixel reduces one step further than two.</summary>
    private static void OneSamplePerPixel(Bundle results)
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), Uncached());
        (_, long one, _) = Render(document, 390 / 612.0, scaled: true, samplesPerPixel: 1);
        (_, long two, _) = Render(document, 390 / 612.0, scaled: true);
        Put(results, DeviceProtocol.ScaledKey(DeviceProtocol.OneSampleCase, DeviceProtocol.ReducedPixels), one);
        Put(results, DeviceProtocol.ScaledKey(DeviceProtocol.OneSampleCase, DeviceProtocol.FullPixels), two);
        bool rejected = false;
        try
        {
            using var backend = new AndroidCanvasBackend { ImageSamplesPerPixel = 0.5 };
        }
        catch (ArgumentOutOfRangeException)
        {
            rejected = true;
        }

        results.PutString(DeviceProtocol.ScaledKey(DeviceProtocol.OneSampleCase, DeviceProtocol.Identical), rejected ? "yes" : "no");
    }

    /// <summary>
    /// Zoom in (1/8 to 1/2), then out again, four tiles at each: the half conversion serves the eighth, as the document's samples do,
    /// so the backend converts two images in all.
    /// </summary>
    private static void Zoom(Bundle results)
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), Uncached());
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        using var backend = new AndroidCanvasBackend { ScaledImageDecode = true };
        using AndroidBitmap tile = AndroidBitmap.CreateBitmap(64, 64, AndroidBitmap.Config.Argb8888!);
        using var canvas = new AndroidCanvas(tile);
        foreach (double scale in (double[])[150 / 612.0, 390 / 612.0, 150 / 612.0])
        {
            Matrix device = new RenderOptions().WithScale(scale).GetDeviceTransform(page);
            for (int i = 0; i < 4; i++)
            {
                backend.Render(list, canvas, device * Matrix.CreateTranslation(-64 * i, -64 * i));
            }
        }

        Put(results, DeviceProtocol.ScaledKey(DeviceProtocol.ZoomCase, DeviceProtocol.Images), backend.ImagesCreated);
        Put(results, DeviceProtocol.ScaledKey(DeviceProtocol.ZoomCase, DeviceProtocol.ReducedPixels), backend.ImagePixelsConverted);
    }

    /// <summary>What a warm replay of a list whose image is reduced allocates (only an optimized build's figure means anything).</summary>
    private static void Allocation(Bundle results)
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover());
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithScale(0.25);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using AndroidBitmap bitmap = AndroidBitmap.CreateBitmap(size.Width, size.Height, AndroidBitmap.Config.Argb8888!);
        using var canvas = new AndroidCanvas(bitmap);
        using var backend = new AndroidCanvasBackend { ScaledImageDecode = true };
        Action render = () => backend.Render(list, canvas, device);
        Put(results, DeviceProtocol.ScaledKey(DeviceProtocol.AllocationCase, DeviceProtocol.Allocated), Allocations.Measure(render, warmUpCalls: 3));
    }

    /// <summary>A list render on white, as a viewer's tile is drawn: the pixels, the pixels the backend converted and the bytes it keeps.</summary>
    private static (byte[] Pixels, long Converted, long Kept) Render(PdfDocument document, double scale, bool scaled, double samplesPerPixel = 2)
    {
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        using AndroidBitmap bitmap = AndroidBitmap.CreateBitmap(size.Width, size.Height, AndroidBitmap.Config.Argb8888!);
        using var canvas = new AndroidCanvas(bitmap);
        canvas.DrawColor(AndroidColor.White);
        using var backend = new AndroidCanvasBackend { ScaledImageDecode = scaled, ImageSamplesPerPixel = samplesPerPixel };
        backend.Render(page.CreateDisplayList(), canvas, options.GetDeviceTransform(page));
        return (Pixels(bitmap), backend.ImagePixelsConverted, backend.CachedImageBytes);
    }

    private static byte[] Page(PdfDocument document, RenderOptions options, out long converted)
    {
        using var backend = new AndroidCanvasBackend();
        using AndroidBitmap bitmap = backend.Render(document.Pages[0], options);
        converted = backend.ImagePixelsConverted;
        return Pixels(bitmap);
    }

    /// <summary>The bitmap's pixels as B, G, R, A bytes.</summary>
    private static byte[] Pixels(AndroidBitmap bitmap)
    {
        int[] argb = new int[bitmap.Width * bitmap.Height];
        bitmap.GetPixels(argb, 0, bitmap.Width, 0, 0, bitmap.Width, bitmap.Height);
        return MemoryMarshal.AsBytes(argb.AsSpan()).ToArray();
    }

    private static void Put(Bundle results, string key, double value) => results.PutString(key, value.ToString("R", CultureInfo.InvariantCulture));

    private static void Put(Bundle results, string key, long value) => results.PutString(key, value.ToString(CultureInfo.InvariantCulture));

    private static byte[] ReadAsset(Context context, string name)
    {
        using Stream asset = context.Assets!.Open(name);
        using var memory = new MemoryStream();
        asset.CopyTo(memory);
        return memory.ToArray();
    }
}
