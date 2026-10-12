using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Every Type 0 font of the fetched real-world corpora resolves its CMap and CIDFont leniently, and reading glyphs from a fixed
/// string of varied bytes gives codes of 1 to 4 bytes, CIDs in range, finite metrics, and glyph ids inside an embedded TrueType
/// program. Skips when the corpora are not fetched. ISO 32000-2 §9.7.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldCompositeFontTests(ITestOutputHelper output)
{
    public static TheoryData<string> CorpusIds => new(Document.RealWorldCorpusTests.CorpusIds);

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Every_Type_0_font_of_a_real_world_corpus_reads_glyphs(string corpusId)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpusId);
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus '{corpusId}' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        byte[] text = new byte[512];
        for (int index = 0; index < text.Length; index++)
        {
            text[index] = (byte)((index * 37) ^ (index >> 3));
        }

        int fonts = 0;
        int embeddedCMaps = 0;
        int programs = 0;
        int glyphs = 0;
        foreach (string file in files)
        {
            if (Document.RealWorldCorpusTests.Unreadable.ContainsKey(file))
            {
                continue;
            }

            using PdfDocument? document = OpenOrNull(file);
            if (document is null)
            {
                continue;
            }

            var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
            foreach (PdfPage page in document.Pages)
            {
                if (page.Resources is not { } resources
                    || !resources.TryGetValue(new CosName("Font"), out CosObject? value)
                    || document.Resolve(value) is not CosDictionary fontResources)
                {
                    continue;
                }

                foreach (KeyValuePair<CosName, CosObject> entry in fontResources)
                {
                    if (document.GetFont(entry.Value) is not PdfType0Font font || !seen.Add(font.Dictionary))
                    {
                        continue;
                    }

                    fonts++;
                    CMap cmap = font.Encoding;
                    embeddedCMaps += cmap.IsIdentity ? 0 : 1;
                    PdfCidFont? descendant = font.DescendantFont;
                    FontProgram? program = descendant?.Program;
                    bool embedded = descendant?.IsEmbedded == true;
                    programs += program is null ? 0 : 1;
                    ReadOnlySpan<byte> rest = text;
                    while (!rest.IsEmpty)
                    {
                        CidGlyph glyph = font.ReadGlyph(rest);
                        Assert.InRange(glyph.Code.Length, 1, Math.Min(4, rest.Length));
                        Assert.InRange(glyph.Cid, 0, 0xFFFF);
                        Assert.True(double.IsFinite(glyph.Width) && double.IsFinite(glyph.VerticalMetrics.VerticalAdvance), file);
                        if (program is not null && embedded && descendant!.CidFontType == PdfCidFontType.CidFontType2)
                        {
                            Assert.InRange(glyph.GlyphId, 0, Math.Max(0, program.GlyphCount - 1));
                        }

                        glyphs++;
                        rest = rest[glyph.Code.Length..];
                    }
                }
            }
        }

        output.WriteLine($"{corpusId}: {fonts} Type 0 fonts, {embeddedCMaps} with embedded CMaps, {programs} programs read, {glyphs} glyphs read.");
    }

    private static PdfDocument? OpenOrNull(string file)
    {
        try
        {
            return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)));
        }
        catch (Exception exception) when (exception is PdfPasswordException or PdfCertificateException or NotSupportedException)
        {
            return null;
        }
    }
}
