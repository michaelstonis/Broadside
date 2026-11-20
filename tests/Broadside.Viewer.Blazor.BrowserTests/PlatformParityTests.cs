using System.Globalization;
using System.Text.Json;
using Broadside.GoldenImages;
using Broadside.Rendering;
using Broadside.TestSupport;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// The trimmed WebAssembly builds do what the Native AOT smoke host does, and get the same results as the desktop runtime (issue #272):
/// in both the AOT build (<c>/viewer/</c>) and the interpreter build (<c>/lite/</c>), each corpus file is opened with the bundled
/// Standard 14 fonts and no operating-system fonts, page 1 is rendered with the software rasterizer, its text extracted, and the
/// document saved and opened again. The render hash (<see cref="RenderHashManifest.Hash"/>) and the text must equal what this test
/// process computes on the desktop runtime: the software rasterizer draws the same bytes on every platform.
/// </summary>
public sealed class PlatformParityTests(BrowserHost host)
{
    /// <summary>Text in a Standard 14 font, vector paths, clipping, an image codec, a shading, transparency and an embedded font program.</summary>
    private static readonly string[] Files =
    [
        "text-standard14.pdf",
        "paths-fill-rules.pdf",
        "clipping-paths.pdf",
        "dct-baseline.pdf",
        "shading-type6-coons.pdf",
        "transparency-soft-masks.pdf",
        "layout-tagged.pdf",
    ];

    [Fact]
    public async Task Each_build_renders_extracts_and_saves_like_the_desktop_runtime()
    {
        BrowserHost.RequireApp();
        BrowserHost.RequireLiteApp();
        var mismatches = new List<string>();
        foreach ((string build, Uri app) in new[] { ("aot", host.AppAddress), ("interpreter", host.LiteAddress) })
        {
            await using IBrowserContext context = await host.Browser.NewContextAsync();
            IPage page = await context.NewPageAsync();
            await page.GotoAsync(new Uri(app, "?worker=0").ToString());
            await page.WaitForFunctionAsync("() => globalThis.getDotnetRuntime && document.querySelector('.bs-viewer')", null, new PageWaitForFunctionOptions { Timeout = 120_000 });

            foreach (string fileName in Files)
            {
                byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(Corpus.Directory, fileName), TestContext.Current.CancellationToken);
                host.Serve(fileName, bytes);
                string address = new Uri(host.BaseAddress, "files/" + Uri.EscapeDataString(fileName)).ToString();
                string json = await page.EvaluateAsync<string>(
                    """
                    async ([address, dpi]) => {
                      const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
                      return exports.Broadside.Viewer.Blazor.Sample.PlatformParity.RunAsync(address, dpi);
                    }
                    """,
                    new object[] { address, GoldenCorpus.Dpi });
                Result browser = Result.Parse(json);
                Result desktop = await RunOnDesktopAsync(bytes);
                Viewer.Log(string.Create(CultureInfo.InvariantCulture, $"parity {build} {fileName}: {browser.Width} x {browser.Height} {browser.Hash[..12]} desktop {desktop.Hash[..12]}"));

                if (browser.Hash != desktop.Hash || browser.Width != desktop.Width || browser.Height != desktop.Height)
                {
                    mismatches.Add($"{build} {fileName}: render {browser.Width} x {browser.Height} {browser.Hash}, desktop {desktop.Width} x {desktop.Height} {desktop.Hash}");
                }

                if (browser.Text != desktop.Text)
                {
                    mismatches.Add($"{build} {fileName}: text \"{browser.Text}\", desktop \"{desktop.Text}\"");
                }

                if (browser.SavedPages != desktop.SavedPages || browser.SavedPages < 1)
                {
                    mismatches.Add($"{build} {fileName}: {browser.SavedPages} page(s) after saving, desktop {desktop.SavedPages}");
                }

                if (!browser.Diagnostics.SequenceEqual(desktop.Diagnostics))
                {
                    mismatches.Add($"{build} {fileName}: diagnostics [{string.Join(", ", browser.Diagnostics)}], desktop [{string.Join(", ", desktop.Diagnostics)}]");
                }
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    private static async Task<Result> RunOnDesktopAsync(byte[] bytes)
    {
        using PdfDocument document = await new PdfEngine(new PdfOptions().UseStandard14Fonts()).OpenAsync(bytes, TestContext.Current.CancellationToken);
        PdfPage page = document.Pages[0];
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(page, new RenderOptions { Dpi = GoldenCorpus.Dpi });
        string text = page.ExtractText().PlainText;
        using var saved = new MemoryStream();
        await document.SaveAsync(saved, TestContext.Current.CancellationToken);
        using PdfDocument reopened = await new PdfEngine(new PdfOptions().UseStandard14Fonts()).OpenAsync(saved.ToArray(), TestContext.Current.CancellationToken);
        return new Result(bitmap.Width, bitmap.Height, RenderHashManifest.Hash(bitmap), text, reopened.Pages.Count, [.. document.Diagnostics.Select(static d => d.Code)]);
    }

    private sealed record Result(int Width, int Height, string Hash, string Text, int SavedPages, string[] Diagnostics)
    {
        public static Result Parse(string json)
        {
            using JsonDocument parsed = JsonDocument.Parse(json);
            JsonElement root = parsed.RootElement;
            return new Result(
                root.GetProperty("width").GetInt32(),
                root.GetProperty("height").GetInt32(),
                root.GetProperty("hash").GetString()!,
                root.GetProperty("text").GetString()!,
                root.GetProperty("savedPages").GetInt32(),
                [.. root.GetProperty("diagnostics").EnumerateArray().Select(static e => e.GetString()!)]);
        }
    }
}
