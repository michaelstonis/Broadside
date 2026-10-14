using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using Broadside.Content;
using Broadside.Filters;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Rendering;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Spec #33 viewer capability 13 and story 8 (issue #103): decoded content, decoded images and masks, glyph outlines and display
/// lists live in per-document caches with byte budgets from <see cref="PdfOptions"/>, evicted least recently used, observable through
/// <see cref="PdfDocument.CacheStatistics"/>. A budget of zero turns its cache off.
/// </summary>
/// <remarks>
/// In the non-parallel collection: a cache entry is current while no COS object anywhere in the process changes, so a test editing
/// another document at the same time would turn an expected hit into a miss.
/// </remarks>
[Collection(HeavyTestCollection.Name)]
public sealed class RenderCacheTests
{
    private static readonly CosName Contents = new("Contents");

    [Fact]
    public void A_page_s_display_list_is_built_once_while_the_document_is_unchanged()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage("0 0 1 rg 10 10 100 50 re f"u8.ToArray()));
        PdfPage page = document.Pages[0];

        DisplayList first = page.CreateDisplayList();
        DisplayList second = page.CreateDisplayList();

        Assert.Same(first, second);
        PdfCacheStatistics lists = document.CacheStatistics.DisplayLists;
        Assert.Equal(PdfOptions.DefaultDisplayListCacheBudget, lists.Budget);
        Assert.Equal(1, lists.Hits);
        Assert.Equal(1, lists.Misses);
        Assert.Equal(1, lists.Count);
        Assert.InRange(lists.Bytes, 1, lists.Budget);
    }

    [Fact]
    public void An_edit_to_the_page_is_seen_by_the_next_list()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage("0 0 1 rg 10 10 100 50 re f"u8.ToArray()));
        PdfPage page = document.Pages[0];
        DisplayList before = page.CreateDisplayList();

        page.Dictionary[Contents] = new CosStream(new CosDictionary(), "1 0 0 rg 0 0 5 5 re f 0 0 5 5 re f"u8.ToArray());
        DisplayList after = page.CreateDisplayList();

        Assert.NotSame(before, after);
        var recorder = new RecordingBackend();
        after.Replay(recorder);
        Assert.Equal(2, recorder.Fills.Count);
    }

    [Fact]
    public void Lists_built_with_other_limits_are_cached_apart_from_the_default_list()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage("0 0 1 rg 10 10 100 50 re f"u8.ToArray()));
        PdfPage page = document.Pages[0];

        DisplayList defaults = page.CreateDisplayList();
        DisplayList limited = page.CreateDisplayList(new ContentOptions().WithMaxOperands(100));

        Assert.NotSame(defaults, limited);
        Assert.Same(limited, page.CreateDisplayList(new ContentOptions().WithMaxOperands(100)));
        Assert.Same(defaults, page.CreateDisplayList());
    }

    [Fact]
    public void Lists_of_other_layers_and_intents_are_cached_apart_from_the_content_list()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("annotations-appearance.pdf"));
        PdfPage page = document.Pages[0];

        DisplayList content = page.CreateDisplayList();
        DisplayList all = page.CreateDisplayList(DisplayLayers.All);
        DisplayList print = page.CreateDisplayList(DisplayLayers.All, DisplayIntent.Print, new ContentOptions());

        Assert.Same(content, page.CreateDisplayList(DisplayLayers.Content));
        Assert.NotSame(content, all);
        Assert.NotSame(all, print);
        Assert.Same(all, page.CreateDisplayList(DisplayLayers.All));
        Assert.Same(print, page.CreateDisplayList(DisplayLayers.All, DisplayIntent.Print, new ContentOptions()));
        Assert.Equal(3, document.CacheStatistics.DisplayLists.Count);
    }

    [Fact]
    public void An_image_xobject_is_decoded_once_for_every_list_that_paints_it()
    {
        var counting = new CountingFlateFilter();
        byte[] file = ContentSamples.OnePage("/Im0 Do"u8.ToArray(), "/XObject << /Im0 5 0 R >>", GrayImage(2, 0, flate: true));
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseFilter(counting).WithDisplayListCacheBudget(0));
        var first = new RecordingBackend();
        var second = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(first);
        document.Pages[0].CreateDisplayList().Replay(second);

        Assert.Equal(1, counting.Calls);
        Assert.Same(first.Images[0].Image, second.Images[0].Image);
        PdfCacheStatistics images = document.CacheStatistics.Images;
        Assert.Equal(1, images.Hits);
        Assert.Equal(1, images.Misses);
        Assert.Equal(1, images.Count);
    }

    [Fact]
    public void Images_beyond_the_budget_are_evicted_least_recently_used_first()
    {
        // One 32 x 32 image's decoded size, measured through the statistics, sets a budget that holds two of them but not three.
        long one;
        byte[] single = ContentSamples.OnePage("/Im0 Do"u8.ToArray(), "/XObject << /Im0 5 0 R >>", GrayImage(32, 1, flate: false));
        using (PdfDocument measuring = PdfDocument.Open(single, new PdfOptions().WithDisplayListCacheBudget(0)))
        {
            _ = measuring.Pages[0].CreateDisplayList();
            one = measuring.CacheStatistics.Images.Bytes;
        }

        long budget = (one * 2) + (one / 2);
        using PdfDocument document = PdfDocument.Open(ThreeImages(), new PdfOptions().WithDisplayListCacheBudget(0).WithImageCacheBudget(budget));
        PdfPage page = document.Pages[0];

        _ = page.CreateDisplayList();
        PdfCacheStatistics afterFirst = document.CacheStatistics.Images;

        Assert.Equal(budget, afterFirst.Budget);
        Assert.Equal(3, afterFirst.Misses);
        Assert.Equal(1, afterFirst.Evictions);
        Assert.Equal(2, afterFirst.Count);
        Assert.InRange(afterFirst.PeakBytes, 1, budget);

        // Im0 was painted first and is the one evicted; building again misses it, which evicts Im1, which then misses and evicts
        // Im2, and so on: least recently used first, so a cache one image too small for the page never hits.
        _ = page.CreateDisplayList();
        PdfCacheStatistics afterSecond = document.CacheStatistics.Images;
        Assert.Equal(0, afterSecond.Hits);
        Assert.Equal(6, afterSecond.Misses);
        Assert.Equal(4, afterSecond.Evictions);
        Assert.InRange(afterSecond.PeakBytes, 1, budget);
    }

    [Fact]
    public void An_image_larger_than_the_budget_is_painted_but_never_cached()
    {
        byte[] file = ContentSamples.OnePage("/Im0 Do"u8.ToArray(), "/XObject << /Im0 5 0 R >>", GrayImage(32, 7, flate: false));
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().WithDisplayListCacheBudget(0).WithImageCacheBudget(16));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Single(recorder.Images);
        PdfCacheStatistics images = document.CacheStatistics.Images;
        Assert.Equal(0, images.Count);
        Assert.Equal(0, images.PeakBytes);
        Assert.Equal(1, images.Misses);
    }

    [Fact]
    public void A_glyph_outline_is_read_once_per_document_however_many_lists_show_it()
    {
        var parser = new CountingParser();
        using PdfDocument document = new PdfEngine(new PdfOptions().UseFontProgramParser(parser).WithDisplayListCacheBudget(0))
            .Open(Corpus.Bytes("text-render-modes.pdf"));
        var first = new RecordingBackend();
        var second = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(first);
        document.Pages[0].CreateDisplayList().Replay(second);

        // Nine glyphs shown per list, two distinct (H and I), each outline read once for both lists.
        Assert.Equal(9, second.Glyphs.Count);
        Assert.Equal(first.Glyphs.Select(static g => g.Outline), second.Glyphs.Select(static g => g.Outline));
        Assert.Equal(2, parser.Outlines);
        PdfCacheStatistics glyphs = document.CacheStatistics.Glyphs;
        Assert.Equal(2, glyphs.Misses);
        Assert.Equal(2, glyphs.Hits);
        Assert.Equal(2, glyphs.Count);
    }

    [Fact]
    public void Decoded_content_is_reused_within_its_budget_and_not_cached_beyond_it()
    {
        byte[] file = Corpus.Bytes("flate-stream.pdf");
        using PdfDocument cached = PdfDocument.Open(file, new PdfOptions().WithDisplayListCacheBudget(0));
        using PdfDocument tiny = PdfDocument.Open(file, new PdfOptions().WithDisplayListCacheBudget(0).WithContentCacheBudget(1));

        var expected = new RecordingBackend();
        var actual = new RecordingBackend();
        cached.Pages[0].CreateDisplayList().Replay(expected);
        cached.Pages[0].CreateDisplayList().Replay(new RecordingBackend());
        tiny.Pages[0].CreateDisplayList().Replay(new RecordingBackend());
        tiny.Pages[0].CreateDisplayList().Replay(actual);

        Assert.Equal(expected.Operations, actual.Operations);
        Assert.Equal(1, cached.CacheStatistics.Content.Hits);
        Assert.Equal(1, cached.CacheStatistics.Content.Count);
        Assert.Equal(0, tiny.CacheStatistics.Content.Hits);
        Assert.Equal(2, tiny.CacheStatistics.Content.Misses);
        Assert.Equal(0, tiny.CacheStatistics.Content.PeakBytes);
    }

    [Fact]
    public void Budgets_of_zero_turn_every_cache_off_and_keep_the_output()
    {
        byte[] file = Corpus.Bytes("text-render-modes.pdf");
        using PdfDocument on = PdfDocument.Open(file);
        using PdfDocument off = PdfDocument.Open(file, NoCaches());

        DisplayList first = off.Pages[0].CreateDisplayList();
        DisplayList second = off.Pages[0].CreateDisplayList();

        Assert.NotSame(first, second);
        var expected = new RecordingBackend();
        var actual = new RecordingBackend();
        on.Pages[0].CreateDisplayList().Replay(expected);
        second.Replay(actual);
        Assert.Equal(expected.Operations, actual.Operations);
        PdfDocumentCacheStatistics statistics = off.CacheStatistics;
        foreach (PdfCacheStatistics cache in new[] { statistics.Content, statistics.Images, statistics.Glyphs, statistics.DisplayLists })
        {
            Assert.Equal(0, cache.Budget);
            Assert.Equal(0, cache.Count);
            Assert.Equal(0, cache.PeakBytes);
            Assert.Equal(0, cache.Hits);
        }
    }

    [Fact]
    public void Budgets_are_options_with_defaults_and_refuse_negative_values()
    {
        var options = new PdfOptions();

        Assert.Equal(PdfOptions.DefaultContentCacheBudget, options.ContentCacheBudget);
        Assert.Equal(PdfOptions.DefaultImageCacheBudget, options.ImageCacheBudget);
        Assert.Equal(PdfOptions.DefaultGlyphCacheBudget, options.GlyphCacheBudget);
        Assert.Equal(PdfOptions.DefaultDisplayListCacheBudget, options.DisplayListCacheBudget);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithImageCacheBudget(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithGlyphCacheBudget(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithDisplayListCacheBudget(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithContentCacheBudget(-1));
        Assert.Same(options, options.WithImageCacheBudget(1).WithGlyphCacheBudget(2).WithDisplayListCacheBudget(3).WithContentCacheBudget(4));
        Assert.Equal((1L, 2L, 3L, 4L), (options.ImageCacheBudget, options.GlyphCacheBudget, options.DisplayListCacheBudget, options.ContentCacheBudget));
    }

    [Fact]
    public void Disposing_the_document_empties_its_caches()
    {
        PdfDocument document = PdfDocument.Open(ContentSamples.OnePage("/Im0 Do"u8.ToArray(), "/XObject << /Im0 5 0 R >>", GrayImage(2, 0, flate: false)));
        _ = document.Pages[0].CreateDisplayList();
        Assert.Equal(1, document.CacheStatistics.Images.Count);

        document.Dispose();

        PdfDocumentCacheStatistics statistics = document.CacheStatistics;
        Assert.Equal(0, statistics.Images.Count + statistics.DisplayLists.Count + statistics.Content.Count + statistics.Glyphs.Count);
        Assert.Equal(0, statistics.Images.Bytes);
    }

    [Fact]
    public void A_list_charges_each_tiling_cell_once_however_many_paints_share_it_and_its_text_positions()
    {
        // Issue #112's cells are lists of their own, shared by every paint of one pattern: a list charges each once (issue #103).
        var cell = new StringBuilder();
        for (int square = 0; square < 40; square++)
        {
            cell.Append(System.Globalization.CultureInfo.InvariantCulture, $"{square % 8} {square / 8} 0.5 0.5 re f ");
        }

        string pattern = ContentSamples.Stream(
            cell.ToString(),
            "/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 8 5] /XStep 8 /YStep 5 /Resources << >>");
        long Charge(int paints, out DisplayList list)
        {
            var content = new StringBuilder("/Pattern cs /P1 scn ");
            for (int paint = 0; paint < paints; paint++)
            {
                content.Append(System.Globalization.CultureInfo.InvariantCulture, $"q 1 0 0 1 {paint} 0 cm 0 0 10 10 re f Q ");
            }

            byte[] file = ContentSamples.OnePage(Encoding.ASCII.GetBytes(content.ToString()), "/Pattern << /P1 5 0 R >>", pattern);
            using PdfDocument document = PdfDocument.Open(file);
            list = document.Pages[0].CreateDisplayList();
            return list.RetainedBytes;
        }

        long one = Charge(1, out DisplayList single);
        long fifty = Charge(50, out DisplayList many);
        long cellBytes = Assert.Single(single.TilingPaints).Cell.RetainedBytes;
        Assert.Equal(50, many.TilingPaints.Length);
        Assert.Single(many.TilingPaints.Select(paint => paint.Cell).Distinct());
        Assert.InRange(one, cellBytes + 1, long.MaxValue);
        Assert.InRange((fifty - one) / 49, 1, cellBytes / 2);

        using PdfDocument text = PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(100)));
        DisplayList textList = text.Pages[0].CreateDisplayList();
        long textTables = (textList.TextGlyphs.Length * (long)Unsafe.SizeOf<DisplayTextGlyph>())
            + (textList.Text.Length * sizeof(char))
            + (textList.Glyphs.Length * (long)Unsafe.SizeOf<DisplayGlyph>());
        Assert.InRange(textList.TextGlyphs.Length, 100, int.MaxValue);
        Assert.InRange(textList.RetainedBytes, textTables, long.MaxValue);
    }

    /// <summary>Options with every cache budget at zero.</summary>
    internal static PdfOptions NoCaches() =>
        new PdfOptions().WithContentCacheBudget(0).WithImageCacheBudget(0).WithGlyphCacheBudget(0).WithDisplayListCacheBudget(0);

    /// <summary>A page painting three distinct 32 x 32 gray images, Im0, Im1 and Im2, in that order.</summary>
    private static byte[] ThreeImages() => ContentSamples.OnePage(
        "q 10 0 0 10 0 0 cm /Im0 Do Q q 10 0 0 10 20 0 cm /Im1 Do Q q 10 0 0 10 40 0 cm /Im2 Do Q"u8.ToArray(),
        "/XObject << /Im0 5 0 R /Im1 6 0 R /Im2 7 0 R >>",
        GrayImage(32, 1, flate: false),
        GrayImage(32, 2, flate: false),
        GrayImage(32, 3, flate: false));

    /// <summary>A <paramref name="size"/>-square 8-bit DeviceGray image XObject whose samples depend on <paramref name="seed"/>.</summary>
    private static string GrayImage(int size, int seed, bool flate)
    {
        byte[] samples = new byte[size * size];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (byte)((i * 7) + (seed * 31));
        }

        if (flate)
        {
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal))
            {
                zlib.Write(samples);
            }

            samples = compressed.ToArray();
        }

        return ContentSamples.Stream(
            Encoding.Latin1.GetString(samples),
            $"/Type /XObject /Subtype /Image /Width {size} /Height {size} /ColorSpace /DeviceGray /BitsPerComponent 8" + (flate ? " /Filter /FlateDecode" : string.Empty));
    }

    /// <summary>FlateDecode that counts how often it runs.</summary>
    private sealed class CountingFlateFilter : IStreamFilter
    {
        private readonly FlateDecodeFilter _inner = new();
        private int _calls;

        public int Calls => _calls;

        public CosName Name => _inner.Name;

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
            Interlocked.Increment(ref _calls);
            _inner.Decode(encoded, output, context);
        }
    }

    /// <summary>Parses TrueType programs and counts the outlines read from them.</summary>
    private sealed class CountingParser : IFontProgramParser
    {
        private readonly Broadside.Fonts.TrueType.TrueTypeFontProgramParser _inner = new();
        private int _outlines;

        public int Outlines => _outlines;

        public IReadOnlyList<FontProgramFormat> Formats => _inner.Formats;

        public bool CanParse(ReadOnlySpan<byte> data) => _inner.CanParse(data);

        public FontProgram? Parse(ReadOnlyMemory<byte> data, FontProgramContext context) =>
            _inner.Parse(data, context) is { } program ? new CountingProgram(program, this) : null;

        public void Count() => Interlocked.Increment(ref _outlines);
    }

    /// <summary>A program that counts the outlines read from it and defers everything to the parsed one.</summary>
    private sealed class CountingProgram(FontProgram inner, CountingParser parser) : FontProgram
    {
        public override FontProgramFormat Format => inner.Format;

        public override int GlyphCount => inner.GlyphCount;

        public override Matrix FontMatrix => inner.FontMatrix;

        public override IReadOnlyList<FontCharacterMap> CharacterMaps => inner.CharacterMaps;

        public override GlyphOutlineStatus GetOutline(int glyphId, GlyphOutline outline)
        {
            parser.Count();
            return inner.GetOutline(glyphId, outline);
        }

        public override GlyphMetrics GetMetrics(int glyphId) => inner.GetMetrics(glyphId);

        public override bool TryGetGlyphId(string glyphName, out int glyphId) => inner.TryGetGlyphId(glyphName, out glyphId);

        public override string? GetGlyphName(int glyphId) => inner.GetGlyphName(glyphId);
    }
}
