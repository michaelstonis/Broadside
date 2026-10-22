using System.Globalization;
using Broadside.GoldenImages;
using Broadside.TestSupport;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// The WebGL 2 backend (#353) in Chromium: pages it draws agree with the software rasterizer's reviewed renders within the "webgl"
/// rows of <see cref="GoldenThresholds"/>, what it cannot draw falls back with a diagnostic, and the demo's View panel switches the
/// viewer to it. Chromium uses the GPU where it can (Metal on macOS) and SwiftShader otherwise; without WebGL 2 the tests skip.
/// </summary>
public sealed class WebGLBackendTests(BrowserHost host)
{
    /// <summary>Pages drawn on the GPU (paths, strokes, clips, glyph outlines, images, stencil masks) or rasterized groups.</summary>
    public static TheoryData<string, int> GpuPages => GoldenCorpus.PagesOf(
        "paths-fill-rules.pdf",
        "paths-strokes.pdf",
        "clipping-paths.pdf",
        "clip-before-path.pdf",
        "text-standard14.pdf",
        "text-type1-embedded.pdf",
        "text-truetype-embedded.pdf",
        "text-cff-embedded.pdf",
        "text-render-modes.pdf",
        "text-type3.pdf",
        "image-stencil-mask.pdf",
        "image-smask.pdf",
        "image-explicit-mask.pdf",
        "image-4bpc-indexed.pdf",
        "dct-baseline.pdf",
        "form-xobject-nested.pdf",
        "extgstate-params.pdf");

    [Theory]
    [MemberData(nameof(GpuPages))]
    public async Task A_page_drawn_with_WebGL_matches_the_software_rasterizer(string fileName, int page)
    {
        (GoldenImage render, string codes) = await RenderAsync(fileName, page);
        Assert.DoesNotContain("Unavailable", codes, StringComparison.Ordinal);
        Assert.DoesNotContain("BackendShadingRasterized", codes, StringComparison.Ordinal);
        GoldenCorpus.AssertMatchesReference("webgl", fileName, page, render);
    }

    [Theory]
    [InlineData("paths-fill-rules.pdf", 0)]
    [InlineData("text-standard14.pdf", 0)]
    [InlineData("dct-baseline.pdf", 0)]
    public async Task A_thumbnail_drawn_with_WebGL_matches_the_software_rasterizer_and_the_render_after_it_too(string fileName, int pageIndex)
    {
        // #380: the Pages panel's PngSurface read the bitmap while the tile was still in the WebGL canvas (only the paper), and the
        // next render read the canvas back into that thumbnail's released bitmap.
        BrowserHost.RequireApp();
        host.Serve(fileName, await File.ReadAllBytesAsync(Path.Combine(Corpus.Directory, fileName), TestContext.Current.CancellationToken));
        await using IBrowserContext context = await host.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(host.AppAddress.ToString());
        await page.WaitForFunctionAsync("() => globalThis.getDotnetRuntime && document.querySelector('.bs-viewer')", null, new PageWaitForFunctionOptions { Timeout = 120_000 });
        string address = new Uri(host.BaseAddress, "files/" + Uri.EscapeDataString(fileName)).ToString();
        string result = await page.EvaluateAsync<string>(
            """
            async ([address, page, dpi]) => {
              const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
              return exports.Broadside.Viewer.Blazor.Sample.RenderStats.RenderThumbnailsAsync(address, page, dpi);
            }
            """,
            new object[] { address, pageIndex, GoldenCorpus.Dpi });
        string[] parts = result.Split(';', 3);
        Assert.SkipWhen(parts[0].Contains("BackendWebGLUnavailable", StringComparison.Ordinal), "WebGL 2 is not available in this Chromium.");
        Viewer.Log($"webgl thumbnails {fileName}#{pageIndex}: {parts[0]}");
        Assert.DoesNotContain("Rasterized", parts[0], StringComparison.Ordinal); // drawn on the GPU, so held in the canvas when presented
        foreach (string base64 in parts.AsSpan(1))
        {
            using var png = new MemoryStream(Convert.FromBase64String(base64));
            GoldenCorpus.AssertMatchesReference("webgl", fileName, pageIndex, GoldenImage.FromPng(png));
        }
    }

