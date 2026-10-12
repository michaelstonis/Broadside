using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Fonts.Cmaps.Tests;

/// <summary>
/// A Japanese CIDFont that is not embedded, end to end (issue #93, carried from #92): <c>text-cid-nonembedded.pdf</c> shows あいう日本語
/// through UniJIS-UTF16-H in a non-embedded Adobe-Japan1 CIDFontType0; each code reaches its CID through the predefined CMap,
/// the CID its Unicode value through Adobe-Japan1-UCS2, and that value the glyph of the font a resolver supplies through its
/// Unicode "cmap". ISO 32000-2 §9.7.4, §9.7.5.2, §9.7.6.2, §9.8, §9.10.2; ADR 0009.
/// </summary>
public sealed class CidSubstitutionCorpusTests
{
    private const string Text = "あいう日本語";

    [Fact]
    public void A_non_embedded_Japanese_CIDFont_draws_its_kana_and_kanji_with_a_resolvers_font()
    {
        var resolver = new MinchoResolver();
        using PdfDocument document = PdfDocument.Open(
            Corpus.Path("text-cid-nonembedded.pdf"),
            new PdfOptions().UsePredefinedCMaps().UseSystemFontResolver(null).UseFontResolver(resolver));
        PdfType0Font font = Assert.IsType<PdfType0Font>(Assert.Single(document.Pages).GetFont("F1"));

        List<CidGlyph> glyphs = ReadAll(font, Convert.FromHexString("30423044304665E5672C8A9E"));

        // CIDs from Adobe's UniJIS-UTF16-H (cmap-resources); glyphs 1 to 6 are the six characters in the resolver's font.
        Assert.Equal([843, 845, 847, 3284, 3722, 1952], glyphs.Select(glyph => glyph.Cid));
        Assert.Equal([1, 2, 3, 4, 5, 6], glyphs.Select(glyph => glyph.GlyphId));
        Assert.Equal(Text, string.Concat(glyphs.Select(glyph => font.GetUnicode(glyph.Code))));
        FontSubstitute substitute = Assert.IsType<FontSubstitute>(font.DescendantFont!.Substitute);
        Assert.Equal(("BroadsideMincho-Regular", "KozMinPr6N-Regular", FontMatchKind.Family), (substitute.Name, substitute.RequestedName, substitute.MatchKind));
        Assert.Equal(new CidSystemInfo("Adobe", "Japan1", 6), Assert.Single(resolver.Queries).SystemInfo);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "FontSubstituted");
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is "FontProgramNotFound" or "CMapUnavailable" or "TextUcs2CmapMissing");
    }

    [Fact]
    public void Without_a_font_for_it_the_CIDFont_shows_glyph_0_and_says_so()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-cid-nonembedded.pdf"), new PdfOptions().UsePredefinedCMaps().UseSystemFontResolver(null));
        PdfType0Font font = Assert.IsType<PdfType0Font>(Assert.Single(document.Pages).GetFont("F1"));

        List<CidGlyph> glyphs = ReadAll(font, Convert.FromHexString("30423044304665E5672C8A9E"));

        Assert.All(glyphs, glyph => Assert.Equal(0, glyph.GlyphId));
        Assert.Equal(Text, string.Concat(glyphs.Select(glyph => font.GetUnicode(glyph.Code))));
        Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "FontProgramNotFound");
    }

    private static List<CidGlyph> ReadAll(PdfType0Font font, byte[] text)
    {
        var glyphs = new List<CidGlyph>();
        for (int offset = 0; offset < text.Length;)
        {
            CidGlyph glyph = font.ReadGlyph(text.AsSpan(offset));
            glyphs.Add(glyph);
            offset += Math.Max(1, glyph.Code.Length);
        }

        return glyphs;
    }

    /// <summary>Has a Mincho family font whose Unicode "cmap" maps あいう日本語 to glyphs 1 to 6; finds it for any Japan1 CIDFont.</summary>
    private sealed class MinchoResolver : IFontResolver
    {
        private readonly byte[] _program = Program();

        public List<FontQuery> Queries { get; } = [];

        public FontResolution? ResolveFont(FontQuery query)
        {
            Queries.Add(query);
            return query.SystemInfo is { Ordering: "Japan1" } ? new FontResolution(_program, "BroadsideMincho-Regular", FontMatchKind.Family) : null;
        }

        private static byte[] Program()
        {
            var builder = new TrueTypeBuilder
            {
                Cmap = TrueTypeBuilder.CmapTable((3, 1, TrueTypeBuilder.Format4([.. Text.Select((ch, index) => ((int)ch, (int)ch, index + 1 - ch))]))),
                Metrics = [.. Enumerable.Repeat(((ushort)1000, (short)0), Text.Length + 1)],
            };
            builder.Glyphs.Add([]);
            for (int index = 0; index < Text.Length; index++)
            {
                builder.Glyphs.Add(TrueTypeBuilder.Rectangle(100, 0, 900, 100 + (100 * index)));
            }

            return builder.Build();
        }
    }
}
