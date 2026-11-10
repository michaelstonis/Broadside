using System.Diagnostics;
using System.Globalization;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// Issue #375's measurements: ImageIO against the managed decoder, at full size and each reduction, in each way ImageIO can serve a
/// reduction, with decode times. Explicit: run with <c>--explicit only</c> and read the output.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ImageIOMeasurements(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    public void Fidelity_and_time_against_the_managed_decoder()
    {
        var images = new List<(string Name, byte[] Jpeg, int Width, int Height, int Components)>
        {
            ("cover 2550x3150 4:4:4", CatalogueSamples.Jpeg(), 2550, 3150, 3),
            ("cover 2550x3150 4:2:0", CatalogueSamples.Jpeg(2550, 3150, DctChromaSubsampling.HorizontalAndVertical), 2550, 3150, 3),
            ("photo 5280x3792 4:2:0", CatalogueSamples.Jpeg(5280, 3792, DctChromaSubsampling.HorizontalAndVertical), 5280, 3792, 3),
        };
        foreach (string path in Directory.GetFiles(ImageIOTesting.VectorDirectory, "*.jpg").Order(StringComparer.Ordinal))
        {
            byte[] jpeg = File.ReadAllBytes(path);
            if (ImageIOTesting.Request(jpeg, 0) is { } request)
            {
                images.Add((Path.GetFileNameWithoutExtension(path), jpeg, request.Width, request.Height, request.Components));
            }
        }

        foreach ((string name, byte[] jpeg, int width, int height, int components) in images)
        {
            PlatformImageRequest? found = ImageIOTesting.Request(jpeg, 0);
            if (found is not { } request)
            {
                Log($"{name}: not eligible");
                continue;
            }

            for (int r = 0; r <= 3; r++)
            {
                PlatformImageRequest at = request with { Reduction = r };
                (DecodedImage? managed, double managedMs) = Time(() => ImageIOTesting.Managed(jpeg, at));
                using (managed)
                {
                    // Full: ImageIO at full size, averaged to the reduction (r > 0); subsample: kCGImageSourceSubsampleFactor.
                    foreach (string way in r == 0 ? (string[])["full"] : ["full then average", "subsample"])
                    {
                        (DecodedImage? platform, double ms) = Time(() => way == "subsample"
                            ? ImageIODecoder.Decode(jpeg, at, r)
                            : ImageIOTesting.Average(ImageIODecoder.Decode(jpeg, request, 0), r));
                        using (platform)
                        {
                            string fidelity = platform is null || managed is null
                                ? "failed"
                                : platform.Reduction != managed.Reduction
                                    ? $"delivered r{platform.Reduction}"
                                    : ImageIOTesting.Compare(platform, managed).ToString();
                            Log(string.Create(CultureInfo.InvariantCulture, $"{name} ({width}x{height}x{components}) r{r} {way}: managed {managedMs:0.0} ms, imageio {ms:0.0} ms, {fidelity}"));
                        }
                    }
                }
            }
        }
    }

    [Fact(Explicit = true)]
    public async Task Verification_over_the_real_world_corpus()
    {
        // The files listed in BROADSIDE_IMAGEIO_FILES (paths under BROADSIDE_CORPUS_DIR), each image verified against the managed
        // decoder at full size and at the reduction a quarter-scale view asks for.
        string root = Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR") ?? string.Empty;
        string list = Environment.GetEnvironmentVariable("BROADSIDE_IMAGEIO_FILES") ?? string.Empty;
        foreach (string file in await File.ReadAllLinesAsync(list, TestContext.Current.CancellationToken))
        {
            foreach (double scale in new[] { 1.0, 0.25 })
            {
                try
                {
                    var options = new PdfOptions().UsePlatformImageDecoder(ImageIODecoder.Shared).WithPlatformImageVerification(true).WithDisplayListCacheBudget(0);
                    using PdfDocument document = PdfDocument.Open(Path.Combine(root, file), options);
                    for (int i = 0; i < Math.Min(document.Pages.Count, 10); i++)
                    {
                        Graphics.Matrix device = new RenderOptions().WithScale(scale).GetDeviceTransform(document.Pages[i]);
                        await document.Pages[i].CreateDisplayList().PrefetchImagesAsync(device, null, scale == 1 ? 0 : 2, TestContext.Current.CancellationToken);
                    }

                    foreach (Diagnostics.Diagnostic diagnostic in document.Diagnostics)
                    {
                        if (diagnostic.Code is "PlatformImageVerified" or "PlatformImageMismatch" or "ImageDecodedManaged")
                        {
                            Log($"{file}\t{scale}\t{diagnostic.Code}\t{diagnostic.Message}");
                        }
                    }
                }
#pragma warning disable CA1031 // a measurement: a file that does not open is skipped
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    Log($"{file}\t{scale}\terror\t{exception.GetType().Name}");
                }
            }
        }
    }

    [Fact(Explicit = true)]
    public async Task Fidelity_by_coding_over_the_real_world_corpus()
    {
        // Every image the core offers in the files of BROADSIDE_IMAGEIO_FILES, with its coding (components, subsampling, progressive,
        // the luma quantizer's DC and mean step) and ImageIO's full-size difference from the managed decoder.
        string root = Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR") ?? string.Empty;
        string list = Environment.GetEnvironmentVariable("BROADSIDE_IMAGEIO_FILES") ?? string.Empty;
        foreach (string file in await File.ReadAllLinesAsync(list, TestContext.Current.CancellationToken))
        {
            try
            {
                var probe = new ProbingDecoder(file, Log);
                using PdfDocument document = PdfDocument.Open(Path.Combine(root, file), new PdfOptions().UsePlatformImageDecoder(probe).WithDisplayListCacheBudget(0));
                for (int i = 0; i < Math.Min(document.Pages.Count, 10); i++)
                {
                    Graphics.Matrix device = new RenderOptions().GetDeviceTransform(document.Pages[i]);
                    await document.Pages[i].CreateDisplayList().PrefetchImagesAsync(device, null, 0, TestContext.Current.CancellationToken);
                }
            }
#pragma warning disable CA1031 // a measurement: a file that does not open is skipped
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }

    /// <summary>Logs each offered image's coding and ImageIO's difference from the managed decoder; decodes nothing for the document.</summary>
    private sealed class ProbingDecoder(string file, Action<string> log) : IPlatformImageDecoder
    {
        public string Name => "probe";

        public PlatformImageFidelity Fidelity => PlatformImageFidelity.Tolerance;

        public PlatformImageFeatures Features => PlatformImageFeatures.ArithmeticCoding;

        public bool CanDecode(in PlatformImageRequest request) => true;

        public ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
        {
            byte[] jpeg = encoded.ToArray();
            using DecodedImage? platform = ImageIODecoder.Decode(jpeg, request, 0);
            using DecodedImage? managed = ImageIOTesting.Managed(jpeg, request);
            string result = platform is null || managed is null ? "failed" : ImageIOTesting.Compare(platform, managed).ToString();
            double mean = ImageIODecoder.MeanQuantizerStep(jpeg);
            log(string.Create(CultureInfo.InvariantCulture, $"{file}\t{request.Width}x{request.Height}\tc{request.Components}\t{(request.ChromaSubsampled ? "sub" : "full")}\t{(request.Progressive ? "prog" : "seq")}\t{(request.ArithmeticCoding ? "arith" : "huff")}\tmean{mean:0.0}\t{result}"));
            return ValueTask.FromResult<DecodedImage?>(null);
        }
    }

    [Fact(Explicit = true)]
    public async Task First_render_of_the_cover_with_and_without_ImageIO()
    {
        // A fresh document each time, so the first render decodes the photograph: with ImageIO the prefetch decodes it, without it
        // the replay decodes it with the managed codec. Full size (the renderer's scaled decode is off), 1 point per pixel.
        byte[] file = CatalogueSamples.Cover();
        foreach (bool imageIO in new[] { false, true, false, true })
        {
            double best = double.MaxValue;
            for (int i = 0; i < 5; i++)
            {
                PdfOptions options = new PdfOptions().WithDisplayListCacheBudget(0);
                options = imageIO ? options.UsePlatformImageDecoder(ImageIODecoder.Shared) : options;
#pragma warning disable CA2000 // disposed by the using declaration; the analyzer loses it across the await below
                using PdfDocument document = PdfDocument.Open(file, options);
#pragma warning restore CA2000
                PdfPage page = document.Pages[0];
                using global::CoreGraphics.CGBitmapContext context = BitmapRendering.CreateContext(612, 792, out _, out _);
                using var renderer = new CoreGraphicsRenderer();
                long start = Stopwatch.GetTimestamp();
                Rendering.DisplayList list = page.CreateDisplayList();
                Graphics.Matrix device = new RenderOptions().GetDeviceTransform(page);
                await renderer.PrefetchImagesAsync(list, device, 612, 792, TestContext.Current.CancellationToken);
                renderer.Render(list, context, device);
                best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            Log(string.Create(CultureInfo.InvariantCulture, $"first render, {(imageIO ? "ImageIO" : "managed")}: {best:0.0} ms"));
        }
    }

    /// <summary>Writes a line to the test output and, when <c>BROADSIDE_IMAGEIO_LOG</c> names a file, appends it there.</summary>
    private void Log(string line)
    {
        output.WriteLine(line);
        if (Environment.GetEnvironmentVariable("BROADSIDE_IMAGEIO_LOG") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private static (DecodedImage? Image, double Ms) Time(Func<DecodedImage?> decode)
    {
        decode()?.Dispose();
        double best = double.MaxValue;
        DecodedImage? kept = null;
        for (int i = 0; i < 5; i++)
        {
            long start = Stopwatch.GetTimestamp();
            DecodedImage? image = decode();
            best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            kept?.Dispose();
            kept = image;
        }

        return (kept, best);
    }
}
