using System.Buffers.Binary;
using Broadside.Fonts.OpenType;
using Broadside.Fonts.TrueType;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The feature lookups a language system collects from a damaged <c>GSUB</c> table (OpenType 1.9 Layout Common Table Formats:
/// LangSys, FeatureList, Feature tables). ISO 32000-2 §9.9.
/// </summary>
public sealed class LayoutTableTests
{
    [Fact]
    public void A_language_system_that_lists_one_feature_many_times_or_counts_past_the_table_collects_its_lookups_once()
    {
        // Fuzz findings (#620, gsub and gpos timeouts): each repeated index (and each index read past the end, as feature 0) added
        // the feature's lookups again, so building a shape plan took seconds for a table of a few hundred bytes.
        byte[] table = Gsub(repeats: 200, declaredCount: 0xFFFF);
        LayoutTable layout = LayoutTable.Read(table)!;
        int languageSystem = layout.FindLanguageSystem(layout.FindScript(SfntFile.Tag("DFLT")), 0);
        Assert.True(languageSystem > 0);

        var lookups = new List<int>();
        layout.CollectLookups(languageSystem, SfntFile.Tag("liga"), required: false, lookups);

        Assert.Equal([0, 1], lookups);
    }

    /// <summary>
    /// A GSUB table whose DFLT default LangSys declares <paramref name="declaredCount"/> feature indices and holds
    /// <paramref name="repeats"/> of them, all 0: the one feature, liga, with lookups 0 and 1 (two type 1 lookups without subtables).
    /// </summary>
    private static byte[] Gsub(int repeats, int declaredCount)
    {
        var bytes = new List<byte>();
        void U16(int value) => bytes.AddRange([(byte)(value >> 8), (byte)value]);
        void Tag(string tag) => bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(tag));

        const int scriptList = 10;
        const int langSys = scriptList + 8 + 4;
        int featureList = langSys + 6 + (repeats * 2);
        int lookupList = featureList + 8 + 8;

        U16(1);
        U16(0);
        U16(scriptList);
        U16(featureList);
        U16(lookupList);

        // ScriptList: DFLT, its Script table at +8 with the default LangSys at +4.
        U16(1);
        Tag("DFLT");
        U16(8);
        U16(4);
        U16(0);

        // LangSys: no lookup order, no required feature, then the indices.
        U16(0);
        U16(0xFFFF);
        U16(declaredCount);
        for (int index = 0; index < repeats; index++)
        {
            U16(0);
        }

        // FeatureList: liga, its Feature table at +8 with lookups 0 and 1.
        U16(1);
        Tag("liga");
        U16(8);
        U16(0);
        U16(2);
        U16(0);
        U16(1);

        // LookupList: two lookups of type 1 without subtables.
        U16(2);
        U16(6);
        U16(12);
        for (int lookup = 0; lookup < 2; lookup++)
        {
            U16(1);
            U16(0);
            U16(0);
        }

        byte[] table = [.. bytes];
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(table));
        return table;
    }
}
