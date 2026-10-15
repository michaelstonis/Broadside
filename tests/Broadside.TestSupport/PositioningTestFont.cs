namespace Broadside.TestSupport;

/// <summary>
/// A TrueType program (1000 units per em) with a <c>GPOS</c> table exercising every lookup type, in the spirit of this feature file
/// (glyph ids in the comments; every glyph is a rectangle 500 + id units wide, marks included until positioning zeroes them):
/// <code>
/// languagesystem DFLT dflt; languagesystem latn dflt; languagesystem arab dflt; languagesystem hebr dflt;
/// markClass [acute fatha] &lt;anchor 150 600&gt; @TOP; markClass [dotbelow kasra qamats] &lt;anchor 150 0&gt; @BOTTOM;
/// feature dist { pos x &lt;10 5 20 0&gt;;                                         # type 1 format 1: x 24
///                pos y 30; pos z 40;                                          # type 1 format 2: y 25, z 26
///                pos h 15;                                                    # type 9 extension around type 1: h 8
///                pos k' lookup K l;                                           # type 7 format 1: k 11 advance + 100 before l
///                pos m n' lookup N p; } dist;                                 # type 8 format 3: n 14 advance + 60 after m, before p
/// feature kern { pos a v -80; pos v a -70;                                    # type 2 format 1 (no Value2): a 1, v 22
///                pos t [o e] &lt;0 0 -50 0&gt; &lt;5 0 0 0&gt;; } kern;              # type 2 format 2: t 20, o 15, e 5
/// feature curs { lookupflag RightToLeft IgnoreMarks;
///                pos cursive beh.init &lt;anchor NULL&gt; &lt;anchor 0 200&gt;;        # type 3: beh.init 36, beh.fina 37
///                pos cursive beh.fina &lt;anchor 500 0&gt; &lt;anchor NULL&gt;; } curs;
/// feature mark { pos base [a beh beh.init beh.fina shin] &lt;anchor 250 700&gt; mark @TOP &lt;anchor 250 -20&gt; mark @BOTTOM;   # type 4
///                pos ligature f_i &lt;anchor 200 750&gt; mark @TOP ligComponent &lt;anchor 450 750&gt; mark @TOP; } mark;      # type 5: f_i 31
/// feature mkmk { pos mark acute &lt;anchor 150 900&gt; mark @GRAVE; } mkmk;        # type 6: grave 30 (anchor 150 600) on acute 28
/// </code>
/// GSUB: <c>liga</c> f i -> f_i (IgnoreMarks); Arabic <c>init</c>/<c>fina</c> beh 32 -> 36/37. Marks (GDEF class 3): acute 28,
/// dot below 29, grave 30, fatha 34, kasra 35, qamats 39, dagesh 40; f_i 31 is a ligature. Alef 33, shin 38, space 27.
/// </summary>
/// <remarks>Built here table by table, as <see cref="ShapingTestFont"/>: the corpus generator has no font compiler.</remarks>
public static class PositioningTestFont
{
    /// <summary>The number of glyphs.</summary>
    public const int GlyphCount = 41;

    /// <summary>Glyph ids of the marks and other non-Latin glyphs.</summary>
    public const int Acute = 28, DotBelow = 29, Grave = 30, FI = 31, Beh = 32, Alef = 33, Fatha = 34, Kasra = 35, BehInit = 36, BehFinal = 37, Shin = 38, Qamats = 39, Dagesh = 40;

    /// <summary>The glyph id of a lowercase Latin letter.</summary>
    public static int Letter(char letter) => letter - 'a' + 1;

    /// <summary>The advance width of a glyph in font units (thousandths of the font size).</summary>
    public static int Width(int glyph) => 500 + glyph;

