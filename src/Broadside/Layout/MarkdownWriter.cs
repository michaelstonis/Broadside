using System.Buffers;
using System.Globalization;

namespace Broadside.Layout;

/// <summary>
/// Writes a layout tree as CommonMark with the GitHub Flavored Markdown table and footnote extensions (issue #629): headings, paragraphs,
/// lists, tables, figures, captions, links and notes, straight to a <see cref="TextWriter"/> with line feeds.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.4 (the standard structure types each block stands for), §14.9.3 (alternate descriptions) and §14.8.2.2 (artifacts
/// are not in the tree, so they are not written). Text is escaped as CommonMark 0.31 §2.4 allows. Not thread-safe: one per call.
/// </remarks>
internal sealed class MarkdownWriter : ILayoutInlineSink
{
    private static readonly SearchValues<char> LabelCharacters = SearchValues.Create("0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ(");
    private static readonly SearchValues<char> EntityCharacters = SearchValues.Create("#0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");

    private readonly TextWriter _writer;
    private readonly MarkdownSaveOptions _options;
    private readonly LayoutInlines _inlines;
    private readonly List<string> _indents = [];
    private readonly Dictionary<int, int> _figures = [];
    private readonly Dictionary<LayoutNode, int> _noteNumbers = new(ReferenceEqualityComparer.Instance);
    private readonly List<LayoutNode> _notes = [];

    // Block state.
    private bool _wroteBlock;
    private bool _atLineStart = true;
    private bool _tightNext;
    private string? _marker;
    private int _lastPage = -1;

    // Inline state of the block being written.
    private Inline _inline;
    private string? _leader;
    private bool _blockStarted;
    private bool _atTextStart;
    private bool _html;
    private bool _plain;
    private InlineEmphasis _emphasis;
    private LayoutNode? _link;
    private string? _uri;

    private MarkdownWriter(TextWriter writer, MarkdownSaveOptions options)
    {
        _writer = writer;
        _options = options;
        _inlines = new LayoutInlines(options.InlineEmphasis);
    }

    private enum Inline
    {
        None,
        Block,
        Cell,
    }

    /// <summary>Writes <paramref name="tree"/> to <paramref name="writer"/>.</summary>
    /// <param name="tree">The tree.</param>
    /// <param name="writer">Where to write.</param>
    /// <param name="options">The options.</param>
    public static void Write(LayoutTree tree, TextWriter writer, MarkdownSaveOptions options)
    {
        var markdown = new MarkdownWriter(writer, options);
        markdown.Block(tree.Root);
        markdown.Footnotes();
        if (markdown._wroteBlock)
        {
            markdown.EndLine();
        }
    }

    /// <inheritdoc/>
    public void Text(ReadOnlySpan<char> text, InlineEmphasis emphasis, LayoutNode? link, string? uri, bool spaceBefore, LayoutPage page)
    {
        if (text.IsEmpty)
        {
            return;
        }

        if (_plain)
        {
            emphasis = InlineEmphasis.None;
        }

        if (uri is null)
        {
            link = null;
        }

        bool first = !_blockStarted;
        StartInline(page.PageIndex);
        if (!ReferenceEquals(link, _link))
        {
            CloseEmphasis();
            CloseLink();
            if (spaceBefore && !first)
            {
                Raw(" ");
            }

            OpenLink(link, uri);
        }
        else if (emphasis != _emphasis)
        {
            CloseEmphasis();
            if (spaceBefore && !first)
            {
                Raw(" ");
            }
        }
        else if (spaceBefore && !first)
        {
            Raw(" ");
        }

        OpenEmphasis(emphasis);
        Escaped(text);
    }

    /// <inheritdoc/>
    public void NoteReference(LayoutNode note)
    {
        if (note.Text.Length == 0)
        {
            return;
        }

        int count = _noteNumbers.Count;
        int number = NoteNumber(note);
        if (_noteNumbers.Count > count)
        {
            _notes.Add(note);
        }

        StartInline(note.Extents.Count > 0 ? note.Extents[0].Page.PageIndex : _lastPage);
        CloseEmphasis();
        Raw(_html ? string.Create(CultureInfo.InvariantCulture, $"<sup>{number}</sup>") : string.Create(CultureInfo.InvariantCulture, $"[^{number}]"));
    }

