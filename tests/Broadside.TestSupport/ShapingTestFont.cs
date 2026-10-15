namespace Broadside.TestSupport;

/// <summary>
/// A TrueType program with a <c>GSUB</c> table exercising every lookup type, in the spirit of this feature file (glyph ids in the
/// comments; every glyph is a rectangle 500 + id units wide):
/// <code>
/// languagesystem DFLT dflt; languagesystem latn dflt; languagesystem latn TRK; languagesystem arab dflt;
/// feature smcp { sub [a b c] by [a.sc b.sc c.sc]; } smcp;                     # type 1 format 1 (delta 27): 1-3 -> 28-30
/// feature locl { script latn; language TRK; sub i by i.trk; } locl;          # type 1 format 2: 9 -> 31
/// feature ccmp { sub q by q q.tail; } ccmp;                                   # type 2: 17 -> 17 32
/// feature salt { sub g from [g.alt1 g.alt2]; } salt;                          # type 3: 7 -> 33
/// feature liga { sub f f i by f_f_i; sub f f by f_f; sub f i by f_i;          # type 4: 35, 37, 36
///                lookupflag IgnoreMarks; sub d j by d_j; } liga;               # type 4 skipping the mark 48: 49
/// feature calt { sub x y' lookup Y;                                           # type 5 format 1: y 25 -> 38 after x
///                sub @vowel @vowel' lookup VOWEL;                             # type 5 format 2: e o a -> 39 40 41 after a vowel
///                sub k' lookup K l;                                           # type 5 format 3: k 11 -> 42 before l
///                sub m n' lookup N p;                                         # type 6 format 1: n 14 -> 43
///                sub @u v' lookup V @w;                                       # type 6 format 2: v 22 -> 44
///                sub r s' lookup S t;                                         # type 6 format 3: s 19 -> 45
///                sub h by h.alt;                                              # type 7 around type 1: 8 -> 46
///                rsub z' z by z.join; } calt;                                 # type 8: 26 -> 47 before z
/// feature init/medi/fina { ... } ; feature rlig { sub lam.init alef.fina by lam_alef; sub lam.medi alef.fina by lam_alef.fina; }
/// </code>
/// Arabic: beh 50, teh 51, alef 52, lam 53; beh.init/medi/fina 54-56, teh 57-59, alef.fina 60, lam 61-63, lam_alef 64,
/// lam_alef.fina 65. Hebrew (no lookups): shin 66, lamed 67, vav 68, final mem 69. Space 27, combining acute 48 (GDEF mark).
/// </summary>
/// <remarks>The corpus generator has no font compiler (it is stdlib-only Python), so the program is built here, table by table.</remarks>
public static class ShapingTestFont
{
    /// <summary>The number of glyphs.</summary>
    public const int GlyphCount = 70;

    /// <summary>The glyph id of a lowercase Latin letter.</summary>
    public static int Letter(char letter) => letter - 'a' + 1;

