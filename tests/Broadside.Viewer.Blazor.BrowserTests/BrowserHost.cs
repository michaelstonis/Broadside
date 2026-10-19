using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(Broadside.Viewer.Blazor.BrowserTests.BrowserHost))]

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// Serves the published Blazor sample (a static site under <c>/viewer/</c>, standard media types only) and test documents over HTTP (documents with byte-range support and an optional gate that holds
/// every request starting at or past an offset), and runs one headless Chromium for all tests. When the non-AOT build with a renamed
/// runtime folder is published too (<c>publish-static.sh --no-aot --runtime-folder framework artifacts/blazor-sample-lite</c>), it is served
/// under <c>/lite/</c> by a host that refuses every path segment starting with an underscore, as some static hosts do. WebKit and Firefox
/// start on demand (<see cref="GetBrowserAsync"/>).
/// </summary>
public sealed class BrowserHost : IAsyncLifetime
{
    private readonly ConcurrentDictionary<string, HostedDocument> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IBrowser> _browsers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _launching = new(1, 1);
    private WebApplication? _server;
    private IPlaywright? _playwright;

    /// <summary>Gets the published app's <c>wwwroot</c>, or <see langword="null"/> when it is not published.</summary>
    public static string? AppDirectory { get; } = FindApp("BROADSIDE_BLAZOR_APP", "blazor-sample");

    /// <summary>Gets the address of the app (a sub-path of the server), ending in a slash.</summary>
    public Uri AppAddress => new(BaseAddress, "viewer/");

    /// <summary>Gets the non-AOT build with its runtime outside <c>_framework</c>, or <see langword="null"/> when it is not published.</summary>
    public static string? LiteDirectory { get; } = FindApp("BROADSIDE_BLAZOR_LITE_APP", "blazor-sample-lite");

    /// <summary>Gets the address of the non-AOT build (served by a host that refuses paths starting with an underscore).</summary>
    public Uri LiteAddress => new(BaseAddress, "lite/");

    /// <summary>Gets the server's base address, ending in a slash.</summary>
    public Uri BaseAddress { get; private set; } = new("http://127.0.0.1/");

    /// <summary>Gets Playwright (device profiles).</summary>
    public IPlaywright Playwright => _playwright!;

    /// <summary>Gets the browser.</summary>
    public IBrowser Browser { get; private set; } = null!;

    /// <summary>Skips (locally) or fails (CI) a test when the sample is not published.</summary>
    public static void RequireApp()
    {
        if (AppDirectory is not null)
        {
            return;
        }

        string message = "The Blazor sample is not published: run `samples/Broadside.Viewer.Blazor.Sample/publish-static.sh artifacts/blazor-sample` (needs the wasm-tools workload) or set BROADSIDE_BLAZOR_APP.";
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BROADSIDE_BLAZOR_REQUIRED") != "1", message);
        Assert.Fail(message);
    }

    /// <summary>Skips (locally) or fails (CI) a test when the non-AOT build is not published.</summary>
    public static void RequireLiteApp()
    {
        if (LiteDirectory is not null)
        {
            return;
        }

        string message = "The non-AOT sample is not published: run `samples/Broadside.Viewer.Blazor.Sample/publish-static.sh --no-aot --runtime-folder framework artifacts/blazor-sample-lite` or set BROADSIDE_BLAZOR_LITE_APP.";
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BROADSIDE_BLAZOR_REQUIRED") != "1", message);
        Assert.Fail(message);
    }

    /// <summary>
    /// Gets a browser by Playwright name (<c>chromium</c>, <c>webkit</c>, <c>firefox</c>), launched on first use. A browser that is not
    /// installed skips the test, unless BROADSIDE_BROWSERS_REQUIRED lists it (CI), which makes that a failure. Chromium is <see cref="Browser"/>.
    /// </summary>
    public async Task<IBrowser> GetBrowserAsync(string name)
    {
        if (name == "chromium")
        {
            return Browser;
        }

        await _launching.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_browsers.TryGetValue(name, out IBrowser? browser))
            {
                return browser;
            }