    /// <inheritdoc/>
    public void Embedded(LayoutNode node)
    {
        if (node.AlternateDescription is { } alternate && !string.IsNullOrWhiteSpace(alternate))
        {
            StartInline(node.Extents.Count > 0 ? node.Extents[0].Page.PageIndex : _lastPage);
            CloseEmphasis();
            CloseLink();
            if (!_atTextStart)
            {
                Raw(" ");
            }

            Raw(_html ? "<em>" : "*");
            Escaped(Collapse(alternate));
            Raw(_html ? "</em>" : "*");
        }
    }

    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int FirstPage(LayoutNode node) => node.Extents.Count > 0 ? node.Extents[0].Page.PageIndex : -1;

    private static bool IsOrdered(LayoutList list)
    {
        switch (list.Numbering)
        {
            case "Ordered" or "Decimal" or "UpperRoman" or "LowerRoman" or "UpperAlpha" or "LowerAlpha":
                return true;
            case "Unordered" or "Disc" or "Circle" or "Square" or "Description":
                return false;
        }

        // ListNumbering None (the default) or a name the file made up: the first label tells, "1." or "a)" being a number.
        foreach (LayoutNode item in list.Children)
        {
            foreach (LayoutNode child in item.Children)
            {
                if (child.Kind == LayoutNodeKind.Label)
                {
                    ReadOnlySpan<char> label = child.Text.AsSpan().Trim();
                    return label.Length is >= 2 and <= 6 && label[^1] is '.' or ')' && char.IsAsciiLetterOrDigit(label[0])
                        && !label[..^1].ContainsAnyExcept(LabelCharacters);
                }
            }

            break;
        }

        return false;
    }

    private static LayoutNode RootOf(LayoutNode node)
    {
        while (node.Parent is { } parent)
        {
            node = parent;
        }

        return node;
    }

    private void Block(LayoutNode node)
    {
        switch (node.Kind)
        {
            case LayoutNodeKind.Heading:
                Heading((LayoutHeading)node);
                break;
            case LayoutNodeKind.List when node is LayoutList list:
                List(list);
                break;
            case LayoutNodeKind.Table when node is LayoutTable table:
                Table(table);
                break;
            case LayoutNodeKind.Figure:
                Figure(node);
                break;
            case LayoutNodeKind.Note:
                NoteDefinition(node);
                break;
            default:
                Container(node, skipLabels: false);
                break;
        }
    }

    /// <summary>Writes a node's content: each run of its own text and inline children as a paragraph, each block child as a block.</summary>
    private void Container(LayoutNode node, bool skipLabels)
    {
        List<LayoutContentItem> items = LayoutInlines.Items(node);
        int run = -1;
        for (int index = 0; index < items.Count; index++)
        {
            LayoutNode? child = items[index].Child;
            bool inline = child is null || LayoutInlines.IsInline(child) || (child.Kind == LayoutNodeKind.Label && !skipLabels);
            if (inline)
            {
                if (run < 0)
                {
                    run = index;
                }

                continue;
            }

            if (run >= 0)
            {
                Paragraph(items, run, index);
                run = -1;
            }

            if (child!.Kind == LayoutNodeKind.Label)
            {
                continue;
            }

            Block(child);
        }

        if (run >= 0)
        {
            Paragraph(items, run, items.Count);
        }
    }

    private void Paragraph(List<LayoutContentItem> items, int start, int end)
    {
        BeginInline(Inline.Block, leader: null);
        _inlines.Walk(items, start, end, this);
        EndInline();
    }

    private void Heading(LayoutHeading heading)
    {
        int level = Math.Clamp(heading.Level, 1, 6);
        BeginInline(Inline.Block, new string('#', level) + " ");
        _plain = true;
        _inlines.Walk(heading, this);
        EndInline();
    }

