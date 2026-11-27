using System.Text.RegularExpressions;

namespace Broadside.Text;

/// <summary>
/// What <see cref="TextPage.Find(TextQuery)"/>, <see cref="PdfPage.FindText(TextQuery)"/> and <see cref="PdfDocument.FindText"/> look for: a literal
/// text or a regular expression, and how it compares with a page's text.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.10 (the Unicode text a page shows) and §14.8.2.6.2 (word breaks). A page's searchable text is its words in reading
/// order joined by single spaces, the breaks between lines included, so a phrase matches across a line break
/// (<see cref="SearchableText.Text"/>).
/// </para>
/// <para>
/// A literal query compares folded text: compatibility characters fold to what they stand for (a ligature "ﬁ" matches "fi", a
/// full-width "Ａ" matches "A") from the full compatibility decomposition (NFKD) table the library ships, so the result does not
/// depend on the host's globalization data; combining marks are dropped unless <see cref="MatchDiacritics"/> is set and case is
/// lowered unless <see cref="MatchCase"/> is set. Any run of white space in the query matches the single space between two words or
/// two lines.
/// </para>
/// <para>
/// A regular expression (<see cref="FromRegex(string)"/>) runs on the searchable text unfolded, so its indices map exactly to glyphs:
/// <see cref="MatchDiacritics"/> does not apply, and when <see cref="MatchCase"/> is <see langword="false"/> it runs with
/// <see cref="RegexOptions.IgnoreCase"/> and <see cref="RegexOptions.CultureInvariant"/>. A match of zero length is skipped.
/// </para>
/// <para>A query may be shared between threads while no thread changes it.</para>
/// </remarks>
public sealed class TextQuery
{
    private static readonly TimeSpan DefaultRegexTimeout = TimeSpan.FromSeconds(2);

    private readonly Regex? _regex;
    private TimeSpan _regexTimeout = DefaultRegexTimeout;
    private CompiledRegex? _compiled;

    /// <summary>Creates a query for a literal text.</summary>
    /// <param name="text">The text to find; runs of white space match any break between words or lines.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty or only white space.</exception>
    public TextQuery(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Pattern = text;
    }

    private TextQuery(string pattern, Regex? regex)
    {
        Pattern = pattern;
        IsRegex = true;
        _regex = regex;
    }

    /// <summary>Gets the literal text, or the regular expression's pattern.</summary>
    public string Pattern { get; }

    /// <summary>Gets whether <see cref="Pattern"/> is a regular expression.</summary>
    public bool IsRegex { get; }

    /// <summary>Gets or sets whether upper and lower case differ (<see langword="false"/>, the default, ignores case).</summary>
    public bool MatchCase { get; set; }

    /// <summary>
    /// Gets or sets whether accents and other combining marks differ (<see langword="false"/>, the default, so "e" matches "é"). A
    /// regular expression ignores it: it runs on the text as the page shows it.
    /// </summary>
    public bool MatchDiacritics { get; set; }

    /// <summary>Gets or sets whether a match must start and end at word boundaries (no letter, digit or combining mark just before or after it).</summary>
    /// <remarks>ISO 32000-2 §14.8.2.6.2.</remarks>
    public bool WholeWord { get; set; }

