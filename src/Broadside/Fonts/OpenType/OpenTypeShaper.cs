using System.Buffers;
using System.Globalization;
using System.Text;
using Broadside.Fonts.TrueType;

namespace Broadside.Fonts.OpenType;

/// <summary>
/// Shapes text in a canvas font: characters to nominal glyphs, the font's <c>GSUB</c> lookups for the plan's features (with Arabic
/// joining forms), then positioning (<c>GPOS</c> lookups, else legacy pair kerning), then visual order for a right-to-left run. Fonts without OpenType layout tables get
/// one glyph per character and pair kerning.
/// </summary>
/// <remarks>
/// OpenType specification, GSUB, GPOS, kern and "Developing OpenType Fonts for Arabic Script"; Unicode §9.2 (cursive joining). The work runs in
/// a pooled <see cref="GlyphBuffer"/>: shaping and measuring allocate nothing per glyph (a <see cref="ShapedText"/> allocates its
/// result arrays once per run).
/// </remarks>
internal static class OpenTypeShaper
{
    private static readonly uint Arabic = SfntFile.Tag("arab");
    private static readonly uint Hebrew = SfntFile.Tag("hebr");
    private static readonly uint Greek = SfntFile.Tag("grek");
    private static readonly uint Cyrillic = SfntFile.Tag("cyrl");
    private static readonly uint Latin = SfntFile.Tag("latn");

    /// <summary>Shapes <paramref name="text"/> into the glyphs <paramref name="font"/> shows for it.</summary>
    /// <exception cref="ArgumentException">A character has no glyph in the font (the message names it).</exception>
    public static ShapedText Shape(PdfCanvasFont font, string text, TextShaping shaping)
    {
        GlyphBuffer buffer = GlyphBuffer.Rent();
        try
        {
            ShapePlan plan = Run(font, text, shaping, buffer, out bool rightToLeft);
            int count = buffer.Length;
            var glyphs = new CanvasGlyph[count];
            for (int index = 0; index < count; index++)
            {
                glyphs[index] = Resolve(font, text, buffer[index]);
                buffer[index].XAdvance = glyphs[index].Width;
            }

            Position(font, text, plan, buffer, rightToLeft);
            var shaped = new ShapedGlyph[count];
            var ordered = new CanvasGlyph[count];
            for (int index = 0; index < count; index++)
            {
                int source = rightToLeft ? count - 1 - index : index;
                ref ShapingGlyph glyph = ref buffer[source];
                shaped[index] = new ShapedGlyph((int)glyphs[source].Code, glyph.Cluster, glyph.XAdvance, glyph.XOffset, glyph.YOffset);
                ordered[index] = glyphs[source];
            }

            return new ShapedText(font, text, rightToLeft, ordered, shaped);
        }
        finally
        {
            GlyphBuffer.Return(buffer);
        }
    }

    /// <summary>The advance of <paramref name="text"/> as shaped, in glyph space units (thousandths of the font size).</summary>
    /// <exception cref="ArgumentException">A character has no glyph in the font (the message names it).</exception>
    public static double Measure(PdfCanvasFont font, ReadOnlySpan<char> text, TextShaping shaping)
    {
        GlyphBuffer buffer = GlyphBuffer.Rent();
        try
        {
            ShapePlan plan = Run(font, text, shaping, buffer, out bool rightToLeft);
            for (int index = 0; index < buffer.Length; index++)
            {
                buffer[index].XAdvance = Resolve(font, text, buffer[index]).Width;
            }

            Position(font, text, plan, buffer, rightToLeft);
            double units = 0;
            for (int index = 0; index < buffer.Length; index++)
            {
                units += buffer[index].XAdvance;
            }

            return units;
        }
        finally
        {
            GlyphBuffer.Return(buffer);
        }
    }

    /// <summary>
    /// The positioning step, in logical order after substitution, on glyphs whose <see cref="ShapingGlyph.XAdvance"/> holds their
    /// width: the font's <c>GPOS</c> lookups for the plan's features, then legacy pair kerning when the plan asks for it (the font's
    /// own pairs, or its <c>kern</c> table). Marks are skipped as kerning partners.
    /// </summary>
    internal static void Position(PdfCanvasFont font, ReadOnlySpan<char> text, ShapePlan plan, GlyphBuffer buffer, bool rightToLeft)
    {
        OpenTypeLayout? layout = font.OpenTypeLayout;
        if (layout?.Gpos is { } gpos && plan.Positioning.Length > 0)
        {
            ApplyPositioning(gpos, layout.Gdef, layout.Scale, plan.Positioning, buffer, rightToLeft);
        }

        if (!plan.Kerning || (layout is not null && layout.Kern is null))
        {
            return;
        }

        int previous = -1;
        for (int index = 0; index < buffer.Length; index++)
        {
            if (buffer[index].Class == GdefTable.MarkGlyph)
            {
                continue;
            }

            if (previous >= 0)
            {
                buffer[previous].XAdvance += layout?.Kern is { } kern
                    ? kern.Pair(buffer[previous].Glyph, buffer[index].Glyph) * layout.Scale
                    : font.GetKerning(Resolve(font, text, buffer[previous]), Resolve(font, text, buffer[index]));
            }

            previous = index;
        }
    }