    private void List(LayoutList list)
    {
        bool ordered = IsOrdered(list);
        bool description = list.Numbering == "Description";
        if (_indents.Count > 0)
        {
            _tightNext = true;
        }

        bool firstItem = true;
        int number = 1;
        foreach (LayoutNode child in list.Children)
        {
            if (child.Kind != LayoutNodeKind.ListItem)
            {
                Block(child);
                firstItem = true;
                continue;
            }

            string marker = ordered ? string.Create(CultureInfo.InvariantCulture, $"{number}. ") : "- ";
            if (!firstItem)
            {
                _tightNext = true;
            }

            _indents.Add(new string(' ', marker.Length));
            _marker = marker;
            Container(child, skipLabels: !description);
            _indents.RemoveAt(_indents.Count - 1);
            if (_marker is null)
            {
                number++;
                firstItem = false;
            }

            // An item that showed nothing never wrote its marker.
            _marker = null;
        }

        _tightNext = false;
    }

    private void Figure(LayoutNode figure)
    {
        string? alternate = figure.AlternateDescription is { } text && !string.IsNullOrWhiteSpace(text) ? Collapse(text) : null;
        int page = FirstPage(figure);
        if (_options.IncludeImages)
        {
            int number = _figures.TryGetValue(page, out int count) ? count + 1 : 1;
            _figures[page] = number;
            StartBlock(page);
            Raw("![");
            Escaped(alternate ?? string.Empty);
            Raw(page >= 0
                ? string.Create(CultureInfo.InvariantCulture, $"](page-{page + 1}-figure-{number})")
                : string.Create(CultureInfo.InvariantCulture, $"](figure-{number})"));
        }
        else if (alternate is not null)
        {
            StartBlock(page);
            Raw("*");
            Escaped(alternate);
            Raw("*");
        }

        foreach (LayoutNode child in figure.Children)
        {
            if (child.Kind is LayoutNodeKind.Caption or LayoutNodeKind.Figure or LayoutNodeKind.Table or LayoutNodeKind.List)
            {
                Block(child);
            }
        }
    }

    private void NoteDefinition(LayoutNode note)
    {
        if (note.Text.Length == 0)
        {
            return;
        }

        int number = NoteNumber(note);
        BeginInline(Inline.Block, string.Create(CultureInfo.InvariantCulture, $"[^{number}]: "));
        _inlines.Walk(note, this);
        EndInline();
    }

    private void Footnotes()
    {
        for (int index = 0; index < _notes.Count; index++)
        {
            LayoutNode note = _notes[index];
            BeginInline(Inline.Block, string.Create(CultureInfo.InvariantCulture, $"[^{_noteNumbers[note]}]: "));
            _inlines.Walk(note, this);
            EndInline();
        }
    }

    private int NoteNumber(LayoutNode note)
    {
        if (!_noteNumbers.TryGetValue(note, out int number))
        {
            number = _noteNumbers.Count + 1;
            _noteNumbers.Add(note, number);
        }

        return number;
    }