    /// <summary>The program's GPOS table.</summary>
    public static byte[] Gpos()
    {
        var gpos = new GsubBuilder();
        int x = Letter('x'), y = Letter('y'), z = Letter('z'), h = Letter('h'), k = Letter('k'), l = Letter('l'), m = Letter('m'), n = Letter('n'), p = Letter('p');
        int a = Letter('a'), v = Letter('v'), t = Letter('t'), o = Letter('o'), e = Letter('e');
        const int placement = GposBuilder.XPlacement | GposBuilder.YPlacement;

        int single1 = gpos.Lookup(1, 0, GposBuilder.Adjustment(placement | GposBuilder.XAdvance, [10, 5, 20], x));
        int single2 = gpos.Lookup(1, 0, GposBuilder.SingleEach(GposBuilder.XAdvance, new Dictionary<int, int[]> { [y] = [30], [z] = [40] }));
        int extension = gpos.Lookup(9, 0, GsubBuilder.Extension(1, GposBuilder.Adjustment(GposBuilder.XAdvance, [15], h)));
        int plusHundred = gpos.Lookup(1, 0, GposBuilder.Adjustment(GposBuilder.XAdvance, [100], k));
        int plusSixty = gpos.Lookup(1, 0, GposBuilder.Adjustment(GposBuilder.XAdvance, [60], n));
        int context = gpos.Lookup(7, 0, GsubBuilder.ContextGlyphs([k, l], (0, plusHundred)));
        int chain = gpos.Lookup(8, 0, GsubBuilder.ChainCoverages([[m]], [[n]], [[p]], (0, plusSixty)));

        int pairs = gpos.Lookup(2, 0, GposBuilder.Pairs(GposBuilder.XAdvance, 0, (a, v, [-80], []), (v, a, [-70], [])));
        int classPairs = gpos.Lookup(2, 0, GposBuilder.ClassPairs(
            GposBuilder.XAdvance,
            GposBuilder.XPlacement,
            [t],
            new Dictionary<int, int> { [t] = 1 },
            new Dictionary<int, int> { [o] = 1, [e] = 1 },
            [[[0, 0], [0, 0]], [[0, 0], [-50, 5]]]));

        int cursive = gpos.Lookup(3, 0x0009, GposBuilder.Cursive((BehInit, null, (0, 200)), (BehFinal, (500, 0), null)));

        var marks = new Dictionary<int, (int, int, int)>
        {
            [Acute] = (0, 150, 600),
            [Fatha] = (0, 150, 600),
            [DotBelow] = (1, 150, 0),
            [Kasra] = (1, 150, 0),
            [Qamats] = (1, 150, 0),
        };
        (int, int)?[] baseAnchors = [(250, 700), (250, -20)];
        var bases = new Dictionary<int, (int, int)?[]> { [a] = baseAnchors, [Beh] = baseAnchors, [BehInit] = baseAnchors, [BehFinal] = baseAnchors, [Shin] = baseAnchors };
        int toBase = gpos.Lookup(4, 0, GposBuilder.MarkToBase(marks, bases, 2));
        int toLigature = gpos.Lookup(5, 0, GposBuilder.MarkToLigature(
            new Dictionary<int, (int, int, int)> { [Acute] = (0, 150, 600) },
            new Dictionary<int, (int, int)?[][]> { [FI] = [[(200, 750)], [(450, 750)]] },
            1));
        int toMark = gpos.Lookup(6, 0, GposBuilder.MarkToBase(
            new Dictionary<int, (int, int, int)> { [Grave] = (0, 150, 600) },
            new Dictionary<int, (int, int)?[]> { [Acute] = [(150, 900)] },
            1));

        int[] features =
        [
            gpos.Feature("dist", single1, single2, extension, context, chain),
            gpos.Feature("kern", pairs, classPairs),
            gpos.Feature("curs", cursive),
            gpos.Feature("mark", toBase, toLigature),
            gpos.Feature("mkmk", toMark),
        ];
        return gpos.Script("DFLT", features).Script("latn", features).Script("arab", features).Script("hebr", features).Build();
    }

    /// <summary>The program's GSUB table: the f_i ligature and the Arabic initial and final forms of beh.</summary>
    public static byte[] Gsub()
    {
        var gsub = new GsubBuilder();
        int liga = gsub.Lookup(4, 0x0008, GsubBuilder.Ligature(([Letter('f'), Letter('i')], FI)));
        int init = gsub.Lookup(1, 0, GsubBuilder.SingleMap(new Dictionary<int, int> { [Beh] = BehInit }));
        int fina = gsub.Lookup(1, 0, GsubBuilder.SingleMap(new Dictionary<int, int> { [Beh] = BehFinal }));
        int[] latin = [gsub.Feature("liga", liga)];
        int[] arabic = [gsub.Feature("init", init), gsub.Feature("fina", fina)];
        return gsub.Script("DFLT", latin).Script("latn", latin).Script("arab", arabic).Build();
    }

    /// <summary>The program's GDEF table.</summary>
    public static byte[] Gdef()
    {
        var classes = new Dictionary<int, int>();
        for (int glyph = 1; glyph < GlyphCount; glyph++)
        {
            classes[glyph] = glyph is Acute or DotBelow or Grave or Fatha or Kasra or Qamats or Dagesh ? 3 : glyph == FI ? 2 : 1;
        }

        return GsubBuilder.Gdef(classes);
    }

    /// <summary>Builds the program: with its GPOS table unless <paramref name="gpos"/> is false, and with a legacy <c>kern</c> table of <paramref name="kern"/> pairs.</summary>
    public static byte[] Build(bool gpos = true, params (int Left, int Right, int Value)[] kern) => Build(gpos ? Gpos() : null, kern);

    /// <summary>Builds the program with <paramref name="gpos"/> as its GPOS table (none for null), and a legacy <c>kern</c> table of <paramref name="kern"/> pairs.</summary>
    public static byte[] Build(byte[]? gpos, params (int Left, int Right, int Value)[] kern)
    {
        var builder = new TrueTypeBuilder
        {
            Name = "BroadsidePositioning",
            Cmap = TrueTypeBuilder.CmapTable((3, 10, TrueTypeBuilder.Format12(
                12,
                (0x20, 0x20, 27),
                (0x61, 0x7A, 1),
                (0x0300, 0x0300, Grave),
                (0x0301, 0x0301, Acute),
                (0x0323, 0x0323, DotBelow),
                (0x05B8, 0x05B8, Qamats),
                (0x05BC, 0x05BC, Dagesh),
                (0x05E9, 0x05E9, Shin),
                (0x0627, 0x0627, Alef),
                (0x0628, 0x0628, Beh),
                (0x064E, 0x064E, Fatha),
                (0x0650, 0x0650, Kasra)))),
            Metrics = [.. Enumerable.Range(0, GlyphCount).Select(glyph => ((ushort)Width(glyph), (short)50))],
        };
        for (int glyph = 0; glyph < GlyphCount; glyph++)
        {
            builder.Glyphs.Add(TrueTypeBuilder.Rectangle(50, 0, 450 + glyph, 600 + glyph));
        }

        builder.Overrides["GSUB"] = Gsub();
        builder.Overrides["GDEF"] = Gdef();
        if (gpos is not null)
        {
            builder.Overrides["GPOS"] = gpos;
        }

        if (kern.Length > 0)
        {
            builder.Overrides["kern"] = GposBuilder.Kern(kern);
        }

        return builder.Build();
    }
}
