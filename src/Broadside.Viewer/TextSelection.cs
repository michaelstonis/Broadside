using System.Diagnostics.CodeAnalysis;
using System.Text;
using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// The text selection of a document session: an anchor and a focus caret in reading order, possibly on different pages, driven by
/// press-drag, double- and triple-click, select-all and keyboard moves, read back as quads per page for the shell to draw and as
/// text to copy. Pointer coordinates come from <see cref="PageLayout.TryGetPageAt"/> (<see cref="PagePoint"/>, default user
/// space), so the selection is independent of zoom, rotation and layout mode.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.4.4, §14.8.2.5 (reading order), §14.9.4 (replacement text). Spec #33 viewer capability 4. A page takes part once
/// its selectable text is loaded (<see cref="LoadPageAsync"/>); gestures on a page not loaded yet do nothing. <see cref="Begin"/>,
/// <see cref="Extend"/>, <see cref="Move"/> and <see cref="GetQuads"/> allocate nothing, so a shell calls them on every pointer
/// move. Not thread-safe: drive it from the UI thread; loading may run anywhere.
/// </remarks>
public sealed class TextSelection
{
    private readonly SelectableText?[] _pages;
    private TextPosition _anchorStart;
    private TextPosition _anchorEnd;

    /// <summary>Creates an empty selection over a session's pages, in the layout tree's reading order.</summary>
    /// <param name="session">The session.</param>
    public TextSelection(DocumentSession session)
        : this(session, null)
    {
    }