    private void Table(LayoutTable table)
    {
        LayoutNode root = RootOf(table);
        if (table.PreviousPart is { } previous && ReferenceEquals(RootOf(previous), root))
        {
            // Written with the part it continues.
            return;
        }

        // The parts of a table split over pages are one grid: each later part's rows follow, without the header rows it repeats.
        var found = new List<(LayoutTableCell Cell, int Row, int Column)>();
        var captionsBefore = new List<LayoutNode>();
        var captionsAfter = new List<LayoutNode>();
        int rows = 0;
        int columns = 0;
        bool firstPart = true;
        for (LayoutTable? part = table; part is not null; part = part.NextPart is { } next && ReferenceEquals(RootOf(next), root) ? next : null)
        {
            int skip = 0;
            if (!firstPart)
            {
                foreach (LayoutNode row in part.Children)
                {
                    if (row.Kind != LayoutNodeKind.TableRow || row.Children.Count == 0
                        || !row.Children.All(static cell => cell is LayoutTableCell { IsHeader: true }))
                    {
                        break;
                    }

                    skip++;
                }
            }

            int partRows = part.RowCount;
            bool seenRow = false;
            foreach (LayoutNode child in part.Children)
            {
                if (child.Kind == LayoutNodeKind.Caption)
                {
                    (seenRow ? captionsAfter : captionsBefore).Add(child);
                    continue;
                }

                if (child.Kind != LayoutNodeKind.TableRow)
                {
                    continue;
                }

                seenRow = true;
                foreach (LayoutNode node in child.Children)
                {
                    if (node is LayoutTableCell { Row: >= 0, Column: >= 0 } cell && cell.Row >= skip)
                    {
                        int row = rows + cell.Row - skip;
                        found.Add((cell, row, cell.Column));
                        columns = Math.Max(columns, cell.Column + cell.ColumnSpan);
                        partRows = Math.Max(partRows, cell.Row + cell.RowSpan);
                    }
                }
            }

            rows += Math.Max(0, partRows - skip);
            firstPart = false;
        }

        List<(LayoutTableCell Cell, int Row, int Column)> cells = [.. found.OrderBy(static entry => entry.Row).ThenBy(static entry => entry.Column)];
        foreach (LayoutNode caption in captionsBefore)
        {
            Container(caption, skipLabels: false);
        }

        if (rows > 0 && columns > 0)
        {
            LayoutTableCell?[][] grid = [.. Enumerable.Range(0, rows).Select(_ => new LayoutTableCell?[columns])];
            bool spans = false;
            foreach ((LayoutTableCell cell, int row, int column) in cells)
            {
                spans |= cell.RowSpan > 1 || cell.ColumnSpan > 1;
                for (int r = row; r < Math.Min(rows, row + cell.RowSpan); r++)
                {
                    for (int c = column; c < Math.Min(columns, column + cell.ColumnSpan); c++)
                    {
                        grid[r][c] ??= cell;
                    }
                }
            }

            bool header = cells.Any(static entry => entry.Row == 0 && entry.Cell.IsHeader);
            if ((spans || !header) && _options.Tables == MarkdownTableStyle.Html)
            {
                HtmlTable(table, cells, rows);
            }
            else
            {
                PipeTable(table, grid, rows, columns, spans, header);
            }
        }

        foreach (LayoutNode caption in captionsAfter)
        {
            Container(caption, skipLabels: false);
        }
    }

    private void PipeTable(LayoutTable table, LayoutTableCell?[][] grid, int rows, int columns, bool spans, bool header)
    {
        StartBlock(FirstPage(table));
        if (spans)
        {
            Raw("<!-- merged cells: a spanned cell's text is repeated in each cell it covers -->");
            NewLine();
        }

        if (!header)
        {
            Raw("<!-- no header row: the first row is written as the header -->");
            NewLine();
        }

        for (int row = 0; row < rows; row++)
        {
            if (row > 0)
            {
                NewLine();
            }

            Raw("|");
            for (int column = 0; column < columns; column++)
            {
                Raw(" ");
                if (grid[row][column] is { } cell)
                {
                    Cell(cell, html: false);
                }

                Raw(" |");
            }

            if (row == 0)
            {
                NewLine();
                Raw("|");
                for (int column = 0; column < columns; column++)
                {
                    Raw(" --- |");
                }
            }
        }
    }

    private void HtmlTable(LayoutTable table, List<(LayoutTableCell Cell, int Row, int Column)> cells, int rows)
    {
        StartBlock(FirstPage(table));
        Raw("<table>");
        int next = 0;
        for (int row = 0; row < rows; row++)
        {
            NewLine();
            Raw("<tr>");
            while (next < cells.Count && cells[next].Row < row)
            {
                next++;
            }

            for (; next < cells.Count && cells[next].Row == row; next++)
            {
                LayoutTableCell cell = cells[next].Cell;
                string tag = cell.IsHeader ? "th" : "td";
                Raw("<");
                Raw(tag);
                if (cell.RowSpan > 1)
                {
                    Raw(string.Create(CultureInfo.InvariantCulture, $" rowspan=\"{Math.Min(cell.RowSpan, rows - row)}\""));
                }

                if (cell.ColumnSpan > 1)
                {
                    Raw(string.Create(CultureInfo.InvariantCulture, $" colspan=\"{cell.ColumnSpan}\""));
                }

                Raw(">");
                Cell(cell, html: true);
                Raw("</");
                Raw(tag);
                Raw(">");
            }

            Raw("</tr>");
        }

        NewLine();
        Raw("</table>");
    }

