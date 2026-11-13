using Broadside.GoldenImages;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests.CrossPlatform;

/// <summary>
/// The software rasterizer gives the same bytes on every platform (issue #233). <see cref="Every_corpus_page_renders_to_the_same_bytes_every_time"/>
/// hashes every page of every hand-written corpus file at both <see cref="RenderHashManifest.Dpis"/> and, with
/// <see cref="RenderHashManifest.OutputVariable"/> set, writes the manifest and the renders; the <c>render-determinism</c> workflow runs
/// it on Linux x64, Windows x64, macOS arm64 and with hardware intrinsics off, then <see cref="The_manifests_of_every_runner_agree"/>
/// compares the manifests and fails with the differing pages and their diff images.
/// </summary>
[Trait("Category", Category)]
public sealed class RenderHashTests
{
    /// <summary>The trait category of the hashing test, which the workflow filters on.</summary>
    public const string Category = "RenderHash";

    [Fact]
    public void Every_corpus_page_renders_to_the_same_bytes_every_time()
    {
        string? output = Environment.GetEnvironmentVariable(RenderHashManifest.OutputVariable);
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
        }

        var entries = new List<RenderHashEntry>();
        var unstable = new List<string>();
        using var rasterizer = new SoftwareRasterizer();
        foreach (string fileName in Corpus.AllFileNames)
        {
            using PdfDocument document = GoldenCorpus.Open(fileName);
            for (int page = 0; page < document.Pages.Count; page++)
            {
                foreach (double dpi in RenderHashManifest.Dpis)
                {
                    var options = new RenderOptions { Dpi = dpi };
                    string name = RenderHashManifest.EntryName(fileName, page, dpi);
                    using Bitmap bitmap = rasterizer.Render(document.Pages[page], options);
                    string hash = RenderHashManifest.Hash(bitmap);

                    // A second render reuses the document's caches (decoded images, fonts, colour converters) and must not change a byte.
                    using Bitmap again = rasterizer.Render(document.Pages[page], options);
                    if (RenderHashManifest.Hash(again) != hash)
                    {
                        unstable.Add(name);
                    }

                    entries.Add(new RenderHashEntry(name, bitmap.Width, bitmap.Height, hash));
                    if (!string.IsNullOrEmpty(output))
                    {
                        GoldenImage.FromBitmap(bitmap).SavePng(Path.Combine(output, name + ".png"));
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(output))
        {
            RenderHashManifest.Write(Path.Combine(output, RenderHashManifest.FileName), entries);
        }

        Assert.True(unstable.Count == 0, $"Rendered twice in one process, these pages differ: {string.Join(", ", unstable)}.");
        Assert.Equal(GoldenCorpus.Pages.Count * RenderHashManifest.Dpis.Count, entries.Count);
    }

    [Fact]
    [Trait("Category", "RenderHashComparison")]
    public void The_manifests_of_every_runner_agree()
    {
        string? directory = Environment.GetEnvironmentVariable(RenderHashManifest.CompareVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(directory), $"{RenderHashManifest.CompareVariable} is not set; the render-determinism workflow sets it.");

        (IReadOnlyList<RenderHashMismatch> mismatches, string report) = RenderHashManifest.CompareRuns(directory);
        TestContext.Current.TestOutputHelper?.WriteLine(report);
        Assert.True(mismatches.Count == 0, $"The software rasterizer drew different bytes on different runners.\n{report}");
    }

    [Fact]
    public void A_comparison_lists_every_render_that_differs_or_is_missing_and_draws_its_diff_images()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("broadside-render-hash-");
        try
        {
            using var white = new Bitmap(4, 3);
            white.Pixels.Fill(255);
            using var marked = new Bitmap(4, 3);
            marked.Pixels.Fill(255);
            marked.GetRow(1)[4..8].Clear();
            marked.GetRow(1)[7] = 255;
            string same = RenderHashManifest.Hash(white);
            string other = RenderHashManifest.Hash(marked);
            Assert.NotEqual(same, other);

            WriteRun(root, "linux-x64", ("a.page0.72dpi", white), ("b.page0.72dpi", white), ("c.page0.72dpi", white));
            WriteRun(root, "macos-arm64", ("a.page0.72dpi", white), ("b.page0.72dpi", marked), ("c.page0.72dpi", white));
            WriteRun(root, "windows-x64", ("a.page0.72dpi", white), ("b.page0.72dpi", white));

            (IReadOnlyList<RenderHashMismatch> mismatches, string report) = RenderHashManifest.CompareRuns(root.FullName);

            Assert.Equal(["b.page0.72dpi", "c.page0.72dpi"], mismatches.Select(mismatch => mismatch.Name));
            Assert.Equal(other, mismatches[0].Hashes["macos-arm64"]);
            Assert.Null(mismatches[1].Hashes["windows-x64"]);
            Assert.Contains("2 differ", report, StringComparison.Ordinal);
            Assert.Contains("windows-x64=missing", report, StringComparison.Ordinal);
            string diff = Path.Combine(root.FullName, RenderHashManifest.DifferencesDirectory, "b.page0.72dpi.linux-x64-vs-macos-arm64.diff.png");
            Assert.True(File.Exists(diff));
            Assert.Contains("1 pixels differ", report, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_manifest_reads_back_what_was_written()
    {
        string path = Path.GetTempFileName();
        try
        {
            RenderHashEntry[] entries = [new("z.page1.150dpi", 1275, 1650, "ab"), new("a.page0.72dpi", 612, 792, "cd")];
            RenderHashManifest.Write(path, entries);

            Assert.StartsWith("# ", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Equal(entries.OrderBy(entry => entry.Name, StringComparer.Ordinal), RenderHashManifest.Read(path).Values);
            File.AppendAllText(path, "broken line\n");
            Assert.Throws<FormatException>(() => RenderHashManifest.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteRun(DirectoryInfo root, string runner, params (string Name, Bitmap Bitmap)[] renders)
    {
        string directory = Path.Combine(root.FullName, runner);
        Directory.CreateDirectory(directory);
        foreach ((string name, Bitmap bitmap) in renders)
        {
            GoldenImage.FromBitmap(bitmap).SavePng(Path.Combine(directory, name + ".png"));
        }

        RenderHashManifest.Write(
            Path.Combine(directory, RenderHashManifest.FileName),
            renders.Select(render => new RenderHashEntry(render.Name, render.Bitmap.Width, render.Bitmap.Height, RenderHashManifest.Hash(render.Bitmap))));
    }
}
