using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Tests.Document;
using Broadside.Tests.Fonts;

namespace Broadside.Fonts.Cmaps.Tests;

/// <summary>
/// The predefined CMaps and the Registry-Ordering-UCS2 tables are parsed once per engine and shared, immutable, by every document
/// it opens (issue #256): a font resolver that counts the requests it answers shows how often a table is parsed, because the core
/// asks for a table's bytes exactly when it parses it. ISO 32000-2 §9.7.5.2 (Table 116) and §9.10.2 (step d).
/// </summary>
public sealed class EngineSharedTableTests
{
    private const string Japan1Font =
        "<< /Type /Font /Subtype /Type0 /BaseFont /Test /Encoding /Identity-H /DescendantFonts [5 0 R] >>";

    private const string Japan1Descendant =
        "<< /Type /Font /Subtype /CIDFontType0 /BaseFont /Test /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 0 >> >>";

    [Fact]
    public void A_hundred_Adobe_Japan1_documents_of_one_engine_parse_the_UCS2_table_once()
    {
        var resolver = new CountingResolver(new PredefinedCMapResolver());
        var engine = new PdfEngine(new PdfOptions().UseFontResolver(resolver));

        for (int index = 0; index < 100; index++)
        {
            using PdfDocument document = engine.Open(Japan1File());
            Assert.Equal("¥", FontPdf.Font(document).GetUnicode(new CharacterCode(0x003D, 2, IsValid: true)));
        }

        Assert.Equal(1, resolver.Count(FontResourceKind.CidToUnicode, "Adobe-Japan1-UCS2"));
    }

    [Fact]
    public void Two_engines_with_different_CMap_packages_share_nothing()
    {
        var real = new CountingResolver(new PredefinedCMapResolver());
        var stub = new CountingResolver(new StubResolver(Ucs2Table("Adobe-Japan1-UCS2", "003D", "0058")));
        var first = new PdfEngine(new PdfOptions().UseFontResolver(real));
        var second = new PdfEngine(new PdfOptions().UseFontResolver(stub));

        using PdfDocument fromReal = first.Open(Japan1File());
        using PdfDocument fromStub = second.Open(Japan1File());
        using PdfDocument fromRealAgain = first.Open(Japan1File());
        var code = new CharacterCode(0x003D, 2, IsValid: true);

        Assert.Equal("¥", FontPdf.Font(fromReal).GetUnicode(code));
        Assert.Equal("X", FontPdf.Font(fromStub).GetUnicode(code));
        Assert.Equal("¥", FontPdf.Font(fromRealAgain).GetUnicode(code));
        Assert.Equal(1, real.Count(FontResourceKind.CidToUnicode, "Adobe-Japan1-UCS2"));
        Assert.Equal(1, stub.Count(FontResourceKind.CidToUnicode, "Adobe-Japan1-UCS2"));
    }

    [Fact]
    public void Two_engines_with_the_same_package_each_parse_their_own_table()
    {
        var firstResolver = new CountingResolver(new PredefinedCMapResolver());
        var secondResolver = new CountingResolver(new PredefinedCMapResolver());
        var first = new PdfEngine(new PdfOptions().UseFontResolver(firstResolver));
        var second = new PdfEngine(new PdfOptions().UseFontResolver(secondResolver));

        using PdfDocument a = first.Open(Japan1File());
        using PdfDocument b = second.Open(Japan1File());
        _ = FontPdf.Font(a).GetUnicode(new CharacterCode(0x003D, 2, IsValid: true));
        _ = FontPdf.Font(b).GetUnicode(new CharacterCode(0x003D, 2, IsValid: true));

        Assert.Equal(1, firstResolver.Count(FontResourceKind.CidToUnicode, "Adobe-Japan1-UCS2"));
        Assert.Equal(1, secondResolver.Count(FontResourceKind.CidToUnicode, "Adobe-Japan1-UCS2"));
    }

    [Fact]
    public void Concurrent_first_use_builds_each_table_once()
    {
        const int threadCount = 32;
        var resolver = new CountingResolver(new PredefinedCMapResolver());
        var engine = new PdfEngine(new PdfOptions().UseFontResolver(resolver));
        PdfDocument[] documents = [.. Enumerable.Range(0, threadCount).Select(_ => engine.Open(Japan1File()))];
        try
        {
            using var barrier = new Barrier(threadCount);
            string[] results = new string[threadCount];

            Parallel.For(0, threadCount, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, index =>
            {
                barrier.SignalAndWait();
                results[index] = FontPdf.Font(documents[index]).GetUnicode(new CharacterCode(0x003D, 2, IsValid: true));
            });

            Assert.All(results, result => Assert.Equal("¥", result));
            Assert.Equal(1, resolver.Count(FontResourceKind.CidToUnicode, "Adobe-Japan1-UCS2"));
        }
        finally
        {
            foreach (PdfDocument document in documents)
            {
                document.Dispose();
            }
        }
    }

    [Fact]
    public void Concurrent_first_use_of_a_predefined_CMap_and_the_one_it_uses_parses_each_once()
    {
        const int threadCount = 32;
        var resolver = new CountingResolver(new PredefinedCMapResolver());
        var engine = new PdfEngine(new PdfOptions().UseFontResolver(resolver));
        PdfDocument[] documents = [.. Enumerable.Range(0, threadCount).Select(_ => engine.Open(CMapFile("90ms-RKSJ-V")))];
        try
        {
            using var barrier = new Barrier(threadCount);
            string[] parents = new string[threadCount];

            Parallel.For(0, threadCount, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, index =>
            {
                barrier.SignalAndWait();
                CMap encoding = ((PdfType0Font)FontPdf.Font(documents[index])).Encoding;
                parents[index] = encoding.Parent?.Name ?? "none";
            });

            Assert.All(parents, parent => Assert.Equal("90ms-RKSJ-H", parent));
            Assert.Equal(1, resolver.Count(FontResourceKind.CMap, "90ms-RKSJ-V"));
            Assert.Equal(1, resolver.Count(FontResourceKind.CMap, "90ms-RKSJ-H"));
        }
        finally
        {
            foreach (PdfDocument document in documents)
            {
                document.Dispose();
            }
        }
    }