    private void Cell(LayoutTableCell cell, bool html)
    {
        Inline saved = _inline;
        _inline = Inline.Cell;
        _html = html;
        _blockStarted = true;
        _atTextStart = true;
        _inlines.Reset();
        _inlines.Walk(cell, this);
        CloseEmphasis();
        CloseLink();
        _html = false;
        _inline = saved;
    }

    private void BeginInline(Inline kind, string? leader)
    {
        _inline = kind;
        _leader = leader;
        _blockStarted = false;
        _atTextStart = true;
        _emphasis = InlineEmphasis.None;
        _link = null;
        _uri = null;
        _plain = false;
        _inlines.Reset();
    }

    private void StartInline(int page)
    {
        if (_blockStarted)
        {
            return;
        }

        _blockStarted = true;
        StartBlock(page);
        if (_leader is not null)
        {
            Raw(_leader);
        }
    }

    private void EndInline()
    {
        CloseEmphasis();
        CloseLink();
        _inline = Inline.None;
        _leader = null;
        _plain = false;
    }

    /// <summary>Starts a block: ends the line before, a blank line between blocks, and the page comment when the page changes.</summary>
    private void StartBlock(int page)
    {
        if (_options.IncludePageBreaks && page >= 0 && page != _lastPage && _indents.Count == 0)
        {
            Separate();
            Raw(string.Create(CultureInfo.InvariantCulture, $"<!-- page {page + 1} -->"));
            _wroteBlock = true;
        }

        if (page >= 0)
        {
            _lastPage = page;
        }

        Separate();
        _wroteBlock = true;
        _atTextStart = true;
    }

    private void Separate()
    {
        if (_wroteBlock)
        {
            EndLine();
            if (!_tightNext)
            {
                _writer.Write('\n');
            }
        }

        _tightNext = false;
    }

    private void EndLine()
    {
        if (!_atLineStart)
        {
            _writer.Write('\n');
            _atLineStart = true;
        }
    }

    private void NewLine()
    {
        _writer.Write('\n');
        _atLineStart = true;
    }

    private void Raw(string text) => Raw(text.AsSpan());

    private void Raw(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return;
        }

        if (_atLineStart)
        {
            for (int index = 0; index < _indents.Count; index++)
            {
                if (index == _indents.Count - 1 && _marker is not null)
                {
                    _writer.Write(_marker);
                    _marker = null;
                }
                else
                {
                    _writer.Write(_indents[index]);
                }
            }

            _atLineStart = false;
        }