    /// <summary>Applies <c>GPOS</c> lookups to a run whose advances hold the glyph widths, then resolves mark and cursive attachments.</summary>
    internal static void ApplyPositioning(LayoutTable gpos, GdefTable? gdef, double scale, LookupStep[] steps, GlyphBuffer buffer, bool rightToLeft)
    {
        int budget = (int)Math.Min(int.MaxValue, Math.Max(1024L, (long)buffer.Length * 64 * Math.Clamp(steps.Length, 1, 64)));
        var applier = new LayoutApplier(gpos, gdef, buffer, budget, scale, rightToLeft);
        foreach (LookupStep step in steps)
        {
            applier.Apply(step.Lookup, step.Mask);
        }

        LayoutApplier.Finish(buffer, rightToLeft);
    }

    /// <summary>Fills <paramref name="buffer"/> with the glyphs of <paramref name="text"/> and applies the plan's substitutions; the glyphs stay in logical order.</summary>
    internal static ShapePlan Run(PdfCanvasFont font, ReadOnlySpan<char> text, TextShaping shaping, GlyphBuffer buffer, out bool rightToLeft)
    {
        OpenTypeLayout? layout = font.OpenTypeLayout;
        GdefTable? gdef = layout?.Gdef;
        int index = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            CanvasGlyph glyph = font.GlyphOrThrow(rune, index, nameof(text));
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            int glyphClass = category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark ? GdefTable.MarkGlyph : GdefTable.BaseGlyph;
            int id = layout is null ? rune.Value : glyph.Key;
            if (gdef?.GlyphClass(id) is int known and > 0)
            {
                glyphClass = known;
            }

            buffer.Add(new ShapingGlyph { Glyph = id, Cluster = index, Mask = ShapePlan.Global, Class = glyphClass });
            index += rune.Utf16SequenceLength;
        }

        uint script = shaping.Script is { } requested ? SfntFile.Tag(requested) : DetectScript(text);
        rightToLeft = shaping.RightToLeft ?? (script == Arabic || script == Hebrew);
        ShapePlan plan = layout?.PlanFor(shaping, script) ?? ShapePlan.WithoutLayout(shaping.IsEnabled("kern"));
        if (layout?.Gsub is not { } gsub || plan.Stages.Length == 0)
        {
            return plan;
        }

        if (plan.Joining)
        {
            ApplyJoiningMasks(text, buffer);
        }

        buffer.MaxLength = Math.Max(64, buffer.Length * 32);
        var applier = new LayoutApplier(gsub, gdef, buffer, Math.Max(1024, buffer.Length * 64 * plan.Stages.Length));
        foreach (LookupStep[] stage in plan.Stages)
        {
            foreach (LookupStep step in stage)
            {
                applier.Apply(step.Lookup, step.Mask);
            }
        }

        return plan;
    }

    /// <summary>The script tag of the first letter of a recognised script in <paramref name="text"/>, or 0.</summary>
    internal static uint DetectScript(ReadOnlySpan<char> text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            int value = rune.Value;
            if (value is (>= 0x0600 and <= 0x06FF) or (>= 0x0750 and <= 0x077F) or (>= 0x0870 and <= 0x08FF) or (>= 0xFB50 and <= 0xFDFF) or (>= 0xFE70 and <= 0xFEFF))
            {
                return Arabic;
            }

            if (value is (>= 0x0590 and <= 0x05FF) or (>= 0xFB1D and <= 0xFB4F))
            {
                return Hebrew;
            }

            if (value is (>= 0x0370 and <= 0x03FF) or (>= 0x1F00 and <= 0x1FFF))
            {
                return Greek;
            }

            if (value is >= 0x0400 and <= 0x052F)
            {
                return Cyrillic;
            }

            if (Rune.IsLetter(rune) && value is < 0x0250 or (>= 0x1E00 and <= 0x1EFF))
            {
                return Latin;
            }
        }

        return 0;
    }

    private static void ApplyJoiningMasks(ReadOnlySpan<char> text, GlyphBuffer buffer)
    {
        ArabicJoining.Form[] forms = ArrayPool<ArabicJoining.Form>.Shared.Rent(buffer.Length);
        try
        {
            ArabicJoining.AssignForms(text, buffer, forms);
            for (int index = 0; index < buffer.Length; index++)
            {
                buffer[index].Mask |= forms[index] switch
                {
                    ArabicJoining.Form.Isolated => ShapePlan.Isolated,
                    ArabicJoining.Form.Final => ShapePlan.Final,
                    ArabicJoining.Form.Medial => ShapePlan.Medial,
                    ArabicJoining.Form.Initial => ShapePlan.Initial,
                    _ => 0,
                };
            }
        }
        finally
        {
            ArrayPool<ArabicJoining.Form>.Shared.Return(forms);
        }
    }

    /// <summary>The canvas glyph of a shaped glyph: by glyph id in a font with layout tables (an id past the program shows glyph 0), else by its character.</summary>
    private static CanvasGlyph Resolve(PdfCanvasFont font, ReadOnlySpan<char> text, in ShapingGlyph shaped)
    {
        if (font.OpenTypeLayout is { } layout)
        {
            int id = shaped.Glyph < layout.GlyphCount ? shaped.Glyph : 0;
            return font.TryGetGlyphById(id, out CanvasGlyph glyph) ? glyph : default;
        }

        font.TryGetGlyph(shaped.Glyph, out CanvasGlyph canvasGlyph);
        return canvasGlyph;
    }
}
