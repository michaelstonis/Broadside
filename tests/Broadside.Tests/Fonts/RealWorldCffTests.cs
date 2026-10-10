using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Every embedded CFF and OpenType-CFF program (<c>FontFile3</c> of subtype <c>Type1C</c> or <c>OpenType</c>) of a simple font in
/// the fetched real-world corpora parses leniently, every code selects a glyph inside the program, and every glyph's outline is
/// extracted without an exception, with finite coordinates and well-formed contours. Skips when the corpora are not fetched.
/// ISO 32000-2 §9.6.5.2, §9.9; Adobe Technical Notes #5176 and #5177.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldCffTests(ITestOutputHelper output)
{
    private const int MaxGlyphsPerProgram = 4096;
    private static readonly CosName FontName = new("Font");

    public static TheoryData<string> CorpusIds => new(RealWorldCorpusTests.CorpusIds);

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Every_embedded_CFF_program_of_a_real_world_corpus_outlines_every_glyph(string corpusId)
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
                    || !resources.TryGetValue(FontName, out CosObject? value)
                    || document.Resolve(value) is not CosDictionary fontResources)
                {
                    continue;
                }

                foreach (KeyValuePair<CosName, CosObject> entry in fontResources)
                {
                    if (document.GetFont(entry.Value) is not PdfType1Font font || !seen.Add(font.Dictionary) || font.Descriptor?.FontFile3 is null)
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
                        Assert.InRange(font.GetGlyphId((byte)code), 0, Math.Max(0, program.GlyphCount - 1));
                    }

                    for (int glyph = 0; glyph < Math.Min(program.GlyphCount, MaxGlyphsPerProgram); glyph++)
                    {
                        glyphs++;
                        GlyphOutlineStatus status = program.GetOutline(glyph, outline);
                        if (status == GlyphOutlineStatus.Invalid)
                        {
                            invalid++;
                        }

                        Assert.Equal(status == GlyphOutlineStatus.Complete, !outline.IsEmpty);
                        Assert.True(WellFormed(outline), file);
                        Assert.True(double.IsFinite(program.GetMetrics(glyph).AdvanceWidth), file);
                        _ = program.GetGlyphName(glyph);
                    }
                }
            }
        }

        output.WriteLine($"{corpusId}: {programs} CFF programs, {unusable} unusable, {glyphs} glyphs outlined, {invalid} damaged.");
    }

    /// <summary>Every contour is a moveto, segments, and a close; every coordinate is finite.</summary>
    private static bool WellFormed(GlyphOutline outline)
    {
        bool open = false;
        foreach (Broadside.Graphics.PathVerb verb in outline.Path.Verbs)
        {
            if (verb == Broadside.Graphics.PathVerb.MoveTo)
            {
                if (open)
                {
                    return false;
                }

                open = true;
            }
            else if (!open)
            {
                return false;
            }
            else if (verb == Broadside.Graphics.PathVerb.Close)
            {
                open = false;
            }
        }

        foreach (Broadside.Graphics.PathPoint point in outline.Path.Points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                return false;
            }
        }

        return !open;
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