    /// <summary>
    /// Gets or sets how long a regular expression may run on one page (2 seconds by default) before
    /// <see cref="RegexMatchTimeoutException"/> is thrown for that page; <see cref="Regex.InfiniteMatchTimeout"/> for no limit. A
    /// literal query ignores it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative and not <see cref="Regex.InfiniteMatchTimeout"/>.</exception>
    public TimeSpan RegexTimeout
    {
        get => _regexTimeout;
        set
        {
            if (value != Regex.InfiniteMatchTimeout)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            }

            _regexTimeout = value;
        }
    }

    /// <summary>
    /// Gets or sets a test each match must pass to be returned, or <see langword="null"/> (the default) to return every match: a
    /// validator such as a check digit, which a pattern cannot express. <see cref="TextPatterns"/> sets one on the presets that have a
    /// checksum.
    /// </summary>
    /// <remarks>
    /// The test runs on each match once it is found, and a match it rejects is dropped; the search still goes on after that match's
    /// end, so a rejected match does not make room for an overlapping one. It may run on several threads at once when the query is
    /// shared.
    /// </remarks>
    public Func<TextMatch, bool>? MatchFilter { get; set; }

    /// <summary>Creates a query for a regular expression (<see cref="System.Text.RegularExpressions"/>).</summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The query; case is ignored until <see cref="MatchCase"/> is set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> is empty or not a valid regular expression.</exception>
    public static TextQuery FromRegex(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        // Parse now, so an invalid pattern fails here and not on the first page.
        _ = new Regex(pattern, RegexOptions.CultureInvariant, Regex.InfiniteMatchTimeout);
        return new TextQuery(pattern, null);
    }

    /// <summary>Creates a query for a regular expression, keeping its options.</summary>
    /// <param name="regex">The regular expression.</param>
    /// <returns>
    /// The query. <see cref="MatchCase"/> starts as <see langword="true"/> unless <paramref name="regex"/> ignores case, and
    /// <see cref="RegexTimeout"/> as its match timeout when it has one, so by default the expression runs as given.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="regex"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="regex"/>'s pattern is empty.</exception>
    public static TextQuery FromRegex(Regex regex)
    {
        ArgumentNullException.ThrowIfNull(regex);
        string pattern = regex.ToString();
        ArgumentException.ThrowIfNullOrEmpty(pattern, nameof(regex));
        return new TextQuery(pattern, regex)
        {
            MatchCase = (regex.Options & RegexOptions.IgnoreCase) == 0,
            RegexTimeout = regex.MatchTimeout == Regex.InfiniteMatchTimeout ? DefaultRegexTimeout : regex.MatchTimeout,
        };
    }

    /// <summary>Sets <see cref="MatchCase"/>.</summary>
    /// <param name="matchCase">Whether case differs.</param>
    /// <returns>This query.</returns>
    public TextQuery WithMatchCase(bool matchCase = true)
    {
        MatchCase = matchCase;
        return this;
    }

    /// <summary>Sets <see cref="MatchDiacritics"/>.</summary>
    /// <param name="matchDiacritics">Whether diacritics differ.</param>
    /// <returns>This query.</returns>
    public TextQuery WithMatchDiacritics(bool matchDiacritics = true)
    {
        MatchDiacritics = matchDiacritics;
        return this;
    }

    /// <summary>Sets <see cref="WholeWord"/>.</summary>
    /// <param name="wholeWord">Whether matches are whole words.</param>
    /// <returns>This query.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.6.2.</remarks>
    public TextQuery WithWholeWord(bool wholeWord = true)
    {
        WholeWord = wholeWord;
        return this;
    }

    /// <summary>Sets <see cref="RegexTimeout"/>.</summary>
    /// <param name="timeout">How long a regular expression may run on one page.</param>
    /// <returns>This query.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is zero or negative and not <see cref="Regex.InfiniteMatchTimeout"/>.</exception>
    public TextQuery WithRegexTimeout(TimeSpan timeout)
    {
        RegexTimeout = timeout;
        return this;
    }

    /// <summary>Sets <see cref="MatchFilter"/>.</summary>
    /// <param name="filter">The test each match must pass, or <see langword="null"/> for none.</param>
    /// <returns>This query.</returns>
    public TextQuery WithMatchFilter(Func<TextMatch, bool>? filter)
    {
        MatchFilter = filter;
        return this;
    }

    /// <summary>Returns <see cref="Pattern"/>.</summary>
    /// <returns>The pattern.</returns>
    public override string ToString() => Pattern;

    /// <summary>The regular expression to run for the current <see cref="MatchCase"/> and <see cref="RegexTimeout"/>; built once per combination in use.</summary>
    internal Regex GetRegex()
    {
        bool matchCase = MatchCase;
        TimeSpan timeout = RegexTimeout;
        if (Volatile.Read(ref _compiled) is { } known && known.MatchCase == matchCase && known.Timeout == timeout)
        {
            return known.Regex;
        }

        RegexOptions options = _regex?.Options ?? RegexOptions.CultureInvariant;
        options = matchCase ? options & ~RegexOptions.IgnoreCase : options | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        Regex regex = _regex is not null && _regex.Options == options && _regex.MatchTimeout == timeout
            ? _regex
            : new Regex(Pattern, options, timeout);

        // Immutable: a thread losing the race keeps its own, an equal copy.
        Volatile.Write(ref _compiled, new CompiledRegex(matchCase, timeout, regex));
        return regex;
    }

    private sealed record CompiledRegex(bool MatchCase, TimeSpan Timeout, Regex Regex);
}
