using System.Buffers;
using System.Text;

namespace Broadside.Layout.Flow;

/// <summary>
/// Parses the inlines of a paragraph, heading or table cell (CommonMark 0.31, "Inlines"): code spans, emphasis by the delimiter run
/// algorithm, links and images (inline, full, collapsed and shortcut references), autolinks, raw HTML, entities, escapes and line
/// breaks, and GFM strikethrough. Linear in the length of the text: code span closers are found from a table of run positions,
/// failed raw HTML closer searches are remembered, and once a link is made the link openers below it are already inactive.
/// </summary>
internal sealed class MarkdownInlineParser(Dictionary<string, (string Destination, string Title)> references, bool gfm)
{
    private static readonly SearchValues<char> Special = SearchValues.Create("\n`[]\\!<&*_");
    private static readonly SearchValues<char> SpecialGfm = SearchValues.Create("\n`[]\\!<&*_~");
    private readonly int[] _htmlNoClose = new int[6];
    private string _subject = string.Empty;
    private int _position;
    private Delimiter? _delimiters;
    private Bracket? _brackets;
    private Dictionary<int, int>? _lastBacktickRun;

    /// <summary>Parses <paramref name="block"/>'s text into its inline children.</summary>
    public void Parse(MarkdownNode block)
    {
        _subject = block.Content?.ToString().Trim(" \t\n\r".ToCharArray()) ?? string.Empty;
        block.Content = null;
        _position = 0;
        _delimiters = null;
        _brackets = null;
        _lastBacktickRun = null;
        Array.Clear(_htmlNoClose);
        while (ParseInline(block))
        {
        }

        ProcessEmphasis(null);
    }

    /// <summary>
    /// Parses a link reference definition at the start of <paramref name="text"/> into <paramref name="references"/> (the first
    /// definition of a label wins); returns its length, or 0 when the text does not start with one.
    /// </summary>
    public static int ParseReference(string text, Dictionary<string, (string Destination, string Title)> references)
    {
        var parser = new MarkdownInlineParser(references, gfm: false) { _subject = text };
        return parser.ParseReference();
    }

    private int ParseReference()
    {
        int start = _position;
        int labelLength = ParseLinkLabel();
        if (labelLength == 0)
        {
            return 0;
        }

        string rawLabel = _subject.Substring(start, labelLength);
        if (Peek() != ':')
        {
            _position = start;
            return 0;
        }

        _position++;
        SkipSpaceAndNewline();
        string? destination = ParseLinkDestination();
        if (destination is null)
        {
            _position = start;
            return 0;
        }

        int beforeTitle = _position;
        SkipSpaceAndNewline();
        string? title = null;
        if (_position != beforeTitle)
        {
            title = ParseLinkTitle();
        }

        if (title is null)
        {
            title = string.Empty;
            _position = beforeTitle;
        }

        bool atLineEnd = true;
        if (!SpaceAtEndOfLine())
        {
            if (title.Length == 0)
            {
                atLineEnd = false;
            }
            else
            {
                title = string.Empty;
                _position = beforeTitle;
                atLineEnd = SpaceAtEndOfLine();
            }
        }

        if (!atLineEnd)
        {
            _position = start;
            return 0;
        }

        string label = MarkdownText.NormalizeLabel(rawLabel.AsSpan(1, rawLabel.Length - 2));
        if (label.Length == 0)
        {
            _position = start;
            return 0;
        }

        references.TryAdd(label, (destination, title));
        return _position - start;
    }

    private char Peek() => _position < _subject.Length ? _subject[_position] : '\0';

    private bool AtEnd => _position >= _subject.Length;

    private static MarkdownNode Text(string text) => new(MarkdownKind.Text) { Literal = text, IsOpen = false };

    private bool ParseInline(MarkdownNode block)
    {
        if (AtEnd)
        {
            return false;
        }

        char c = _subject[_position];
        bool handled = c switch
        {
            '\n' => ParseNewline(block),
            '\\' => ParseBackslash(block),
            '`' => ParseBackticks(block),
            '*' or '_' => HandleDelimiter(c, block),
            '~' when gfm => HandleDelimiter(c, block),
            '[' => ParseOpenBracket(block),
            '!' => ParseBang(block),
            ']' => ParseCloseBracket(block),
            '<' => ParseAutolink(block) || ParseHtmlTag(block),
            '&' => ParseEntity(block),
            _ => ParseString(block),
        };

        if (!handled)
        {
            _position++;
            block.AppendChild(Text(c.ToString()));
        }

        return true;
    }

