using System.Collections.Concurrent;
using System.Security.Cryptography;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Memory budgets and concurrent replay (issue #103, spec #33 viewer capability 13 and story 8): the render caches never change a
/// bitmap, whatever their budgets and however many threads render one document at once, and the image cache stays within its budget
/// on an image-heavy real-world file.
/// </summary>
/// <remarks>
/// In the non-parallel collection: the stress test runs its own eight threads, and a cache hit counted here would turn into a miss
/// if a test in parallel changed a COS object (any change makes multi-object entries stale).
/// </remarks>
[Collection(HeavyTestCollection.Name)]
public sealed class RenderCacheRenderingTests
{
    /// <summary>Budgets small enough that rendering the corpus evicts from every cache all the time.</summary>
    private static readonly Func<PdfOptions, PdfOptions> Tiny = static options => options
        .WithContentCacheBudget(256)
        .WithImageCacheBudget(256)
        .WithGlyphCacheBudget(1024)
        .WithDisplayListCacheBudget(512);

    private static readonly Func<PdfOptions, PdfOptions> Defaults = static options => options;

    private static readonly Func<PdfOptions, PdfOptions> NoCaches = static options => options
        .WithContentCacheBudget(0).WithImageCacheBudget(0).WithGlyphCacheBudget(0).WithDisplayListCacheBudget(0);

    private static readonly Lazy<IReadOnlyDictionary<(string File, int Page), string>> Sequential =
        new(() => RenderSequentially(NoCaches), LazyThreadSafetyMode.ExecutionAndPublication);

    public static TheoryData<string> Budgets => ["tiny", "default"];

    public static TheoryData<string> ZeroBudgets => ["content", "images", "glyphs", "display lists", "all"];

    [Theory]
    [MemberData(nameof(Budgets))]
    public void Rendering_every_corpus_page_on_many_threads_gives_the_bitmaps_of_a_sequential_render(string budgets)
    {
        // One document per file shared by every thread, each page rendered four times in an interleaved order on eight threads,
        // against each page rendered once, alone, with every cache off.
        PdfEngine engine = CreateEngine(budgets == "tiny" ? Tiny : Defaults);
        Dictionary<string, PdfDocument> documents = Corpus.AllFileNames.ToDictionary(static file => file, file => Open(engine, file));
        try
        {
            (string File, int Page)[] work =
            [
                .. from repeat in Enumerable.Range(0, 4)
                   from page in Sequential.Value.Keys.OrderBy(key => HashCode.Combine(key.File, key.Page, repeat))
                   select page,
            ];
            var rendered = new ConcurrentBag<((string File, int Page) Page, string Hash)>();
            using var rasterizers = new ThreadLocal<SoftwareRasterizer>(static () => new SoftwareRasterizer(), trackAllValues: true);

            Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = 8 }, page =>
            {
                using Bitmap bitmap = rasterizers.Value!.Render(documents[page.File].Pages[page.Page]);
                rendered.Add((page, Hash(bitmap)));
            });

            foreach (SoftwareRasterizer rasterizer in rasterizers.Values)
            {
                rasterizer.Dispose();
            }

            Assert.Equal(work.Length, rendered.Count);
            foreach (((string File, int Page) page, string hash) in rendered)
            {
                Assert.True(Sequential.Value[page] == hash, $"{page.File} page {page.Page} rendered differently on many threads.");
            }

            if (budgets == "tiny")
            {
                long images = documents.Values.Sum(static document => document.CacheStatistics.Images.Evictions);
                long glyphs = documents.Values.Sum(static document => document.CacheStatistics.Glyphs.Evictions);
                long lists = documents.Values.Sum(static document => document.CacheStatistics.DisplayLists.Evictions);
                Assert.True(images > 0 && glyphs > 0 && lists > 0, $"Evictions: images {images}, glyphs {glyphs}, display lists {lists}.");
            }

            foreach (PdfDocument document in documents.Values)
            {
                PdfDocumentCacheStatistics statistics = document.CacheStatistics;
                foreach (PdfCacheStatistics cache in new[] { statistics.Content, statistics.Images, statistics.Glyphs, statistics.DisplayLists })
                {
                    Assert.InRange(cache.PeakBytes, 0, cache.Budget);
                }
            }
        }
        finally
        {
            foreach (PdfDocument document in documents.Values)
            {
                document.Dispose();
            }
        }
    }

    [Theory]
    [MemberData(nameof(ZeroBudgets))]
    public void A_budget_of_zero_turns_its_cache_off_without_changing_any_bitmap(string cache)
    {
        Func<PdfOptions, PdfOptions> configure = cache switch
        {
            "content" => static options => options.WithContentCacheBudget(0),
            "images" => static options => options.WithImageCacheBudget(0),
            "glyphs" => static options => options.WithGlyphCacheBudget(0),
            "display lists" => static options => options.WithDisplayListCacheBudget(0),
            _ => NoCaches,
        };

        Dictionary<(string File, int Page), string> withDefaults = RenderSequentially(Defaults);
        Dictionary<(string File, int Page), string> withoutCache = RenderSequentially(configure);

        Assert.Equal(withDefaults.Count, withoutCache.Count);
        foreach (KeyValuePair<(string File, int Page), string> page in withDefaults)
        {
            Assert.True(withoutCache[page.Key] == page.Value, $"{page.Key.File} page {page.Key.Page} changed with the {cache} cache off.");
        }
    }

    [Fact]
    public void Rendering_an_image_heavy_real_world_file_holds_no_more_decoded_images_than_a_16_MB_budget()
    {
        // pdfium's quick_start_guide.pdf: eleven pages whose distinct images decode to more than the budget, several of them painted
        // on more than one page, so the cache both evicts and hits.
        if (RealWorldCorpus.Directory("pdfium-tests") is null)
        {
            Assert.Skip($"pdfium-tests not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        const long budget = 16L << 20;
        using PdfDocument document = PdfDocument.Open(
            RealWorldCorpus.PathOf("pdfium-tests/fx/text/quick_start_guide.pdf"),
            new PdfOptions().WithImageCacheBudget(budget).WithDisplayListCacheBudget(0));
        using var rasterizer = new SoftwareRasterizer();

        long peak = 0;
        foreach (PdfPage page in document.Pages)
        {
            using Bitmap bitmap = rasterizer.Render(page);
            PdfCacheStatistics images = document.CacheStatistics.Images;
            Assert.InRange(images.Bytes, 0, budget);
            peak = Math.Max(peak, images.Bytes);
        }

        PdfCacheStatistics final = document.CacheStatistics.Images;
        Assert.Equal(11, document.Pages.Count);
        Assert.Equal(budget, final.Budget);
        Assert.InRange(final.PeakBytes, peak, budget);
        Assert.True(final.Evictions > 0, "The images exceed the budget, so some must have been evicted.");
        Assert.True(final.Hits > 0, "Images painted on several pages are decoded once while they stay in the cache.");
        Assert.True(peak > budget / 2, $"The cache should fill towards its budget; it peaked at {peak} bytes.");
    }

    private static PdfEngine CreateEngine(Func<PdfOptions, PdfOptions> configure) =>
        new(configure(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps().UseSystemFontResolver(null)));

    private static PdfDocument Open(PdfEngine engine, string fileName)
    {
        string? password = Corpus.UserPassword(fileName);
        return password is null ? engine.Open(Corpus.Bytes(fileName)) : engine.Open(Corpus.Bytes(fileName), new PdfPassword(password));
    }

    private static Dictionary<(string File, int Page), string> RenderSequentially(Func<PdfOptions, PdfOptions> configure)
    {
        PdfEngine engine = CreateEngine(configure);
        var hashes = new Dictionary<(string File, int Page), string>();
        using var rasterizer = new SoftwareRasterizer();
        foreach (string file in Corpus.AllFileNames)
        {
            using PdfDocument document = Open(engine, file);
            for (int page = 0; page < document.Pages.Count; page++)
            {
                using Bitmap bitmap = rasterizer.Render(document.Pages[page]);
                hashes[(file, page)] = Hash(bitmap);
            }
        }

        return hashes;
    }

    private static string Hash(Bitmap bitmap)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> size = stackalloc byte[8];
        BitConverter.TryWriteBytes(size, bitmap.Width);
        BitConverter.TryWriteBytes(size[4..], bitmap.Height);
        hash.AppendData(size);
        for (int y = 0; y < bitmap.Height; y++)
        {
            hash.AppendData(bitmap.GetRow(y)[..(bitmap.Width * 4)]);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
