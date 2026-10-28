using System.Globalization;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.TestSupport;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// The browser image decoder (#373) against the managed decoder, in each browser, through the verification mode: Chromium's
/// <c>ImageDecoder</c> YCbCr planes are bit-identical at full size and within the tolerance reduced; Firefox's decoder is within the
/// tolerance; WebKit's page decodes with the managed codec by default, and its <c>createImageBitmap</c> path (the render worker's
/// there) is within the tolerance, keeps an Exif-6 image's own orientation and does not convert an ICC-tagged image. Feature detection
/// decides the path; the user agent is never read.
/// </summary>
public sealed class BrowserImageDecoderTests(BrowserHost host)
{
    private static readonly PageWaitForFunctionOptions Slow = new() { Timeout = 120_000 };

    [Theory]
    [InlineData("chromium")]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public async Task The_browser_decodes_like_the_managed_decoder_within_its_fidelity(string browserName)
    {
        BrowserHost.RequireApp();
        IBrowser browser = await host.GetBrowserAsync(browserName);
        IPage page = await OpenAsync(browser);
        string[] files = ["photo-444.pdf", "photo-420.pdf"];
        host.Serve(files[0], Page(CatalogueSamples.Jpeg(1320, 948, DctChromaSubsampling.None)));
        host.Serve(files[1], Page(CatalogueSamples.Jpeg(1320, 948, DctChromaSubsampling.HorizontalAndVertical)));
        foreach (string file in files)
        {
            if (browserName == "webkit")
            {
                // No ImageDecoder in WebKit: the page decodes with the managed codec (readback would block it); the bitmap path is
                // measured on request, as the render worker uses it.
                Report managed = await VerifyAsync(page, file, scale: 1, samplesPerPixel: 0, bitmapOnPage: false);
                Assert.Equal("none", managed.Decoder);
                Assert.Empty(managed.Codes);
                Report bitmap = await VerifyAsync(page, file, scale: 1, samplesPerPixel: 0, bitmapOnPage: true);
                Assert.Equal("createImageBitmap", bitmap.Decoder);

                // WebKit interpolates subsampled chroma (max 27, 1.03% of pixels over 2 levels measured at 4:2:0): not offered.
                Assert.Equal(file == "photo-444.pdf" ? ["PlatformImageVerified"] : [], bitmap.Codes);
                continue;
            }

            Report full = await VerifyAsync(page, file, scale: 1.4, samplesPerPixel: 0, bitmapOnPage: false);
            Assert.SkipWhen(browserName == "firefox" && full.Decoder != "ImageDecoder", $"This Firefox has no ImageDecoder for JPEG ({full.Decoder}).");
            Assert.Equal("ImageDecoder", full.Decoder);
            if (browserName != "chromium" && file == "photo-420.pdf" && full.Codes.Length == 0)
            {
                // The start-up calibration found this browser's subsampled chroma not bit-identical (Firefox interpolates: max 30,
                // 1.08% over 2 levels measured), so 4:2:0 images decode managed.
                continue;
            }

            Assert.Equal(["PlatformImageVerified"], full.Codes);
            if (browserName == "chromium")
            {
                Assert.Contains("max 0,", full.Messages[0], StringComparison.Ordinal); // bit-identical: the YCbCr planes
            }

            // Drawn at about a quarter of its size with 2 samples per pixel: a reduced decode through desiredWidth where the browser
            // scales in the DCT (Chromium; probed at start-up), else a full-size decode (Firefox resamples: 43 dB measured).
            Report reduced = await VerifyAsync(page, file, scale: 0.2, samplesPerPixel: 2, bitmapOnPage: false);
            Assert.Equal(["PlatformImageVerified"], reduced.Codes);
            if (browserName == "chromium")
            {
                Assert.DoesNotContain("at reduction 0", reduced.Messages[0], StringComparison.Ordinal);
                Assert.Contains("max 0,", reduced.Messages[0], StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("chromium")]
    [InlineData("webkit")]
    public async Task An_Exif_rotated_or_ICC_tagged_JPEG_keeps_its_own_orientation_and_colours(string browserName)
    {
        BrowserHost.RequireApp();
        IBrowser browser = await host.GetBrowserAsync(browserName);
        IPage page = await OpenAsync(browser);
        host.Serve("photo-exif-icc.pdf", Page(WithExifAndIcc(CatalogueSamples.Jpeg(640, 400, DctChromaSubsampling.None))));

        // A rotated decode would be 400 x 640, a converted one far outside the tolerance: either is PlatformImageMismatch.
        Report report = await VerifyAsync(page, "photo-exif-icc.pdf", scale: 1, samplesPerPixel: 0, bitmapOnPage: browserName == "webkit");
        Assert.Equal(["PlatformImageVerified"], report.Codes);
    }

    [Fact]
    public async Task The_render_worker_decodes_with_the_browser_and_its_tiles_match_the_pages()
    {
        BrowserHost.RequireApp();
        host.Serve("worker-photo.pdf", Page(CatalogueSamples.Jpeg(1320, 948, DctChromaSubsampling.None)));
        int[] inWorker = await CanvasAsync("?file=../files/worker-photo.pdf", "yes");
        int[] onPage = await CanvasAsync("?worker=0&file=../files/worker-photo.pdf", "no");

        Assert.Equal(onPage.Length, inWorker.Length);
        int max = 0;
        for (int i = 0; i < onPage.Length; i++)
        {
            max = Math.Max(max, Math.Abs(onPage[i] - inWorker[i]));
        }

        // Both decode with ImageDecoder (planes or desiredWidth): the same samples, drawn by the same rasterizer.
        Assert.True(max <= 2, $"The worker's tiles differ from the page's by up to {max} levels.");

        async Task<int[]> CanvasAsync(string query, string worker)
        {
            await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 600, Height = 700 } });
            IPage page = await context.NewPageAsync();
            await page.GotoAsync(new Uri(host.AppAddress, query).ToString());
            await page.WaitForFunctionAsync("() => Number(document.querySelector('.bs-viewer')?.dataset.tilesPresented) > 1", null, Slow);
            await page.WaitForTimeoutAsync(1500);
            Assert.Equal(worker, await Viewer.Data(page, "bsWorker"));
            return await page.EvaluateAsync<int[]>("""
                () => {
                  const canvas = document.querySelector('.bs-viewer canvas');
                  const w = Math.floor(canvas.width / 4), h = Math.floor(canvas.height / 4);
                  return Array.from(canvas.getContext('2d').getImageData(w, h, 2 * w, 2 * h).data.filter((_, i) => i % 64 === 0));
                }
                """);
        }
    }

