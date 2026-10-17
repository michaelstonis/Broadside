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
/// every request starting at or past an offset), and runs one headless Chromium for all tests.
/// </summary>
public sealed class BrowserHost : IAsyncLifetime
{
    private readonly ConcurrentDictionary<string, HostedDocument> _documents = new(StringComparer.Ordinal);
    private WebApplication? _server;
    private IPlaywright? _playwright;

    /// <summary>Gets the published app's <c>wwwroot</c>, or <see langword="null"/> when it is not published.</summary>
    public static string? AppDirectory { get; } = FindApp();

    /// <summary>Gets the address of the app (a sub-path of the server), ending in a slash.</summary>
    public Uri AppAddress => new(BaseAddress, "viewer/");

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

    /// <summary>Serves a document at <c>files/{name}</c>.</summary>
    public HostedDocument Serve(string name, byte[] bytes, long? gateFrom = null)
    {
        var document = new HostedDocument(bytes, gateFrom);
        _documents[name] = document;
        return document;
    }

    public async ValueTask InitializeAsync()
    {
        if (AppDirectory is null)
        {
            return;
        }

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _server = builder.Build();
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".wasm"] = "application/wasm";
        var files = new PhysicalFileProvider(AppDirectory);
        // The app lives under a sub-path, as in a static host's folder: everything it loads must be relative.
        _server.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files, RequestPath = "/viewer" });
        _server.UseStaticFiles(new StaticFileOptions { FileProvider = files, RequestPath = "/viewer", ContentTypeProvider = types });
        _server.MapGet("/files/{name}", ServeDocumentAsync);
        await _server.StartAsync().ConfigureAwait(false);
        string address = _server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        BaseAddress = new Uri(address.TrimEnd('/') + "/");

        _playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        string? channel = Environment.GetEnvironmentVariable("BROADSIDE_BROWSER_CHANNEL");
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = string.IsNullOrEmpty(channel) ? null : channel }).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (HostedDocument document in _documents.Values)
        {
            document.Release();
        }

        if (Browser is not null)
        {
            await Browser.DisposeAsync().ConfigureAwait(false);
        }

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

    private static string? FindApp()
    {
        string? configured = Environment.GetEnvironmentVariable("BROADSIDE_BLAZOR_APP");
        if (!string.IsNullOrEmpty(configured))
        {
            return Directory.Exists(configured) ? Path.GetFullPath(configured) : null;
        }

        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
            {
                string app = Path.Combine(directory.FullName, "artifacts", "blazor-sample");
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
