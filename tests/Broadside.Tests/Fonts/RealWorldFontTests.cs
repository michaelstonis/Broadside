using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Every font in the fetched real-world corpora reads through the font model in lenient mode: each simple font gives a glyph
/// name and a finite width for all 256 codes, and every descriptor entry reads, without an exception. Skips when the corpora are
/// not fetched. ISO 32000-2 §9.5, §9.6, §9.8.
/// </summary>
[Trait("Category", "Corpus")]
public class RealWorldFontTests(ITestOutputHelper output)
{
    public static TheoryData<string> CorpusIds => new(RealWorldCorpusTests.CorpusIds);

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Every_font_of_a_real_world_corpus_reads_leniently_without_an_exception(string corpusId)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpusId);
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus '{corpusId}' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        int fonts = 0;
        int standard14 = 0;
        foreach (string file in files)
        {
            if (RealWorldCorpusTests.Unreadable.ContainsKey(file))
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
                    if (document.GetFont(entry.Value) is not { } font || !seen.Add(font.Dictionary))
                    {
                        continue;
                    }

                    fonts++;
                    Read(font, file);
                    if (font is PdfSimpleFont { Standard14: not null })
                    {
                        standard14++;
                    }
                }
            }
        }

        output.WriteLine($"{corpusId}: {fonts} fonts read, {standard14} with Standard 14 metrics.");
    }

    private static void Read(PdfFont font, string file)
    {
        _ = font.BaseFont;
        _ = font.IsEmbedded;
        if (font.Descriptor is { } descriptor)
        {
            _ = (descriptor.FontName, descriptor.FontFamily, descriptor.FontStretch, descriptor.FontWeight, descriptor.Flags, descriptor.FontBBox);
            _ = descriptor.ItalicAngle + descriptor.Ascent + descriptor.Descent + descriptor.Leading + descriptor.CapHeight + descriptor.XHeight
                + descriptor.StemV + descriptor.StemH + descriptor.AvgWidth + descriptor.MaxWidth + descriptor.MissingWidth;
            _ = (descriptor.FontFile, descriptor.FontFile2, descriptor.FontFile3, descriptor.CharSet);
        }

        if (font is PdfSimpleFont simple)
        {
            for (int code = 0; code < 256; code++)
            {
                Assert.False(string.IsNullOrEmpty(simple.GetGlyphName((byte)code)), file);
                Assert.True(double.IsFinite(simple.GetWidth((byte)code)), file);
            }
        }
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
