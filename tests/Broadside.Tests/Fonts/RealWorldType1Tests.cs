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
public sealed class RealWorldType1Tests(ITestOutputHelper output)
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
        int converted = 0;
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
                    converted += ConvertsToCff(program, file) ? 1 : 0;
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

        output.WriteLine($"{corpusId}: {programs} Type 1 programs, {unusable} unusable, {glyphs} glyphs outlined, {invalid} dropped, {outsideBox} reaching 500 units past a non-empty FontBBox, {converted} converted to CFF.");
        output.WriteLine(string.Join(", ", codes.Select(code => $"{code.Key} {code.Value}")));
    }

    /// <summary>
    /// Issue #152: the program converts to CFF (unless a coordinate is past the Type 2 range), and every glyph draws the same verbs,
    /// points (within 2^-16 of a unit) and advance in the converted program; a glyph the Type 1 interpreter drops is empty.
    /// </summary>
    private static bool ConvertsToCff(FontProgram program, string file)
    {
        var type1 = (Broadside.Fonts.Type1.Type1FontProgram)program;
        if (Broadside.Fonts.Type1.Type1CffConverter.Convert(type1, out string? problem) is not { } data)
        {
            Assert.NotNull(problem);
            return false;
        }

        FontProgram cff = new Broadside.Fonts.Cff.CffFontProgramParser().Parse(data, new FontProgramContext())!;
        Assert.Equal(type1.GlyphCount, cff.GlyphCount);
        var before = new GlyphOutline();
        var after = new GlyphOutline();
        for (int glyph = 0; glyph < Math.Min(type1.GlyphCount, MaxGlyphsPerProgram); glyph++)
        {
            GlyphOutlineStatus status = type1.GetOutline(glyph, before);
            GlyphOutlineStatus converts = cff.GetOutline(glyph, after);
            Assert.True(status == GlyphOutlineStatus.Invalid ? converts == GlyphOutlineStatus.Empty : converts == status, $"{file} glyph {glyph}: {status} -> {converts}");
            if (status == GlyphOutlineStatus.Invalid)
            {
                continue;
            }

            PathVerb[] expected = [.. before.Path.Verbs.ToArray().Where(verb => verb != PathVerb.Close)];
            Assert.True(expected.SequenceEqual(after.Path.Verbs.ToArray().Where(verb => verb != PathVerb.Close)), $"{file} glyph {glyph}: verbs");
            for (int index = 0; index < before.Path.Points.Length; index++)
            {
                PathPoint p = before.Path.Points[index];
                PathPoint q = after.Path.Points[index];
                Assert.True(Math.Abs(p.X - q.X) <= 1.0 / 65536 && Math.Abs(p.Y - q.Y) <= 1.0 / 65536, $"{file} glyph {glyph} point {index}");
            }

            Assert.True(Math.Abs(type1.GetMetrics(glyph).AdvanceWidth - cff.GetMetrics(glyph).AdvanceWidth) <= 1.0 / 65536, $"{file} glyph {glyph}: width");
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
