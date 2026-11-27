namespace Broadside.Text;

/// <summary>The differences between two documents' words, as <see cref="PdfDocument.CompareText"/> finds them.</summary>
/// <remarks>
/// ISO 32000-2 §14.8.2.6.2 (word breaks) and §14.8.2.2 (artifacts). Immutable: it may be read on several threads at once and kept
/// after both documents are disposed.
/// </remarks>
public sealed class TextComparison
{
    internal TextComparison(IReadOnlyList<TextChange> changes, bool comparedByPage)
    {
        Changes = changes;
        ComparedByPage = comparedByPage;
    }

    /// <summary>
    /// Gets the changes in reading order of both documents. A run of deleted words next to a run of inserted words is one
    /// <see cref="TextChangeKind.Changed"/>, and a change whose words are on more than one page is split into one change per page.
    /// </summary>
    public IReadOnlyList<TextChange> Changes { get; }

    /// <summary>Gets a value indicating whether the documents have the same words, compared as the options asked.</summary>
    public bool IsIdentical => Changes.Count == 0;

    /// <summary>
    /// Gets a value indicating whether the whole-document diff passed <see cref="TextComparisonOptions.MaxEditDistance"/>, so each
    /// page was compared with the page of the same index in the other document instead.
    /// </summary>
    public bool ComparedByPage { get; }
}