    /// <summary>Creates an empty selection over a session's pages.</summary>
    /// <param name="session">The session.</param>
    /// <param name="readingOrder">The reading order; <see langword="null"/> for <see cref="LayoutReadingOrder.Default"/>.</param>
    public TextSelection(DocumentSession session, IReadingOrderProvider? readingOrder)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
        ReadingOrder = readingOrder ?? LayoutReadingOrder.Default;
        _pages = new SelectableText?[session.PageCount];
    }

    /// <summary>Raised whenever the anchor or focus changes, so the shell redraws the selection.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the session.</summary>
    public DocumentSession Session { get; }

    /// <summary>Gets the reading order the selection follows.</summary>
    public IReadingOrderProvider ReadingOrder { get; }

    /// <summary>Gets where the selection started: the end that stays put while it extends.</summary>
    public TextPosition Anchor { get; private set; }

    /// <summary>Gets where the selection ends: the end a drag or a keyboard move changes (the caret).</summary>
    public TextPosition Focus { get; private set; }

    /// <summary>Gets the granularity of the gesture that made the selection.</summary>
    public SelectionGranularity Granularity { get; private set; }

    /// <summary>Gets the first selected position in reading order.</summary>
    public TextPosition Start => Anchor <= Focus ? Anchor : Focus;

    /// <summary>Gets the position after the last selected glyph.</summary>
    public TextPosition End => Anchor <= Focus ? Focus : Anchor;

    /// <summary>Gets whether nothing is selected.</summary>
    public bool IsEmpty
    {
        get
        {
            TextPosition start = Normalize(Start), end = Normalize(End);
            if (start == end)
            {
                return true;
            }

            // A selection from one page's end to the next page's start selects nothing.
            return end.PageIndex == start.PageIndex + 1 && end.Offset == 0 && TryGetPage(start.PageIndex, out SelectableText? text) && start.Offset >= text.Length;
        }
    }

    /// <summary>Loads (once) a page's selectable text: its display list, hit-test index and reading order.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The page's selectable text.</returns>
    public async ValueTask<SelectableText> LoadPageAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _pages.Length);
        if (Volatile.Read(ref _pages[pageIndex]) is { } loaded)
        {
            return loaded;
        }

        DisplayList list = await Session.GetDisplayListAsync(pageIndex, DisplayLayers.All, cancellationToken).ConfigureAwait(false);

        // The hit-test index and reading order are built off the UI thread in background mode (#357).
        SelectableText text = await Session.ReadAsync(
            _ =>
            {
                PdfPage page = Session.Document.Pages[pageIndex];
                return SelectableText.Create(page, page.CreateHitTestIndex(list), ReadingOrder);
            },
            cancellationToken).ConfigureAwait(false);
        return Interlocked.CompareExchange(ref _pages[pageIndex], text, null) ?? text;
    }

    /// <summary>Gets a page's selectable text when it is loaded.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="text">The text, when loaded.</param>
    /// <returns>Whether the page's text is loaded.</returns>
    public bool TryGetPage(int pageIndex, [NotNullWhen(true)] out SelectableText? text)
    {
        text = (uint)pageIndex < (uint)_pages.Length ? Volatile.Read(ref _pages[pageIndex]) : null;
        return text is not null;
    }

    /// <summary>
    /// Starts a selection at a point: a press (<see cref="SelectionGranularity.Character"/>: an empty selection at the nearest
    /// caret), a double-click (the word there) or a triple-click (the line or paragraph there).
    /// </summary>
    /// <param name="point">The point, from <see cref="PageLayout.TryGetPageAt"/>.</param>
    /// <param name="granularity">The unit to select and to extend by.</param>
    /// <returns>Whether the page's text is loaded (otherwise the selection is cleared).</returns>
    public bool Begin(in PagePoint point, SelectionGranularity granularity = SelectionGranularity.Character)
    {
        if (!TryGetPage(point.PageIndex, out SelectableText? text))
        {
            Clear();
            return false;
        }

        Granularity = granularity;
        (TextPosition start, TextPosition end) = UnitAt(text, point);
        _anchorStart = start;
        _anchorEnd = end;
        Set(start, end);
        return true;
    }

    /// <summary>
    /// Extends the selection to a point (a drag): the focus moves to the nearest caret, or, after a double- or triple-click, to the
    /// far edge of the word or line there while the first one stays selected.
    /// </summary>
    /// <param name="point">The point, from <see cref="PageLayout.TryGetPageAt"/>.</param>
    /// <returns>Whether the selection changed.</returns>
    public bool Extend(in PagePoint point)
    {
        if (!TryGetPage(point.PageIndex, out SelectableText? text))
        {
            return false;
        }

        (TextPosition start, TextPosition end) = UnitAt(text, point);
        return start < _anchorStart ? Set(_anchorEnd, start) : Set(_anchorStart, end);
    }

    /// <summary>Selects between two carets.</summary>
    /// <param name="anchor">The end that stays put.</param>
    /// <param name="focus">The caret.</param>
    public void Select(TextPosition anchor, TextPosition focus)
    {
        Granularity = SelectionGranularity.Character;
        _anchorStart = _anchorEnd = anchor;
        Set(anchor, focus);
    }

    /// <summary>Selects every page's text.</summary>
    public void SelectAll() => Select(new TextPosition(0, 0), new TextPosition(Math.Max(_pages.Length - 1, 0), int.MaxValue));

    /// <summary>Selects nothing.</summary>
    public void Clear() => Select(default, default);

    /// <summary>Moves the caret (the focus) by a keyboard step; with <paramref name="extend"/> (Shift held) the anchor stays.</summary>
    /// <param name="move">The step.</param>
    /// <param name="extend">Whether to extend the selection rather than collapse it to the new caret.</param>
    /// <returns>Whether the caret's page is loaded (otherwise nothing changes).</returns>
    public bool Move(SelectionMove move, bool extend = true)
    {
        TextPosition focus = Normalize(Focus);
        if (!TryGetPage(focus.PageIndex, out SelectableText? text))
        {
            return false;
        }

        int page = focus.PageIndex, offset = focus.Offset, length = text.Length;
        TextPosition target = move switch
        {
            SelectionMove.CharacterBackward => offset > 0 ? new(page, offset - 1) : PreviousPageEnd(page) ?? focus,
            SelectionMove.CharacterForward => offset < length ? new(page, offset + 1) : NextPageStart(page) ?? focus,
            SelectionMove.WordBackward => WordBackward(text, page, offset),
            SelectionMove.WordForward => WordForward(text, page, offset),
            SelectionMove.LineStart => length == 0 ? focus : new(page, text.GetLineRange(Math.Min(offset, length - 1)).Start),
            SelectionMove.LineEnd => length == 0 ? focus : new(page, text.GetLineRange(Math.Min(offset, length - 1)).End),
            SelectionMove.LineUp => LineUp(text, page, offset),
            SelectionMove.LineDown => LineDown(text, page, offset),
            SelectionMove.DocumentStart => new(0, 0),
            SelectionMove.DocumentEnd => new(Math.Max(_pages.Length - 1, 0), int.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(move)),
        };

        if (extend)
        {
            Set(Anchor, target);
        }
        else
        {
            Select(target, target);
        }

        return true;
    }

    /// <summary>Gets the quads to highlight on a page, in its default user space (<see cref="PageLayout.GetPageTransform"/> maps them to the viewport).</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="destination">Receives as many quads as fit.</param>
    /// <returns>The number of quads the page needs (0 when nothing on it is selected or its text is not loaded).</returns>
    /// <remarks>ISO 32000-2 §9.4.4. Allocates nothing.</remarks>
    public int GetQuads(int pageIndex, Span<Quad> destination)
    {
        if (!TryGetRange(pageIndex, out SelectableText? text, out int start, out int end))
        {
            return 0;
        }

        return text.GetQuads(start, end, destination);
    }

    /// <summary>Gets the selected text in reading order, for copying; loads the pages it spans that are not loaded yet.</summary>
    /// <param name="cancellationToken">Cancels loading.</param>
    /// <returns>The logical text (replacement text applied), a line feed between lines and between pages.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.5, §14.9.4.</remarks>
    public async ValueTask<string> GetTextAsync(CancellationToken cancellationToken = default)
    {
        TextPosition start = Start, end = End;
        var builder = new StringBuilder();
        for (int page = start.PageIndex; page <= end.PageIndex && page < _pages.Length; page++)
        {
            SelectableText text = await LoadPageAsync(page, cancellationToken).ConfigureAwait(false);
            int from = page == start.PageIndex ? start.Offset : 0;
            int to = page == end.PageIndex ? end.Offset : int.MaxValue;
            int before = builder.Length;
            if (before > 0 && builder[^1] != '\n')
            {
                builder.Append('\n');
            }

            int marked = builder.Length;
            text.AppendText(builder, from, to);
            if (builder.Length == marked)
            {
                builder.Length = before;
            }
        }

        return builder.ToString();
    }

    /// <summary>Gets the link under a point, for hover feedback (a hand cursor) and activation.</summary>
    /// <param name="point">The point, from <see cref="PageLayout.TryGetPageAt"/>.</param>
    /// <returns>The topmost visible link there, or <see langword="null"/> (also when the page's text is not loaded).</returns>
    /// <remarks>ISO 32000-2 §12.5.6.5.</remarks>
    public HitTestLink? FindLink(in PagePoint point) =>
        TryGetPage(point.PageIndex, out SelectableText? text) ? text.FindLink(new PathPoint(point.X, point.Y)) : null;

    private bool TryGetRange(int pageIndex, [NotNullWhen(true)] out SelectableText? text, out int start, out int end)
    {
        start = end = 0;
        TextPosition first = Start, last = End;
        if (pageIndex < first.PageIndex || pageIndex > last.PageIndex || !TryGetPage(pageIndex, out text))
        {
            text = null;
            return false;
        }

        start = pageIndex == first.PageIndex ? Math.Min(first.Offset, text.Length) : 0;
        end = pageIndex == last.PageIndex ? Math.Min(last.Offset, text.Length) : text.Length;
        return end > start;
    }

    private (TextPosition Start, TextPosition End) UnitAt(SelectableText text, in PagePoint point)
    {
        var at = new PathPoint(point.X, point.Y);
        int page = point.PageIndex;
        if (Granularity == SelectionGranularity.Character || text.Length == 0)
        {
            var caret = new TextPosition(page, text.GetOffset(at));
            return (caret, caret);
        }

        int glyph = text.GetNearestGlyph(at);
        (int start, int end) = Granularity switch
        {
            SelectionGranularity.Word => text.GetWordRange(glyph),
            SelectionGranularity.Line => text.GetLineRange(glyph),
            _ => text.GetParagraphRange(glyph),
        };
        return (new(page, start), new(page, end));
    }

    private bool Set(TextPosition anchor, TextPosition focus)
    {
        if (anchor == Anchor && focus == Focus)
        {
            return false;
        }

        Anchor = anchor;
        Focus = focus;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>A position with its offset clamped to its page's length when the page is loaded.</summary>
    private TextPosition Normalize(TextPosition position) =>
        TryGetPage(position.PageIndex, out SelectableText? text) && position.Offset > text.Length ? position with { Offset = text.Length } : position;

    private TextPosition? PreviousPageEnd(int page) => page > 0 && TryGetPage(page - 1, out SelectableText? previous) ? new(page - 1, previous.Length) : null;

    private TextPosition? NextPageStart(int page) => page + 1 < _pages.Length && TryGetPage(page + 1, out _) ? new(page + 1, 0) : null;

    private TextPosition WordBackward(SelectableText text, int page, int offset)
    {
        int position = offset - 1;
        while (position >= 0 && text.IsSpace(position))
        {
            position--;
        }

        return position >= 0 ? new(page, text.GetWordRange(position).Start) : PreviousPageEnd(page) ?? new(page, 0);
    }

    private TextPosition WordForward(SelectableText text, int page, int offset)
    {
        int position = offset;
        while (position < text.Length && text.IsSpace(position))
        {
            position++;
        }

        return position < text.Length ? new(page, text.GetWordRange(position).End) : NextPageStart(page) ?? new(page, text.Length);
    }

    private TextPosition LineUp(SelectableText text, int page, int offset)
    {
        if (text.Length == 0)
        {
            return new(page, 0);
        }

        PathPoint caret = text.GetCaretPoint(offset);
        int lineStart = text.GetLineRange(Math.Min(offset, text.Length - 1)).Start;
        if (lineStart > 0)
        {
            (int start, int end) = text.GetLineRange(lineStart - 1);
            return new(page, text.GetOffset(caret, start, end));
        }

        if (page > 0 && TryGetPage(page - 1, out SelectableText? previous) && previous.Length > 0)
        {
            (int start, int end) = previous.GetLineRange(previous.Length - 1);
            return new(page - 1, previous.GetOffset(caret, start, end));
        }

        return new(page, 0);
    }

    private TextPosition LineDown(SelectableText text, int page, int offset)
    {
        if (text.Length == 0)
        {
            return new(page, 0);
        }

        PathPoint caret = text.GetCaretPoint(offset);
        int lineEnd = text.GetLineRange(Math.Min(offset, text.Length - 1)).End;
        if (offset < text.Length && lineEnd < text.Length)
        {
            (int start, int end) = text.GetLineRange(lineEnd);
            return new(page, text.GetOffset(caret, start, end));
        }

        if (page + 1 < _pages.Length && TryGetPage(page + 1, out SelectableText? next) && next.Length > 0)
        {
            (int start, int end) = next.GetLineRange(0);
            return new(page + 1, next.GetOffset(caret, start, end));
        }

        return new(page, text.Length);
    }
}
