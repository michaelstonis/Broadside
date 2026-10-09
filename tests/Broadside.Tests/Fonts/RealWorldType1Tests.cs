using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Every embedded Type 1 program (<c>FontFile</c>) of a simple font in the fetched real-world corpora parses leniently, every code
/// selects a glyph id inside the program, and every glyph's outline and metrics are extracted without an exception, with finite
/// coordinates. Skips when the corpora are not fetched. ISO 32000-2 §9.6.2, §9.6.5.2, §9.9.
/// </summary>
[Trait("Category", "Corpus")]
public class RealWorldType1Tests(ITestOutputHelper output)
{
    private const int MaxGlyphsPerProgram = 4096;

    public static TheoryData<string> CorpusIds => new(RealWorldCorpusTests.CorpusIds);

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Every_embedded_Type_1_program_of_a_real_world_corpus_outlines_every_glyph(string corpusId)
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
        int outsideBox = 0;
        var codes = new SortedDictionary<string, int>(StringComparer.Ordinal);
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
                    if (document.GetFont(entry.Value) is not PdfType1Font font || !seen.Add(font.Dictionary) || font.Descriptor?.FontFile is null)
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
                        Assert.InRange(font.GetGlyphId((byte)code), 0, program.GlyphCount - 1);
                    }

                    PdfRectangle box = program.FontBBox;
                    bool hasBox = box.Width > 0 && box.Height > 0; // Type 1 Font Format §2.3: FontBBox may be all zeros
                    for (int glyph = 0; glyph < Math.Min(program.GlyphCount, MaxGlyphsPerProgram); glyph++)
                    {
                        glyphs++;
                        if (program.GetOutline(glyph, outline) == GlyphOutlineStatus.Invalid)
                        {
                            invalid++;
                        }

                        bool outside = false;
                        foreach (PathPoint point in outline.Path.Points)
                        {
                            Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y), file);
                            outside |= point.X < box.Left - 500 || point.X > box.Right + 500 || point.Y < box.Bottom - 500 || point.Y > box.Top + 500;
                        }

                        outsideBox += outside && hasBox ? 1 : 0;
                        Assert.True(double.IsFinite(program.GetMetrics(glyph).AdvanceWidth), file);
                        Assert.NotNull(program.GetGlyphName(glyph));
                    }
                }
            }

            foreach (Broadside.Diagnostics.Diagnostic diagnostic in document.Diagnostics.Where(diagnostic => diagnostic.Code.StartsWith("FontType1", StringComparison.Ordinal)))
            {
                codes[diagnostic.Code] = codes.GetValueOrDefault(diagnostic.Code) + 1;
            }
        }

        output.WriteLine($"{corpusId}: {programs} Type 1 programs, {unusable} unusable, {glyphs} glyphs outlined, {invalid} dropped, {outsideBox} reaching 500 units past a non-empty FontBBox.");
        output.WriteLine(string.Join(", ", codes.Select(code => $"{code.Key} {code.Value}")));
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
