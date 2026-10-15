using Broadside.Fonts;
using Broadside.Fonts.Cff;
using Broadside.Fonts.Type1;
using Broadside.Graphics;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>type1-to-cff</c> target (issue #152): the input is a Type 1 program (PFA or PFB), or a file whose first <c>%!</c> or PFB
/// segment starts one (so <c>text-type1-embedded.pdf</c> seeds the target). Invariants: converting never throws; a converted
/// program parses through the CFF parser with as many glyphs; every glyph the Type 1 interpreter draws draws the same verbs and
/// points (within 2^-16 of a unit) and advance in the converted program, and one it drops is empty; the converted program subsets
/// with every glyph asked for kept; and embedding the Type 1 program, showing the printable ASCII it covers and saving never
/// throws, and the saved page extracts one glyph per character shown.
/// </summary>
/// <remarks>Type 1 Font Format chapters 6 and 8; Adobe Technical Notes #5176 and #5177; ISO 32000-2 §9.9 and §9.9.2.</remarks>
internal static class Type1ToCffTarget
{
    private const double Tolerance = 1.0 / 65536;

    public static void Target(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith("%PDF-"u8))
        {
            int text = data[5..].IndexOf("%!"u8);
            int pfb = data.IndexOf((ReadOnlySpan<byte>)[0x80, 0x01]);
            int start = text < 0 ? pfb : pfb >= 0 ? Math.Min(text + 5, pfb) : text + 5;
            if (start < 0)
            {
                return;
            }

            data = data[start..];
        }

        byte[] bytes = data.ToArray();
        if (new Type1FontProgramParser().Parse(bytes, new FontProgramContext()) is not Type1FontProgram type1)
        {
            return;
        }

        if (Type1CffConverter.Convert(type1, out string? problem) is not { } converted)
        {
            if (problem is null)
            {
                throw new InvalidOperationException("A conversion failed without saying why.");
            }

            return;
        }

        if (new CffFontProgramParser().Parse(converted, new FontProgramContext()) is not { } cff || cff.GlyphCount != type1.GlyphCount)
        {
            throw new InvalidOperationException($"The converted program does not re-parse with {type1.GlyphCount} glyphs.");
        }

        var before = new GlyphOutline();
        var after = new GlyphOutline();
        for (int glyph = 0; glyph < type1.GlyphCount; glyph++)
        {
            GlyphOutlineStatus status = type1.GetOutline(glyph, before);
            GlyphOutlineStatus converts = cff.GetOutline(glyph, after);
            bool same = status == GlyphOutlineStatus.Invalid ? converts == GlyphOutlineStatus.Empty : converts == status && Same(before, after);
            if (!same || Math.Abs(type1.GetMetrics(glyph).AdvanceWidth - cff.GetMetrics(glyph).AdvanceWidth) > Tolerance)
            {
                throw new InvalidOperationException($"Glyph {glyph} ({status}) draws differently once converted ({converts}).");
            }
        }

        int[] glyphs = [.. Enumerable.Range(1, Math.Min(type1.GlyphCount - 1, 40))];
        if (CffSubsetter.Subset(converted, glyphs) is { } subset && glyphs.Any(glyph => subset.Glyphs[subset.NewId(glyph)] != glyph))
        {
            throw new InvalidOperationException("The subset of the converted program lost a glyph asked for.");
        }

        CffSubsetTarget.Embed(bytes);
    }

    /// <summary>The same drawing verbs (Type 2 closes contours implicitly) and points within the tolerance.</summary>
    private static bool Same(GlyphOutline left, GlyphOutline right)
    {
        PathView a = left.Path;
        PathView b = right.Path;
        if (!Drawing(a.Verbs).SequenceEqual(Drawing(b.Verbs)) || a.Points.Length != b.Points.Length)
        {
            return false;
        }

        for (int index = 0; index < a.Points.Length; index++)
        {
            if (Math.Abs(a.Points[index].X - b.Points[index].X) > Tolerance || Math.Abs(a.Points[index].Y - b.Points[index].Y) > Tolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static PathVerb[] Drawing(ReadOnlySpan<PathVerb> verbs) => [.. verbs.ToArray().Where(verb => verb != PathVerb.Close)];
}
