using static Broadside.Fonts.OpenType.LayoutData;

namespace Broadside.Fonts.OpenType;

/// <summary>
/// A font's <c>GSUB</c> or <c>GPOS</c> table: its script, feature and lookup lists, with every lookup's type, flags and subtables
/// read once (extension subtables, GSUB type 7 and GPOS type 9, resolved to the subtable they point at). The subtables themselves
/// are read in place while shaping.
/// </summary>
/// <remarks>
/// OpenType specification, "GSUB — Glyph Substitution Table" and "GPOS — Glyph Positioning Table" (versions 1.0 and 1.1; feature
/// variations are not applied) and "OpenType Layout Common Table Formats" (ScriptList, LangSys, FeatureList, LookupList). Both
/// tables share that header. Offsets that point outside the table make the record absent; nothing is trusted to size an allocation
/// beyond what the table's bytes can hold.
/// </remarks>
internal sealed class LayoutTable
{
    private const int SubstitutionExtension = 7;
    private const int PositioningExtension = 9;

    private readonly int _scriptList;
    private readonly int _featureList;

    private LayoutTable(ReadOnlyMemory<byte> data, int scriptList, int featureList, Lookup[] lookups, bool positioning)
    {
        Data = data;
        _scriptList = scriptList;
        _featureList = featureList;
        Lookups = lookups;
        IsPositioning = positioning;
    }

    /// <summary>Gets a value indicating whether this is a <c>GPOS</c> table (else <c>GSUB</c>).</summary>
    public bool IsPositioning { get; }

    /// <summary>Gets the table's bytes.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the lookups of the LookupList, in order.</summary>
    public Lookup[] Lookups { get; }

    /// <summary>Reads a <c>GSUB</c> table, or returns <see langword="null"/> when it is not one of major version 1.</summary>
    public static LayoutTable? Read(ReadOnlyMemory<byte> data) => Read(data, positioning: false);

    /// <summary>Reads a <c>GPOS</c> table, or returns <see langword="null"/> when it is not one of major version 1.</summary>
    public static LayoutTable? ReadPositioning(ReadOnlyMemory<byte> data) => Read(data, positioning: true);