    [Fact]
    public void Documents_of_one_engine_share_the_same_parsed_CMap()
    {
        var engine = new PdfEngine(new PdfOptions().UsePredefinedCMaps());

        using PdfDocument first = engine.Open(CMapFile("90ms-RKSJ-H"));
        using PdfDocument second = engine.Open(CMapFile("90ms-RKSJ-H"));

        Assert.Same(((PdfType0Font)FontPdf.Font(first)).Encoding, ((PdfType0Font)FontPdf.Font(second)).Encoding);
    }

    [Fact]
    public void A_table_that_deviates_is_reported_by_every_document_that_uses_it_though_it_is_parsed_once()
    {
        var resolver = new CountingResolver(new StubResolver("/CIDInit /ProcSet findresource begin 12 dict begin begincmap 1 beginbfchar <003D> 42 endbfchar endcmap end end"));
        var engine = new PdfEngine(new PdfOptions().UseFontResolver(resolver));

        using PdfDocument first = engine.Open(Japan1File());
        using PdfDocument second = engine.Open(Japan1File());
        _ = FontPdf.Font(first).GetUnicode(new CharacterCode(0x003D, 2, IsValid: true));
        _ = FontPdf.Font(second).GetUnicode(new CharacterCode(0x003D, 2, IsValid: true));

        Assert.Equal(1, resolver.Count(FontResourceKind.CidToUnicode, "Adobe-Japan1-UCS2"));
        Assert.Single(first.Diagnostics, diagnostic => diagnostic.Code == "CidToUnicodeResourceInvalid");
        Assert.Single(second.Diagnostics, diagnostic => diagnostic.Code == "CidToUnicodeResourceInvalid");
    }

    [Fact]
    public void Tables_beyond_the_cache_bound_are_parsed_for_the_document_that_needs_them_and_not_kept()
    {
        var resolver = new CountingResolver(new AnyCMapResolver());
        var engine = new PdfEngine(new PdfOptions().UseFontResolver(resolver));
        int names = 300; // more than the 256 tables the engine keeps

        for (int index = 0; index < names; index++)
        {
            using PdfDocument document = engine.Open(CMapFile("Cm" + index.ToString(CultureInfo.InvariantCulture)));
            _ = ((PdfType0Font)FontPdf.Font(document)).Encoding;
        }

        foreach (int index in new[] { 0, names - 1 })
        {
            using PdfDocument document = engine.Open(CMapFile("Cm" + index.ToString(CultureInfo.InvariantCulture)));
            _ = ((PdfType0Font)FontPdf.Font(document)).Encoding;
        }

        Assert.Equal(1, resolver.Count(FontResourceKind.CMap, "Cm0"));
        Assert.Equal(2, resolver.Count(FontResourceKind.CMap, "Cm" + (names - 1).ToString(CultureInfo.InvariantCulture)));
    }

    private static byte[] Japan1File() => new TestPdf().Build(FileObjects(Japan1Font, Japan1Descendant));

    private static byte[] CMapFile(string encoding) =>
        new TestPdf().Build(FileObjects(
            $"<< /Type /Font /Subtype /Type0 /BaseFont /Test /Encoding /{encoding} /DescendantFonts [5 0 R] >>",
            Japan1Descendant));

    private static string[] FileObjects(string font, string descendant) =>
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> >>",
        font,
        descendant,
    ];

    private static string Ucs2Table(string name, string code, string text) =>
        $"/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /{name} def 1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfchar <{code}> <{text}> endbfchar endcmap end end";

    private sealed class CountingResolver(IFontResolver inner) : IFontResolver
    {
        private readonly ConcurrentDictionary<(FontResourceKind, string), int> _counts = new();

        public int Count(FontResourceKind kind, string name) => _counts.TryGetValue((kind, name), out int count) ? count : 0;

        public bool TryResolveResource(FontResourceKind kind, string name, out ReadOnlyMemory<byte> data)
        {
            bool found = inner.TryResolveResource(kind, name, out data);
            if (found)
            {
                _counts.AddOrUpdate((kind, name), 1, static (_, count) => count + 1);
            }

            return found;
        }
    }

    private sealed class StubResolver(string ucs2Text) : IFontResolver
    {
        public bool TryResolveResource(FontResourceKind kind, string name, out ReadOnlyMemory<byte> data)
        {
            if (kind == FontResourceKind.CidToUnicode && name == "Adobe-Japan1-UCS2")
            {
                data = Encoding.ASCII.GetBytes(ucs2Text);
                return true;
            }

            data = default;
            return false;
        }
    }

    private sealed class AnyCMapResolver : IFontResolver
    {
        public bool TryResolveResource(FontResourceKind kind, string name, out ReadOnlyMemory<byte> data)
        {
            if (kind == FontResourceKind.CMap)
            {
                data = Encoding.ASCII.GetBytes(
                    $"/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /{name} def 1 begincodespacerange <0000> <FFFF> endcodespacerange 1 begincidchar <0041> 5 endcidchar endcmap end end");
                return true;
            }

            data = default;
            return false;
        }
    }
}
