namespace Broadside.Fonts.Resolution;

/// <summary>
/// Splits a font name into its family and its style: <c>ABCDEF+Arial,BoldItalic</c> is family <c>Arial</c>, bold and italic;
/// <c>TimesNewRomanPS-BoldMT</c> is family <c>TimesNewRoman</c>, bold.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.6.2.1 (Table 109, <c>BaseFont</c>), §9.6.3 (the Windows <c>Name,Style</c> form of TrueType names) and §9.9.2 (the
/// subset tag). The style words are those pdf.js (<c>font_substitutions.js</c>) and PDFBox (<c>FontMapperImpl</c>) read; MT and PS
/// are vendor tags and are dropped.
/// </remarks>
internal static class FontNameParser
{
    /// <summary>Style words, longest first so <c>BoldItalic</c> is read before <c>Bold</c>, with the weight each implies (0 for none).</summary>
    private static readonly (string Word, FontStyle Style, int Weight)[] StyleWords =
    [
        ("BoldOblique", FontStyle.Bold | FontStyle.Italic, 700),
        ("BoldItalic", FontStyle.Bold | FontStyle.Italic, 700),
        ("ExtraLight", FontStyle.None, 200),
        ("UltraLight", FontStyle.None, 200),
        ("ExtraBold", FontStyle.Bold, 800),
        ("UltraBold", FontStyle.Bold, 800),
        ("Condensed", FontStyle.Condensed, 0),
        ("SemiBold", FontStyle.Bold, 600),
        ("DemiBold", FontStyle.Bold, 600),
        ("Inclined", FontStyle.Italic, 0),
        ("Oblique", FontStyle.Italic, 0),
        ("Regular", FontStyle.None, 400),
        ("Italic", FontStyle.Italic, 0),
        ("Medium", FontStyle.None, 500),
        ("Narrow", FontStyle.Condensed, 0),
        ("Normal", FontStyle.None, 400),
        ("Black", FontStyle.Bold, 900),
        ("Heavy", FontStyle.Bold, 900),
        ("Light", FontStyle.None, 300),
        ("Roman", FontStyle.None, 400),
        ("Bold", FontStyle.Bold, 700),
        ("Book", FontStyle.None, 400),
        ("Cond", FontStyle.Condensed, 0),
        ("Demi", FontStyle.Bold, 600),
        ("Thin", FontStyle.None, 100),
        ("PSMT", FontStyle.None, 0),
        ("MT", FontStyle.None, 0),
        ("PS", FontStyle.None, 0),
    ];

    /// <summary>Style words that may end a family name written without a separator (<c>ArialBold</c>, <c>ArialMT</c>); not Roman or Book, which end real family names.</summary>
    private static readonly HashSet<string> SuffixWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "BoldOblique", "BoldItalic", "SemiBold", "DemiBold", "ExtraBold", "Oblique", "Italic", "Black", "Heavy", "Bold", "PSMT", "MT", "PS",
    };

    /// <summary>Removes a subset tag, six uppercase letters and a plus sign (§9.9.2).</summary>
    /// <param name="name">The name.</param>
    /// <returns>The name without the tag.</returns>
    public static string WithoutSubsetTag(string name)
    {
        if (name.Length > 7 && name[6] == '+' && !name.AsSpan(0, 6).ContainsAnyExceptInRange('A', 'Z'))
        {
            return name[7..];
        }

        return name;
    }

    /// <summary>Parses a font name.</summary>
    /// <param name="name">The name as written, possibly with a subset tag.</param>
    /// <returns>The parts.</returns>
    public static ParsedFontName Parse(string name)
    {
        string postScriptName = string.Concat(WithoutSubsetTag(name).Where(ch => !char.IsWhiteSpace(ch)));
        string family = postScriptName;
        FontStyle style = FontStyle.None;
        int weight = 0;

        int comma = family.IndexOf(',', StringComparison.Ordinal);
        if (comma >= 0)
        {
            ReadStyle(family.AsSpan(comma + 1), ref style, ref weight);
            family = family[..comma];
        }

        // Peel style parts after hyphens from the end: Helvetica-Narrow-Bold, Arial-BoldItalicMT, Times-Roman.
        while (family.LastIndexOf('-') is var hyphen and > 0 && IsStyle(family.AsSpan(hyphen + 1)))
        {
            ReadStyle(family.AsSpan(hyphen + 1), ref style, ref weight);
            family = family[..hyphen];
        }

        // Then style words written without a separator: ArialMT, TimesNewRomanPS, ArialBold.
        bool peeled = true;
        while (peeled)
        {
            peeled = false;
            foreach ((string word, FontStyle wordStyle, int wordWeight) in StyleWords)
            {
                if (SuffixWords.Contains(word) && family.Length >= word.Length + 3 && family.EndsWith(word, StringComparison.OrdinalIgnoreCase))
                {
                    style |= wordStyle;
                    weight = wordWeight != 0 ? Math.Max(weight, wordWeight) : weight;
                    family = family[..^word.Length];
                    peeled = true;
                    break;
                }
            }
        }

        return new ParsedFontName(postScriptName, family.Length == 0 ? postScriptName : family, style, weight);
    }

    /// <summary>The key families are compared by: letters and digits only, lower case (<c>Times New Roman</c> and <c>TimesNewRoman</c> agree).</summary>
    /// <param name="family">The family name.</param>
    /// <returns>The key.</returns>
    public static string FamilyKey(string family) => string.Concat(family.Where(char.IsAsciiLetterOrDigit)).ToLowerInvariant();

    /// <summary>Whether a whole part is style words (and nothing else).</summary>
    private static bool IsStyle(ReadOnlySpan<char> part)
    {
        if (part.IsEmpty)
        {
            return false;
        }

        while (!part.IsEmpty)
        {
            int length = MatchWord(part, out _, out _);
            if (length == 0)
            {
                return false;
            }

            part = part[length..];
        }

        return true;
    }

    /// <summary>Reads the style words of a part, skipping anything else.</summary>
    private static void ReadStyle(ReadOnlySpan<char> part, ref FontStyle style, ref int weight)
    {
        while (!part.IsEmpty)
        {
            int length = MatchWord(part, out FontStyle wordStyle, out int wordWeight);
            if (length == 0)
            {
                part = part[1..];
                continue;
            }

            style |= wordStyle;
            weight = wordWeight != 0 ? Math.Max(weight, wordWeight) : weight;
            part = part[length..];
        }
    }

    private static int MatchWord(ReadOnlySpan<char> part, out FontStyle style, out int weight)
    {
        foreach ((string word, FontStyle wordStyle, int wordWeight) in StyleWords)
        {
            if (part.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                style = wordStyle;
                weight = wordWeight;
                return word.Length;
            }
        }

        style = FontStyle.None;
        weight = 0;
        return 0;
    }
}

/// <summary>The parts of a font name.</summary>
/// <param name="PostScriptName">The name without subset tag and white space.</param>
/// <param name="FamilyName">The family: the name without style words and vendor tags.</param>
/// <param name="Style">The style the name states.</param>
/// <param name="Weight">The weight the name states (100 to 900), or 0 when it states none.</param>
internal readonly record struct ParsedFontName(string PostScriptName, string FamilyName, FontStyle Style, int Weight);

/// <summary>The style a font name states.</summary>
[Flags]
internal enum FontStyle
{
    /// <summary>Nothing stated.</summary>
    None = 0,

    /// <summary>Bold or heavier.</summary>
    Bold = 1,

    /// <summary>Italic or oblique.</summary>
    Italic = 2,

    /// <summary>Narrow or condensed.</summary>
    Condensed = 4,
}
