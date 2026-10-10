using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// With the package configured and no system fonts, every non-embedded Standard 14 font of the fetched real-world corpora gets a
/// program, and every glyph its codes select is drawable or <c>.notdef</c>. Skips when the corpora are not fetched.
/// ISO 32000-2 §9.6.2.2, §9.8.
/// </summary>
[Trait("Category", "Corpus")]
public class RealWorldStandard14Tests(ITestOutputHelper output)
{
    public static TheoryData<string> CorpusIds => new(["pdfjs", "pdfbox", "qpdf", "pdfium-tests", "pdf20examples"]);

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Every_non_embedded_Standard_14_font_of_a_real_world_corpus_has_a_program(string corpusId)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpusId);
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus '{corpusId}' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        PdfOptions options = new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null);
        int standard14 = 0, similar = 0, notFound = 0;
        var missing = new List<string>();
        foreach (string file in files)
        {
            using PdfDocument? document = OpenOrNull(file, options);
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
                    if (document.GetFont(entry.Value) is not PdfSimpleFont { FontType: not PdfFontType.Type3 } font || !seen.Add(font.Dictionary) || font.Program is not null)
                    {
                        continue;
                    }

                    FontSubstitute? substitute = font.Substitute;
                    if (font.Standard14 is not null)
                    {
                        standard14++;
                        if (substitute is null && !font.IsEmbedded)
                        {
                            missing.Add($"{file}: /{font.BaseFont}");
                        }
                    }
                    else if (substitute is not null)
                    {
                        similar++;
                    }
                    else
                    {
                        notFound++;
                    }

                    for (int code = 0; code < 256 && substitute is not null; code++)
                    {
                        int glyph = font is PdfTrueTypeFont trueType ? trueType.GetGlyphId((byte)code) : ((PdfType1Font)font).GetGlyphId((byte)code);
                        Assert.InRange(glyph, 0, substitute.Program.GlyphCount - 1);
                    }
                }
            }
        }

        output.WriteLine($"{corpusId}: {standard14} non-embedded Standard 14 fonts, {similar} other fonts given a similar package font, {notFound} not found (symbolic).");
        Assert.Empty(missing);
        if (corpusId == "pdfjs")
        {
            Assert.InRange(standard14, 100, int.MaxValue);
        }
    }

    private static PdfDocument? OpenOrNull(string file, PdfOptions options)
    {
        try
        {
            return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)), options);
        }
        catch (Exception exception) when (exception is PdfPasswordException or PdfCertificateException or NotSupportedException or DiagnosticException)
        {
            return null;
        }
    }
}
