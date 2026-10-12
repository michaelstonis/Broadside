using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Fonts.TrueType;

/// <summary>The glyph names of a "post" table, read once, and the reverse lookup from name to glyph id.</summary>
/// <remarks>
/// OpenType specification, "post — PostScript table", and Apple TrueType Reference Manual, "The 'post' table": version 1.0 names
/// the first 258 glyphs with the standard Macintosh order; version 2.0 indexes that order or its own Pascal strings; version 2.5
/// (deprecated) offsets the standard order; versions 3.0 and 4.0 name nothing. ISO 32000-2 §9.6.5.4 uses the names as the last
/// fallback for selecting a glyph.
/// </remarks>
internal sealed class PostTable
{
    /// <summary>The 258 standard Macintosh glyph names, in their "post" order (Apple TrueType Reference Manual, "The 'post' table").</summary>
    internal static readonly string[] MacStandardNames =
    [
        ".notdef", ".null", "nonmarkingreturn", "space", "exclam", "quotedbl", "numbersign", "dollar", "percent", "ampersand",
        "quotesingle", "parenleft", "parenright", "asterisk", "plus", "comma", "hyphen", "period", "slash", "zero", "one", "two",
        "three", "four", "five", "six", "seven", "eight", "nine", "colon", "semicolon", "less", "equal", "greater", "question", "at",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y",
        "Z", "bracketleft", "backslash", "bracketright", "asciicircum", "underscore", "grave", "a", "b", "c", "d", "e", "f", "g", "h",
        "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z", "braceleft", "bar", "braceright",
        "asciitilde", "Adieresis", "Aring", "Ccedilla", "Eacute", "Ntilde", "Odieresis", "Udieresis", "aacute", "agrave",
        "acircumflex", "adieresis", "atilde", "aring", "ccedilla", "eacute", "egrave", "ecircumflex", "edieresis", "iacute", "igrave",
        "icircumflex", "idieresis", "ntilde", "oacute", "ograve", "ocircumflex", "odieresis", "otilde", "uacute", "ugrave",
        "ucircumflex", "udieresis", "dagger", "degree", "cent", "sterling", "section", "bullet", "paragraph", "germandbls",
        "registered", "copyright", "trademark", "acute", "dieresis", "notequal", "AE", "Oslash", "infinity", "plusminus", "lessequal",
        "greaterequal", "yen", "mu", "partialdiff", "summation", "product", "pi", "integral", "ordfeminine", "ordmasculine", "Omega",
        "ae", "oslash", "questiondown", "exclamdown", "logicalnot", "radical", "florin", "approxequal", "Delta", "guillemotleft",
        "guillemotright", "ellipsis", "nonbreakingspace", "Agrave", "Atilde", "Otilde", "OE", "oe", "endash", "emdash",
        "quotedblleft", "quotedblright", "quoteleft", "quoteright", "divide", "lozenge", "ydieresis", "Ydieresis", "fraction",
        "currency", "guilsinglleft", "guilsinglright", "fi", "fl", "daggerdbl", "periodcentered", "quotesinglbase", "quotedblbase",
        "perthousand", "Acircumflex", "Ecircumflex", "Aacute", "Edieresis", "Egrave", "Iacute", "Icircumflex", "Idieresis", "Igrave",
        "Oacute", "Ocircumflex", "apple", "Ograve", "Uacute", "Ucircumflex", "Ugrave", "dotlessi", "circumflex", "tilde", "macron",
        "breve", "dotaccent", "ring", "cedilla", "hungarumlaut", "ogonek", "caron", "Lslash", "lslash", "Scaron", "scaron", "Zcaron",
        "zcaron", "brokenbar", "Eth", "eth", "Yacute", "yacute", "Thorn", "thorn", "minus", "multiply", "onesuperior", "twosuperior",
        "threesuperior", "onehalf", "onequarter", "threequarters", "franc", "Gbreve", "gbreve", "Idotaccent", "Scedilla", "scedilla",
        "Cacute", "cacute", "Ccaron", "ccaron", "dcroat",
    ];

    private readonly string?[] _names;
    private Dictionary<string, int>? _byName;

    private PostTable(string?[] names) => _names = names;

    /// <summary>Gets a table that names nothing.</summary>
    public static PostTable None { get; } = new([]);

