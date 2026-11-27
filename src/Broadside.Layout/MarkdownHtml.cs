namespace Broadside.Layout.Flow;

/// <summary>
/// Recognizes raw HTML the way CommonMark 0.31 does ("HTML blocks" and "Raw HTML"), only to know where it starts and ends: the
/// document builder never renders it. Hand-written scanners rather than regular expressions, so no input backtracks.
/// </summary>
internal static class MarkdownHtml
{
    private static readonly string[] Type1Tags = ["pre", "script", "style", "textarea"];

    private static readonly HashSet<string> Type6Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "base", "basefont", "blockquote", "body", "caption", "center", "col", "colgroup", "dd", "details",
        "dialog", "dir", "div", "dl", "dt", "fieldset", "figcaption", "figure", "footer", "form", "frame", "frameset", "h1", "h2", "h3",
        "h4", "h5", "h6", "head", "header", "hr", "html", "iframe", "legend", "li", "link", "main", "menu", "menuitem", "nav", "noframes",
        "ol", "optgroup", "option", "p", "param", "search", "section", "summary", "table", "tbody", "td", "tfoot", "th", "thead", "title",
        "tr", "track", "ul",
    };

    /// <summary>The kind (1 to 7) of HTML block <paramref name="line"/> starts, or 0.</summary>
    /// <param name="line">The line from its first non-space character.</param>
    /// <param name="canInterruptParagraph">Whether the line could interrupt a paragraph; kind 7 cannot.</param>
    public static int BlockStart(ReadOnlySpan<char> line, bool canInterruptParagraph)
    {
        if (line.Length < 2 || line[0] != '<')
        {
            return 0;
        }

        foreach (string tag in Type1Tags)
        {
            if (line.Length > tag.Length && line[1..].StartsWith(tag, StringComparison.OrdinalIgnoreCase)
                && (line.Length == tag.Length + 1 || line[tag.Length + 1] is ' ' or '\t' or '\n' or '\r' or '\f' or '\v' or '>'))
            {
                return 1;
            }
        }

        if (line.StartsWith("<!--"))
        {
            return 2;
        }

        if (line[1] == '?')
        {
            return 3;
        }

        if (line.StartsWith("<![CDATA["))
        {
            return 5;
        }

        if (line[1] == '!' && line.Length > 2 && char.IsAsciiLetter(line[2]))
        {
            return 4;
        }

        int nameStart = line[1] == '/' ? 2 : 1;
        int nameEnd = nameStart;
        while (nameEnd < line.Length && char.IsAsciiLetterOrDigit(line[nameEnd]))
        {
            nameEnd++;
        }

        if (nameEnd > nameStart && Type6Tags.GetAlternateLookup<ReadOnlySpan<char>>().Contains(line[nameStart..nameEnd]))
        {
            ReadOnlySpan<char> after = line[nameEnd..];
            if (after.IsEmpty || after[0] is ' ' or '\t' or '\f' or '\v' or '>' || after.StartsWith("/>"))
            {
                return 6;
            }
        }

        if (!canInterruptParagraph)
        {
            return 0;
        }

        int end = line[1] == '/' ? CloseTag(line, 0) : OpenTag(line, 0, stackalloc int[6]);
        return end > 0 && MarkdownText.IsBlank(line[end..]) ? 7 : 0;
    }

    /// <summary>Whether <paramref name="line"/> holds the end condition of an HTML block of kind 1 to 5.</summary>
    public static bool BlockEnds(int kind, ReadOnlySpan<char> line) => kind switch
    {
        1 => line.Contains("</pre>", StringComparison.OrdinalIgnoreCase) || line.Contains("</script>", StringComparison.OrdinalIgnoreCase)
            || line.Contains("</style>", StringComparison.OrdinalIgnoreCase) || line.Contains("</textarea>", StringComparison.OrdinalIgnoreCase),
        2 => line.Contains("-->", StringComparison.Ordinal),
        3 => line.Contains("?>", StringComparison.Ordinal),
        4 => line.Contains('>'),
        5 => line.Contains("]]>", StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>The end of the open tag at <paramref name="start"/>, or 0; <paramref name="noClose"/> remembers failed searches for closing quotes (slots 4 and 5).</summary>
    public static int OpenTag(ReadOnlySpan<char> text, int start, Span<int> noClose)
    {
        int index = TagName(text, start + 1);
        if (index < 0)
        {
            return 0;
        }

        while (true)
        {
            int afterSpace = SkipWhitespace(text, index);
            if (afterSpace < text.Length && text[afterSpace] == '>')
            {
                return afterSpace + 1;
            }

            if (afterSpace + 1 < text.Length && text[afterSpace] == '/' && text[afterSpace + 1] == '>')
            {
                return afterSpace + 2;
            }

            if (afterSpace == index)
            {
                return 0;
            }

            // An attribute: name, then optionally = and a value.
            index = afterSpace;
            if (index >= text.Length || !(char.IsAsciiLetter(text[index]) || text[index] is '_' or ':'))
            {
                return 0;
            }

            index++;
            while (index < text.Length && (char.IsAsciiLetterOrDigit(text[index]) || text[index] is '_' or '.' or ':' or '-'))
            {
                index++;
            }

            int beforeValue = SkipWhitespace(text, index);
            if (beforeValue < text.Length && text[beforeValue] == '=')
            {
                int value = SkipWhitespace(text, beforeValue + 1);
                if (value >= text.Length)
                {
                    return 0;
                }

                char quote = text[value];
                if (quote is '"' or '\'')
                {
                    int close = Until(text, value + 1, quote == '"' ? "\"" : "'", ref noClose[quote == '"' ? 4 : 5]);
                    if (close == 0)
                    {
                        return 0;
                    }

                    index = close;
                }
                else
                {
                    int valueEnd = value;
                    while (valueEnd < text.Length && text[valueEnd] > ' ' && text[valueEnd] is not ('"' or '\'' or '=' or '<' or '>' or '`'))
                    {
                        valueEnd++;
                    }

                    if (valueEnd == value)
                    {
                        return 0;
                    }

                    index = valueEnd;
                }
            }
        }
    }

    /// <summary>The end of the closing tag at <paramref name="start"/>, or 0.</summary>
    public static int CloseTag(ReadOnlySpan<char> text, int start)
    {
        if (start + 1 >= text.Length || text[start + 1] != '/')
        {
            return 0;
        }

        int index = TagName(text, start + 2);
        if (index < 0)
        {
            return 0;
        }

        index = SkipWhitespace(text, index);
        return index < text.Length && text[index] == '>' ? index + 1 : 0;
    }

    /// <summary>
    /// The end of the inline raw HTML at <paramref name="start"/> (an open or closing tag, comment, processing instruction, declaration
    /// or CDATA section), or 0. <paramref name="noClose"/> remembers searches for a closing string that failed, so a run of unclosed
    /// comments costs linear time.
    /// </summary>
    public static int Inline(ReadOnlySpan<char> text, int start, Span<int> noClose)
    {
        if (start + 1 >= text.Length)
        {
            return 0;
        }

        char next = text[start + 1];
        if (char.IsAsciiLetter(next))
        {
            return OpenTag(text, start, noClose);
        }

        if (next == '/')
        {
            return CloseTag(text, start);
        }

        if (next == '?')
        {
            return Until(text, start + 2, "?>", ref noClose[0]);
        }

        if (next != '!')
        {
            return 0;
        }

        ReadOnlySpan<char> rest = text[start..];
        if (rest.StartsWith("<!-->"))
        {
            return start + 5;
        }

        if (rest.StartsWith("<!--->"))
        {
            return start + 6;
        }

        if (rest.StartsWith("<!--"))
        {
            return Until(text, start + 4, "-->", ref noClose[1]);
        }

        if (rest.StartsWith("<![CDATA["))
        {
            return Until(text, start + 9, "]]>", ref noClose[2]);
        }

        if (rest.Length > 2 && char.IsAsciiLetter(rest[2]))
        {
            return Until(text, start + 3, ">", ref noClose[3]);
        }

        return 0;
    }

    /// <summary>The end of the first <paramref name="close"/> at or after <paramref name="from"/>, or 0; <paramref name="failedFrom"/> is the least position a search already failed from (0 for none).</summary>
    private static int Until(ReadOnlySpan<char> text, int from, string close, ref int failedFrom)
    {
        if (failedFrom > 0 && from >= failedFrom)
        {
            return 0;
        }

        int found = text[from..].IndexOf(close, StringComparison.Ordinal);
        if (found < 0)
        {
            failedFrom = failedFrom == 0 ? from : Math.Min(failedFrom, from);
            return 0;
        }

        return from + found + close.Length;
    }

    /// <summary>The end of the tag name at <paramref name="index"/>, or -1.</summary>
    private static int TagName(ReadOnlySpan<char> text, int index)
    {
        if (index >= text.Length || !char.IsAsciiLetter(text[index]))
        {
            return -1;
        }

        index++;
        while (index < text.Length && (char.IsAsciiLetterOrDigit(text[index]) || text[index] == '-'))
        {
            index++;
        }

        return index;
    }

    private static int SkipWhitespace(ReadOnlySpan<char> text, int index)
    {
        while (index < text.Length && MarkdownText.IsAsciiWhitespace(text[index]))
        {
            index++;
        }

        return index;
    }
}
