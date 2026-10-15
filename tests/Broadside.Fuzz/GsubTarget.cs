using Broadside.Fonts;
using Broadside.Fonts.OpenType;
using Broadside.Fonts.TrueType;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>gsub</c> target (issue #174): the input is a <c>GSUB</c> table (an sfnt program's <c>GSUB</c> and <c>GDEF</c> tables are
/// read from it when it is one; any other input gets a version 1.0 header put in front, so arbitrary bytes, corpus files included,
/// become script, feature and lookup lists). The table is read, then every lookup of the Latin, Arabic and default plans is applied
/// to runs of glyph ids taken from the input and from 0 upward, with GDEF classes from the same bytes. Invariants: nothing throws or
/// loops; a run never grows past its bound; clusters stay non-decreasing (ligatures merge them, multiple substitutions repeat them).
/// </summary>
/// <remarks>OpenType specification: "GSUB — Glyph Substitution Table" (lookup types 1–8), "GDEF", "OpenType Layout Common Table Formats".</remarks>
internal static class GsubTarget
{
    private static readonly TextShaping Everything = TextShaping.Default.WithFeature("smcp", true).WithFeature("salt", true).WithFeature("dlig", true);

    public static void Target(ReadOnlySpan<byte> data)
    {
        byte[] bytes = data.ToArray();
        GsubTable? gsub;
        GdefTable? gdef;
        if (SfntFile.Open(bytes, new FontProgramContext()) is { } sfnt && sfnt.TryGetTable(SfntFile.Tag("GSUB"), out ReadOnlyMemory<byte> table))
        {
            gsub = GsubTable.Read(table);
            gdef = sfnt.TryGetTable(SfntFile.Tag("GDEF"), out ReadOnlyMemory<byte> definitions) ? GdefTable.Read(definitions) : null;
        }
        else
        {
            gsub = GsubTable.Read((byte[])[0, 1, 0, 0, .. bytes]);
            gdef = GdefTable.Read((byte[])[0, 1, 0, 2, .. bytes]);
        }

        if (gsub is null)
        {
            return;
        }

        var layout = new OpenTypeLayout(gsub, gdef, 65536);
        foreach (string script in (string[])["latn", "arab", "DFLT"])
        {
            ShapePlan plan = layout.PlanFor(Everything.WithScript(script), SfntFile.Tag(script));
            Run(gsub, gdef, plan, bytes, ascending: false);
            Run(gsub, gdef, plan, bytes, ascending: true);
        }
    }

    private static void Run(GsubTable gsub, GdefTable? gdef, ShapePlan plan, byte[] bytes, bool ascending)
    {
        GlyphBuffer buffer = GlyphBuffer.Rent();
        int count = ascending ? 48 : Math.Min(bytes.Length / 2, 48);
        for (int index = 0; index < count; index++)
        {
            int glyph = ascending ? index : (bytes[index * 2] << 8) | bytes[(index * 2) + 1];
            uint mask = ShapePlan.Global | (uint)(2 << (index % 4));
            buffer.Add(new ShapingGlyph { Glyph = glyph, Cluster = index, Mask = mask, Class = gdef?.GlyphClass(glyph) is int known and > 0 ? known : 1 });
        }

        int bound = Math.Max(64, buffer.Length * 32);
        buffer.MaxLength = bound;
        var applier = new GsubApplier(gsub, gdef, buffer, Math.Max(1024, buffer.Length * 64 * Math.Max(1, plan.Stages.Length)));
        foreach (LookupStep[] stage in plan.Stages)
        {
            foreach (LookupStep step in stage)
            {
                applier.Apply(step.Lookup, step.Mask);
            }
        }

        if (buffer.Length > bound)
        {
            throw new InvalidOperationException($"The run grew to {buffer.Length} glyphs, past its bound {bound}.");
        }

        for (int index = 1; index < buffer.Length; index++)
        {
            if (buffer[index].Cluster < buffer[index - 1].Cluster || (uint)buffer[index].Glyph > 0xFFFF)
            {
                throw new InvalidOperationException($"Glyph {index} has cluster {buffer[index].Cluster} after {buffer[index - 1].Cluster} (glyph {buffer[index].Glyph}).");
            }
        }

        GlyphBuffer.Return(buffer);
    }
}
