using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// The browser kernel benchmark (issue #470) in Chromium, in both builds of the demo: the AOT build (<c>/viewer/</c>, where
/// <c>Vector128</c> compiles to WebAssembly SIMD) and the interpreter build (<c>/lite/</c>, published with <c>--no-aot</c>, where only
/// part of the <c>Vector128</c> API maps to SIMD opcodes). Each SIMD kernel's scalar and vector methods must give the same bytes in
/// both; their times go to the measurement log as <c>kernel aot|interpreter name: ...</c> lines. Times are reported, not asserted: a
/// kernel PR reads them and does not ship a kernel whose vector method is slower than its scalar one in the interpreter build.
/// </summary>
public sealed class KernelBenchmarkTests(BrowserHost host)
{
    [Fact]
    public async Task Each_kernel_matches_its_scalar_path_and_is_timed_in_the_AOT_and_the_interpreter_builds()
    {
        BrowserHost.RequireApp();
        BrowserHost.RequireLiteApp();
        foreach ((string build, Uri address) in new[] { ("aot", host.AppAddress), ("interpreter", host.LiteAddress) })
        {
            await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 800, Height = 600 } });
            IPage page = await context.NewPageAsync();
            await page.GotoAsync(new Uri(address, "?worker=0").ToString());
            await page.WaitForFunctionAsync("() => Number(document.querySelector('.bs-viewer')?.dataset.tilesPresented) > 0", null, new PageWaitForFunctionOptions { Timeout = 120_000 });

            string report = await page.EvaluateAsync<string>("""
                async () => {
                  const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
                  return exports.Broadside.Viewer.Blazor.Sample.KernelBenchmarks.Run();
                }
                """);
            string[] lines = report.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.NotEmpty(lines);
            foreach (string line in lines)
            {
                Viewer.Log($"kernel {build} {line}");
                Assert.Contains("equal yes", line, StringComparison.Ordinal);
            }
        }
    }
}