    /// <summary>The program's GSUB table.</summary>
    public static byte[] Gsub()
    {
        var gsub = new GsubBuilder();
        int smcp = gsub.Lookup(1, 0, GsubBuilder.SingleDelta(27, 1, 2, 3));
        int locl = gsub.Lookup(1, 0, GsubBuilder.SingleMap(new Dictionary<int, int> { [9] = 31 }));
        int ccmp = gsub.Lookup(2, 0, GsubBuilder.Multiple(new Dictionary<int, int[]> { [17] = [17, 32] }));
        int salt = gsub.Lookup(3, 0, GsubBuilder.Alternate(new Dictionary<int, int[]> { [7] = [33, 34] }));
        int liga = gsub.Lookup(4, 0, GsubBuilder.Ligature(([6, 6, 9], 35), ([6, 6], 37), ([6, 9], 36)));
        int ligaMarks = gsub.Lookup(4, 0x0008, GsubBuilder.Ligature(([4, 10], 49)));

        int y = gsub.Lookup(1, 0, GsubBuilder.SingleMap(new Dictionary<int, int> { [25] = 38 }));
        int vowel = gsub.Lookup(1, 0, GsubBuilder.SingleMap(new Dictionary<int, int> { [5] = 39, [15] = 40, [1] = 41 }));
        int k = gsub.Lookup(1, 0, GsubBuilder.SingleMap(new Dictionary<int, int> { [11] = 42 }));
        int n = gsub.Lookup(1, 0, GsubBuilder.SingleMap(new Dictionary<int, int> { [14] = 43 }));
        int v = gsub.Lookup(1, 0, GsubBuilder.SingleMap(new Dictionary<int, int> { [22] = 44 }));
        int s = gsub.Lookup(1, 0, GsubBuilder.SingleMap(new Dictionary<int, int> { [19] = 45 }));
        var vowels = new Dictionary<int, int> { [1] = 1, [5] = 1, [15] = 1 };
        int context1 = gsub.Lookup(5, 0, GsubBuilder.ContextGlyphs([24, 25], (1, y)));
        int context2 = gsub.Lookup(5, 0, GsubBuilder.ContextClasses(vowels, [1, 5, 15], [1, 1], (1, vowel)));
        int context3 = gsub.Lookup(5, 0, GsubBuilder.ContextCoverages([[11], [12]], (0, k)));
        int chain1 = gsub.Lookup(6, 0, GsubBuilder.ChainGlyphs([13], [14], [16], (0, n)));
        int chain2 = gsub.Lookup(6, 0, GsubBuilder.ChainClasses((new Dictionary<int, int> { [21] = 1 }, new Dictionary<int, int> { [22] = 1 }, new Dictionary<int, int> { [23] = 1 }), [22], [1], [1], [1], (0, v)));
        int chain3 = gsub.Lookup(6, 0, GsubBuilder.ChainCoverages([[18]], [[19]], [[20]], (0, s)));
        int extension = gsub.Lookup(7, 0, GsubBuilder.Extension(1, GsubBuilder.SingleMap(new Dictionary<int, int> { [8] = 46 })));
        int reverse = gsub.Lookup(8, 0, GsubBuilder.ReverseChain(new Dictionary<int, int> { [26] = 47 }, [], [[26, 47]]));

        int init = gsub.Lookup(1, 0, GsubBuilder.SingleMap(new Dictionary<int, int> { [50] = 54, [51] = 57, [53] = 61 }));
        int medi = gsub.Lookup(1, 0, GsubBuilder.SingleMap(new Dictionary<int, int> { [50] = 55, [51] = 58, [53] = 62 }));
        int fina = gsub.Lookup(1, 0, GsubBuilder.SingleMap(new Dictionary<int, int> { [50] = 56, [51] = 59, [52] = 60, [53] = 63 }));
        int rlig = gsub.Lookup(4, 0, GsubBuilder.Ligature(([61, 60], 64), ([62, 60], 65)));

        int[] latin =
        [
            gsub.Feature("smcp", smcp), gsub.Feature("ccmp", ccmp), gsub.Feature("salt", salt), gsub.Feature("liga", liga, ligaMarks),
            gsub.Feature("calt", context1, context2, context3, chain1, chain2, chain3, extension, reverse),
        ];
        int turkish = gsub.Feature("locl", locl);
        int[] arabic = [gsub.Feature("init", init), gsub.Feature("medi", medi), gsub.Feature("fina", fina), gsub.Feature("rlig", rlig)];
        return gsub.Script("DFLT", latin).Script("latn", latin, ("TRK ", [.. latin, turkish])).Script("arab", arabic).Build();
    }

    /// <summary>The program's GDEF table: the combining acute is a mark, the ligatures are ligatures, the rest base glyphs.</summary>
    public static byte[] Gdef()
    {
        var classes = new Dictionary<int, int>();
        for (int glyph = 1; glyph < GlyphCount; glyph++)
        {
            classes[glyph] = glyph is 35 or 36 or 37 or 49 or 64 or 65 ? 2 : glyph == 48 ? 3 : 1;
        }

        return GsubBuilder.Gdef(classes);
    }

    /// <summary>Builds the program.</summary>
    public static byte[] Build()
    {
        var builder = new TrueTypeBuilder
        {
            Name = "BroadsideShaping",
            Cmap = TrueTypeBuilder.CmapTable((3, 10, TrueTypeBuilder.Format12(
                12,
                (0x20, 0x20, 27),
                (0x61, 0x7A, 1),
                (0x0301, 0x0301, 48),
                (0x05D5, 0x05D5, 68),
                (0x05DC, 0x05DC, 67),
                (0x05DD, 0x05DD, 69),
                (0x05E9, 0x05E9, 66),
                (0x0627, 0x0627, 52),
                (0x0628, 0x0628, 50),
                (0x062A, 0x062A, 51),
                (0x0644, 0x0644, 53)))),
            Metrics = [.. Enumerable.Range(0, GlyphCount).Select(glyph => ((ushort)(500 + glyph), (short)50))],
        };
        for (int glyph = 0; glyph < GlyphCount; glyph++)
        {
            builder.Glyphs.Add(TrueTypeBuilder.Rectangle(50, 0, 450 + glyph, 600 + glyph));
        }

        builder.Overrides["GSUB"] = Gsub();
        builder.Overrides["GDEF"] = Gdef();
        return builder.Build();
    }
}
