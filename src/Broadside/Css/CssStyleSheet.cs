namespace Broadside.Css;

/// <summary>One declaration of a rule or a <c>style</c> attribute: a property name, its value as written, and whether it is <c>!important</c>.</summary>
/// <param name="Property">The property name, lowercase.</param>
/// <param name="Value">The value, trimmed, without <c>!important</c>.</param>
/// <param name="Important">Whether the declaration ends in <c>!important</c>.</param>
/// <remarks>CSS Syntax Module Level 3 §5.4.6 (consume a declaration).</remarks>
internal readonly record struct CssDeclaration(string Property, string Value, bool Important);

/// <summary>A style rule: one selector of its selector list and the declarations of its block, with the position of the rule in its sheet.</summary>
/// <remarks>CSS Syntax Module Level 3 §5.4.3 (qualified rules). A rule with a selector list is split into one rule per selector, so each has its own specificity.</remarks>
internal sealed class CssRule(CssSelector selector, IReadOnlyList<CssDeclaration> declarations, int order)
{
    /// <summary>Gets the selector.</summary>
    public CssSelector Selector { get; } = selector;

    /// <summary>Gets the declarations, in the order written.</summary>
    public IReadOnlyList<CssDeclaration> Declarations { get; } = declarations;

    /// <summary>Gets the position of the rule in the sheets it came from; a later rule wins over an earlier one of equal specificity.</summary>
    public int Order { get; } = order;
}

/// <summary>
/// A style sheet reduced to what a static document needs: style rules with type, universal, class, id and attribute selectors,
/// descendant and child combinators, and <c>:first-child</c>. At-rules are skipped whole (an <c>@import</c> is never fetched).
/// </summary>
/// <remarks>
/// <para>
/// CSS Syntax Module Level 3 §5 (parsing) and Selectors Level 3 §5–§9, §16 (specificity). The parser is lenient the way CSS is: a rule
/// whose selector cannot be read is dropped, a declaration without a colon is dropped, and parsing goes on at the next rule or
/// declaration; each drop is reported through the callback. It is shared by every importer that styles markup (SVG today), so it
/// knows nothing of the properties.
/// </para>
/// </remarks>
internal sealed class CssStyleSheet
{
    private readonly List<CssRule> _rules = [];

    /// <summary>Gets the rules, in sheet order.</summary>
    public IReadOnlyList<CssRule> Rules => _rules;

    /// <summary>Parses <paramref name="text"/> and adds its rules after the rules already in this sheet.</summary>
    /// <param name="text">The style sheet text.</param>
    /// <param name="onError">Receives a description of each part that is skipped; <see langword="null"/> to ignore them.</param>
    public void Add(ReadOnlySpan<char> text, Action<string>? onError)
    {
        int position = 0;
        while (true)
        {
            SkipWhiteSpaceAndComments(text, ref position);
            if (position >= text.Length)
            {
                return;
            }

            // CDO and CDC tokens are allowed at the top level of a sheet and ignored (§5.4.1).
            if (text[position..].StartsWith("<!--", StringComparison.Ordinal))
            {
                position += 4;
                continue;
            }

            if (text[position..].StartsWith("-->", StringComparison.Ordinal))
            {
                position += 3;
                continue;
            }

            int preludeStart = position;
            int blockStart = FindTopLevel(text, position, '{', stopAtSemicolon: text[position] == '@');
            if (text[position] == '@')
            {
                // An at-rule: ends at a semicolon or with its block.
                if (blockStart < 0)
                {
                    int end = text[position..].IndexOf(';');
                    onError?.Invoke("an at-rule without a block is skipped");
                    position = end < 0 ? text.Length : position + end + 1;
                    continue;
                }

                onError?.Invoke($"the at-rule '{Name(text[position..blockStart])}' is skipped");
                position = SkipBlock(text, blockStart);
                continue;
            }

            if (blockStart < 0)
            {
                onError?.Invoke("a rule without a declaration block is skipped");
                return;
            }

            int blockEnd = SkipBlock(text, blockStart);
            ReadOnlySpan<char> prelude = text[preludeStart..blockStart];
            ReadOnlySpan<char> block = text[(blockStart + 1)..Math.Max(blockStart + 1, blockEnd - 1)];
            position = blockEnd;
            var declarations = new List<CssDeclaration>();
            ParseDeclarations(block, declarations, onError);
            foreach (ReadOnlySpan<char> part in SplitTopLevel(prelude, ','))
            {
                if (CssSelector.TryParse(part, out CssSelector? selector))
                {
                    _rules.Add(new CssRule(selector, declarations, _rules.Count));
                }
                else
                {
                    onError?.Invoke($"the selector '{part.Trim().ToString()}' cannot be read; its rule is skipped");
                }
            }
        }
    }

