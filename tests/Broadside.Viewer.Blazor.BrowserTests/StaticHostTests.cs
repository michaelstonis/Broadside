using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// The demo's second publish mode (#338): no ahead-of-time compilation, so no file is over the 16 MiB per-file limit some static hosts
/// have, and the runtime in <c>framework/</c> instead of <c>_framework/</c> for hosts that reserve paths starting with an underscore. It
/// must boot from such a host (the test server refuses every such path under <c>/lite/</c>) in every browser.
/// </summary>
public sealed class StaticHostTests(BrowserHost host)
{
    /// <summary>The per-file limit of the strictest host the demo is published to.</summary>
    public const long FileLimit = 16 * 1024 * 1024;

    [Fact]
    public void The_non_AOT_build_has_no_large_file_and_no_underscore_path()
    {
        BrowserHost.RequireLiteApp();
        string root = BrowserHost.LiteDirectory!;
        string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        long total = 0;
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(root, file);
            Assert.DoesNotContain(relative.Split(Path.DirectorySeparatorChar), static segment => segment.StartsWith('_'));
            long length = new FileInfo(file).Length;
            Assert.True(length < FileLimit, $"{relative} is {length:N0} bytes");
            total += length;
        }

        string runtime = Assert.Single(files, static file => Path.GetFileName(file).StartsWith("dotnet.native.", StringComparison.Ordinal) && file.EndsWith(".wasm", StringComparison.Ordinal));
        Assert.True(new FileInfo(runtime).Length < 8 * 1024 * 1024, "the runtime module holds no AOT-compiled code");
        Viewer.Log($"non-AOT static site: {files.Length} files, {total:N0} B, largest {files.Max(static file => new FileInfo(file).Length):N0} B");
    }

    [Theory]
    [InlineData("chromium")]
    [InlineData("webkit")]
    [InlineData("firefox")]
    public async Task The_non_AOT_build_boots_from_a_host_that_refuses_underscore_paths(string browserName)
    {
        BrowserHost.RequireLiteApp();
        IBrowser browser = await host.GetBrowserAsync(browserName);
        await using IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 390, Height = 844 } });
        IPage page = await context.NewPageAsync();
        var refused = new List<string>();
        page.Response += (_, response) =>
        {
            if (response.Status == 404)
            {
                refused.Add(response.Url);
            }
        };
        await page.GotoAsync(host.LiteAddress.ToString());
        await page.WaitForFunctionAsync(
            "() => { const v = document.querySelector('.bs-viewer'); return v && Number(v.dataset.tilesPresented) > 0; }",
            null,
            new PageWaitForFunctionOptions { Timeout = 120_000 });
        Assert.True(await page.EvaluateAsync<int>(Viewer.CountDarkPixels) > 0, "the gallery's first document is drawn");
        Assert.Empty(refused);
    }
}
