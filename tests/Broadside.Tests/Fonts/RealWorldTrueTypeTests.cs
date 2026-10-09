using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Every embedded TrueType program of a simple font in the fetched real-world corpora parses leniently, every code selects a glyph
/// id inside the program, and every glyph's outline is extracted without an exception. Skips when the corpora are not fetched.
/// ISO 32000-2 §9.6.3, §9.6.5.4, §9.9.
/// </summary>
[Trait("Category", "Corpus")]
public class RealWorldTrueTypeTests(ITestOutputHelper output)
{
    private const int MaxGlyphsPerProgram = 4096;

    public static TheoryData<string> CorpusIds => new(RealWorldCorpusTests.CorpusIds);

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Every_embedded_TrueType_program_of_a_real_world_corpus_outlines_every_glyph(string corpusId)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpusId);
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus '{corpusId}' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        int programs = 0;
        int unusable = 0;
        int glyphs = 0;
        int invalid = 0;
        var outline = new GlyphOutline();
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
                    if (document.GetFont(entry.Value) is not PdfTrueTypeFont font || !seen.Add(font.Dictionary) || font.Descriptor?.FontFile2 is null)
                    {
                        continue;
                    }

                    if (font.Program is not { } program)
                    {
                        unusable++;
                        continue;
                    }

                    programs++;
                    for (int code = 0; code < 256; code++)
                    {
                        int glyph = font.GetGlyphId((byte)code);
                        Assert.InRange(glyph, 0, Math.Max(0, program.GlyphCount - 1));
                    }

                    for (int glyph = 0; glyph < Math.Min(program.GlyphCount, MaxGlyphsPerProgram); glyph++)
                    {
                        glyphs++;
                        if (program.GetOutline(glyph, outline) == GlyphOutlineStatus.Invalid)
                        {
                            invalid++;
                        }

                        foreach (Broadside.Graphics.PathPoint point in outline.Path.Points)
                        {
                            Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y), file);
                        }

                        _ = program.GetMetrics(glyph);
                        _ = program.GetGlyphName(glyph);
                    }
                }
            }
        }

        output.WriteLine($"{corpusId}: {programs} TrueType programs, {unusable} unusable, {glyphs} glyphs outlined, {invalid} damaged.");
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
