using System.Globalization;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.GoldenImages;
using Broadside.TestSupport;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// Issue #428 (the WebGL half of #362) in Chromium: a viewer's WebGL renders upload a large image at the reduction that keeps two samples
/// per device pixel and stay within the page tolerance of uploading every sample (PSNR ≥ 35 dB, at most 4% of pixels more than 8 levels
/// off), still drawn on the GPU; and the render worker decodes images as the page's options say, not always at two samples per pixel.
/// </summary>
public sealed class WebGLScaledImageDecodeTests(BrowserHost host)
{
    private static readonly PageWaitForFunctionOptions Slow = new() { Timeout = 120_000 };

    [Theory]
    [InlineData(390)]
    [InlineData(150)]
    public async Task The_catalogue_cover_uploads_a_reduction_within_the_page_tolerance(int width)
    {
        host.Serve("scaled-cover.pdf", CatalogueSamples.Cover());
        double scale = width / 612.0;

        (long reducedBytes, string reducedCodes, byte[] reduced) = await RenderAsync("scaled-cover.pdf", scale, 2);
        (long fullBytes, _, byte[] full) = await RenderAsync("scaled-cover.pdf", scale, 0);

        Assert.DoesNotContain("Rasterized", reducedCodes, StringComparison.Ordinal);
        // 2550 x 3150 samples: a half (390 px) keeps a quarter of the texture, an eighth (150 px) a sixty-fourth.
        Assert.True(reducedBytes * 3 < fullBytes, $"{reducedBytes} texture bytes reduced, {fullBytes} in full");
        ScaledImagePages.AssertWithinTolerance(full, reduced, $"WebGL cover at {width} px");
    }

    public static TheoryData<string> Pages => [.. ScaledImagePages.Names];

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Large_images_and_their_masks_at_a_quarter_scale_stay_within_the_page_tolerance(string name)
    {
        // Masks of another size fall back to the software rasterizer, which reduces as the page asks too.
        string file = $"scaled-{name}.pdf";
        host.Serve(file, ScaledImagePages.Page(name));

        (_, _, byte[] reduced) = await RenderAsync(file, 0.25, 2);
        (_, _, byte[] full) = await RenderAsync(file, 0.25, 0);

        ScaledImagePages.AssertWithinTolerance(full, reduced, $"WebGL {name}");
    }

    [Fact]
    public async Task The_render_worker_decodes_images_as_the_pages_options_say()
    {
        // 2550 samples over about 580 pixels: two samples per pixel keep a half, one a quarter. The worker used to keep two whatever
        // the options said; with ?k=1 its tiles must now match the page's own at k = 1, and the page's k = 1 and k = 2 must differ.
        BrowserHost.RequireApp();
        host.Serve("worker-k.pdf", Page(CatalogueSamples.Jpeg(2550, 3150, DctChromaSubsampling.None)));
        int[] workerOne = await CanvasAsync("?k=1&file=../files/worker-k.pdf", "yes");
        int[] pageOne = await CanvasAsync("?worker=0&k=1&file=../files/worker-k.pdf", "no");
        int[] pageTwo = await CanvasAsync("?worker=0&file=../files/worker-k.pdf", "no");

        Assert.True(MaxDifference(pageOne, workerOne) <= 2, $"The worker's tiles at k = 1 differ from the page's by up to {MaxDifference(pageOne, workerOne)} levels.");
        Assert.True(MaxDifference(pageOne, pageTwo) > 2, "k = 1 and k = 2 drew the same pixels, so the test shows nothing.");

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

    private static int MaxDifference(int[] a, int[] b)
    {
        Assert.Equal(a.Length, b.Length);
        int max = 0;
        for (int i = 0; i < a.Length; i++)
        {
            max = Math.Max(max, Math.Abs(a[i] - b[i]));
        }

        return max;
    }

    /// <summary>Renders the first page of a served file with WebGL in a fresh page: the texture bytes, the diagnostics and the RGB pixels.</summary>
    private async Task<(long TextureBytes, string Codes, byte[] Pixels)> RenderAsync(string fileName, double scale, double samplesPerPixel)
    {
        BrowserHost.RequireApp();
        await using IBrowserContext context = await host.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(host.AppAddress.ToString());
        await page.WaitForFunctionAsync("() => globalThis.getDotnetRuntime && document.querySelector('.bs-viewer')", null, Slow);
        string address = new Uri(host.BaseAddress, "files/" + Uri.EscapeDataString(fileName)).ToString();
        string result = await page.EvaluateAsync<string>(
            """
            async ([address, scale, k]) => {
              const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
              return exports.Broadside.Viewer.Blazor.Sample.RenderStats.RenderPageScaledAsync(address, 0, scale, k);
            }
            """,
            new object[] { address, scale, samplesPerPixel });
        string[] parts = result.Split(';', 3);
        Assert.SkipWhen(parts[1].Contains("BackendWebGLUnavailable", StringComparison.Ordinal), "WebGL 2 is not available in this Chromium.");
        Viewer.Log(string.Create(CultureInfo.InvariantCulture, $"webgl scaled {fileName} k={samplesPerPixel}: {parts[0]} texture bytes, {parts[1]}"));
        using var png = new MemoryStream(Convert.FromBase64String(parts[2]));
        GoldenImage image = GoldenImage.FromPng(png);
        byte[] pixels = new byte[image.Width * image.Height * 4];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                (byte r, byte g, byte b) = image[x, y];
                int i = ((y * image.Width) + x) * 4;
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (r, g, b, 255);
            }
        }

        return (long.Parse(parts[0], CultureInfo.InvariantCulture), parts[1], pixels);
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
}