            IBrowserType type = name switch
            {
                "webkit" => _playwright!.Webkit,
                "firefox" => _playwright!.Firefox,
                _ => throw new ArgumentOutOfRangeException(nameof(name), name, "chromium, webkit or firefox"),
            };
            try
            {
                browser = await type.LaunchAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException exception) when (exception.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
            {
                string required = Environment.GetEnvironmentVariable("BROADSIDE_BROWSERS_REQUIRED") ?? "";
                string message = $"Playwright's {name} is not installed (pwsh bin/<config>/net10.0/playwright.ps1 install {name}).";
                Assert.SkipUnless(required.Split(',', StringSplitOptions.TrimEntries).Contains(name, StringComparer.Ordinal), message);
                Assert.Fail(message);
                throw;
            }

            _browsers[name] = browser;
            return browser;
        }
        finally
        {
            _launching.Release();
        }
    }

    /// <summary>Serves a document at <c>files/{name}</c>.</summary>
    public HostedDocument Serve(string name, byte[] bytes, long? gateFrom = null)
    {
        var document = new HostedDocument(bytes, gateFrom);
        _documents[name] = document;
        return document;
    }

    public async ValueTask InitializeAsync()
    {
        if (AppDirectory is null && LiteDirectory is null)
        {
            return;
        }

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _server = builder.Build();
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".wasm"] = "application/wasm";
        // A host that reserves paths starting with an underscore (the non-AOT build must not need any).
        // BROADSIDE_CROSS_ORIGIN_ISOLATED=1 serves everything with COOP/COEP (performance.measureUserAgentSpecificMemory needs it; the
        // render worker benchmark measures the page's and the worker's memory with it, #357). Off by default: static hosts send neither.
        bool isolated = Environment.GetEnvironmentVariable("BROADSIDE_CROSS_ORIGIN_ISOLATED") == "1";
        _server.Use(async (context, next) =>
        {
            if (isolated)
            {
                context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
                context.Response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";
            }

            string path = context.Request.Path.Value ?? "";
            if (path.StartsWith("/lite/", StringComparison.Ordinal) && path.Split('/').Any(static segment => segment.StartsWith('_')))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context).ConfigureAwait(false);
        });
        foreach ((string? directory, string prefix) in new[] { (AppDirectory, "/viewer"), (LiteDirectory, "/lite") })
        {
            if (directory is null)
            {
                continue;
            }

            var files = new PhysicalFileProvider(directory);
            // The app lives under a sub-path, as in a static host's folder: everything it loads must be relative.
            _server.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files, RequestPath = prefix });
            _server.UseStaticFiles(new StaticFileOptions { FileProvider = files, RequestPath = prefix, ContentTypeProvider = types });
        }

        _server.MapGet("/files/{name}", ServeDocumentAsync);
        await _server.StartAsync().ConfigureAwait(false);
        string address = _server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        BaseAddress = new Uri(address.TrimEnd('/') + "/");

        _playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        string? channel = Environment.GetEnvironmentVariable("BROADSIDE_BROWSER_CHANNEL");
        // WebGL 2 for the WebGL backend's tests (#353): the GPU where headless Chromium can use it (Metal through ANGLE on macOS),
        // SwiftShader (software WebGL) elsewhere, which Chromium only enables on request.
        string[] args = OperatingSystem.IsMacOS()
            ? ["--enable-gpu", "--ignore-gpu-blocklist", "--use-angle=metal", "--enable-unsafe-swiftshader"]
            : ["--ignore-gpu-blocklist", "--enable-unsafe-swiftshader"];
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = string.IsNullOrEmpty(channel) ? null : channel, Args = args }).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (HostedDocument document in _documents.Values)
        {
            document.Release();
        }

        foreach (IBrowser browser in _browsers.Values)
        {
            await browser.DisposeAsync().ConfigureAwait(false);
        }

        if (Browser is not null)
        {
            await Browser.DisposeAsync().ConfigureAwait(false);
        }

        _launching.Dispose();

        _playwright?.Dispose();
        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ServeDocumentAsync(HttpContext context, string name)
    {
        if (!_documents.TryGetValue(name, out HostedDocument? document))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        long length = document.Bytes.Length;
        long start = 0;
        long end = length - 1;
        string? range = context.Request.Headers.Range;
        bool partial = range is not null && TryParseRange(range, length, out start, out end);
        if (partial && start >= document.GateFrom)
        {
            Interlocked.Increment(ref document.Held);
            await document.Gate.Task.WaitAsync(context.RequestAborted).ConfigureAwait(false);
        }

        int count = (int)(end - start + 1);
        Interlocked.Add(ref document.Served, count);
        context.Response.Headers.AcceptRanges = "bytes";
        context.Response.ContentType = "application/pdf";
        context.Response.ContentLength = count;
        if (partial)
        {
            context.Response.StatusCode = StatusCodes.Status206PartialContent;
            context.Response.Headers.ContentRange = string.Create(CultureInfo.InvariantCulture, $"bytes {start}-{end}/{length}");
        }

        await context.Response.Body.WriteAsync(document.Bytes.AsMemory((int)start, count), context.RequestAborted).ConfigureAwait(false);
    }

    private static bool TryParseRange(string header, long length, out long start, out long end)
    {
        start = 0;
        end = length - 1;
        if (!header.StartsWith("bytes=", StringComparison.Ordinal) || header.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = header["bytes=".Length..].Split('-');
        if (parts[0].Length == 0)
        {
            start = Math.Max(0, length - long.Parse(parts[1], CultureInfo.InvariantCulture));
            return true;
        }

        start = long.Parse(parts[0], CultureInfo.InvariantCulture);
        if (parts[1].Length > 0)
        {
            end = Math.Min(end, long.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        return start <= end;
    }

    private static string? FindApp(string variable, string folder)
    {
        string? configured = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrEmpty(configured))
        {
            return Directory.Exists(configured) ? Path.GetFullPath(configured) : null;
        }

        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
            {
                string app = Path.Combine(directory.FullName, "artifacts", folder);
                return File.Exists(Path.Combine(app, "index.html")) ? app : null;
            }
        }

        return null;
    }
}

/// <summary>A document the host serves, with the bytes it has sent and the requests it holds at its gate.</summary>
public sealed class HostedDocument(byte[] bytes, long? gateFrom)
{
#pragma warning disable CA1051 // counters updated with Interlocked
    public long Served;
    public int Held;
#pragma warning restore CA1051

    public byte[] Bytes { get; } = bytes;

    public long GateFrom { get; } = gateFrom ?? long.MaxValue;

    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Lets every held request through.</summary>
    public void Release() => Gate.TrySetResult();
}
