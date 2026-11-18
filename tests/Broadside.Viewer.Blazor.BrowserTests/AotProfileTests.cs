using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// Records the AOT profile of the <c>--hot-aot</c> demo build (issue #547): the methods a viewing session runs, which that build
/// compiles ahead of time while the interpreter runs the rest. Explicit: publish the profiling build
/// (<c>publish-static.sh --aot-profiler artifacts/blazor-sample-profiler</c>), then run this test with
/// <c>BROADSIDE_BLAZOR_APP</c> pointing at it and <c>BROADSIDE_AOT_PROFILE_OUT</c> naming the file to write (normally
/// <c>samples/Broadside.Viewer.Blazor.Sample/hot-aot.aotprofile</c>).
/// </summary>
/// <remarks>
/// Two sessions, one profile each (the profiler records one runtime). In the first, the catalogue opens in the viewer on the page's
/// thread at a desktop and a phone viewport, scrolls and zooms; every sample document opens from the gallery; then every page of
/// every sample renders with the software rasterizer. The second (<c>-worker</c> beside the output file) scrolls the catalogue
/// with the render worker, which records the page's side of the worker protocol. The worker's own runtime runs the rendering code the
/// first session records.
/// </remarks>
public sealed class AotProfileTests(BrowserHost host)
{
    private static readonly PageWaitForFunctionOptions Budget = new() { Timeout = 600_000, PollingInterval = 50 };

    [Fact(Explicit = true)]
    public async Task Record_the_hot_aot_profile()
    {
        BrowserHost.RequireApp();
        string? output = Environment.GetEnvironmentVariable("BROADSIDE_AOT_PROFILE_OUT");
        Assert.SkipWhen(string.IsNullOrEmpty(output), "BROADSIDE_AOT_PROFILE_OUT is not set.");
        string[] samples = [.. Directory.GetFiles(Path.Combine(BrowserHost.AppDirectory!, "documents"), "*.pdf").Select(static f => Path.GetFileName(f)).Order(StringComparer.Ordinal)];

        // The page's runtime when it renders: the viewer, every sample and every page.
        byte[] rendering = WithRuntimeHelpers(await RecordAsync(worker: false, samples));
        await File.WriteAllBytesAsync(output!, rendering, TestContext.Current.CancellationToken);

        // The page's runtime when the render worker renders: the viewer's side of the worker protocol.
        byte[] remote = await RecordAsync(worker: true, []);
        string remoteOutput = Path.ChangeExtension(output!, null) + "-worker" + Path.GetExtension(output);
        await File.WriteAllBytesAsync(remoteOutput, remote, TestContext.Current.CancellationToken);
        Viewer.Log($"AOT profiles: {rendering.Length} bytes to {output}, {remote.Length} bytes to {remoteOutput}");
    }

