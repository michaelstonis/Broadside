using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Every CIDFontType0 with an embedded program (<c>FontFile3</c>, CID-keyed or not) of a Type 0 font in the fetched real-world
/// corpora parses leniently, every CID of its charset selects a glyph inside the program, and every glyph's outline is extracted
/// with its Font DICT's Private DICT without an exception, with finite coordinates. Skips when the corpora are not fetched.
/// ISO 32000-2 §9.7.4.2, §9.9; Adobe Technical Note #5176 §18-19.
/// </summary>
[Trait("Category", "Corpus")]
public class RealWorldCidCffTests(ITestOutputHelper output)
{
    private const int MaxGlyphsPerProgram = 4096;
    private static readonly CosName FontName = new("Font");

    public static TheoryData<string> CorpusIds => new(RealWorldCorpusTests.CorpusIds);

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Every_embedded_CIDFontType0_program_of_a_real_world_corpus_outlines_every_glyph(string corpusId)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpusId);
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus '{corpusId}' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        int programs = 0;
        int cidKeyed = 0;
        int glyphs = 0;
        int invalid = 0;
        var outline = new GlyphOutline();
        foreach (string file in files.Where(file => !RealWorldCorpusTests.Unreadable.ContainsKey(file)))
        {
            using PdfDocument? document = OpenOrNull(file);
            if (document is null)
            {
                continue;
            }

            var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
            foreach (PdfPage page in document.Pages)
            {
                if (page.Resources is not { } resources
                    || !resources.TryGetValue(FontName, out CosObject? value)
                    || document.Resolve(value) is not CosDictionary fontResources)
                {
                    continue;
                }

                foreach (KeyValuePair<CosName, CosObject> entry in fontResources)
                {
                    if (document.GetFont(entry.Value) is not PdfType0Font { DescendantFont: PdfCidFontType0 { Program: { } program } font }
                        || !seen.Add(font.Dictionary))
                    {
                        continue;
                    }

                    programs++;
                    if (program is Broadside.Fonts.Cff.CffFontProgram { Font: { IsCidKeyed: true } cff })
                    {
                        cidKeyed++;
                        for (int glyph = 0; glyph < Math.Min(program.GlyphCount, MaxGlyphsPerProgram); glyph++)
                        {
                            Assert.InRange(font.GetGlyphId(cff.Charset[glyph]), 0, glyph);
                        }
                    }

                    for (int glyph = 0; glyph < Math.Min(program.GlyphCount, MaxGlyphsPerProgram); glyph++)
                    {
                        glyphs++;
                        GlyphOutlineStatus status = program.GetOutline(glyph, outline);
                        invalid += status == GlyphOutlineStatus.Invalid ? 1 : 0;
                        Assert.Equal(status == GlyphOutlineStatus.Complete, !outline.IsEmpty);
                        Assert.True(Finite(outline.Path), file);
                        Assert.True(double.IsFinite(program.GetMetrics(glyph).AdvanceWidth), file);
                    }
                }
            }
        }

        output.WriteLine($"{corpusId}: {programs} CIDFontType0 programs ({cidKeyed} CID-keyed), {glyphs} glyphs outlined, {invalid} damaged.");
    }

    private static bool Finite(PathView path)
    {
        foreach (PathPoint point in path.Points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                return false;
            }
        }

        return true;
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
