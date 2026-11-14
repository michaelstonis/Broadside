namespace Broadside.Viewer;

/// <summary>How <see cref="DocumentSearch"/> compares a query with a page's text.</summary>
/// <remarks>
/// ISO 32000-2 §9.10 (the Unicode text a page shows) and §14.8.2.6.2 (word breaks). Spec #33 viewer capability 7. Whatever the options,
/// compatibility characters compare as what they stand for (a ligature "ﬁ" matches "fi") and any run of white space in the query
/// matches the break between two words or two lines.
/// </remarks>
public sealed class TextSearchOptions
{
    /// <summary>Gets or sets whether upper and lower case differ (<see langword="false"/>, the default, ignores case).</summary>
    public bool MatchCase { get; set; }

    /// <summary>Gets or sets whether a match must start and end at word boundaries (no letter or digit just before or after it).</summary>
    /// <remarks>ISO 32000-2 §14.8.2.6.2.</remarks>
    public bool WholeWord { get; set; }

    /// <summary>Gets or sets whether accents and other combining marks differ (<see langword="false"/>, the default, so "e" matches "é").</summary>
    public bool MatchDiacritics { get; set; }

    /// <summary>Sets <see cref="MatchCase"/>.</summary>
    /// <param name="matchCase">Whether case differs.</param>
    /// <returns>These options.</returns>
    public TextSearchOptions WithMatchCase(bool matchCase = true)
    {
        MatchCase = matchCase;
        return this;
    }

    /// <summary>Sets <see cref="WholeWord"/>.</summary>
    /// <param name="wholeWord">Whether matches are whole words.</param>
    /// <returns>These options.</returns>
    public TextSearchOptions WithWholeWord(bool wholeWord = true)
    {
        WholeWord = wholeWord;
        return this;
    }

    /// <summary>Sets <see cref="MatchDiacritics"/>.</summary>
    /// <param name="matchDiacritics">Whether diacritics differ.</param>
    /// <returns>These options.</returns>
    public TextSearchOptions WithMatchDiacritics(bool matchDiacritics = true)
    {
        MatchDiacritics = matchDiacritics;
        return this;
    }
}
