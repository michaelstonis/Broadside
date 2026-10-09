using System.Buffers;

namespace Broadside.Objects;

/// <summary>Splits a byte span into COS tokens without allocating.</summary>
/// <remarks>
/// ISO 32000-2 §7.2. White-space (Table 1) and comments (§7.2.4) separate tokens and are skipped; delimiters (Table 2) end a run of
/// regular characters. The lexer never fails: bytes that cannot start a token become <see cref="CosTokenKind.Invalid"/> tokens and
/// unterminated strings run to the end of input, so the parser decides what is a deviation. Every call to <see cref="Next"/> either
/// advances by at least one byte or returns <see cref="CosTokenKind.EndOfInput"/>.
/// </remarks>
internal ref struct CosLexer
{
    private static readonly SearchValues<byte> Whitespace = SearchValues.Create("\0\t\n\f\r "u8);
    private static readonly SearchValues<byte> EndOfRegular = SearchValues.Create("\0\t\n\f\r ()<>[]{}/%"u8);
    private static readonly SearchValues<byte> NumberCharacters = SearchValues.Create("+-.0123456789"u8);
    private static readonly SearchValues<byte> EndOfLine = SearchValues.Create("\r\n"u8);
    private static readonly SearchValues<byte> LiteralSpecial = SearchValues.Create("()\\"u8);

    private readonly ReadOnlySpan<byte> _source;

    /// <summary>Initializes a new instance of the <see cref="CosLexer"/> struct.</summary>
    /// <param name="source">The bytes to tokenize.</param>
    /// <param name="position">The offset to start at.</param>
    public CosLexer(ReadOnlySpan<byte> source, int position = 0)
    {
        _source = source;
        Position = position;
    }

    /// <summary>Gets or sets the offset of the next byte to read.</summary>
    public int Position { get; set; }

    /// <summary>Gets the source span.</summary>
    public readonly ReadOnlySpan<byte> Source => _source;

    /// <summary>Returns whether <paramref name="value"/> is a white-space character (Table 1).</summary>
    public static bool IsWhitespace(byte value) => Whitespace.Contains(value);

    /// <summary>Returns whether <paramref name="value"/> is a regular character: neither white-space nor a delimiter (§7.2.3).</summary>
    public static bool IsRegular(byte value) => !EndOfRegular.Contains(value);

    /// <summary>Skips white-space and comments.</summary>
    public void SkipWhitespaceAndComments()
    {
        int position = Position;
        while (true)
        {
            int skip = _source[position..].IndexOfAnyExcept(Whitespace);
            if (skip < 0)
            {
                Position = _source.Length;
                return;
            }

            position += skip;
            if (_source[position] != (byte)'%')
            {
                Position = position;
                return;
            }

            int endOfLine = _source[position..].IndexOfAny(EndOfLine);
            if (endOfLine < 0)
            {
                Position = _source.Length;
                return;
            }

            position += endOfLine;
        }
    }

    /// <summary>Returns the next token without consuming it.</summary>
    public CosToken Peek()
    {
        int saved = Position;
        CosToken token = Next();
        Position = saved;
        return token;
    }

    /// <summary>Reads the next token.</summary>
    public CosToken Next()
    {
        SkipWhitespaceAndComments();
        int start = Position;
        if (start >= _source.Length)
        {
            return new CosToken(CosTokenKind.EndOfInput, start, 0);
        }

        CosToken token = _source[start] switch
        {
            (byte)'(' => new CosToken(CosTokenKind.LiteralString, start, LiteralStringLength(_source[start..])),
            (byte)'<' => LessThan(start),
            (byte)'>' => start + 1 < _source.Length && _source[start + 1] == (byte)'>'
                ? new CosToken(CosTokenKind.DictionaryEnd, start, 2)
                : new CosToken(CosTokenKind.Invalid, start, 1),
            (byte)'[' => new CosToken(CosTokenKind.ArrayStart, start, 1),
            (byte)']' => new CosToken(CosTokenKind.ArrayEnd, start, 1),
            (byte)'{' or (byte)'}' => new CosToken(CosTokenKind.Keyword, start, 1),
            (byte)')' => new CosToken(CosTokenKind.Invalid, start, 1),
            (byte)'/' => new CosToken(CosTokenKind.Name, start, 1 + RegularRunLength(start + 1)),
            _ => RegularRun(start),
        };

        Position = token.End;
        return token;
    }

    private readonly CosToken LessThan(int start)
    {
        if (start + 1 < _source.Length && _source[start + 1] == (byte)'<')
        {
            return new CosToken(CosTokenKind.DictionaryStart, start, 2);
        }

        int close = _source[(start + 1)..].IndexOf((byte)'>');
        int length = close < 0 ? _source.Length - start : close + 2;
        return new CosToken(CosTokenKind.HexString, start, length);
    }

    private readonly CosToken RegularRun(int start)
    {
        int length = RegularRunLength(start);
        ReadOnlySpan<byte> run = _source.Slice(start, length);
        if (!run.ContainsAnyExcept(NumberCharacters) && run.ContainsAnyInRange((byte)'0', (byte)'9'))
        {
            return new CosToken(run.Contains((byte)'.') ? CosTokenKind.Real : CosTokenKind.Integer, start, length);
        }

        return new CosToken(CosTokenKind.Keyword, start, length);
    }

    private readonly int RegularRunLength(int start)
    {
        int end = _source[start..].IndexOfAny(EndOfRegular);
        return end < 0 ? _source.Length - start : end;
    }

    /// <summary>
    /// The length of the literal string that starts at <paramref name="text"/>[0], through its matching closing parenthesis, or the
    /// whole span when the string is unterminated. A backslash escapes the byte after it; other parentheses nest (§7.3.4.2).
    /// </summary>
    private static int LiteralStringLength(ReadOnlySpan<byte> text)
    {
        int depth = 1;
        int index = 1;
        while (true)
        {
            int special = text[index..].IndexOfAny(LiteralSpecial);
            if (special < 0)
            {
                return text.Length;
            }

            index += special;
            switch (text[index])
            {
                case (byte)'\\':
                    index += 2;
                    if (index >= text.Length)
                    {
                        return text.Length;
                    }

                    continue;
                case (byte)'(':
                    depth++;
                    break;
                default:
                    depth--;
                    if (depth == 0)
                    {
                        return index + 1;
                    }

                    break;
            }

            index++;
        }
    }
}
