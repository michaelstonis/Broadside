using System.Text;

namespace Broadside.Layout.Flow;

/// <summary>
/// The GFM extended autolinks: <c>www.</c> domains, <c>http://</c> and <c>https://</c> URLs and email addresses in text outside links,
/// without angle brackets. Trailing punctuation, unbalanced closing parentheses and a trailing entity-like <c>&amp;x;</c> stay text.
/// </summary>
internal static class MarkdownAutolinks
{
    /// <summary>Turns the extended autolinks in the text of <paramref name="document"/> into links.</summary>
    public static void Apply(MarkdownNode document)
    {
        var texts = new List<MarkdownNode>();
        int links = 0;
        foreach ((MarkdownNode node, bool entering) in document.Walk())
        {
            if (node.Kind is MarkdownKind.Link or MarkdownKind.Image)
            {
                links += entering ? 1 : -1;
            }
            else if (entering && links == 0 && node.Kind == MarkdownKind.Text && node.Previous is not { Kind: MarkdownKind.Text })
            {
                texts.Add(node);
            }
        }

        var merged = new StringBuilder();
        foreach (MarkdownNode text in texts)
        {
            // Adjacent text nodes (delimiter runs left as text, escapes) read as one.
            if (text.Next is { Kind: MarkdownKind.Text })
            {
                merged.Clear().Append(text.Literal);
                while (text.Next is { Kind: MarkdownKind.Text } next)
                {
                    merged.Append(next.Literal);
                    next.Unlink();
                }

                text.Literal = merged.ToString();
            }

            Link(text);
        }
    }

    private static void Link(MarkdownNode node)
    {
        string text = node.Literal;
        if (text.AsSpan().IndexOfAny(":@w") < 0)
        {
            return;
        }

        MarkdownNode current = node;
        int consumed = 0;
        int index = 0;
        while (index < text.Length)
        {
            if (!Candidate(text, index, out int end, out string destination))
            {
                index++;
                continue;
            }

            int start = index;
            if (text[index] != 'w' && text[index] != 'h')
            {
                // An email address: back up over its local part.
                start = LocalPartStart(text, index);
            }

            if (start < consumed)
            {
                index++;
                continue;
            }

            var link = new MarkdownNode(MarkdownKind.Link) { Destination = MarkdownText.NormalizeUri(destination), IsOpen = false };
            link.AppendChild(new MarkdownNode(MarkdownKind.Text) { Literal = text[start..end], IsOpen = false });
            if (start > consumed)
            {
                var before = new MarkdownNode(MarkdownKind.Text) { Literal = text[consumed..start], IsOpen = false };
                current.InsertBefore(before);
            }

            current.InsertBefore(link);
            consumed = end;
            index = end;
        }

        if (consumed == 0)
        {
            return;
        }

        if (consumed < text.Length)
        {
            current.Literal = text[consumed..];
        }
        else
        {
            current.Unlink();
        }
    }

