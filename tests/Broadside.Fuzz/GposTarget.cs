using Broadside.Fonts;
using Broadside.Fonts.OpenType;
using Broadside.Fonts.TrueType;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>gpos</c> target (issue #175): the input is an sfnt program (its <c>GPOS</c>, <c>GDEF</c> and <c>kern</c> tables are read) or
/// any other bytes, which get a GPOS version 1.0 header put in front (and are read as a legacy <c>kern</c> table as they are). It
/// builds the Latin, Arabic, Hebrew and default shape plans and positions runs of glyph ids from the input and from 0 upward, both
/// directions, requiring every advance and offset to stay finite and every attachment to be resolved; kern pairs are looked up.
/// </summary>
/// <remarks>OpenType specification: "GPOS — Glyph Positioning Table" (lookup types 1–9), "GDEF", "kern", "OpenType Layout Common Table Formats".</remarks>
internal static class GposTarget
{
    private static readonly TextShaping Everything = TextShaping.Default.WithFeature("cpsp", true).WithFeature("palt", true);

    public static void Target(ReadOnlySpan<byte> data)
    {
        byte[] bytes = data.ToArray();
        LayoutTable? gpos;
        GdefTable? gdef;
        KernTable? kern;
        if (SfntFile.Open(bytes, new FontProgramContext()) is { } sfnt)
        {
            gpos = sfnt.TryGetTable(SfntFile.Tag("GPOS"), out ReadOnlyMemory<byte> table) ? LayoutTable.ReadPositioning(table) : null;
            gdef = sfnt.TryGetTable(SfntFile.Tag("GDEF"), out ReadOnlyMemory<byte> definitions) ? GdefTable.Read(definitions) : null;
            kern = sfnt.TryGetTable(SfntFile.Tag("kern"), out ReadOnlyMemory<byte> kerning) ? KernTable.Read(kerning) : null;
        }
        else
        {
            gpos = LayoutTable.ReadPositioning((byte[])[0, 1, 0, 0, .. bytes]);
            gdef = GdefTable.Read((byte[])[0, 1, 0, 2, .. bytes]);
            kern = KernTable.Read(bytes);
        }

        for (int index = 0; kern is not null && index + 1 < Math.Min(bytes.Length, 64); index += 2)
        {
            _ = kern.Pair(bytes[index], bytes[index + 1]);
        }

        if (gpos is null)
        {
            return;
        }

        var layout = new OpenTypeLayout(null, gdef, 65536, gpos, kern, 0.5);
        foreach (string script in (string[])["latn", "arab", "hebr", "DFLT"])
        {
            ShapePlan plan = layout.PlanFor(Everything.WithScript(script), SfntFile.Tag(script));
            foreach (bool rightToLeft in (bool[])[false, true])
            {
                Run(layout, plan, bytes, ascending: false, rightToLeft);
                Run(layout, plan, bytes, ascending: true, rightToLeft);
            }
        }

        _ = layout.PairAdjustment(bytes.Length > 0 ? bytes[0] : 0, bytes.Length > 1 ? bytes[1] : 1);
    }

    private static void Run(OpenTypeLayout layout, ShapePlan plan, byte[] bytes, bool ascending, bool rightToLeft)
    {
        GlyphBuffer buffer = GlyphBuffer.Rent();
        int count = ascending ? 48 : Math.Min(bytes.Length / 2, 48);
        for (int index = 0; index < count; index++)
        {
            int glyph = ascending ? index : (bytes[index * 2] << 8) | bytes[(index * 2) + 1];
            int glyphClass = layout.Gdef?.GlyphClass(glyph) is int known and > 0 ? known : 1 + (index % 3);
            buffer.Add(new ShapingGlyph { Glyph = glyph, Cluster = index, Mask = ShapePlan.Global, Class = glyphClass, Component = index % 3, XAdvance = 500 });
        }

        OpenTypeShaper.ApplyPositioning(layout.Gpos!, layout.Gdef, layout.Scale, plan.Positioning, buffer, rightToLeft);
        for (int index = 0; index < buffer.Length; index++)
        {
            ShapingGlyph glyph = buffer[index];
            if (!double.IsFinite(glyph.XAdvance) || !double.IsFinite(glyph.XOffset) || !double.IsFinite(glyph.YOffset) || glyph.AttachChain != 0)
            {
                throw new InvalidOperationException($"Glyph {index} ends at advance {glyph.XAdvance}, offset ({glyph.XOffset}, {glyph.YOffset}), attachment {glyph.AttachChain}.");
            }
        }

        GlyphBuffer.Return(buffer);
    }
}
