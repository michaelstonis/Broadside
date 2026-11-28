using System.Text;

namespace Broadside.Layout.Flow;

/// <summary>
/// Parses Markdown into a syntax tree: CommonMark 0.31 blocks line by line, then each paragraph's and heading's inlines, and
/// optionally the GFM table, strikethrough, autolink and task list extensions. Every string parses; the work is linear in the length
/// of the input. A parser instance parses one document; nothing is shared between calls.
/// </summary>
/// <remarks>The algorithm is the CommonMark reference implementation's (commonmark.js): open blocks matched from the root on each line, new block starts tried on what is left, and lazy paragraph continuation.</remarks>
internal sealed class MarkdownParser
{
    private const int CodeIndent = 4;

    private readonly bool _gfm;
    private readonly Dictionary<string, (string Destination, string Title)> _references = new(StringComparer.Ordinal);
    private readonly MarkdownNode _document = new(MarkdownKind.Document) { StartLine = 1 };
    private MarkdownNode _tip;
    private MarkdownNode _oldTip;
    private MarkdownNode _lastMatchedContainer;
    private string _line = string.Empty;
    private int _lineNumber;
    private int _offset;
    private int _column;
    private int _nextNonspace;
    private int _nextNonspaceColumn;
    private int _indent;
    private bool _indented;
    private bool _blank;
    private bool _partiallyConsumedTab;
    private bool _allClosed = true;
    private int _spacesOnlyFrom = -1;
    private int _spacesOnlyTo;

    private MarkdownParser(bool gfm)
    {
        _gfm = gfm;
        _tip = _document;
        _oldTip = _document;
        _lastMatchedContainer = _document;
    }