    private bool ParseString(MarkdownNode block)
    {
        int start = _position;
        ReadOnlySpan<char> rest = _subject.AsSpan(start);
        int end = rest.IndexOfAny(gfm ? SpecialGfm : Special);
        int length = end < 0 ? rest.Length : end;
        if (length == 0)
        {
            return false;
        }

        _position += length;
        block.AppendChild(Text(_subject.Substring(start, length)));
        return true;
    }

    private bool ParseNewline(MarkdownNode block)
    {
        _position++;
        if (block.LastChild is { Kind: MarkdownKind.Text } last && last.Literal.EndsWith(' '))
        {
            bool hard = last.Literal.Length >= 2 && last.Literal[^2] == ' ';
            last.Literal = last.Literal.TrimEnd(' ');
            block.AppendChild(new MarkdownNode(hard ? MarkdownKind.LineBreak : MarkdownKind.SoftBreak) { IsOpen = false });
        }
        else
        {
            block.AppendChild(new MarkdownNode(MarkdownKind.SoftBreak) { IsOpen = false });
        }

        while (Peek() == ' ')
        {
            _position++;
        }

        return true;
    }

    private bool ParseBackslash(MarkdownNode block)
    {
        _position++;
        if (Peek() == '\n')
        {
            _position++;
            block.AppendChild(new MarkdownNode(MarkdownKind.LineBreak) { IsOpen = false });
        }
        else if (!AtEnd && MarkdownText.IsEscapable(_subject[_position]))
        {
            block.AppendChild(Text(_subject[_position].ToString()));
            _position++;
        }
        else
        {
            block.AppendChild(Text("\\"));
        }

        return true;
    }

    private bool ParseBackticks(MarkdownNode block)
    {
        int start = _position;
        while (Peek() == '`')
        {
            _position++;
        }

        int ticks = _position - start;
        int afterOpen = _position;
        if (!HasBacktickRunAfter(ticks, afterOpen))
        {
            block.AppendChild(Text(_subject.Substring(start, ticks)));
            return true;
        }

        int index = afterOpen;
        while (index < _subject.Length)
        {
            int run = _subject.AsSpan(index).IndexOf('`');
            if (run < 0)
            {
                break;
            }

            int runStart = index + run;
            int runEnd = runStart;
            while (runEnd < _subject.Length && _subject[runEnd] == '`')
            {
                runEnd++;
            }

            if (runEnd - runStart == ticks)
            {
                string contents = _subject[afterOpen..runStart].Replace('\n', ' ');
                if (contents.Length >= 2 && contents[0] == ' ' && contents[^1] == ' ' && contents.AsSpan().ContainsAnyExcept(' '))
                {
                    contents = contents[1..^1];
                }

                block.AppendChild(new MarkdownNode(MarkdownKind.Code) { Literal = contents, IsOpen = false });
                _position = runEnd;
                return true;
            }

            index = runEnd;
        }

        block.AppendChild(Text(_subject.Substring(start, ticks)));
        return true;
    }

    /// <summary>Whether a run of exactly <paramref name="ticks"/> backticks starts after <paramref name="position"/>, from one scan of the subject.</summary>
    private bool HasBacktickRunAfter(int ticks, int position)
    {
        if (_lastBacktickRun is null)
        {
            _lastBacktickRun = [];
            int index = 0;
            while (index < _subject.Length)
            {
                int run = _subject.AsSpan(index).IndexOf('`');
                if (run < 0)
                {
                    break;
                }

                int runStart = index + run;
                int runEnd = runStart;
                while (runEnd < _subject.Length && _subject[runEnd] == '`')
                {
                    runEnd++;
                }

                _lastBacktickRun[runEnd - runStart] = runStart;
                index = runEnd;
            }
        }

        return _lastBacktickRun.TryGetValue(ticks, out int last) && last >= position;
    }