    /// <summary>Whether an autolink is at <paramref name="index"/> (the <c>w</c> of <c>www.</c>, the <c>h</c> of a URL or the <c>@</c> of an address).</summary>
    private static bool Candidate(string text, int index, out int end, out string destination)
    {
        end = 0;
        destination = string.Empty;
        char c = text[index];
        bool boundary = index == 0 || MarkdownText.IsAsciiWhitespace(text[index - 1]) || text[index - 1] is '*' or '_' or '~' or '(';
        if (c == 'w' && boundary && string.CompareOrdinal(text, index, "www.", 0, 4) == 0)
        {
            int domainEnd = Domain(text, index);
            if (domainEnd < 0)
            {
                return false;
            }

            end = Trim(text, index, Path(text, domainEnd));
            destination = "http://" + text[index..end];
            return true;
        }

        if (c == 'h' && boundary)
        {
            int scheme = string.CompareOrdinal(text, index, "https://", 0, 8) == 0 ? 8 : string.CompareOrdinal(text, index, "http://", 0, 7) == 0 ? 7 : 0;
            if (scheme == 0)
            {
                return false;
            }

            int domainEnd = Domain(text, index + scheme);
            if (domainEnd < 0)
            {
                return false;
            }

            end = Trim(text, index, Path(text, domainEnd));
            destination = text[index..end];
            return true;
        }

        if (c == '@')
        {
            int start = LocalPartStart(text, index);
            if (start == index || (start > 0 && (char.IsAsciiLetterOrDigit(text[start - 1]) || text[start - 1] == '/')))
            {
                return false;
            }

            int domainEnd = index + 1;
            int dots = 0;
            while (domainEnd < text.Length && (char.IsAsciiLetterOrDigit(text[domainEnd]) || text[domainEnd] is '-' or '_' or '.'))
            {
                if (text[domainEnd] == '.')
                {
                    if (domainEnd + 1 >= text.Length || !(char.IsAsciiLetterOrDigit(text[domainEnd + 1]) || text[domainEnd + 1] is '-' or '_'))
                    {
                        break;
                    }

                    dots++;
                }

                domainEnd++;
            }

            if (dots == 0 || text[domainEnd - 1] is '-' or '_')
            {
                return false;
            }

            end = domainEnd;
            destination = "mailto:" + text[start..end];
            return true;
        }

        return false;
    }

    private static int LocalPartStart(string text, int at)
    {
        int start = at;
        while (start > 0 && (char.IsAsciiLetterOrDigit(text[start - 1]) || text[start - 1] is '.' or '-' or '_' or '+'))
        {
            start--;
        }

        return start;
    }

    /// <summary>The end of a valid domain at <paramref name="start"/> (segments of letters, digits, <c>_</c> and <c>-</c>, at least one period, no underscore in the last two), or -1.</summary>
    private static int Domain(string text, int start)
    {
        int index = start;
        int lastDot = -1;
        int previousDot = -1;
        int segmentStart = start;
        bool underscoreInLast = false;
        bool underscoreInPrevious = false;
        while (index < text.Length)
        {
            char c = text[index];
            if (c == '.' && index > segmentStart && index + 1 < text.Length && (char.IsAsciiLetterOrDigit(text[index + 1]) || text[index + 1] is '-' or '_'))
            {
                previousDot = lastDot;
                lastDot = index;
                underscoreInPrevious = underscoreInLast;
                underscoreInLast = false;
                segmentStart = index + 1;
            }
            else if (char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c > 0x7F && char.IsLetterOrDigit(c))
            {
                underscoreInLast |= c == '_';
            }
            else
            {
                break;
            }

            index++;
        }

        _ = previousDot;
        return lastDot < 0 || underscoreInLast || underscoreInPrevious || index == segmentStart ? -1 : index;
    }

    private static int Path(string text, int index)
    {
        while (index < text.Length && !MarkdownText.IsAsciiWhitespace(text[index]) && text[index] != '<')
        {
            index++;
        }

        return index;
    }

    /// <summary>Drops trailing punctuation, unbalanced closing parentheses and a trailing entity-like reference from the link ending at <paramref name="end"/>.</summary>
    private static int Trim(string text, int start, int end)
    {
        int open = 0;
        int close = 0;
        foreach (char c in text.AsSpan(start, end - start))
        {
            open += c == '(' ? 1 : 0;
            close += c == ')' ? 1 : 0;
        }

        while (end > start)
        {
            char last = text[end - 1];
            if (last is '?' or '!' or '.' or ',' or ':' or '*' or '_' or '~')
            {
                end--;
                continue;
            }

            if (last == ')')
            {
                if (close > open)
                {
                    close--;
                    end--;
                    continue;
                }

                break;
            }

            if (last == ';')
            {
                int amp = end - 2;
                while (amp > start && char.IsAsciiLetterOrDigit(text[amp]))
                {
                    amp--;
                }

                if (amp > start && amp < end - 2 && text[amp] == '&')
                {
                    end = amp;
                    continue;
                }

                break;
            }

            break;
        }

        return end;
    }
}