    private async Task<byte[]> RecordAsync(bool worker, string[] samples)
    {
        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            DeviceScaleFactor = 2,
        });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(host.AppAddress, $"?file=documents/catalogue.pdf{(worker ? "" : "&worker=0")}").ToString());
        await WaitForTilesAsync(page);
        await ScrollAndZoomAsync(page, 120);

        // The phone viewport: a different scale, tile size and layout.
        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForTimeoutAsync(1000);
        await ScrollAndZoomAsync(page, 60);

        // Every sample from the gallery, through the viewer (first view); the 1,168-page one aside.
        await page.SetViewportSizeAsync(1280, 900);
        foreach (string sample in samples.Where(static s => s != "shakespeare.pdf"))
        {
            if (await page.Locator("[data-sheet=open]").CountAsync() == 0)
            {
                await page.Locator("[data-panel-button=open]").ClickAsync();
            }

            ILocator entry = page.Locator($"[data-gallery='documents/{sample}']");
            if (await entry.CountAsync() == 0)
            {
                continue;
            }

            await entry.ClickAsync();
            await page.WaitForFunctionAsync("name => document.querySelector('.topbar .name')?.textContent === name", sample, Budget);
            await WaitForTilesAsync(page);
        }

        // Every page of every sample, unsliced (the two that need a password fail to open; their failure is part of the session).
        foreach (string sample in samples.Where(static s => s != "shakespeare.pdf"))
        {
            await page.EvaluateAsync(
                """
                async (address) => {
                  const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
                  try { await exports.Broadside.Viewer.Blazor.Sample.RenderStats.RenderDocumentPagesAsync(address, 96); } catch { }
                }
                """,
                new Uri(host.AppAddress, "documents/" + sample).ToString());
        }

        int[] profile = await page.EvaluateAsync<int[]>(
            """
            async () => {
              const runtime = getDotnetRuntime(0);
              const exports = await runtime.getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
              exports.Broadside.Viewer.Blazor.Sample.AotProfile.Write();
              const data = runtime.INTERNAL.aotProfileData;
              return data ? Array.from(data) : [];
            }
            """);
        Assert.NotEmpty(profile);
        return [.. profile.Select(static b => (byte)b)];
    }

    // The memory helpers on System.String that AOT-compiled code calls to clear and copy blocks. The interpreter never calls them (it
    // clears and copies blocks itself), so no interpreted session records them, and it cannot run them either: a build that compiles
    // a caller but not the helper stops with "NIY encountered in method string:memset". The profile always lists them.
    private static readonly (string Name, string Signature, int Parameters)[] RuntimeHelpers =
    [
        ("memset", "void(byte*,int,int)", 3), ("memcpy", "void(byte*,byte*,int)", 3), ("bzero", "void(byte*,int)", 2),
        ("bzero_aligned_1", "void(byte*,int)", 2), ("bzero_aligned_2", "void(byte*,int)", 2), ("bzero_aligned_4", "void(byte*,int)", 2),
        ("bzero_aligned_8", "void(byte*,int)", 2), ("memcpy_aligned_1", "void(byte*,byte*,int)", 3), ("memcpy_aligned_2", "void(byte*,byte*,int)", 3),
        ("memcpy_aligned_4", "void(byte*,byte*,int)", 3), ("memcpy_aligned_8", "void(byte*,byte*,int)", 3),
    ];

    // Appends RuntimeHelpers to a profile in Mono's AOT profile format (mono/profiler/aot.h): records of a kind byte, an int id and the
    // kind's fields (strings as an int length and UTF-8 bytes), ended by a record of kind 0 and id 0. The new records take ids far
    // above the recorded ones.
    private static byte[] WithRuntimeHelpers(byte[] profile)
    {
        const byte imageRecord = 1, typeRecord = 2, methodRecord = 4, classKind = 0x12;
        Assert.True(profile.Length > 5 && profile.AsSpan(profile.Length - 5).IndexOfAnyExcept((byte)0) < 0, "the profile ends with its end record");
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(profile.AsSpan(0, profile.Length - 5));
            const int imageId = 0x7FFF0000, typeId = imageId + 1;
            writer.Write(imageRecord);
            writer.Write(imageId);
            WriteString(writer, "System.Private.CoreLib");
            WriteString(writer, "");
            writer.Write(typeRecord);
            writer.Write(typeId);
            writer.Write(classKind);
            writer.Write(imageId);
            writer.Write(-1);
            WriteString(writer, "System.String");
            int id = typeId + 1;
            foreach ((string name, string signature, int parameters) in RuntimeHelpers)
            {
                writer.Write(methodRecord);
                writer.Write(id++);
                writer.Write(typeId);
                writer.Write(-1);
                writer.Write(parameters);
                WriteString(writer, name);
                WriteString(writer, signature);
            }

            writer.Write(new byte[5]);
        }

        return stream.ToArray();

        static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
    }

    private static async Task WaitForTilesAsync(IPage page) =>
        await page.WaitForFunctionAsync("() => Number(document.querySelector('.bs-viewer')?.dataset.tilesPresented) > 0", null, Budget);

    private static async Task ScrollAndZoomAsync(IPage page, int step)
    {
        LocatorBoundingBoxResult box = (await page.Locator(".bs-viewer").BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
        await page.WaitForTimeoutAsync(3000);
        for (int i = 0; i < 60; i++)
        {
            await page.Mouse.WheelAsync(0, step);
            await page.WaitForTimeoutAsync(16);
        }

        await page.WaitForTimeoutAsync(3000);
        for (int i = 0; i < 3; i++)
        {
            await page.Keyboard.DownAsync("Control");
            await page.Mouse.WheelAsync(0, -100);
            await page.Keyboard.UpAsync("Control");
            await page.WaitForTimeoutAsync(1500);
        }

        await page.Keyboard.DownAsync("Control");
        await page.Mouse.WheelAsync(0, 300);
        await page.Keyboard.UpAsync("Control");
        await page.WaitForTimeoutAsync(3000);
    }
}