    private bool ParseAutolink(MarkdownNode block)
    {
        // Neither form holds spaces, controls or another '<', so the scan stops at the next one: linear over a run of '<'.
        ReadOnlySpan<char> rest = _subject.AsSpan(_position);
        int close = 1;
        while (close < rest.Length && rest[close] > ' ' && rest[close] is not ('<' or '>'))
        {
            close++;
        }

        if (close < 2 || close >= rest.Length || rest[close] != '>')
        {
            return false;
        }

        ReadOnlySpan<char> inside = rest[1..close];
        if (IsUri(inside))
        {
            string destination = inside.ToString();
            AppendAutolink(block, MarkdownText.NormalizeUri(destination), destination);
            _position += close + 1;
            return true;
        }

        if (IsEmail(inside))
        {
            string address = inside.ToString();
            AppendAutolink(block, MarkdownText.NormalizeUri("mailto:" + address), address);
            _position += close + 1;
            return true;
        }

        return false;
    }

    private static void AppendAutolink(MarkdownNode block, string destination, string text)
    {
        var link = new MarkdownNode(MarkdownKind.Link) { Destination = destination, IsOpen = false };
        link.AppendChild(Text(text));
        block.AppendChild(link);
    }

    /// <summary>An absolute URI as an autolink allows: a scheme of 2 to 32 characters, a colon, then no spaces, controls or angle brackets.</summary>
    private static bool IsUri(ReadOnlySpan<char> text)
    {
        if (text.Length == 0 || !char.IsAsciiLetter(text[0]))
        {
            return false;
        }

        int colon = 1;
        while (colon < text.Length && (char.IsAsciiLetterOrDigit(text[colon]) || text[colon] is '+' or '.' or '-'))
        {
            colon++;
        }

        if (colon < 2 || colon > 32 || colon >= text.Length || text[colon] != ':')
        {
            return false;
        }

        foreach (char c in text[(colon + 1)..])
        {
            if (c <= ' ' || c is '<' or '>')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsEmail(ReadOnlySpan<char> text)
    {
        int at = text.IndexOf('@');
        if (at < 1)
        {
            return false;
        }

        foreach (char c in text[..at])
        {
            if (!(char.IsAsciiLetterOrDigit(c) || ".!#$%&'*+/=?^_`{|}~-".Contains(c, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        ReadOnlySpan<char> domain = text[(at + 1)..];
        if (domain.IsEmpty)
        {
            return false;
        }

        foreach (Range range in domain.Split('.'))
        {
            ReadOnlySpan<char> label = domain[range];
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }

            foreach (char c in label)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c == '-'))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool ParseHtmlTag(MarkdownNode block)
    {
        int end = MarkdownHtml.Inline(_subject, _position, _htmlNoClose);
        if (end == 0)
        {
            return false;
        }

        block.AppendChild(new MarkdownNode(MarkdownKind.HtmlInline) { Literal = _subject[_position..end], IsOpen = false });
        _position = end;
        return true;
    }

    private bool ParseEntity(MarkdownNode block)
    {
        int length = MarkdownEntities.Match(_subject, _position, out string? replacement);
        if (length == 0)
        {
            return false;
        }

        block.AppendChild(Text(replacement!));
        _position += length;
        return true;
    }

    private bool HandleDelimiter(char c, MarkdownNode block)
    {
        int start = _position;
        while (Peek() == c)
        {
            _position++;
        }

        int count = _position - start;
        (bool canOpen, bool canClose) = Flanking(c, start, _position);
        MarkdownNode node = Text(_subject.Substring(start, count));
        block.AppendChild(node);
        if (c == '~' && count > 2)
        {
            return true;
        }

        var delimiter = new Delimiter(c, count, node, canOpen, canClose) { Previous = _delimiters };
        _delimiters?.Next = delimiter;

        _delimiters = delimiter;
        return true;
    }

    /// <summary>Whether the delimiter run from <paramref name="start"/> to <paramref name="end"/> can open and close emphasis (CommonMark 0.31, "Emphasis and strong emphasis").</summary>
    private (bool CanOpen, bool CanClose) Flanking(char c, int start, int end)
    {
        Rune before = start == 0 ? new Rune('\n') : RuneBefore(start);
        Rune after = end >= _subject.Length ? new Rune('\n') : RuneAt(end);
        bool afterWhitespace = MarkdownText.IsUnicodeWhitespace(after);
        bool afterPunctuation = MarkdownText.IsUnicodePunctuation(after);
        bool beforeWhitespace = MarkdownText.IsUnicodeWhitespace(before);
        bool beforePunctuation = MarkdownText.IsUnicodePunctuation(before);
        bool leftFlanking = !afterWhitespace && (!afterPunctuation || beforeWhitespace || beforePunctuation);
        bool rightFlanking = !beforeWhitespace && (!beforePunctuation || afterWhitespace || afterPunctuation);
        return c == '_'
            ? (leftFlanking && (!rightFlanking || beforePunctuation), rightFlanking && (!leftFlanking || afterPunctuation))
            : (leftFlanking, rightFlanking);
    }

    private Rune RuneBefore(int index)
    {
        char c = _subject[index - 1];
        if (char.IsLowSurrogate(c) && index >= 2 && char.IsHighSurrogate(_subject[index - 2]))
        {
            return new Rune(_subject[index - 2], c);
        }

        return Rune.TryCreate(c, out Rune rune) ? rune : Rune.ReplacementChar;
    }

    private Rune RuneAt(int index)
    {
        char c = _subject[index];
        if (char.IsHighSurrogate(c) && index + 1 < _subject.Length && char.IsLowSurrogate(_subject[index + 1]))
        {
            return new Rune(c, _subject[index + 1]);
        }

        return Rune.TryCreate(c, out Rune rune) ? rune : Rune.ReplacementChar;
    }

    private static int BottomIndex(Delimiter closer) => closer.Character switch
    {
        '_' => (closer.CanOpen ? 3 : 0) + (closer.OriginalCount % 3),
        '*' => 6 + (closer.CanOpen ? 3 : 0) + (closer.OriginalCount % 3),
        _ => 12 + closer.OriginalCount - 1,
    };

    private void ProcessEmphasis(Delimiter? stackBottom)
    {
        var openersBottom = new Delimiter?[14];
        Array.Fill(openersBottom, stackBottom);
        Delimiter? closer = _delimiters;
        while (closer is not null && !ReferenceEquals(closer.Previous, stackBottom))
        {
            closer = closer.Previous;
        }

        while (closer is not null)
        {
            if (!closer.CanClose)
            {
                closer = closer.Next;
                continue;
            }

            int bottomIndex = BottomIndex(closer);
            Delimiter? opener = closer.Previous;
            bool found = false;
            while (opener is not null && !ReferenceEquals(opener, stackBottom) && !ReferenceEquals(opener, openersBottom[bottomIndex]))
            {
                if (opener.Character == closer.Character && opener.CanOpen)
                {
                    bool matches = closer.Character == '~'
                        ? opener.Count == closer.Count
                        : !((closer.CanOpen || opener.CanClose) && closer.OriginalCount % 3 != 0 && (opener.OriginalCount + closer.OriginalCount) % 3 == 0);
                    if (matches)
                    {
                        found = true;
                        break;
                    }
                }

                opener = opener.Previous;
            }

            Delimiter oldCloser = closer;
            if (found)
            {
                int used = closer.Character == '~' ? closer.Count : closer.Count >= 2 && opener!.Count >= 2 ? 2 : 1;
                MarkdownNode openerNode = opener!.Node;
                MarkdownNode closerNode = closer.Node;
                opener.Count -= used;
                closer.Count -= used;
                openerNode.Literal = openerNode.Literal[..^used];
                closerNode.Literal = closerNode.Literal[..^used];
                var emphasis = new MarkdownNode(closer.Character == '~' ? MarkdownKind.Strikethrough : used == 1 ? MarkdownKind.Emphasis : MarkdownKind.Strong) { IsOpen = false };
                MarkdownNode? node = openerNode.Next;
                while (node is not null && !ReferenceEquals(node, closerNode))
                {
                    MarkdownNode? next = node.Next;
                    emphasis.AppendChild(node);
                    node = next;
                }

                openerNode.InsertAfter(emphasis);

                // Delimiters between the opener and the closer can no longer match anything.
                if (!ReferenceEquals(opener.Next, closer))
                {
                    opener.Next = closer;
                    closer.Previous = opener;
                }

                if (opener.Count == 0)
                {
                    openerNode.Unlink();
                    RemoveDelimiter(opener);
                }

                if (closer.Count == 0)
                {
                    closerNode.Unlink();
                    Delimiter? next = closer.Next;
                    RemoveDelimiter(closer);
                    closer = next;
                }
            }
            else
            {
                closer = closer.Next;
                openersBottom[bottomIndex] = oldCloser.Previous;
                if (!oldCloser.CanOpen)
                {
                    RemoveDelimiter(oldCloser);
                }
            }
        }

        while (_delimiters is not null && !ReferenceEquals(_delimiters, stackBottom))
        {
            RemoveDelimiter(_delimiters);
        }
    }

    private void RemoveDelimiter(Delimiter delimiter)
    {
        delimiter.Previous?.Next = delimiter.Next;
        if (delimiter.Next is null)
        {
            _delimiters = delimiter.Previous;
        }
        else
        {
            delimiter.Next.Previous = delimiter.Previous;
        }
    }

    private bool ParseOpenBracket(MarkdownNode block)
    {
        int start = _position;
        _position++;
        MarkdownNode node = Text("[");
        block.AppendChild(node);
        AddBracket(node, start, image: false);
        return true;
    }

    private bool ParseBang(MarkdownNode block)
    {
        int start = _position;
        _position++;
        if (Peek() == '[')
        {
            _position++;
            MarkdownNode node = Text("![");
            block.AppendChild(node);
            AddBracket(node, start + 1, image: true);
        }
        else
        {
            block.AppendChild(Text("!"));
        }

        return true;
    }

    private void AddBracket(MarkdownNode node, int index, bool image)
    {
        _brackets?.BracketAfter = true;
        _brackets = new Bracket(node, index, image, _delimiters) { Previous = _brackets };
    }

    private bool ParseCloseBracket(MarkdownNode block)
    {
        _position++;
        int start = _position;
        Bracket? opener = _brackets;
        if (opener is null)
        {
            block.AppendChild(Text("]"));
            return true;
        }

        if (!opener.Active)
        {
            block.AppendChild(Text("]"));
            _brackets = opener.Previous;
            return true;
        }

        bool image = opener.Image;
        int saved = _position;
        string? destination = null;
        string? title = null;
        bool matched = false;
        if (Peek() == '(')
        {
            _position++;
            SkipSpaceAndNewline();
            destination = ParseLinkDestination();
            if (destination is not null)
            {
                SkipSpaceAndNewline();
                if (_position > 0 && MarkdownText.IsAsciiWhitespace(_subject[_position - 1]))
                {
                    title = ParseLinkTitle();
                }

                SkipSpaceAndNewline();
                if (Peek() == ')')
                {
                    _position++;
                    matched = true;
                }
            }

            if (!matched)
            {
                _position = saved;
            }
        }

        if (!matched)
        {
            int beforeLabel = _position;
            int labelLength = ParseLinkLabel();
            string? label = null;
            if (labelLength > 2)
            {
                label = _subject.Substring(beforeLabel + 1, labelLength - 2);
            }
            else if (!opener.BracketAfter)
            {
                label = _subject.Substring(opener.Index + 1, start - opener.Index - 2);
            }

            if (labelLength == 0)
            {
                _position = saved;
            }

            if (label is not null && label.Length <= 999 && references.TryGetValue(MarkdownText.NormalizeLabel(label), out (string Destination, string Title) reference))
            {
                destination = reference.Destination;
                title = reference.Title;
                matched = true;
            }
        }

        if (!matched)
        {
            _brackets = opener.Previous;
            _position = start;
            block.AppendChild(Text("]"));
            return true;
        }

        var link = new MarkdownNode(image ? MarkdownKind.Image : MarkdownKind.Link) { Destination = destination!, Title = title ?? string.Empty, IsOpen = false };
        MarkdownNode? node = opener.Node.Next;
        while (node is not null)
        {
            MarkdownNode? next = node.Next;
            link.AppendChild(node);
            node = next;
        }

        block.AppendChild(link);
        ProcessEmphasis(opener.PreviousDelimiter);
        _brackets = opener.Previous;
        opener.Node.Unlink();

        // No links in links: the link openers before this one become plain brackets. Those below an inactive one already are.
        if (!image)
        {
            for (Bracket? earlier = _brackets; earlier is not null; earlier = earlier.Previous)
            {
                if (!earlier.Image)
                {
                    if (!earlier.Active)
                    {
                        break;
                    }

                    earlier.Active = false;
                }
            }
        }

        return true;
    }

    private string? ParseLinkDestination()
    {
        if (Peek() == '<')
        {
            int index = _position + 1;
            while (index < _subject.Length)
            {
                char c = _subject[index];
                if (c == '\\' && index + 1 < _subject.Length && MarkdownText.IsEscapable(_subject[index + 1]))
                {
                    index += 2;
                }
                else if (c is '\n' or '<')
                {
                    return null;
                }
                else if (c == '>')
                {
                    string inside = _subject[(_position + 1)..index];
                    _position = index + 1;
                    return MarkdownText.NormalizeUri(MarkdownText.Unescape(inside));
                }
                else
                {
                    index++;
                }
            }

            return null;
        }

        int start = _position;
        int parens = 0;
        while (!AtEnd)
        {
            char c = _subject[_position];
            if (c == '\\' && _position + 1 < _subject.Length && MarkdownText.IsEscapable(_subject[_position + 1]))
            {
                _position += 2;
            }
            else if (c == '(')
            {
                // Nesting is limited, as the spec allows, so an unclosed run of openers costs linear time.
                _position++;
                if (++parens > 32)
                {
                    _position = start;
                    return null;
                }
            }
            else if (c == ')')
            {
                if (parens < 1)
                {
                    break;
                }

                _position++;
                parens--;
            }
            else if (c <= ' ' || c == '\u007F')
            {
                break;
            }
            else
            {
                _position++;
            }
        }

        if ((_position == start && Peek() != ')') || parens != 0)
        {
            _position = start;
            return null;
        }

        return MarkdownText.NormalizeUri(MarkdownText.Unescape(_subject[start.._position]));
    }

    private string? ParseLinkTitle()
    {
        char open = Peek();
        char close = open switch
        {
            '"' => '"',
            '\'' => '\'',
            '(' => ')',
            _ => '\0',
        };

        if (close == '\0')
        {
            return null;
        }

        int index = _position + 1;
        while (index < _subject.Length)
        {
            char c = _subject[index];
            if (c == '\\' && index + 1 < _subject.Length)
            {
                index += 2;
            }
            else if (c == close)
            {
                string title = MarkdownText.Unescape(_subject[(_position + 1)..index]);
                _position = index + 1;
                return title;
            }
            else if (open == '(' && c == '(')
            {
                return null;
            }
            else
            {
                index++;
            }
        }

        return null;
    }

    /// <summary>The length of the link label at the position (brackets included, at most 999 characters inside), or 0.</summary>
    private int ParseLinkLabel()
    {
        if (Peek() != '[')
        {
            return 0;
        }

        int index = _position + 1;
        while (index < _subject.Length && index - _position <= 1000)
        {
            char c = _subject[index];
            if (c == '\\' && index + 1 < _subject.Length)
            {
                index += 2;
            }
            else if (c == '[')
            {
                return 0;
            }
            else if (c == ']')
            {
                int length = index + 1 - _position;
                if (length > 1001)
                {
                    return 0;
                }

                _position = index + 1;
                return length;
            }
            else
            {
                index++;
            }
        }

        return 0;
    }

    private void SkipSpaceAndNewline()
    {
        while (Peek() == ' ')
        {
            _position++;
        }

        if (Peek() == '\n')
        {
            _position++;
            while (Peek() == ' ')
            {
                _position++;
            }
        }
    }

    private bool SpaceAtEndOfLine()
    {
        int index = _position;
        while (index < _subject.Length && _subject[index] == ' ')
        {
            index++;
        }

        if (index >= _subject.Length)
        {
            _position = index;
            return true;
        }

        if (_subject[index] == '\n')
        {
            _position = index + 1;
            return true;
        }

        return false;
    }

    private sealed class Delimiter(char character, int count, MarkdownNode node, bool canOpen, bool canClose)
    {
        public char Character { get; } = character;

        public int Count { get; set; } = count;

        public int OriginalCount { get; } = count;

        public MarkdownNode Node { get; } = node;

        public bool CanOpen { get; } = canOpen;

        public bool CanClose { get; } = canClose;

        public Delimiter? Previous { get; set; }

        public Delimiter? Next { get; set; }
    }

    private sealed class Bracket(MarkdownNode node, int index, bool image, Delimiter? previousDelimiter)
    {
        public MarkdownNode Node { get; } = node;

        public int Index { get; } = index;

        public bool Image { get; } = image;

        public Delimiter? PreviousDelimiter { get; } = previousDelimiter;

        public Bracket? Previous { get; init; }

        public bool Active { get; set; } = true;

        public bool BracketAfter { get; set; }
    }
}