    private static LayoutTable? Read(ReadOnlyMemory<byte> data, bool positioning)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < 10 || U16(span, 0) != 1)
        {
            return null;
        }

        int lookupList = Offset16(span, 0, 8);
        int count = lookupList < 0 ? 0 : Math.Min(U16(span, lookupList), (span.Length - lookupList - 2) / 2);
        var lookups = new Lookup[Math.Max(0, count)];
        for (int index = 0; index < lookups.Length; index++)
        {
            lookups[index] = ReadLookup(span, Offset16(span, lookupList, lookupList + 2 + (index * 2)), positioning ? PositioningExtension : SubstitutionExtension);
        }

        return new LayoutTable(data, Offset16(span, 0, 4), Offset16(span, 0, 6), lookups, positioning);
    }

    /// <summary>The offset of the Script table tagged <paramref name="tag"/>, or -1.</summary>
    public int FindScript(uint tag)
    {
        ReadOnlySpan<byte> span = Data.Span;
        int count = _scriptList < 0 ? 0 : U16(span, _scriptList);
        for (int index = 0; index < count; index++)
        {
            int record = _scriptList + 2 + (index * 6);
            if (record + 6 > span.Length)
            {
                break;
            }

            if (U32(span, record) == tag)
            {
                return Offset16(span, _scriptList, record + 4);
            }
        }

        return -1;
    }

    /// <summary>The offset of the script's LangSys table for <paramref name="language"/>, else its default LangSys, or -1.</summary>
    public int FindLanguageSystem(int script, uint language)
    {
        if (script < 0)
        {
            return -1;
        }

        ReadOnlySpan<byte> span = Data.Span;
        if (language != 0)
        {
            int count = U16(span, script + 2);
            for (int index = 0; index < count; index++)
            {
                int record = script + 4 + (index * 6);
                if (record + 6 > span.Length)
                {
                    break;
                }

                if (U32(span, record) == language && Offset16(span, script, record + 4) is var found and >= 0)
                {
                    return found;
                }
            }
        }

        return Offset16(span, script, script);
    }

    /// <summary>
    /// Adds the lookup indices of the language system's features tagged <paramref name="feature"/> (and, when
    /// <paramref name="required"/>, of its required feature whatever its tag) to <paramref name="lookups"/>.
    /// </summary>
    public void CollectLookups(int languageSystem, uint feature, bool required, List<int> lookups)
    {
        if (languageSystem < 0 || _featureList < 0)
        {
            return;
        }

        ReadOnlySpan<byte> span = Data.Span;
        int featureCount = U16(span, _featureList);
        int requiredIndex = U16(span, languageSystem + 2);
        if (required && requiredIndex != 0xFFFF)
        {
            AddFeatureLookups(span, requiredIndex, featureCount, 0, lookups);
        }

        if (feature == 0)
        {
            return;
        }

        int count = U16(span, languageSystem + 4);
        for (int index = 0; index < count; index++)
        {
            int featureIndex = U16(span, languageSystem + 6 + (index * 2));
            AddFeatureLookups(span, featureIndex, featureCount, feature, lookups);
        }
    }

    /// <summary>Whether the FeatureList has a feature tagged <paramref name="tag"/> with at least one lookup, in any script.</summary>
    public bool HasFeature(uint tag)
    {
        ReadOnlySpan<byte> span = Data.Span;
        int count = _featureList < 0 ? 0 : U16(span, _featureList);
        for (int index = 0; index < count && _featureList + 8 + (index * 6) <= span.Length; index++)
        {
            int record = _featureList + 2 + (index * 6);
            if (U32(span, record) == tag && Offset16(span, _featureList, record + 4) is var table and >= 0 && U16(span, table + 2) > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void AddFeatureLookups(ReadOnlySpan<byte> span, int featureIndex, int featureCount, uint tag, List<int> lookups)
    {
        int record = _featureList + 2 + (featureIndex * 6);
        if (featureIndex >= featureCount || record + 6 > span.Length || (tag != 0 && U32(span, record) != tag))
        {
            return;
        }

        int table = Offset16(span, _featureList, record + 4);
        int count = table < 0 ? 0 : U16(span, table + 2);
        for (int index = 0; index < count; index++)
        {
            int lookup = U16(span, table + 4 + (index * 2));
            if (lookup < Lookups.Length && table + 6 + (index * 2) <= span.Length)
            {
                lookups.Add(lookup);
            }
        }
    }

    /// <summary>
    /// The absolute offset of subtable <paramref name="index"/> of <paramref name="lookup"/>, through its extension subtable for an
    /// extension lookup, or -1 when it is absent, points outside the table or is an extension of another type.
    /// </summary>
    public int Subtable(in Lookup lookup, int index)
    {
        ReadOnlySpan<byte> span = Data.Span;
        int subtable = Offset16(span, lookup.Offset, lookup.Offset + 6 + (index * 2));
        if (subtable < 0 || !lookup.Extension)
        {
            return subtable;
        }

        // Extension Substitution/Positioning Format 1: the 32-bit offset is relative to the extension subtable.
        long target = subtable + (long)U32(span, subtable + 4);
        return U16(span, subtable) == 1 && U16(span, subtable + 2) == lookup.Type && target > subtable && target < span.Length ? (int)target : -1;
    }

    private static Lookup ReadLookup(ReadOnlySpan<byte> span, int offset, int extensionType)
    {
        if (offset < 0)
        {
            return default;
        }

        int type = U16(span, offset);
        int flag = U16(span, offset + 2);
        int count = Math.Max(0, Math.Min(U16(span, offset + 4), (span.Length - offset - 6) / 2));
        int markSet = (flag & LookupFlags.UseMarkFilteringSet) != 0 ? U16(span, offset + 6 + (count * 2)) : 0;
        bool extension = type == extensionType;
        if (extension)
        {
            // Every extension subtable of a lookup shall point at the same type; the first one says which.
            int first = Offset16(span, offset, offset + 6);
            type = first < 0 || U16(span, first) != 1 ? 0 : U16(span, first + 2);
        }

        return type is >= 1 and <= 8 && type != extensionType ? new Lookup(offset, type, flag, markSet, count, extension) : default;
    }
}

/// <summary>
/// One lookup of a GSUB or GPOS LookupList: where it is, its type (GSUB 1–6 or 8, GPOS 1–8, extensions resolved; 0 for an unusable one), flags and number
/// of subtables, which <see cref="LayoutTable.Subtable"/> finds in place.
/// </summary>
/// <param name="Offset">The absolute offset of the Lookup table.</param>
/// <param name="Type">The lookup type.</param>
/// <param name="Flag">The LookupFlag bits.</param>
/// <param name="MarkFilteringSet">The mark glyph set of <see cref="LookupFlags.UseMarkFilteringSet"/>.</param>
/// <param name="SubtableCount">The number of subtables.</param>
/// <param name="Extension">Whether the subtables are extension subtables (GSUB type 7, GPOS type 9) pointing at subtables of <paramref name="Type"/>.</param>
internal readonly record struct Lookup(int Offset, int Type, int Flag, int MarkFilteringSet, int SubtableCount, bool Extension);
/// <summary>The LookupFlag bits (OpenType specification, "Lookup Table").</summary>
internal static class LookupFlags
{
    /// <summary>GPOS cursive attachment: the last glyph of a cursive chain is the one on the baseline (right-to-left chains).</summary>
    public const int RightToLeft = 0x0001;

    /// <summary>Skip base glyphs.</summary>
    public const int IgnoreBaseGlyphs = 0x0002;

    /// <summary>Skip ligature glyphs.</summary>
    public const int IgnoreLigatures = 0x0004;

    /// <summary>Skip mark glyphs.</summary>
    public const int IgnoreMarks = 0x0008;

    /// <summary>Skip marks not in the lookup's mark glyph set.</summary>
    public const int UseMarkFilteringSet = 0x0010;

    /// <summary>The mark attachment class marks must have to not be skipped (high byte; 0 for any).</summary>
    public const int MarkAttachmentTypeMask = 0xFF00;
}
