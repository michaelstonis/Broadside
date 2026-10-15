using Broadside.Fonts.TrueType;

namespace Broadside.Fonts.OpenType;

/// <summary>
/// The lookups shaping applies for one font, script, language system and set of features, grouped in stages: each stage applies its
/// lookups in LookupList order to the glyphs whose mask they share, and a stage starts after the previous one has run over the whole
/// run. Built once per <see cref="TextShaping"/> and script, then reused.
/// </summary>
/// <remarks>
/// OpenType specification, "GSUB — Glyph Substitution Table" (lookups of the features of one LangSys are applied in LookupList
/// order) and "Developing OpenType Fonts for Arabic Script" (feature order: <c>ccmp</c>, <c>locl</c>, then <c>isol</c>, <c>fina</c>,
/// <c>medi</c>, <c>init</c> one after the other, then <c>rlig</c>, then <c>calt</c>, <c>liga</c> and the other typographic features).
/// </remarks>
internal sealed class ShapePlan
{
    /// <summary>The mask of features that apply to every glyph.</summary>
    public const uint Global = 1;

    /// <summary>The mask of the <c>isol</c> feature.</summary>
    public const uint Isolated = 2;

    /// <summary>The mask of the <c>fina</c> feature.</summary>
    public const uint Final = 4;

    /// <summary>The mask of the <c>medi</c> feature.</summary>
    public const uint Medial = 8;

    /// <summary>The mask of the <c>init</c> feature.</summary>
    public const uint Initial = 16;

    private static readonly uint ArabicScript = SfntFile.Tag("arab");

    /// <summary>The positioning features applied by default besides <c>kern</c> (OpenType feature registry; HarfBuzz's horizontal set).</summary>
    private static readonly string[] PositioningFeatures = ["kern", "mark", "mkmk", "curs", "dist", "abvm", "blwm"];

    private ShapePlan(LookupStep[][] stages, bool joining, bool kerning, LookupStep[] positioning)
    {
        Stages = stages;
        Joining = joining;
        Kerning = kerning;
        Positioning = positioning;
    }

    private static readonly ShapePlan Plain = new([], joining: false, kerning: false, []);
    private static readonly ShapePlan Kerned = new([], joining: false, kerning: true, []);

    /// <summary>Gets the stages, each its lookups in LookupList order.</summary>
    public LookupStep[][] Stages { get; }

    /// <summary>Gets the <c>GPOS</c> lookups to apply after substitution, in LookupList order (one stage).</summary>
    public LookupStep[] Positioning { get; }

    /// <summary>Gets a value indicating whether Arabic joining forms select the <c>isol</c>, <c>fina</c>, <c>medi</c> and <c>init</c> lookups.</summary>
    public bool Joining { get; }

    /// <summary>
    /// Gets a value indicating whether the positioning step adds legacy pair kerning (<c>kern</c> on, and the font's <c>GPOS</c>
    /// table, if any, has no <c>kern</c> feature): the font's own pairs, or its <c>kern</c> table.
    /// </summary>
    public bool Kerning { get; }

    /// <summary>The plan of a font without layout tables: one glyph per character, kerned or not.</summary>
    public static ShapePlan WithoutLayout(bool kerning) => kerning ? Kerned : Plain;

