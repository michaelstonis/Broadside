using System.Globalization;

namespace Broadside.Viewer;

/// <summary>
/// The find box of a <see cref="ViewerToolbar"/>: searches the open document through the viewer's <see cref="DocumentSearch"/>, steps
/// through the matches (scrolling each into view) and reports progress in <see cref="Status"/>.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.10 (the text a page shows). Matches arrive as pages are indexed; <see cref="ViewerCommand.Changed"/> follows them on
/// the thread the toolbar was created on. A document whose pages have no text (scanned images) reports that in <see cref="Status"/>.
/// </remarks>
public sealed class ViewerSearchCommand : ViewerCommand
{
    private readonly ViewerToolbar _toolbar;
    private string _status = string.Empty;
    private string _query = string.Empty;

    internal ViewerSearchCommand(ViewerToolbar toolbar)
        : base(ViewerCommandIds.Search, "Find")
    {
        _toolbar = toolbar;
        Update();
    }

    /// <summary>Gets the query being searched for, or an empty string.</summary>
    public string Query => _query;

    /// <summary>
    /// Gets what the search found so far: empty without a query, "Searching…" while pages are being read, "3 of 12", "No matches",
    /// or "No text in this document" when no page has any text.
    /// </summary>
    public string Status => _status;

    /// <summary>Searches the document from the current page; the first match found becomes current and is scrolled into view.</summary>
    /// <param name="query">The text to find; an empty one clears the search.</param>
    /// <param name="options">How to compare, or <see langword="null"/> to ignore case and diacritics.</param>
    public void Submit(string query, TextSearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        Refresh();
        if (!IsEnabled || _toolbar.Viewer.Search is not { } search)
        {
            return;
        }

        _toolbar.Revealed = null;
        if (query.Trim().Length == 0)
        {
            search.Clear();
        }
        else
        {
            search.Search(query, options, _toolbar.Viewer.PageIndex);
        }

        AfterRun();
    }

    /// <summary>Makes the next match current (wrapping) and scrolls it into view.</summary>
    public void Next() => Step(forward: true);

    /// <summary>Makes the previous match current (wrapping) and scrolls it into view.</summary>
    public void Previous() => Step(forward: false);

    private void Step(bool forward)
    {
        Refresh();
        if (IsEnabled && _toolbar.Viewer.Search is { } search)
        {
            _ = forward ? search.Next() : search.Previous();
            _toolbar.RevealCurrentMatch();
            AfterRun();
        }
    }

    private protected override bool Update()
    {
        DocumentSearch? search = _toolbar.Viewer.Search;
        bool enabled = SetEnabled(search is not null && _toolbar.Viewer.Session is not null);
        string query = search?.Query ?? string.Empty;
        string status = search is null ? string.Empty : Describe(search);
        bool changed = !string.Equals(status, _status, StringComparison.Ordinal) || !string.Equals(query, _query, StringComparison.Ordinal);
        _status = status;
        _query = query;
        return enabled | changed;
    }

    private static string Describe(DocumentSearch search)
    {
        if (search.Query.Length == 0)
        {
            return string.Empty;
        }

        int count = search.MatchCount;
        if (count > 0)
        {
            int current = search.CurrentIndex;
            return current >= 0
                ? string.Create(CultureInfo.InvariantCulture, $"{current + 1} of {count}")
                : string.Create(CultureInfo.InvariantCulture, $"{count} found");
        }

        if (!search.IsSearchComplete)
        {
            return "Searching…";
        }

        return search.IsIndexComplete && !search.HasText ? "No text in this document" : "No matches";
    }
}