    [Fact]
    public async Task A_shading_falls_back_to_the_software_rasterizer_with_a_diagnostic()
    {
        (GoldenImage render, string codes) = await RenderAsync("shading-type2-axial.pdf", 0);
        Assert.Contains("BackendShadingRasterized", codes, StringComparison.Ordinal);
        GoldenCorpus.AssertMatchesReference(GoldenThresholds.SoftwareRasterizer, "shading-type2-axial.pdf", 0, render);
    }

    [Fact]
    public async Task The_view_panel_switches_the_viewer_to_WebGL()
    {
        BrowserHost.RequireApp();
        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1200, Height = 900 } });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(host.AppAddress.ToString());
        await page.WaitForFunctionAsync(
            "() => { const v = document.querySelector('.bs-viewer'); return v && Number(v.dataset.tilesPresented) > 0; }",
            null,
            new PageWaitForFunctionOptions { Timeout = 120_000 });
        await page.Locator("[data-panel-button=view]").ClickAsync();
        await page.Locator("[data-sheet=view]").WaitForAsync();
        await page.Locator("[data-renderer='webgl']").ClickAsync();
        string? status = null;
        for (int i = 0; i < 100; i++)
        {
            status = await page.Locator("[data-renderer-status]").TextContentAsync();
            if (status is not null && (status.Contains("not available", StringComparison.Ordinal) || status.Contains("MSAA", StringComparison.Ordinal)))
            {
                break;
            }

            await page.WaitForTimeoutAsync(100);
        }

        Assert.SkipWhen(status?.Contains("not available", StringComparison.Ordinal) == true, "WebGL 2 is not available in this Chromium.");
        string json = await WaitForTilesAsync(page);
        Viewer.Log($"view panel: {status}; {json}");
        Assert.Contains("\"renderer\":\"webgl\"", json, StringComparison.Ordinal);
        Assert.Contains("\"fallbackTiles\":0", json, StringComparison.Ordinal);
        Assert.True(await page.EvaluateAsync<int>(Viewer.CountDarkPixels) > 0, "the redrawn page has ink");
    }

    private static async Task<string> WaitForTilesAsync(IPage page)
    {
        string json = "";
        for (int i = 0; i < 200; i++)
        {
            await page.Locator(".bs-viewer").HoverAsync();
            await page.Mouse.WheelAsync(0, 200);
            await page.WaitForTimeoutAsync(100);
            json = await page.EvaluateAsync<string>(Snapshot);
            if (!json.Contains("\"tiles\":0,", StringComparison.Ordinal))
            {
                break;
            }
        }

        return json;
    }

    private const string Snapshot = """
        async () => {
          const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
          return exports.Broadside.Viewer.Blazor.Sample.RenderStats.Snapshot();
        }
        """;

    private async Task<(GoldenImage Render, string Codes)> RenderAsync(string fileName, int pageIndex)
    {
        BrowserHost.RequireApp();
        host.Serve(fileName, await File.ReadAllBytesAsync(Path.Combine(Corpus.Directory, fileName)));
        await using IBrowserContext context = await host.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(host.AppAddress.ToString());
        await page.WaitForFunctionAsync("() => globalThis.getDotnetRuntime && document.querySelector('.bs-viewer')", null, new PageWaitForFunctionOptions { Timeout = 120_000 });
        string address = new Uri(host.BaseAddress, "files/" + Uri.EscapeDataString(fileName)).ToString();
        string result = await page.EvaluateAsync<string>(
            """
            async ([address, page, dpi]) => {
              const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
              return exports.Broadside.Viewer.Blazor.Sample.RenderStats.RenderPageAsync(address, page, dpi, true);
            }
            """,
            new object[] { address, pageIndex, GoldenCorpus.Dpi });
        string[] parts = result.Split(';', 3);
        Assert.SkipWhen(parts[1].Contains("BackendWebGLUnavailable", StringComparison.Ordinal), "WebGL 2 is not available in this Chromium.");
        Viewer.Log(string.Create(CultureInfo.InvariantCulture, $"webgl {fileName}#{pageIndex}: {parts[0]} ms {parts[1]}"));
        using var png = new MemoryStream(Convert.FromBase64String(parts[2]));
        return (GoldenImage.FromPng(png), parts[1]);
    }
}
