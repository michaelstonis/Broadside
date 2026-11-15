using System.Globalization;

namespace Broadside.Viewer;

/// <summary>
/// The label of every page of a document (the "iv" or "A-3" a reader sees instead of a page index), read once, so a viewer shows labels in
/// its page box, its outline and its thumbnails without touching the document again.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.4.2, Table 161. Spec #33 viewer capability 10. A document without page labels is labeled by its page numbers, 1 to
/// <see cref="Count"/> (<see cref="HasLabels"/> tells them apart).
/// </remarks>
public sealed class PageLabelTable
{
    private readonly string[] _labels;
    private Dictionary<string, int>? _pages;

    private PageLabelTable(string[] labels, bool hasLabels)
    {
        _labels = labels;
        HasLabels = hasLabels;
    }

    /// <summary>Gets the number of pages.</summary>
    public int Count => _labels.Length;

    /// <summary>Gets a value indicating whether the document defines page labels; otherwise every label is the page's number.</summary>
    public bool HasLabels { get; }

    /// <summary>Reads the labels of a session's document.</summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The table.</returns>
    /// <remarks>ISO 32000-2 §12.4.2. The read runs under the session's read lease (off the UI thread in background mode).</remarks>
    public static async ValueTask<PageLabelTable> LoadAsync(DocumentSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return await session.ReadAsync(_ => Create(session.Document), cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Gets the label of a page.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>The label.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The page does not exist.</exception>
    public string GetLabel(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _labels.Length);
        return _labels[pageIndex];
    }

    /// <summary>Finds the page a label belongs to (the first with that label, as a reader typing it into a page box means).</summary>
    /// <param name="label">The label; compared exactly, then ignoring case.</param>
    /// <param name="pageIndex">The zero-based page when found.</param>
    /// <returns>Whether a page has the label.</returns>
    public bool TryFindPage(string label, out int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (_pages is null)
        {
            var pages = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _labels.Length; i++)
            {
                pages.TryAdd(_labels[i], i);
            }

            _pages = pages;
        }

        if (_pages.TryGetValue(label, out pageIndex))
        {
            return true;
        }

        for (int i = 0; i < _labels.Length; i++)
        {
            if (string.Equals(_labels[i], label, StringComparison.OrdinalIgnoreCase))
            {
                pageIndex = i;
                return true;
            }
        }

        pageIndex = -1;
        return false;
    }

    private static PageLabelTable Create(PdfDocument document)
    {
        if (document.PageLabels is { } labels)
        {
            return new PageLabelTable([.. labels.GetLabels()], hasLabels: true);
        }

        string[] numbers = new string[document.Pages.Count];
        for (int i = 0; i < numbers.Length; i++)
        {
            numbers[i] = (i + 1).ToString(CultureInfo.InvariantCulture);
        }

        return new PageLabelTable(numbers, hasLabels: false);
    }
}
