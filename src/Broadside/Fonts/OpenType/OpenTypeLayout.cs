using Broadside.Fonts.TrueType;

namespace Broadside.Fonts.OpenType;

/// <summary>
/// The OpenType layout tables of one font program that shaping reads (<c>GSUB</c>, <c>GPOS</c>, <c>GDEF</c> and the legacy
/// <c>kern</c> table), with the shape plans built from them. Safe for concurrent use: the plan cache is locked.
/// </summary>
internal sealed class OpenTypeLayout
{
    private readonly Dictionary<(TextShaping Shaping, uint Script), ShapePlan> _plans = [];
    private readonly Lock _lock = new();

    /// <summary>Creates the layout of a program with the given tables (any may be absent).</summary>
    public OpenTypeLayout(LayoutTable? gsub, GdefTable? gdef, int glyphCount, LayoutTable? gpos = null, KernTable? kern = null, double scale = 1)
    {
        Gsub = gsub;
        Gdef = gdef;
        GlyphCount = glyphCount;
        Gpos = gpos;
        Kern = kern;
        Scale = scale;
    }

    /// <summary>Gets the substitution table, or <see langword="null"/>.</summary>
    public LayoutTable? Gsub { get; }

    /// <summary>Gets the positioning table, or <see langword="null"/>.</summary>
    public LayoutTable? Gpos { get; }

    /// <summary>Gets the legacy kerning table, or <see langword="null"/>.</summary>
    public KernTable? Kern { get; }

    /// <summary>Gets the glyph definition table, or <see langword="null"/>.</summary>
    public GdefTable? Gdef { get; }

    /// <summary>Gets the number of glyphs of the program: substituted glyph ids at or past it show as glyph 0.</summary>
    public int GlyphCount { get; }

    /// <summary>Gets the thousandths of the font size per font design unit (1000 / unitsPerEm).</summary>
    public double Scale { get; }

    /// <summary>Reads the layout tables of an sfnt program; <see langword="null"/> when it has no GSUB, GPOS or kern table.</summary>
    /// <param name="sfnt">The program.</param>
    /// <param name="glyphCount">The number of glyphs of the program.</param>
    /// <param name="scale">Thousandths of the font size per font design unit.</param>
    public static OpenTypeLayout? Read(SfntFile sfnt, int glyphCount, double scale)
    {
        LayoutTable? gsub = sfnt.TryGetTable(SfntFile.Tag("GSUB"), out ReadOnlyMemory<byte> table) ? LayoutTable.Read(table) : null;
        LayoutTable? gpos = sfnt.TryGetTable(SfntFile.Tag("GPOS"), out ReadOnlyMemory<byte> positioning) ? LayoutTable.ReadPositioning(positioning) : null;
        KernTable? kern = sfnt.TryGetTable(SfntFile.Tag("kern"), out ReadOnlyMemory<byte> kerning) ? KernTable.Read(kerning) : null;
        if (gsub is null && gpos is null && kern is null)
        {
            return null;
        }

        GdefTable? gdef = sfnt.TryGetTable(SfntFile.Tag("GDEF"), out ReadOnlyMemory<byte> definitions) ? GdefTable.Read(definitions) : null;
        return new OpenTypeLayout(gsub, gdef, glyphCount, gpos, kern, double.IsFinite(scale) && scale > 0 ? scale : 1);
    }

    /// <summary>The plan for <paramref name="shaping"/> of text in <paramref name="script"/>, built the first time it is asked for.</summary>
    public ShapePlan PlanFor(TextShaping shaping, uint script)
    {
        lock (_lock)
        {
            if (!_plans.TryGetValue((shaping, script), out ShapePlan? plan))
            {
                plan = ShapePlan.Create(Gsub, Gpos, shaping, script);
                _plans[(shaping, script)] = plan;
            }

            return plan;
        }
    }

    /// <summary>
    /// The adjustment of the advance of glyph <paramref name="left"/> followed by <paramref name="right"/> under the default
    /// features (the font's <c>GPOS</c> pair kerning, else its <c>kern</c> table), in thousandths of the font size.
    /// </summary>
    public double PairAdjustment(int left, int right)
    {
        ShapePlan plan = PlanFor(TextShaping.Default, 0);
        GlyphBuffer buffer = GlyphBuffer.Rent();
        try
        {
            buffer.Add(new ShapingGlyph { Glyph = left, Mask = ShapePlan.Global, Class = GdefTable.BaseGlyph });
            buffer.Add(new ShapingGlyph { Glyph = right, Cluster = 1, Mask = ShapePlan.Global, Class = GdefTable.BaseGlyph });
            if (Gpos is { } gpos && plan.Positioning.Length > 0)
            {
                OpenTypeShaper.ApplyPositioning(gpos, Gdef, Scale, plan.Positioning, buffer, rightToLeft: false);
            }

            return buffer[0].XAdvance + (plan.Kerning && Kern is { } kern ? kern.Pair(left, right) * Scale : 0);
        }
        finally
        {
            GlyphBuffer.Return(buffer);
        }
    }
}