    /// <summary>Parses a declaration list, such as a block's content or a <c>style</c> attribute, adding each declaration it reads.</summary>
    /// <param name="text">The declarations, separated by semicolons.</param>
    /// <param name="declarations">Receives the declarations in order.</param>
    /// <param name="onError">Receives a description of each declaration that is skipped; <see langword="null"/> to ignore them.</param>
    /// <remarks>CSS Syntax Module Level 3 §5.4.5 (consume a list of declarations).</remarks>
    public static void ParseDeclarations(ReadOnlySpan<char> text, List<CssDeclaration> declarations, Action<string>? onError)
    {
        foreach (ReadOnlySpan<char> raw in SplitTopLevel(text, ';'))
        {
            ReadOnlySpan<char> declaration = StripComments(raw).AsSpan().Trim();
            if (declaration.IsEmpty)
            {
                continue;
            }

            int colon = declaration.IndexOf(':');
            if (colon <= 0)
            {
                onError?.Invoke($"the declaration '{declaration.ToString()}' has no property name and colon; it is skipped");
                continue;
            }

            ReadOnlySpan<char> name = declaration[..colon].Trim();
            ReadOnlySpan<char> value = declaration[(colon + 1)..].Trim();
            bool important = false;
            int bang = value.LastIndexOf('!');
            if (bang >= 0 && value[(bang + 1)..].Trim().Equals("important", StringComparison.OrdinalIgnoreCase))
            {
                important = true;
                value = value[..bang].TrimEnd();
            }

            if (name.IsEmpty || value.IsEmpty)
            {
                onError?.Invoke($"the declaration '{declaration.ToString()}' has an empty name or value; it is skipped");
                continue;
            }

            declarations.Add(new CssDeclaration(name.ToString().ToLowerInvariant(), value.ToString(), important));
        }
    }

    private static string Name(ReadOnlySpan<char> prelude)
    {
        int end = 1;
        while (end < prelude.Length && (char.IsLetterOrDigit(prelude[end]) || prelude[end] == '-'))
        {
            end++;
        }

        return prelude[..end].ToString();
    }

    private static void SkipWhiteSpaceAndComments(ReadOnlySpan<char> text, ref int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
            }
            else if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '*')
            {
                int end = text[(position + 2)..].IndexOf("*/", StringComparison.Ordinal);
                position = end < 0 ? text.Length : position + 2 + end + 2;
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>Finds <paramref name="target"/> outside strings and comments from <paramref name="start"/>; -1 when there is none.</summary>
    private static int FindTopLevel(ReadOnlySpan<char> text, int start, char target, bool stopAtSemicolon)
    {
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (c == target)
            {
                return i;
            }

            if (stopAtSemicolon && c == ';')
            {
                return -1;
            }

            if (c is '"' or '\'')
            {
                i = SkipString(text, i);
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text[(i + 2)..].IndexOf("*/", StringComparison.Ordinal);
                i = end < 0 ? text.Length : i + 2 + end + 1;
            }
        }

        return -1;
    }

    /// <summary>Returns the position after the block that opens at <paramref name="open"/>, nested blocks included.</summary>
    private static int SkipBlock(ReadOnlySpan<char> text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i + 1;
                }
            }
            else if (c is '"' or '\'')
            {
                i = SkipString(text, i);
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text[(i + 2)..].IndexOf("*/", StringComparison.Ordinal);
                i = end < 0 ? text.Length : i + 2 + end + 1;
            }
        }

        return text.Length;
    }

    /// <summary>Returns the position of the quote that closes the string opening at <paramref name="open"/>, or the last position.</summary>
    private static int SkipString(ReadOnlySpan<char> text, int open)
    {
        char quote = text[open];
        for (int i = open + 1; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == quote)
            {
                return i;
            }
        }

        return text.Length - 1;
    }

    /// <summary>Splits <paramref name="text"/> at <paramref name="separator"/> outside strings, parentheses and brackets.</summary>
    internal static List<string> SplitTopLevelToList(ReadOnlySpan<char> text, char separator)
    {
        var parts = new List<string>();
        foreach (ReadOnlySpan<char> part in SplitTopLevel(text, separator))
        {
            parts.Add(part.ToString());
        }

        return parts;
    }

    private static TopLevelSplitter SplitTopLevel(ReadOnlySpan<char> text, char separator) => new(text, separator);

    private static string StripComments(ReadOnlySpan<char> text)
    {
        int open = text.IndexOf("/*", StringComparison.Ordinal);
        if (open < 0)
        {
            return text.ToString();
        }

        var builder = new System.Text.StringBuilder(text.Length);
        while (open >= 0)
        {
            _ = builder.Append(text[..open]);
            int close = text[(open + 2)..].IndexOf("*/", StringComparison.Ordinal);
            if (close < 0)
            {
                return builder.ToString();
            }

            text = text[(open + 2 + close + 2)..];
            open = text.IndexOf("/*", StringComparison.Ordinal);
        }

        return builder.Append(text).ToString();
    }

    /// <summary>Enumerates the parts of a span between top-level separators, without allocating.</summary>
    internal ref struct TopLevelSplitter
    {
        private readonly char _separator;
        private ReadOnlySpan<char> _rest;
        private bool _done;

        public TopLevelSplitter(ReadOnlySpan<char> text, char separator)
        {
            _rest = text;
            _separator = separator;
            _done = false;
            Current = default;
        }

        public ReadOnlySpan<char> Current { get; private set; }

        public readonly TopLevelSplitter GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_done)
            {
                return false;
            }

            int depth = 0;
            for (int i = 0; i < _rest.Length; i++)
            {
                char c = _rest[i];
                if (c is '(' or '[')
                {
                    depth++;
                }
                else if (c is ')' or ']')
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (c is '"' or '\'')
                {
                    i = SkipString(_rest, i);
                }
                else if (c == _separator && depth == 0)
                {
                    Current = _rest[..i];
                    _rest = _rest[(i + 1)..];
                    return true;
                }
            }

            Current = _rest;
            _done = true;
            return true;
        }
    }
}