    /// <summary>Builds the plan for <paramref name="script"/> (an OpenType script tag, 0 for none found) under <paramref name="shaping"/>.</summary>
    public static ShapePlan Create(LayoutTable? gsub, LayoutTable? gpos, TextShaping shaping, uint script)
    {
        bool joining = script == ArabicScript;
        bool kerning = shaping.IsEnabled("kern");
        var lookups = new List<int>();
        LookupStep[] positioning = [];
        if (gpos is not null)
        {
            int positioningSystem = FindLanguageSystem(gpos, shaping, script);
            var stage = new List<LookupStep[]>();
            string[] explicitly = [.. shaping.ExplicitlyEnabled().Where(tag => !PositioningFeatures.Contains(tag))];
            AddStage(gpos, shaping, positioningSystem, stage, lookups, required: true, [.. PositioningFeatures.Concat(explicitly).Select(tag => (tag, Global))]);
            positioning = stage.Count == 0 ? [] : stage[0];

            // The legacy kern table gives way to a GPOS kern feature (in any script, as HarfBuzz decides).
            kerning &= !gpos.HasFeature(SfntFile.Tag("kern"));
        }

        if (gsub is null)
        {
            return new ShapePlan([], joining, kerning, positioning);
        }

        int languageSystem = FindLanguageSystem(gsub, shaping, script);
        var stages = new List<LookupStep[]>();
        AddStage(gsub, shaping, languageSystem, stages, lookups, required: true, ("ccmp", Global), ("locl", Global));
        if (joining)
        {
            AddStage(gsub, shaping, languageSystem, stages, lookups, required: false, ("isol", Isolated));
            AddStage(gsub, shaping, languageSystem, stages, lookups, required: false, ("fina", Final));
            AddStage(gsub, shaping, languageSystem, stages, lookups, required: false, ("medi", Medial));
            AddStage(gsub, shaping, languageSystem, stages, lookups, required: false, ("init", Initial));
        }

        AddStage(gsub, shaping, languageSystem, stages, lookups, required: false, ("rlig", Global));
        string[] typographic = ["rclt", "calt", "liga", "clig"];
        string[] handled = ["ccmp", "locl", "isol", "fina", "medi", "init", "rlig", .. PositioningFeatures, .. typographic];
        (string, uint)[] last = [.. typographic.Select(tag => (tag, Global)), .. shaping.ExplicitlyEnabled().Where(tag => !handled.Contains(tag)).Select(tag => (tag, Global))];
        AddStage(gsub, shaping, languageSystem, stages, lookups, required: false, last);
        return new ShapePlan([.. stages], joining, kerning, positioning);
    }

    /// <summary>The LangSys of the table for the script (else DFLT, dflt or latn) and the shaping's language, or -1.</summary>
    private static int FindLanguageSystem(LayoutTable table, TextShaping shaping, uint script)
    {
        int scriptTable = -1;
        foreach (uint candidate in (ReadOnlySpan<uint>)[script, SfntFile.Tag("DFLT"), SfntFile.Tag("dflt"), SfntFile.Tag("latn")])
        {
            if (candidate != 0 && (scriptTable = table.FindScript(candidate)) >= 0)
            {
                break;
            }
        }

        return table.FindLanguageSystem(scriptTable, shaping.Language is { } language ? SfntFile.Tag(language) : 0);
    }

    private static void AddStage(LayoutTable gsub, TextShaping shaping, int languageSystem, List<LookupStep[]> stages, List<int> lookups, bool required, params (string Tag, uint Mask)[] features)
    {
        var masks = new SortedDictionary<int, uint>();
        if (required)
        {
            // The language system's required feature applies to every glyph, whatever its tag.
            lookups.Clear();
            gsub.CollectLookups(languageSystem, 0, required: true, lookups);
            foreach (int lookup in lookups)
            {
                masks[lookup] = Global;
            }
        }

        foreach ((string tag, uint mask) in features)
        {
            if (!shaping.IsEnabled(tag))
            {
                continue;
            }

            lookups.Clear();
            gsub.CollectLookups(languageSystem, SfntFile.Tag(tag), required: false, lookups);
            foreach (int lookup in lookups)
            {
                masks[lookup] = masks.GetValueOrDefault(lookup) | mask;
            }
        }

        if (masks.Count > 0)
        {
            stages.Add([.. masks.Select(pair => new LookupStep(pair.Key, pair.Value))]);
        }
    }
}

/// <summary>A lookup of a plan stage and the mask of the features it belongs to.</summary>
/// <param name="Lookup">The LookupList index.</param>
/// <param name="Mask">The glyph mask bits it applies to.</param>
internal readonly record struct LookupStep(int Lookup, uint Mask);