    /// <summary>Parses <paramref name="markdown"/> into a document node.</summary>
    /// <param name="markdown">The Markdown.</param>
    /// <param name="gitHubExtensions">Whether the GFM tables, strikethrough, autolinks and task lists are recognized.</param>
    public static MarkdownNode Parse(string markdown, bool gitHubExtensions)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var parser = new MarkdownParser(gitHubExtensions);
        return parser.ParseDocument(markdown);
    }

    private MarkdownNode ParseDocument(string markdown)
    {
        if (markdown.Contains('\0', StringComparison.Ordinal))
        {
            markdown = markdown.Replace('\0', '�');
        }

        int start = 0;
        while (start < markdown.Length)
        {
            int end = markdown.AsSpan(start).IndexOfAny('\r', '\n');
            if (end < 0)
            {
                IncorporateLine(markdown[start..]);
                break;
            }

            IncorporateLine(markdown.Substring(start, end));
            start += end + (markdown[start + end] == '\r' && start + end + 1 < markdown.Length && markdown[start + end + 1] == '\n' ? 2 : 1);
        }

        while (_tip is { } tip && !ReferenceEquals(tip, _document))
        {
            Finalize(tip, _lineNumber);
        }

        Finalize(_document, _lineNumber);
        ProcessInlines();
        return _document;
    }

    private void ProcessInlines()
    {
        var inlines = new MarkdownInlineParser(_references, _gfm);
        foreach ((MarkdownNode node, bool entering) in _document.Walk())
        {
            if (entering && node.Kind is MarkdownKind.Paragraph or MarkdownKind.Heading or MarkdownKind.TableCell)
            {
                if (_gfm && node.Kind == MarkdownKind.Paragraph && node.Previous is null && node.Parent is { Kind: MarkdownKind.Item } item)
                {
                    TaskMarker(item, node);
                }

                inlines.Parse(node);
            }
        }

        if (_gfm)
        {
            MarkdownAutolinks.Apply(_document);
        }
    }

    /// <summary>A GFM task list item: <c>[ ]</c>, <c>[x]</c> or <c>[X]</c> and white space at the start of the item's first paragraph, taken off it.</summary>
    private static void TaskMarker(MarkdownNode item, MarkdownNode paragraph)
    {
        StringBuilder content = paragraph.Content!;
        if (content.Length >= 4 && content[0] == '[' && content[1] is ' ' or '\t' or 'x' or 'X' && content[2] == ']' && MarkdownText.IsAsciiWhitespace(content[3]))
        {
            item.TaskChecked = content[1] is 'x' or 'X';
            content.Remove(0, 4);
        }
    }

    private static char Peek(string line, int index) => index < line.Length ? line[index] : '\0';

    private void IncorporateLine(string line)
    {
        bool allMatched = true;
        MarkdownNode container = _document;
        _oldTip = _tip;
        _offset = 0;
        _column = 0;
        _blank = false;
        _partiallyConsumedTab = false;
        _lineNumber++;
        _line = line;
        _spacesOnlyFrom = -1;

        while (container.LastChild is { IsOpen: true } lastChild)
        {
            container = lastChild;
            FindNextNonspace();
            switch (Continue(container))
            {
                case 0:
                    break;
                case 1:
                    allMatched = false;
                    break;
                default:
                    return;
            }

            if (!allMatched)
            {
                container = container.Parent!;
                break;
            }
        }

        _allClosed = ReferenceEquals(container, _oldTip);
        _lastMatchedContainer = container;
        bool matchedLeaf = container.Kind is not (MarkdownKind.Paragraph or MarkdownKind.Table) && AcceptsLines(container.Kind);
        while (!matchedLeaf)
        {
            FindNextNonspace();
            if (!_indented && !MaybeSpecial(Peek(_line, _nextNonspace)))
            {
                AdvanceNextNonspace();
                break;
            }

            int result = TryBlockStarts(container);
            if (result == 1)
            {
                container = _tip;
            }
            else if (result == 2)
            {
                container = _tip;
                matchedLeaf = true;
            }
            else
            {
                AdvanceNextNonspace();
                break;
            }
        }

        if (!_allClosed && !_blank && _tip.Kind == MarkdownKind.Paragraph)
        {
            // Lazy paragraph continuation.
            AddLine();
        }
        else
        {
            CloseUnmatchedBlocks();
            MarkdownKind kind = container.Kind;
            if (AcceptsLines(kind))
            {
                AddLine();
                if (kind == MarkdownKind.HtmlBlock && container.HtmlBlockType is >= 1 and <= 5
                    && MarkdownHtml.BlockEnds(container.HtmlBlockType, _line.AsSpan(_offset)))
                {
                    Finalize(container, _lineNumber);
                }
            }
            else if (_offset < _line.Length && !_blank)
            {
                container = AddChild(MarkdownKind.Paragraph);
                AdvanceNextNonspace();
                AddLine();
            }
        }
    }

    private static bool MaybeSpecial(char c) => c is '#' or '`' or '~' or '*' or '+' or '_' or '=' or '<' or '>' or '-' or '|' or ':' or (>= '0' and <= '9');

    private static bool AcceptsLines(MarkdownKind kind) => kind is MarkdownKind.Paragraph or MarkdownKind.CodeBlock or MarkdownKind.HtmlBlock or MarkdownKind.Table;

    private static bool CanContain(MarkdownKind parent, MarkdownKind child) => parent switch
    {
        MarkdownKind.Document or MarkdownKind.BlockQuote or MarkdownKind.Item => child != MarkdownKind.Item,
        MarkdownKind.List => child == MarkdownKind.Item,
        _ => false,
    };

    /// <summary>Whether <paramref name="container"/> continues on this line: 0 it does, 1 it does not, 2 the line is consumed (a closing fence).</summary>
    private int Continue(MarkdownNode container)
    {
        switch (container.Kind)
        {
            case MarkdownKind.BlockQuote:
                if (!_indented && Peek(_line, _nextNonspace) == '>')
                {
                    AdvanceNextNonspace();
                    AdvanceOffset(1, false);
                    if (MarkdownText.IsSpaceOrTab(Peek(_line, _offset)))
                    {
                        AdvanceOffset(1, true);
                    }

                    return 0;
                }

                return 1;
            case MarkdownKind.Item:
                MarkdownListData data = container.ListData!;
                if (_blank)
                {
                    if (container.FirstChild is null)
                    {
                        return 1;
                    }

                    AdvanceNextNonspace();
                }
                else if (_indent >= data.MarkerOffset + data.Padding)
                {
                    AdvanceOffset(data.MarkerOffset + data.Padding, true);
                }
                else
                {
                    return 1;
                }

                return 0;
            case MarkdownKind.Heading or MarkdownKind.ThematicBreak:
                return 1;
            case MarkdownKind.CodeBlock:
                if (container.IsFenced)
                {
                    if (_indent <= 3 && Peek(_line, _nextNonspace) == container.FenceChar)
                    {
                        int fence = 0;
                        while (Peek(_line, _nextNonspace + fence) == container.FenceChar)
                        {
                            fence++;
                        }

                        if (fence >= container.FenceLength && MarkdownText.IsBlank(_line.AsSpan(_nextNonspace + fence)))
                        {
                            Finalize(container, _lineNumber);
                            return 2;
                        }
                    }

                    for (int i = container.FenceOffset; i > 0 && MarkdownText.IsSpaceOrTab(Peek(_line, _offset)); i--)
                    {
                        AdvanceOffset(1, true);
                    }
                }
                else if (_indent >= CodeIndent)
                {
                    AdvanceOffset(CodeIndent, true);
                }
                else if (_blank)
                {
                    AdvanceNextNonspace();
                }
                else
                {
                    return 1;
                }

                return 0;
            case MarkdownKind.HtmlBlock:
                return _blank && container.HtmlBlockType is 6 or 7 ? 1 : 0;
            case MarkdownKind.Paragraph or MarkdownKind.Table:
                return _blank ? 1 : 0;
            default:
                return 0;
        }
    }

    /// <summary>Tries each block start on the rest of the line: 0 none, 1 a container started, 2 a leaf started.</summary>
    private int TryBlockStarts(MarkdownNode container)
    {
        ReadOnlySpan<char> rest = _line.AsSpan(_nextNonspace);
        if (_indented)
        {
            if (container.Kind == MarkdownKind.List && ListItemStart(container) == 1)
            {
                return 1;
            }

            // An indented code block; it cannot interrupt a paragraph.
            if (_tip.Kind != MarkdownKind.Paragraph && !_blank)
            {
                AdvanceOffset(CodeIndent, true);
                CloseUnmatchedBlocks();
                _ = AddChild(MarkdownKind.CodeBlock);
                return 2;
            }

            return 0;
        }

        char first = Peek(_line, _nextNonspace);

        // A block quote.
        if (first == '>')
        {
            AdvanceNextNonspace();
            AdvanceOffset(1, false);
            if (MarkdownText.IsSpaceOrTab(Peek(_line, _offset)))
            {
                AdvanceOffset(1, true);
            }

            CloseUnmatchedBlocks();
            _ = AddChild(MarkdownKind.BlockQuote);
            return 1;
        }

        // An ATX heading.
        if (first == '#')
        {
            int level = 0;
            while (level < rest.Length && rest[level] == '#')
            {
                level++;
            }

            if (level <= 6 && (level == rest.Length || MarkdownText.IsSpaceOrTab(rest[level])))
            {
                AdvanceNextNonspace();
                AdvanceOffset(level, false);
                CloseUnmatchedBlocks();
                MarkdownNode heading = AddChild(MarkdownKind.Heading);
                heading.Level = level;
                heading.Content = new StringBuilder(AtxContent(_line.AsSpan(_offset)).ToString());
                AdvanceOffset(_line.Length - _offset, false);
                return 2;
            }
        }

        // A fenced code block.
        if (first is '`' or '~')
        {
            int fence = 0;
            while (fence < rest.Length && rest[fence] == first)
            {
                fence++;
            }

            if (fence >= 3 && (first == '~' || !rest[fence..].Contains('`')))
            {
                CloseUnmatchedBlocks();
                MarkdownNode code = AddChild(MarkdownKind.CodeBlock);
                code.IsFenced = true;
                code.FenceLength = fence;
                code.FenceChar = first;
                code.FenceOffset = _indent;
                AdvanceNextNonspace();
                AdvanceOffset(fence, false);
                return 2;
            }
        }

        // An HTML block.
        if (first == '<')
        {
            bool lazy = !_allClosed && !_blank && _tip.Kind == MarkdownKind.Paragraph;
            int kind = MarkdownHtml.BlockStart(rest, container.Kind != MarkdownKind.Paragraph && !lazy);
            if (kind > 0)
            {
                CloseUnmatchedBlocks();
                MarkdownNode html = AddChild(MarkdownKind.HtmlBlock);
                html.HtmlBlockType = kind;
                return 2;
            }
        }

        // A GFM table: a delimiter row under a paragraph whose last line is a header row with as many cells.
        if (_gfm && container.Kind == MarkdownKind.Paragraph && TableStart(container))
        {
            return 2;
        }

        // A setext heading underline.
        if (container.Kind == MarkdownKind.Paragraph && first is '=' or '-' && IsSetextUnderline(rest))
        {
            CloseUnmatchedBlocks();
            string content = StripReferences(container.Content!.ToString());
            if (content.Length > 0)
            {
                var heading = new MarkdownNode(MarkdownKind.Heading)
                {
                    StartLine = container.StartLine,
                    Level = first == '=' ? 1 : 2,
                    Content = new StringBuilder(content),
                };
                container.InsertAfter(heading);
                container.Unlink();
                _tip = heading;
                AdvanceOffset(_line.Length - _offset, false);
                return 2;
            }

            container.Content = new StringBuilder(content);
        }

        // A thematic break.
        if (first is '*' or '_' or '-' && IsThematicBreak(rest))
        {
            CloseUnmatchedBlocks();
            _ = AddChild(MarkdownKind.ThematicBreak);
            AdvanceOffset(_line.Length - _offset, false);
            return 2;
        }

        // A list item.
        return ListItemStart(container);
    }

    private int ListItemStart(MarkdownNode container)
    {
        if (ParseListMarker(container) is not { } data)
        {
            return 0;
        }

        CloseUnmatchedBlocks();
        if (_tip.Kind != MarkdownKind.List || !_tip.ListData!.Matches(data))
        {
            MarkdownNode list = AddChild(MarkdownKind.List);
            list.ListData = data;
        }

        MarkdownNode item = AddChild(MarkdownKind.Item);
        item.ListData = data;
        return 1;
    }

    private MarkdownListData? ParseListMarker(MarkdownNode container)
    {
        if (_indent >= CodeIndent)
        {
            return null;
        }

        ReadOnlySpan<char> rest = _line.AsSpan(_nextNonspace);
        var data = new MarkdownListData { MarkerOffset = _indent };
        int markerLength;
        if (rest.Length > 0 && rest[0] is '*' or '+' or '-')
        {
            data.BulletChar = rest[0];
            markerLength = 1;
        }
        else
        {
            int digits = 0;
            while (digits < rest.Length && digits < 10 && char.IsAsciiDigit(rest[digits]))
            {
                digits++;
            }

            if (digits is 0 or > 9 || digits >= rest.Length || rest[digits] is not ('.' or ')'))
            {
                return null;
            }

            int start = int.Parse(rest[..digits], System.Globalization.CultureInfo.InvariantCulture);
            if (container.Kind == MarkdownKind.Paragraph && start != 1)
            {
                return null;
            }

            data.Ordered = true;
            data.Start = start;
            data.Delimiter = rest[digits];
            markerLength = digits + 1;
        }

        char after = Peek(_line, _nextNonspace + markerLength);
        if (after is not ('\0' or ' ' or '\t'))
        {
            return null;
        }

        if (container.Kind == MarkdownKind.Paragraph && MarkdownText.IsBlank(_line.AsSpan(_nextNonspace + markerLength)))
        {
            return null;
        }

        AdvanceNextNonspace();
        AdvanceOffset(markerLength, true);
        int spacesStartColumn = _column;
        int spacesStartOffset = _offset;
        char next;
        do
        {
            AdvanceOffset(1, true);
            next = Peek(_line, _offset);
        }
        while (_column - spacesStartColumn < 5 && MarkdownText.IsSpaceOrTab(next));

        bool blankItem = _offset >= _line.Length;
        int spacesAfterMarker = _column - spacesStartColumn;
        if (spacesAfterMarker >= 5 || spacesAfterMarker < 1 || blankItem)
        {
            data.Padding = markerLength + 1;
            _column = spacesStartColumn;
            _offset = spacesStartOffset;
            if (MarkdownText.IsSpaceOrTab(Peek(_line, _offset)))
            {
                AdvanceOffset(1, true);
            }
        }
        else
        {
            data.Padding = markerLength + spacesAfterMarker;
        }

        return data;
    }

    private bool TableStart(MarkdownNode paragraph)
    {
        ReadOnlySpan<char> rest = _line.AsSpan(_nextNonspace);
        if (MarkdownTables.DelimiterRow(rest) is not { } alignments)
        {
            return false;
        }

        string content = paragraph.Content!.ToString();
        ReadOnlySpan<char> lines = content.AsSpan().TrimEnd('\n');
        int lastBreak = lines.LastIndexOf('\n');
        ReadOnlySpan<char> header = lines[(lastBreak + 1)..];
        List<string> cells = MarkdownTables.SplitRow(header);
        if (cells.Count != alignments.Length || (!header.Contains('|') && !rest.Contains('|')))
        {
            return false;
        }

        CloseUnmatchedBlocks();
        var table = new MarkdownNode(MarkdownKind.Table) { StartLine = _lineNumber - 1, Alignments = alignments, Content = new StringBuilder() };
        table.Content.Append(header).Append('\n');
        if (lastBreak < 0)
        {
            paragraph.InsertAfter(table);
            paragraph.Unlink();
        }
        else
        {
            paragraph.Content = new StringBuilder(content, 0, lastBreak + 1, lastBreak + 1);
            Finalize(paragraph, _lineNumber - 2);
            paragraph.InsertAfter(table);
        }

        _tip = table;
        AdvanceOffset(_line.Length - _offset, false);
        return true;
    }

    private static ReadOnlySpan<char> AtxContent(ReadOnlySpan<char> text)
    {
        // A closing sequence of #s is dropped when it is all there is, or follows a space or tab.
        ReadOnlySpan<char> trimmed = text.TrimEnd(" \t");
        int hashes = trimmed.Length;
        while (hashes > 0 && trimmed[hashes - 1] == '#')
        {
            hashes--;
        }

        if (hashes < trimmed.Length && (hashes == 0 || MarkdownText.IsSpaceOrTab(trimmed[hashes - 1])))
        {
            trimmed = trimmed[..hashes];
        }

        return trimmed.Trim(" \t");
    }

    private static bool IsSetextUnderline(ReadOnlySpan<char> rest)
    {
        char c = rest[0];
        int index = 0;
        while (index < rest.Length && rest[index] == c)
        {
            index++;
        }

        return MarkdownText.IsBlank(rest[index..]);
    }

    private static bool IsThematicBreak(ReadOnlySpan<char> rest)
    {
        char c = rest[0];
        int count = 0;
        foreach (char ch in rest)
        {
            if (ch == c)
            {
                count++;
            }
            else if (!MarkdownText.IsSpaceOrTab(ch))
            {
                return false;
            }
        }

        return count >= 3;
    }

    /// <summary>Removes the link reference definitions at the start of a paragraph's text, recording them; returns what is left.</summary>
    private string StripReferences(string content)
    {
        while (content.Length > 0 && content[0] == '[')
        {
            int consumed = MarkdownInlineParser.ParseReference(content, _references);
            if (consumed == 0)
            {
                break;
            }

            content = content[consumed..];
        }

        return content;
    }

    private void Finalize(MarkdownNode block, int lineNumber)
    {
        MarkdownNode? above = block.Parent;
        block.IsOpen = false;
        block.EndLine = lineNumber;
        switch (block.Kind)
        {
            case MarkdownKind.Paragraph:
                string content = StripReferences(block.Content!.ToString());
                block.Content = new StringBuilder(content);
                if (MarkdownText.IsBlank(content))
                {
                    block.Unlink();
                }

                break;
            case MarkdownKind.CodeBlock:
                FinalizeCode(block);
                break;
            case MarkdownKind.HtmlBlock:
                block.Literal = TrimTrailingBlankLines(block.Content!.ToString());
                block.Content = null;
                break;
            case MarkdownKind.Table:
                MarkdownTables.Build(block);
                break;
            case MarkdownKind.Item:
                block.EndLine = block.LastChild is { } last ? last.EndLine : block.StartLine;
                break;
            case MarkdownKind.List:
                FinalizeList(block);
                break;
        }

        _tip = above ?? _document;
    }

    /// <summary><paramref name="text"/> without its trailing line endings and the spaces after them.</summary>
    private static string TrimTrailingBlankLines(string text)
    {
        int end = text.Length;
        while (true)
        {
            int spaces = end;
            while (spaces > 0 && text[spaces - 1] == ' ')
            {
                spaces--;
            }

            if (spaces == 0 || text[spaces - 1] != '\n')
            {
                break;
            }

            end = spaces - 1;
        }

        return text[..end];
    }

    private static void FinalizeCode(MarkdownNode block)
    {
        string content = block.Content!.ToString();
        block.Content = null;
        if (block.IsFenced)
        {
            int newline = content.IndexOf('\n', StringComparison.Ordinal);
            string firstLine = newline < 0 ? content : content[..newline];
            block.Info = MarkdownText.Unescape(firstLine.Trim(' ', '\t', '\n', '\r', '\f', '\v'));
            block.Literal = newline < 0 ? string.Empty : content[(newline + 1)..];
            return;
        }

        // An indented block drops its trailing blank lines, and ends on its last line with content.
        string[] lines = content.Split('\n');
        int count = lines.Length;
        while (count > 0 && MarkdownText.IsBlank(lines[count - 1]))
        {
            count--;
        }

        block.Literal = string.Join('\n', lines, 0, count) + "\n";
        block.EndLine = block.StartLine + count - 1;
    }

    private static void FinalizeList(MarkdownNode list)
    {
        for (MarkdownNode? item = list.FirstChild; item is not null; item = item.Next)
        {
            if (EndsWithBlankLine(item))
            {
                list.ListData!.Tight = false;
                break;
            }

            bool loose = false;
            for (MarkdownNode? child = item.FirstChild; child is not null; child = child.Next)
            {
                if (EndsWithBlankLine(child))
                {
                    loose = true;
                    break;
                }
            }

            if (loose)
            {
                list.ListData!.Tight = false;
                break;
            }
        }

        list.EndLine = list.LastChild?.EndLine ?? list.StartLine;
    }

    /// <summary>Whether a blank line separates <paramref name="block"/> from its next sibling.</summary>
    private static bool EndsWithBlankLine(MarkdownNode block) => block.Next is { } next && block.EndLine != next.StartLine - 1;

    private MarkdownNode AddChild(MarkdownKind kind)
    {
        while (!CanContain(_tip.Kind, kind))
        {
            Finalize(_tip, _lineNumber - 1);
        }

        var block = new MarkdownNode(kind) { StartLine = _lineNumber };
        if (AcceptsLines(kind))
        {
            block.Content = new StringBuilder();
        }

        _tip.AppendChild(block);
        _tip = block;
        return block;
    }

    private void CloseUnmatchedBlocks()
    {
        if (_allClosed)
        {
            return;
        }

        while (!ReferenceEquals(_oldTip, _lastMatchedContainer))
        {
            MarkdownNode parent = _oldTip.Parent!;
            Finalize(_oldTip, _lineNumber - 1);
            _oldTip = parent;
        }

        _allClosed = true;
    }

    private void AddLine()
    {
        if (_partiallyConsumedTab)
        {
            _offset++;
            int charsToTab = 4 - (_column % 4);
            _tip.Content!.Append(' ', charsToTab);
        }

        _tip.Content!.Append(_line, _offset, Math.Max(0, _line.Length - _offset)).Append('\n');
    }

    private void FindNextNonspace()
    {
        int index = _offset;
        int columns = _column;
        if (!_partiallyConsumedTab && _spacesOnlyFrom >= 0 && _offset >= _spacesOnlyFrom && _offset <= _spacesOnlyTo)
        {
            // Only spaces lie between here and the last non-space found on this line, so deep nesting does not rescan them.
            columns += _spacesOnlyTo - _offset;
            index = _spacesOnlyTo;
        }
        else
        {
            bool tab = false;
            while (index < _line.Length)
            {
                char c = _line[index];
                if (c == ' ')
                {
                    index++;
                    columns++;
                }
                else if (c == '\t')
                {
                    tab = true;
                    index++;
                    columns += 4 - (columns % 4);
                }
                else
                {
                    break;
                }
            }

            _spacesOnlyFrom = tab ? -1 : _offset;
            _spacesOnlyTo = index;
        }

        _blank = index >= _line.Length;
        _nextNonspace = index;
        _nextNonspaceColumn = columns;
        _indent = _nextNonspaceColumn - _column;
        _indented = _indent >= CodeIndent;
    }

    private void AdvanceNextNonspace()
    {
        _offset = _nextNonspace;
        _column = _nextNonspaceColumn;
        _partiallyConsumedTab = false;
    }

    private void AdvanceOffset(int count, bool columns)
    {
        while (count > 0 && _offset < _line.Length)
        {
            char c = _line[_offset];
            if (c == '\t')
            {
                int charsToTab = 4 - (_column % 4);
                if (columns)
                {
                    _partiallyConsumedTab = charsToTab > count;
                    int charsToAdvance = charsToTab > count ? count : charsToTab;
                    _column += charsToAdvance;
                    _offset += _partiallyConsumedTab ? 0 : 1;
                    count -= charsToAdvance;
                }
                else
                {
                    _partiallyConsumedTab = false;
                    _column += charsToTab;
                    _offset++;
                    count--;
                }
            }
            else
            {
                _partiallyConsumedTab = false;
                _offset++;
                _column++;
                count--;
            }
        }
    }
}