        _writer.Write(text);
    }

    private void OpenLink(LayoutNode? link, string? uri)
    {
        _link = link;
        _uri = uri;
        if (link is not null)
        {
            Raw(_html ? "<a href=\"" : "[");
            if (_html)
            {
                Html(uri!.AsSpan());
                Raw("\">");
            }
        }
    }

    private void CloseLink()
    {
        if (_link is null)
        {
            return;
        }

        CloseEmphasis();
        if (_html)
        {
            Raw("</a>");
        }
        else
        {
            Raw("](");
            Destination(_uri!);
            Raw(")");
        }

        _link = null;
        _uri = null;
    }

    private void OpenEmphasis(InlineEmphasis emphasis)
    {
        if (emphasis == _emphasis)
        {
            return;
        }

        CloseEmphasis();
        _emphasis = emphasis;
        if (_html)
        {
            Raw((emphasis & InlineEmphasis.Bold) != 0 ? "<strong>" : string.Empty);
            Raw((emphasis & InlineEmphasis.Italic) != 0 ? "<em>" : string.Empty);
        }
        else
        {
            Raw(Marker(emphasis));
        }
    }

    private void CloseEmphasis()
    {
        if (_emphasis == InlineEmphasis.None)
        {
            return;
        }

        if (_html)
        {
            Raw((_emphasis & InlineEmphasis.Italic) != 0 ? "</em>" : string.Empty);
            Raw((_emphasis & InlineEmphasis.Bold) != 0 ? "</strong>" : string.Empty);
        }
        else
        {
            Raw(Marker(_emphasis));
        }

        _emphasis = InlineEmphasis.None;
    }

    private static string Marker(InlineEmphasis emphasis) => emphasis switch
    {
        InlineEmphasis.Bold => "**",
        InlineEmphasis.Italic => "*",
        InlineEmphasis.Bold | InlineEmphasis.Italic => "***",
        _ => string.Empty,
    };

    // CommonMark 0.31 §6.3 (link destinations): spaces, angle brackets and parentheses are percent-encoded, so the destination needs no escaping.
    private void Destination(string uri)
    {
        int start = 0;
        for (int index = 0; index < uri.Length; index++)
        {
            string? encoded = uri[index] switch
            {
                ' ' => "%20",
                '<' => "%3C",
                '>' => "%3E",
                '(' => "%28",
                ')' => "%29",
                '\\' => "%5C",
                _ when char.IsControl(uri[index]) || char.IsWhiteSpace(uri[index]) => "%20",
                _ => null,
            };
            if (encoded is not null)
            {
                Raw(uri.AsSpan(start, index - start));
                Raw(encoded);
                start = index + 1;
            }
        }

        Raw(uri.AsSpan(start));
    }

    private void Escaped(ReadOnlySpan<char> text)
    {
        if (_html)
        {
            Html(text);
            _atTextStart = false;
            return;
        }

        // A line-start construct (CommonMark 0.31 §4.2 thematic breaks, §4.2 ATX headings, §5.1 block quotes, §5.2 list items) can
        // only begin a paragraph's first word; elsewhere ASCII punctuation that opens inlines is backslash-escaped (§2.4).
        if (_atTextStart && _leader is null && _inline == Inline.Block)
        {
            int digits = 0;
            while (digits < text.Length && char.IsAsciiDigit(text[digits]))
            {
                digits++;
            }

            if (digits > 0 && digits < text.Length && digits <= 9 && text[digits] is '.' or ')')
            {
                Raw(text[..digits]);
                Raw("\\");
                text = text[digits..];
            }
            else if (text[0] is '#' or '-' or '+' or '>' or '=')
            {
                Raw("\\");
            }
        }
        else if (_leader is { Length: > 0 } leader && leader[0] == '#' && !text.ContainsAnyExcept('#'))
        {
            // A heading word of only number signs would close the heading (§4.2).
            Raw("\\");
        }

        _atTextStart = false;
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            char unit = text[index];
            bool escape = unit is '\\' or '`' or '*' or '[' or ']' or '<' or '~'
                || (unit == '|' && _inline == Inline.Cell)
                || (unit == '_' && (index == 0 || index == text.Length - 1 || !char.IsLetterOrDigit(text[index - 1]) || !char.IsLetterOrDigit(text[index + 1])))
                || (unit == '&' && IsEntity(text[(index + 1)..]));
            bool control = char.IsControl(unit);
            if (escape || control)
            {
                Raw(text[start..index]);
                Raw(control ? " " : "\\");
                start = control ? index + 1 : index;
            }
        }

        Raw(text[start..]);
    }

    // CommonMark 0.31 §2.5: "&name;" and "&#digits;" are entity and numeric character references; a bare ampersand is text.
    private static bool IsEntity(ReadOnlySpan<char> after)
    {
        int end = after.IndexOf(';');
        return end > 0 && !after[..end].ContainsAnyExcept(EntityCharacters);
    }

    private void Html(ReadOnlySpan<char> text)
    {
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            string? entity = text[index] switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ when char.IsControl(text[index]) => " ",
                _ => null,
            };
            if (entity is not null)
            {
                Raw(text[start..index]);
                Raw(entity);
                start = index + 1;
            }
        }

        Raw(text[start..]);
    }
}