    /// <summary>Reads the glyph names; reports deviations.</summary>
    /// <param name="post">The table.</param>
    /// <param name="glyphCount">The program's glyph count.</param>
    /// <param name="context">The diagnostics sink.</param>
    /// <returns>The names.</returns>
    public static PostTable Read(ReadOnlySpan<byte> post, int glyphCount, FontProgramContext context)
    {
        if (post.Length < 32)
        {
            Report(context, "the table is shorter than its 32-byte header; no glyph has a name");
            return None;
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(post);
        switch (version)
        {
            case 0x00010000:
                return new PostTable([.. MacStandardNames.Take(Math.Min(glyphCount, MacStandardNames.Length))]);
            case 0x00020000:
                return ReadVersion2(post, glyphCount, context);
            case 0x00025000:
                return ReadVersion25(post, glyphCount, context);
            case 0x00030000 or 0x00040000:
                return None;
            default:
                Report(context, string.Create(CultureInfo.InvariantCulture, $"the version 0x{version:X8} is unknown; no glyph has a name"));
                return None;
        }
    }

    /// <summary>Gets a glyph's name, or <see langword="null"/>.</summary>
    public string? NameOf(int glyphId) => (uint)glyphId < (uint)_names.Length ? _names[glyphId] : null;

    /// <summary>Finds the first glyph of a name.</summary>
    public bool TryGetGlyphId(string name, out int glyphId)
    {
        Dictionary<string, int>? byName = Volatile.Read(ref _byName);
        if (byName is null)
        {
            byName = BuildIndex();
            byName = Interlocked.CompareExchange(ref _byName, byName, null) ?? byName;
        }

        return byName.TryGetValue(name, out glyphId);
    }

    private Dictionary<string, int> BuildIndex()
    {
        var index = new Dictionary<string, int>(_names.Length, StringComparer.Ordinal);
        for (int glyph = 0; glyph < _names.Length; glyph++)
        {
            if (_names[glyph] is { } name)
            {
                index.TryAdd(name, glyph);
            }
        }

        return index;
    }

    /// <summary>Version 2.0: an index per glyph into the standard order (below 258) or the Pascal strings that follow (258 to 32767).</summary>
    private static PostTable ReadVersion2(ReadOnlySpan<byte> post, int glyphCount, FontProgramContext context)
    {
        if (post.Length < 34)
        {
            Report(context, "the version 2.0 table has no glyph count; no glyph has a name");
            return None;
        }

        int declared = BinaryPrimitives.ReadUInt16BigEndian(post[32..]);
        int count = Math.Min(declared, Math.Min(glyphCount, (post.Length - 34) / 2));
        if (count != declared)
        {
            Report(context, string.Create(CultureInfo.InvariantCulture, $"the version 2.0 table names {declared} glyphs but the program has {glyphCount} and the table holds indexes for {(post.Length - 34) / 2}; {count} are named"));
        }

        // The Pascal strings follow the indexes for all declared glyphs, and are counted, not terminated.
        int stringsStart = 34 + (2 * declared);
        var strings = new List<string>();
        int position = stringsStart;
        while (position < post.Length)
        {
            int length = post[position];
            if (position + 1 + length > post.Length)
            {
                Report(context, "a glyph name string runs past the end of the table; it is dropped");
                break;
            }

            strings.Add(Encoding.Latin1.GetString(post.Slice(position + 1, length)));
            position += 1 + length;
        }

        var names = new string?[count];
        bool missing = false;
        for (int glyph = 0; glyph < count; glyph++)
        {
            int index = BinaryPrimitives.ReadUInt16BigEndian(post[(34 + (2 * glyph))..]);
            if (index < MacStandardNames.Length)
            {
                names[glyph] = MacStandardNames[index];
            }
            else if (index < 32768)
            {
                int stringIndex = index - MacStandardNames.Length;
                if (stringIndex < strings.Count)
                {
                    names[glyph] = strings[stringIndex];
                }
                else
                {
                    missing = true;
                }
            }
        }

        if (missing)
        {
            Report(context, "some glyph name indexes point past the table's name strings; those glyphs have no name");
        }

        return new PostTable(names);
    }

    /// <summary>Version 2.5: an offset per glyph into the standard order (FreeType's reading: name = standard[glyph + offset]).</summary>
    private static PostTable ReadVersion25(ReadOnlySpan<byte> post, int glyphCount, FontProgramContext context)
    {
        if (post.Length < 34)
        {
            Report(context, "the version 2.5 table has no glyph count; no glyph has a name");
            return None;
        }

        int count = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(post[32..]), Math.Min(glyphCount, post.Length - 34));
        var names = new string?[count];
        for (int glyph = 0; glyph < count; glyph++)
        {
            int index = glyph + (sbyte)post[34 + glyph];
            names[glyph] = index >= 0 && index < MacStandardNames.Length ? MacStandardNames[index] : null;
        }

        return new PostTable(names);
    }

    private static void Report(FontProgramContext context, string problem) =>
        context.Report(DiagnosticCodes.FontTableInvalid, DiagnosticSeverity.Warning, $"The font program's \"post\" table deviates from the OpenType specification: {problem}.");
}
