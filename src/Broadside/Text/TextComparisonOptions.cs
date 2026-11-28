namespace Broadside.Text;

/// <summary>How <see cref="PdfDocument.CompareText"/> compares two documents' words, set fluently with <c>With*</c> methods.</summary>
/// <remarks>
/// ISO 32000-2 §14.8.2.6.2 (word breaks) and §14.8.2.2 (artifacts). Options may be shared between threads while no thread changes
/// them.
/// </remarks>
public sealed class TextComparisonOptions
{
    /// <summary>The default <see cref="MaxEditDistance"/>, in words.</summary>
    public const int DefaultMaxEditDistance = 100_000;

    private int _maxEditDistance = DefaultMaxEditDistance;

    /// <summary>
    /// Gets or sets a value indicating whether upper and lower case differ. Default <see langword="true"/>: words compare exactly.
    /// When <see langword="false"/>, words compare by their folded text, as a literal <see cref="TextQuery"/> folds it with
    /// <see cref="TextQuery.MatchDiacritics"/> set: compatibility characters fold to what they stand for and case is lowered;
    /// diacritics still count.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.10 (the Unicode text a page shows).</remarks>
    public bool MatchCase { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether punctuation is left out of the comparison: each word is compared without its
    /// punctuation characters, and a word made only of punctuation is not compared at all. Default <see langword="false"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.8.2.6.2 (word breaks).</remarks>
    public bool IgnorePunctuation { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether artifacts (running heads, page numbers, decoration) are compared. Default
    /// <see langword="false"/>: they are not extracted (<see cref="TextExtractionOptions.IncludeArtifacts"/>), so a changed page
    /// number is not a change.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    public bool IncludeArtifacts { get; set; }

    /// <summary>
    /// Gets or sets the largest number of inserted and deleted words the whole-document diff looks for. Default
    /// <see cref="DefaultMaxEditDistance"/>. Past it (the sign of a reordered or rewritten document), the documents are compared page
    /// by page instead: each page with the page of the same index in the other document. The bound also limits the time of the
    /// whole-document diff, which grows with the word count times this distance.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int MaxEditDistance
    {
        get => _maxEditDistance;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maxEditDistance = value;
        }
    }

    /// <summary>Sets <see cref="MatchCase"/>.</summary>
    /// <param name="matchCase">Whether upper and lower case differ.</param>
    /// <returns>These options.</returns>
    public TextComparisonOptions WithMatchCase(bool matchCase = true)
    {
        MatchCase = matchCase;
        return this;
    }

    /// <summary>Sets <see cref="IgnorePunctuation"/>.</summary>
    /// <param name="ignore">Whether punctuation is left out of the comparison.</param>
    /// <returns>These options.</returns>
    public TextComparisonOptions WithIgnorePunctuation(bool ignore = true)
    {
        IgnorePunctuation = ignore;
        return this;
    }

    /// <summary>Sets <see cref="IncludeArtifacts"/>.</summary>
    /// <param name="include">Whether artifacts are compared.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    public TextComparisonOptions WithArtifacts(bool include = true)
    {
        IncludeArtifacts = include;
        return this;
    }

    /// <summary>Sets <see cref="MaxEditDistance"/>.</summary>
    /// <param name="words">The largest number of inserted and deleted words the whole-document diff looks for.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="words"/> is negative.</exception>
    public TextComparisonOptions WithMaxEditDistance(int words)
    {
        MaxEditDistance = words;
        return this;
    }
}
