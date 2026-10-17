using System.IO.Compression;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// The published sample (trimmed, AOT-compiled, a static site) carries no native binaries, only standard web media types, and stays
/// under its size budget (#221 acceptance 2).
/// </summary>
public sealed class PublishedAppTests
{
    /// <summary>
    /// The budget for what a browser downloads when the host compresses responses: every file of the site Brotli-compressed at the highest quality, the
    /// sample documents excluded. Set with 7.6 MB measured for #221 (the bundled Standard 14 fonts 1.5 MB of it); the demo (#338) measures 8.9 MB with the layout package and its panels, inside the same budget.
    /// </summary>
    public const long CompressedBudget = 10 * 1024 * 1024;

    /// <summary>The budget for the files as they lie on disk (32 MB measured for #221, 38.3 MB for the demo, documents excluded): the AOT-compiled runtime module is two thirds of it.</summary>
    public const long UncompressedBudget = 40 * 1024 * 1024;

    /// <summary>
    /// The most files the site may have (a static host and a phone fetch each one): #221's 80, plus the demo's allowance (#338) of 10
    /// for the gallery documents (13 PDFs, which a browser fetches only when picked) and its start script, the layout package and the panels.
    /// </summary>
    public const int FileBudget = 90;

    private static readonly string[] WebTypes = [".html", ".js", ".css", ".wasm", ".json", ".png", ".ico", ".svg", ".pdf"];

    [Fact]
    public void The_published_app_has_no_native_binaries_beyond_the_WebAssembly_runtime()
    {
        BrowserHost.RequireApp();
        string root = BrowserHost.AppDirectory!;
        var foreign = new List<string>();
        var natives = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (!WebTypes.Contains(Path.GetExtension(file), StringComparer.Ordinal))
            {
                foreign.Add(name);
            }
            else if (name.EndsWith(".wasm", StringComparison.Ordinal))
            {
                // Managed assemblies ship as Webcil inside a WebAssembly wrapper; the only real native module is the runtime itself.
                if (name.StartsWith("dotnet.native.", StringComparison.Ordinal))
                {
                    natives.Add(name);
                }
                else
                {
                    Assert.True(File.ReadAllBytes(file).AsSpan().IndexOf("WbIL"u8) >= 0, $"{name} is a Webcil assembly");
                }
            }
        }

        Assert.Empty(foreign);
        string runtime = Assert.Single(natives);
        Assert.True(new FileInfo(Directory.GetFiles(root, runtime, SearchOption.AllDirectories).Single()).Length > 8 * 1024 * 1024, "the runtime module holds the AOT-compiled code");
    }

    [Fact]
    public void The_published_app_fits_its_size_budget()
    {
        BrowserHost.RequireApp();
        string[] files = Directory.GetFiles(BrowserHost.AppDirectory!, "*", SearchOption.AllDirectories);
        long compressed = 0;
        long uncompressed = 0;
        foreach (string file in files.Where(static file => !file.EndsWith(".pdf", StringComparison.Ordinal)))
        {
            byte[] bytes = File.ReadAllBytes(file);
            uncompressed += bytes.Length;
            using var sink = new MemoryStream();
            using (var brotli = new BrotliStream(sink, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                brotli.Write(bytes);
            }

            compressed += sink.Length;
        }

        Viewer.Log($"published app: {files.Length} files, {uncompressed:N0} B on disk, {compressed:N0} B Brotli");
        Assert.InRange(files.Length, 1, FileBudget);
        Assert.InRange(compressed, 1, CompressedBudget);
        Assert.InRange(uncompressed, 1, UncompressedBudget);
    }
}
