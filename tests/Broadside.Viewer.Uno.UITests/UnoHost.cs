using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(Broadside.Viewer.Uno.UITests.UnoHost))]

namespace Broadside.Viewer.Uno.UITests;

/// <summary>
/// Serves the published WebAssembly head (a static site) over HTTP and launches Playwright's browsers on demand. Finds the built
/// desktop head too.
/// </summary>
public sealed class UnoHost : IAsyncLifetime
{
    private readonly Dictionary<string, IBrowser> _browsers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _launching = new(1, 1);
    private WebApplication? _server;
    private IPlaywright? _playwright;

    /// <summary>Gets the published WebAssembly head's <c>wwwroot</c>, or <see langword="null"/>.</summary>
    public static string? WasmDirectory { get; } = FindWasm();

    /// <summary>Gets the built desktop head's assembly, or <see langword="null"/>.</summary>
    public static string? DesktopAssembly { get; } = FindDesktop();

    /// <summary>Gets the server's base address, ending in a slash.</summary>
    public Uri BaseAddress { get; private set; } = new("http://127.0.0.1/");

    /// <summary>Skips (locally) or fails (CI) a test when a head is not built.</summary>
    public static void Require(string? built, string what)
    {
        if (built is not null)
        {
            return;
        }

        string message = $"The Uno sample's {what} is not built (see tests/Broadside.Viewer.Uno.UITests/Broadside.Viewer.Uno.UITests.csproj).";
        Assert.SkipWhen(Environment.GetEnvironmentVariable("BROADSIDE_UNO_REQUIRED") != "1", message);
        Assert.Fail(message);
    }

    /// <summary>
    /// Gets a browser by Playwright name (<c>chromium</c>, <c>webkit</c>), launched on first use. A browser that is not installed skips
    /// the test, unless BROADSIDE_BROWSERS_REQUIRED lists it (CI).
    /// </summary>
    public async Task<IBrowser> GetBrowserAsync(string name)
    {
        await _launching.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_browsers.TryGetValue(name, out IBrowser? browser))
            {
                return browser;
            }

            string? listed = Environment.GetEnvironmentVariable("BROADSIDE_BROWSERS_REQUIRED");
            Assert.SkipWhen(
                !string.IsNullOrEmpty(listed) && !listed.Split(',', StringSplitOptions.TrimEntries).Contains(name, StringComparer.Ordinal),
                $"Playwright's {name} is not one of this job's browsers ({listed}).");
            _playwright ??= await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
            try
            {
                browser = name switch
                {
                    "chromium" => await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Channel = Environment.GetEnvironmentVariable("BROADSIDE_BROWSER_CHANNEL") is { Length: > 0 } channel ? channel : null,
                        Args = ["--ignore-gpu-blocklist", "--enable-unsafe-swiftshader"],
                    }).ConfigureAwait(false),
                    "webkit" => await _playwright.Webkit.LaunchAsync().ConfigureAwait(false),
                    _ => throw new ArgumentOutOfRangeException(nameof(name), name, "chromium or webkit"),
                };
            }
            catch (PlaywrightException exception) when (exception.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
            {
                string message = $"Playwright's {name} is not installed (pwsh bin/<config>/net10.0/playwright.ps1 install {name}).";
                Assert.SkipUnless((listed ?? "").Split(',', StringSplitOptions.TrimEntries).Contains(name, StringComparer.Ordinal), message);
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

    public async ValueTask InitializeAsync()
    {
        if (WasmDirectory is null)
        {
            return;
        }

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _server = builder.Build();
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".wasm"] = "application/wasm";
        types.Mappings[".dat"] = "application/octet-stream";
        types.Mappings[".blat"] = "application/octet-stream";
        types.Mappings[".clr"] = "application/octet-stream";
        types.Mappings[".pdb"] = "application/octet-stream";
        types.Mappings[".webcil"] = "application/octet-stream";
        var files = new PhysicalFileProvider(WasmDirectory);
        _server.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        _server.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = types, ServeUnknownFileTypes = true });
        await _server.StartAsync().ConfigureAwait(false);
        string address = _server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        BaseAddress = new Uri(address.TrimEnd('/') + "/");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (IBrowser browser in _browsers.Values)
        {
            await browser.DisposeAsync().ConfigureAwait(false);
        }

        _launching.Dispose();
        _playwright?.Dispose();
        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string? RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string? FindWasm()
    {
        string? configured = Environment.GetEnvironmentVariable("BROADSIDE_UNO_WASM_APP");
        string? folder = !string.IsNullOrEmpty(configured) ? configured : RepositoryRoot() is { } root ? Path.Combine(root, "artifacts", "uno-sample-wasm") : null;
        if (folder is null)
        {
            return null;
        }

        // `dotnet publish -o <folder>` puts the static site in <folder>/wwwroot.
        foreach (string candidate in new[] { Path.Combine(folder, "wwwroot"), folder })
        {
            if (File.Exists(Path.Combine(candidate, "index.html")))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static string? FindDesktop()
    {
        string? configured = Environment.GetEnvironmentVariable("BROADSIDE_UNO_DESKTOP_APP");
        if (!string.IsNullOrEmpty(configured))
        {
            return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        }

        if (RepositoryRoot() is not { } root)
        {
            return null;
        }

        string bin = Path.Combine(root, "artifacts", "bin", "Broadside.Viewer.Uno.Sample");
        if (!Directory.Exists(bin))
        {
            return null;
        }

        // The newest build of the desktop head (release_net10.0-desktop, debug_net10.0-desktop, with or without a runtime identifier).
        return Directory.EnumerateFiles(bin, "Broadside.Viewer.Uno.Sample.dll", SearchOption.AllDirectories)
            .Where(path => path.Contains("net10.0-desktop", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
