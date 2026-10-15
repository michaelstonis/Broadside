using Broadside.Fonts.TrueType;

namespace Broadside.Fonts.OpenType;

/// <summary>
/// The OpenType layout tables of one font program that shaping reads (<c>GSUB</c> and <c>GDEF</c>; <c>GPOS</c> is issue #175), with
/// the shape plans built from them. Safe for concurrent use: the plan cache is locked.
/// </summary>
internal sealed class OpenTypeLayout
{
    private readonly Dictionary<(TextShaping Shaping, uint Script), ShapePlan> _plans = [];
    private readonly Lock _lock = new();

    /// <summary>Creates the layout of a program with the given tables (either may be absent).</summary>
    public OpenTypeLayout(GsubTable? gsub, GdefTable? gdef, int glyphCount)
    {
        Gsub = gsub;
        Gdef = gdef;
        GlyphCount = glyphCount;
    }

    /// <summary>Gets the substitution table, or <see langword="null"/>.</summary>
    public GsubTable? Gsub { get; }

    /// <summary>Gets the glyph definition table, or <see langword="null"/>.</summary>
    public GdefTable? Gdef { get; }

    /// <summary>Gets the number of glyphs of the program: substituted glyph ids at or past it show as glyph 0.</summary>
    public int GlyphCount { get; }

    /// <summary>Reads the layout tables of an sfnt program; <see langword="null"/> when it has no GSUB table.</summary>
    public static OpenTypeLayout? Read(SfntFile sfnt, int glyphCount)
    {
        GsubTable? gsub = sfnt.TryGetTable(SfntFile.Tag("GSUB"), out ReadOnlyMemory<byte> table) ? GsubTable.Read(table) : null;
        if (gsub is null)
        {
            return null;
        }

        GdefTable? gdef = sfnt.TryGetTable(SfntFile.Tag("GDEF"), out ReadOnlyMemory<byte> definitions) ? GdefTable.Read(definitions) : null;
        return new OpenTypeLayout(gsub, gdef, glyphCount);
    }

    /// <summary>The plan for <paramref name="shaping"/> of text in <paramref name="script"/>, built the first time it is asked for.</summary>
    public ShapePlan PlanFor(TextShaping shaping, uint script)
    {
        lock (_lock)
        {
            if (!_plans.TryGetValue((shaping, script), out ShapePlan? plan))
            {
                plan = ShapePlan.Create(Gsub, shaping, script);
                _plans[(shaping, script)] = plan;
            }

            return plan;
        }
    }
}