    [Fact]
    public async Task Without_ImageDecoder_the_WebGL_backend_uploads_the_browsers_bitmap_and_draws_like_the_rasterizer()
    {
        BrowserHost.RequireApp();
        IBrowser browser = await host.GetBrowserAsync("webkit");
        await using IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 800, Height = 900 } });
        IPage page = await context.NewPageAsync();
        host.Serve("mode-b.pdf", Page(CatalogueSamples.Jpeg(1320, 948, DctChromaSubsampling.None)));
        await page.GotoAsync(new Uri(host.AppAddress, "?worker=0&renderer=webgl&file=../files/mode-b.pdf").ToString());
        await page.WaitForFunctionAsync("() => Number(document.querySelector('.bs-viewer')?.dataset.tilesPresented) > 0", null, Slow);
        Assert.SkipUnless(await page.EvaluateAsync<bool>("() => typeof ImageDecoder !== 'function'"), "This WebKit has ImageDecoder: mode B is not used.");
        // Whether the demo draws with WebGL (the View panel's buttons are not in the page until it opens).
        string stats = await page.EvaluateAsync<string>("""
            async () => {
              const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
              return exports.Broadside.Viewer.Blazor.Sample.RenderStats.Snapshot();
            }
            """);
        Assert.SkipWhen(
            stats.Contains("\"renderer\":\"software\"", StringComparison.Ordinal) && !await page.EvaluateAsync<bool>("() => !!document.createElement('canvas').getContext('webgl2')"),
            "WebGL 2 is not available in this WebKit.");
        Assert.Contains("\"renderer\":\"webgl\"", stats, StringComparison.Ordinal);
        await page.WaitForTimeoutAsync(1000);

        // Mode B: the photograph went to the GPU as the browser's own bitmap, never read back nor decoded by the managed codec.
        Assert.True(await page.EvaluateAsync<int>("() => globalThis.__broadsideBitmapTextures ?? 0") > 0, "no ImageBitmap was uploaded to a texture");
    }

    [Fact]
    public async Task A_JPEG_the_browser_rejects_decodes_with_the_managed_codec_and_a_diagnostic()
    {
        BrowserHost.RequireApp();
        string? corpus = Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR");
        string path = Path.Combine(corpus ?? "", "pdfjs", "issue12841_reduced.pdf");
        Assert.SkipUnless(File.Exists(path), "BROADSIDE_CORPUS_DIR with pdfjs/issue12841_reduced.pdf is not set.");
        IPage page = await OpenAsync(host.Browser);
        host.Serve("issue12841_reduced.pdf", await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));

        Report report = await VerifyAsync(page, "issue12841_reduced.pdf", scale: 1, samplesPerPixel: 0, bitmapOnPage: false);

        Assert.Contains("ImageDecodedManaged", report.Codes);
    }

    private async Task<IPage> OpenAsync(IBrowser browser)
    {
        IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 800, Height = 600 } });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(host.AppAddress, "?worker=0").ToString());
        await page.WaitForFunctionAsync("() => Number(document.querySelector('.bs-viewer')?.dataset.tilesPresented) > 0", null, Slow);
        return page;
    }

    private async Task<Report> VerifyAsync(IPage page, string file, double scale, double samplesPerPixel, bool bitmapOnPage)
    {
        string result = await page.EvaluateAsync<string>(
            """
            async ([address, scale, k, bitmap]) => {
              const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
              return await exports.Broadside.Viewer.Blazor.Sample.ImageDecodeChecks.VerifyAsync(address, scale, k, bitmap);
            }
            """,
            new object[] { new Uri(host.BaseAddress, "files/" + file).ToString(), scale, samplesPerPixel, bitmapOnPage });
        string[] lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Only the platform decoder's diagnostics: the page itself may have others.
        string[] diagnostics = [.. lines[2..].Where(static d => d.StartsWith("Platform", StringComparison.Ordinal) || d.StartsWith("ImageDecodedManaged", StringComparison.Ordinal))];
        Viewer.Log($"{file} at {scale.ToString(CultureInfo.InvariantCulture)}: {result}");
        return new Report(lines[0], double.Parse(lines[1], CultureInfo.InvariantCulture), [.. diagnostics.Select(static d => d.Split('|')[0])], [.. diagnostics.Select(static d => d.Split('|', 2)[1])]);
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

    /// <summary>Inserts an APP1 Exif segment with orientation 6 and an APP2 ICC profile segment (sRGB) after SOI.</summary>
    private static byte[] WithExifAndIcc(byte[] jpeg)
    {
        byte[] exif = [.. "Exif\0\0MM\0*\0\0\0\b\0\u0001\u0001\u0012\0\u0003\0\0\0\u0001\0\u0006\0\0\0\0\0\0"u8];
        byte[] icc = [.. "ICC_PROFILE\0\u0001\u0001"u8, .. PdfIccBasedColorSpace.CreateSrgbProfileData()];
        return [0xFF, 0xD8, .. Segment(0xE1, exif), .. Segment(0xE2, icc), .. jpeg.AsSpan(2)];

        static byte[] Segment(byte marker, byte[] payload) =>
            [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];
    }

    private sealed record Report(string Decoder, double Milliseconds, string[] Codes, string[] Messages);
}
