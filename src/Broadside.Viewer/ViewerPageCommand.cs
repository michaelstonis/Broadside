using System.Globalization;

namespace Broadside.Viewer;

/// <summary>
/// The page box of a <see cref="ViewerToolbar"/>: shows the current page's label (its number when the document defines none) and goes
/// to the page a reader types, by label or by number.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.4.2 (page labels). A typed label is looked up first (the first page with it, exactly and then ignoring case, as
/// <see cref="PageLabelTable.TryFindPage"/> does), then a page number from 1. Input that names no page leaves the view where it is
/// and sets <see cref="ViewerCommand.Error"/>; nothing throws.
/// </remarks>
public sealed class ViewerPageCommand : ViewerCommand
{
    private readonly ViewerToolbar _toolbar;
    private string _text = string.Empty;
    private int _count;

    internal ViewerPageCommand(ViewerToolbar toolbar)
        : base(ViewerCommandIds.PageNumber, "Page number")
    {
        _toolbar = toolbar;
        Update();
    }

    /// <summary>Gets the current page's label or number, or an empty string while nothing is open.</summary>
    public string Text => _text;

    /// <summary>Gets the document's page count (0 while nothing is open or a remote document is still opening), for an "of N" beside the box.</summary>
    public int PageCount => _count;

    /// <summary>Goes to the page a reader typed: a page label, or a page number from 1.</summary>
    /// <param name="text">What was typed; surrounding white space is ignored.</param>
    /// <returns>Whether the text named a page (the view goes there); otherwise the view stays and <see cref="ViewerCommand.Error"/> says why.</returns>
    public bool TrySubmit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Refresh();
        IDocumentViewer viewer = _toolbar.Viewer;
        if (!IsEnabled)
        {
            return Refuse("No document is open.");
        }

        string typed = text.Trim();
        int page = -1;
        if (typed.Length > 0 && _toolbar.CurrentLabels is { HasLabels: true } labels && labels.TryFindPage(typed, out int labeled))
        {
            page = labeled;
        }
        else if (int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number >= 1 && number <= viewer.PageCount)
        {
            page = number - 1;
        }

        if (page < 0 || page >= viewer.PageCount)
        {
            return Refuse(typed.Length == 0 ? "Type a page number or label." : $"There is no page \"{typed}\".");
        }

        SetError(null);
        viewer.GoToPage(page);
        AfterRun();
        return true;
    }

    private bool Refuse(string error)
    {
        if (SetError(error))
        {
            OnChanged();
        }

        return false;
    }

    private protected override bool Update()
    {
        IDocumentViewer viewer = _toolbar.Viewer;
        int count = viewer.Session is null ? 0 : viewer.PageCount;
        bool enabled = SetEnabled(count > 0);
        string text = string.Empty;
        if (count > 0)
        {
            int page = Math.Clamp(viewer.PageIndex, 0, count - 1);
            text = _toolbar.CurrentLabels is { } labels && page < labels.Count
                ? labels.GetLabel(page)
                : (page + 1).ToString(CultureInfo.InvariantCulture);
        }

        bool changed = count != _count || !string.Equals(text, _text, StringComparison.Ordinal);
        _count = count;
        _text = text;
        return enabled | changed;
    }
}
