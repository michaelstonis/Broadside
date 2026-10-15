using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Broadside.GoldenImages;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests.CorpusGate;

/// <summary>
/// The pinned real-world subset of the rendering gate (issue #138), held per open decision 8 of the spec's Phase 3 plan: real-world
/// renders are not committed (repository size, corpus licences); each pinned page is held as a reviewed fingerprint in
/// <c>pinned-fingerprints.txt</c> (pixel size and the mean colour of each cell of a grid of at most <see cref="GridCells"/> cells on
/// the long side), compared within <see cref="CellTolerance"/> levels per channel, and the renders themselves are written to
/// <c>artifacts/render-gate/pinned/</c> for CI to keep as artifacts. The pages are listed with the reason each is pinned in
/// <c>pinned-pages.txt</c>. Set <see cref="GoldenCorpus.AcceptVariable"/> to <c>1</c> to rewrite the fingerprints after reviewing the
/// renders against <c>pdftoppm -r 72</c>. Every page of every pinned file is also rendered with all pages in parallel on one
/// document (spec story 8), and the pixels must equal the sequential renders exactly.
/// ISO 32000-2 §8, §10, §11, §12.5.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RenderCorpusPinnedSubsetTests(ITestOutputHelper output)
{
    /// <summary>The most grid cells on the long side of a fingerprint.</summary>
    public const int GridCells = 24;

    /// <summary>How far, in 8-bit levels, a cell's mean channel may move: covers the rasterizer's last-bit differences across OSes.</summary>
    public const int CellTolerance = 3;

    private static readonly string GateDirectory = Path.GetFullPath(Path.Combine(Corpus.Directory, "..", "Broadside.Rendering.Tests", "CorpusGate"));

    private static readonly string PinnedPath = Path.Combine(GateDirectory, "pinned-pages.txt");

    private static readonly string FingerprintPath = Path.Combine(GateDirectory, "pinned-fingerprints.txt");

    private static readonly string RenderDirectory = Path.GetFullPath(Path.Combine(Corpus.Directory, "..", "..", "artifacts", "render-gate", "pinned"));

    [Fact]
    public void Pinned_pages_match_their_reviewed_fingerprints()
    {
        (string File, int[] Pages)[] pinned = FetchedPinned();
        Dictionary<(string File, int Page), string> stored = ReadFingerprints();
        var actual = new SortedDictionary<(string File, int Page), string>(Comparer<(string File, int Page)>.Create(
            static (a, b) => string.CompareOrdinal(a.File, b.File) is int byFile and not 0 ? byFile : a.Page.CompareTo(b.Page)));
        Directory.CreateDirectory(RenderDirectory);
        foreach ((string file, int[] pages) in pinned)
        {
            using PdfDocument document = RenderCorpusGate.Engine.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)));
            using var rasterizer = new SoftwareRasterizer();
            foreach (int page in pages)
            {
                using Bitmap bitmap = rasterizer.Render(document.Pages[page], RenderCorpusGate.OptionsFor(document.Pages[page], CancellationToken.None));
                string png = Path.Combine(RenderDirectory, $"{file.Replace('/', '_')}.page{page}.png");
                using (FileStream stream = File.Create(png))
                {
                    bitmap.SavePng(stream);
                }

                actual[(file, page)] = Fingerprint(bitmap);
            }
        }

        if (Environment.GetEnvironmentVariable(GoldenCorpus.AcceptVariable) == "1")
        {
            foreach (KeyValuePair<(string File, int Page), string> entry in stored.Where(entry => !actual.ContainsKey(entry.Key)))
            {
                actual[entry.Key] = entry.Value;
            }

            WriteFingerprints(actual);
            output.WriteLine($"Accepted {actual.Count} fingerprints into {FingerprintPath}; renders in {RenderDirectory}.");
            return;
        }

        var problems = new List<string>();
        foreach (((string file, int page), string fingerprint) in actual)
        {
            if (!stored.TryGetValue((file, page), out string? expected))
            {
                problems.Add($"{file} page {page}: no reviewed fingerprint (review the render, then accept with {GoldenCorpus.AcceptVariable}=1)");
            }
            else if (Compare(expected, fingerprint) is { } difference)
            {
                problems.Add($"{file} page {page}: {difference}");
            }
        }

        foreach ((string file, int page) in stored.Keys.Where(key => !pinned.Any(entry => entry.File == key.File && entry.Pages.Contains(key.Page)) && RealWorldCorpus.Directory(key.File[..key.File.IndexOf('/', StringComparison.Ordinal)]) is not null))
        {
            problems.Add($"{file} page {page}: fingerprint of a page that is no longer pinned, delete it");
        }

        Assert.True(problems.Count == 0, $"Pinned renders differ from {FingerprintPath} (renders in {RenderDirectory}):\n{string.Join("\n", problems)}");
    }

    [Fact]
    public async Task Parallel_rendering_of_every_page_equals_sequential_rendering()
    {
        (string File, int[] Pages)[] pinned = FetchedPinned();
        var problems = new ConcurrentBag<string>();
        foreach ((string file, _) in pinned)
        {
            RenderFileResult sequential = await RenderCorpusGate.RunFileAsync(file, hashes: true);
            Assert.Equal(RenderFileOutcome.Rendered, sequential.Outcome);
            using PdfDocument document = RenderCorpusGate.Engine.Open(await File.ReadAllBytesAsync(RealWorldCorpus.PathOf(file), TestContext.Current.CancellationToken));
            PdfPage[] pages = [.. document.Pages];
            var hashes = new string[pages.Length];
            Parallel.For(0, pages.Length, index =>
            {
                using var rasterizer = new SoftwareRasterizer();
                using Bitmap bitmap = rasterizer.Render(pages[index], RenderCorpusGate.OptionsFor(pages[index], CancellationToken.None));
                hashes[index] = RenderCorpusGate.Hash(bitmap);
            });
            for (int index = 0; index < pages.Length; index++)
            {
                if (hashes[index] != sequential.PageHashes![index])
                {
                    problems.Add($"{file} page {index}");
                }
            }
        }

        Assert.True(problems.IsEmpty, $"Parallel renders differ from sequential ones:\n{string.Join("\n", problems.Order(StringComparer.Ordinal))}");
    }

    /// <summary>The pinned files of the fetched corpora; skips the test when none is fetched.</summary>
    private static (string File, int[] Pages)[] FetchedPinned()
    {
        var pinned = new List<(string, int[])>();
        foreach (string line in File.ReadLines(PinnedPath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split('\t');
            Assert.True(fields.Length == 3 && fields[2].Trim().Length > 0, $"Pinned line needs file, pages and a reason: {line}");
            if (RealWorldCorpus.Directory(fields[0][..fields[0].IndexOf('/', StringComparison.Ordinal)]) is not null)
            {
                Assert.True(File.Exists(RealWorldCorpus.PathOf(fields[0])), $"Pinned file missing from its fetched corpus: {fields[0]}");
                pinned.Add((fields[0], [.. fields[1].Split(',').Select(page => int.Parse(page, CultureInfo.InvariantCulture))]));
            }
        }

        if (pinned.Count == 0)
        {
            Assert.Skip($"No corpus of the pinned subset is fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        return [.. pinned];
    }

    /// <summary>The fingerprint: <c>WxH</c>, the grid size, then each cell's mean R, G and B as hex, row by row.</summary>
    private static string Fingerprint(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        int columns = width >= height ? GridCells : Math.Max(1, (int)Math.Round(GridCells * (double)width / height));
        int rows = height >= width ? GridCells : Math.Max(1, (int)Math.Round(GridCells * (double)height / width));
        var sums = new long[rows * columns * 3];
        var counts = new long[rows * columns];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = bitmap.GetRow(y);
            int cellRow = (int)((long)y * rows / height);
            for (int x = 0; x < width; x++)
            {
                int cell = (cellRow * columns) + (int)((long)x * columns / width);
                sums[cell * 3] += row[(x * 4) + 2];
                sums[(cell * 3) + 1] += row[(x * 4) + 1];
                sums[(cell * 3) + 2] += row[x * 4];
                counts[cell]++;
            }
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{width}x{height}\t{columns}x{rows}\t");
        for (int index = 0; index < sums.Length; index++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{(sums[index] + (counts[index / 3] / 2)) / counts[index / 3]:x2}");
        }

        return text.ToString();
    }

    /// <summary>Null when the two fingerprints agree within <see cref="CellTolerance"/>, else what differs.</summary>
    private static string? Compare(string expected, string actual)
    {
        string[] e = expected.Split('\t');
        string[] a = actual.Split('\t');
        if (e[0] != a[0] || e[1] != a[1])
        {
            return $"size {a[0]} (grid {a[1]}), reviewed {e[0]} (grid {e[1]})";
        }

        byte[] reviewed = Convert.FromHexString(e[2]);
        byte[] rendered = Convert.FromHexString(a[2]);
        int worst = 0;
        int cells = 0;
        for (int index = 0; index < reviewed.Length; index++)
        {
            int difference = Math.Abs(reviewed[index] - rendered[index]);
            worst = Math.Max(worst, difference);
            cells += difference > CellTolerance ? 1 : 0;
        }

        return worst > CellTolerance ? $"{cells} cell channel(s) beyond {CellTolerance} levels, worst {worst}" : null;
    }

    private static Dictionary<(string File, int Page), string> ReadFingerprints()
    {
        var entries = new Dictionary<(string, int), string>();
        if (!File.Exists(FingerprintPath))
        {
            return entries;
        }

        foreach (string line in File.ReadLines(FingerprintPath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split('\t', 3);
            entries[(fields[0], int.Parse(fields[1], CultureInfo.InvariantCulture))] = fields[2];
        }

        return entries;
    }

    private static void WriteFingerprints(SortedDictionary<(string File, int Page), string> entries)
    {
        var text = new StringBuilder();
        text.Append("# Reviewed fingerprints of the pinned real-world pages (issue #138; see RenderCorpusPinnedSubsetTests). One line per page:\n");
        text.Append("# corpus/path<TAB>page<TAB>pixel size<TAB>grid<TAB>mean R, G, B of each cell as hex, row by row. Rewritten by BROADSIDE_ACCEPT_GOLDEN=1.\n");
        foreach (((string file, int page), string fingerprint) in entries)
        {
            text.Append(CultureInfo.InvariantCulture, $"{file}\t{page}\t{fingerprint}\n");
        }

        File.WriteAllText(FingerprintPath, text.ToString());
    }
}
