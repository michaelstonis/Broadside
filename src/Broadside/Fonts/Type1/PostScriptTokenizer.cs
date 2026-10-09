using System.Buffers;
using System.Globalization;

namespace Broadside.Fonts.Type1;

/// <summary>The kinds of token the PostScript subset of a Type 1 program is made of.</summary>
internal enum PostScriptTokenKind
{
    /// <summary>The end of the input.</summary>
    EndOfInput,

    /// <summary>An integer, including the radix form <c>8#173</c>.</summary>
    Integer,

    /// <summary>A real number.</summary>
    Real,

    /// <summary>A literal name, <c>/name</c>; its text excludes the slash.</summary>
    LiteralName,

    /// <summary>An executable name, such as <c>def</c> or <c>RD</c>, or any other regular token that is not a number.</summary>
    Name,

    /// <summary>A literal string <c>( … )</c> or a hexadecimal string <c>&lt; … &gt;</c>.</summary>
    String,

    /// <summary><c>[</c>.</summary>
    ArrayStart,

    /// <summary><c>]</c>.</summary>
    ArrayEnd,

    /// <summary><c>{</c>.</summary>
    ProcedureStart,

    /// <summary><c>}</c>.</summary>
    ProcedureEnd,

    /// <summary><c>&lt;&lt;</c> or <c>&gt;&gt;</c>.</summary>
    DictionaryDelimiter,
}

/// <summary>One token: its kind, where its text is, and its value when it is a number.</summary>
internal readonly record struct PostScriptToken(PostScriptTokenKind Kind, int Start, int Length, double Value)
{
    /// <summary>Gets a value indicating whether the token is a number.</summary>
    public bool IsNumber => Kind is PostScriptTokenKind.Integer or PostScriptTokenKind.Real;

    /// <summary>Gets the value as a 32-bit integer when the token is an integer in range.</summary>
    public bool TryGetInt32(out int value)
    {
        if (Kind == PostScriptTokenKind.Integer && Value is >= int.MinValue and <= int.MaxValue)
        {
            value = (int)Value;
            return true;
        }

        value = 0;
        return false;
    }
}

/// <summary>
/// Splits the clear text and the decrypted private portion of a Type 1 program into PostScript tokens: the subset a Type 1 program
/// is written in, read with a state machine rather than a PostScript interpreter.
/// </summary>
/// <remarks>
/// Type 1 Font Format chapter 10 (the parsing rules of Adobe Type Manager) and the PostScript Language Reference §3.2: white space
/// (NUL, TAB, LF, FF, CR, SP), comments from <c>%</c> to the end of the line, literal and executable names, integers, reals and radix
/// numbers, literal strings with nested parentheses and escapes, hexadecimal strings, and the delimiters <c>[ ] { } &lt;&lt; &gt;&gt;</c>.
/// After <c>n RD</c> (or <c>-|</c>) a reader takes one separator byte and then <c>n</c> binary bytes with <see cref="ReadBinary"/>.
/// Never throws: malformed input becomes executable names, unterminated strings run to the end of the input.
/// </remarks>
internal ref struct PostScriptTokenizer
{
    private static readonly SearchValues<byte> WhiteSpace = SearchValues.Create("\0\t\n\f\r "u8);
    private static readonly SearchValues<byte> Delimiters = SearchValues.Create("()<>[]{}/%"u8);
    private static readonly SearchValues<byte> NumberCharacters = SearchValues.Create("0123456789+-.eE"u8);

    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    /// <summary>Initializes a new instance of the <see cref="PostScriptTokenizer"/> struct.</summary>
    public PostScriptTokenizer(ReadOnlySpan<byte> data, int position = 0)
    {
        _data = data;
        _position = position;
    }

    /// <summary>Gets the position after the last token read.</summary>
    public readonly int Position => _position;

    /// <summary>Whether a byte is PostScript white space.</summary>
    public static bool IsWhiteSpace(byte value) => WhiteSpace.Contains(value);

    /// <summary>Gets a token's text (without the slash of a literal name).</summary>
    public readonly ReadOnlySpan<byte> Text(in PostScriptToken token) => _data.Slice(token.Start, token.Length);

    /// <summary>Whether a token is the executable name <paramref name="name"/>.</summary>
    public readonly bool IsName(in PostScriptToken token, ReadOnlySpan<byte> name) =>
        token.Kind == PostScriptTokenKind.Name && Text(token).SequenceEqual(name);

    /// <summary>Whether a token is the literal name <paramref name="name"/>.</summary>
    public readonly bool IsLiteral(in PostScriptToken token, ReadOnlySpan<byte> name) =>
        token.Kind == PostScriptTokenKind.LiteralName && Text(token).SequenceEqual(name);

    /// <summary>Reads the next token.</summary>
    public PostScriptToken Next()
    {
        ReadOnlySpan<byte> data = _data;
        while (true)
        {
            int skip = data[_position..].IndexOfAnyExcept(WhiteSpace);
            if (skip < 0)
            {
                _position = data.Length;
                return new PostScriptToken(PostScriptTokenKind.EndOfInput, data.Length, 0, 0);
            }

            _position += skip;
            if (data[_position] != (byte)'%')
            {
                break;
            }

            int end = data[_position..].IndexOfAny((byte)'\r', (byte)'\n');
            _position = end < 0 ? data.Length : _position + end;
        }

        int start = _position;
        byte first = data[start];
        switch (first)
        {
            case (byte)'[':
                _position++;
                return new PostScriptToken(PostScriptTokenKind.ArrayStart, start, 1, 0);
            case (byte)']':
                _position++;
                return new PostScriptToken(PostScriptTokenKind.ArrayEnd, start, 1, 0);
            case (byte)'{':
                _position++;
                return new PostScriptToken(PostScriptTokenKind.ProcedureStart, start, 1, 0);
            case (byte)'}':
                _position++;
                return new PostScriptToken(PostScriptTokenKind.ProcedureEnd, start, 1, 0);
            case (byte)'(':
                return ReadLiteralString(start);
            case (byte)'<':
                return ReadAngle(start);
            case (byte)'>':
                if (start + 1 < data.Length && data[start + 1] == (byte)'>')
                {
                    _position += 2;
                    return new PostScriptToken(PostScriptTokenKind.DictionaryDelimiter, start, 2, 0);
                }

                _position++;
                return new PostScriptToken(PostScriptTokenKind.Name, start, 1, 0);
            case (byte)')':
                _position++;
                return new PostScriptToken(PostScriptTokenKind.Name, start, 1, 0);
            case (byte)'/':
                {
                    int nameStart = start + 1;
                    if (nameStart < data.Length && data[nameStart] == (byte)'/')
                    {
                        nameStart++; // an immediately evaluated name, //name: read as a literal
                    }

                    int length = RegularLength(nameStart);
                    _position = nameStart + length;
                    return new PostScriptToken(PostScriptTokenKind.LiteralName, nameStart, length, 0);
                }

            default:
                {
                    int length = RegularLength(start);
                    _position = start + length;
                    ReadOnlySpan<byte> text = data.Slice(start, length);
                    return TryParseNumber(text, out double value, out bool isInteger)
                        ? new PostScriptToken(isInteger ? PostScriptTokenKind.Integer : PostScriptTokenKind.Real, start, length, value)
                        : new PostScriptToken(PostScriptTokenKind.Name, start, length, 0);
                }
        }
    }

    /// <summary>Reads the next token without consuming it.</summary>
    public readonly PostScriptToken Peek()
    {
        PostScriptTokenizer copy = this;
        return copy.Next();
    }

    /// <summary>
    /// Takes the binary data that follows <c>n RD</c>: one separator byte after the <c>RD</c> token, then <paramref name="count"/>
    /// bytes, fewer when the input ends first.
    /// </summary>
    /// <param name="count">The number of bytes the program declares.</param>
    /// <param name="start">Where the bytes start.</param>
    /// <param name="length">How many bytes are present.</param>
    /// <returns><see langword="false"/> when fewer than <paramref name="count"/> bytes are left.</returns>
    /// <remarks>Type 1 Font Format §2.5 and chapter 10: exactly one blank separates <c>RD</c> from the binary data.</remarks>
    public bool ReadBinary(int count, out int start, out int length)
    {
        start = Math.Min(_position + 1, _data.Length);
        int available = _data.Length - start;
        length = Math.Min(Math.Max(count, 0), available);
        _position = start + length;
        return length == count;
    }

    /// <summary>Skips a value: a balanced array or procedure, or a single token.</summary>
    public void SkipValue()
    {
        PostScriptToken token = Next();
        if (token.Kind is PostScriptTokenKind.ArrayStart or PostScriptTokenKind.ProcedureStart)
        {
            SkipToClose();
        }
    }

    /// <summary>Skips tokens up to and including the bracket that closes one already read.</summary>
    public void SkipToClose()
    {
        int depth = 1;
        while (depth > 0)
        {
            PostScriptToken token = Next();
            switch (token.Kind)
            {
                case PostScriptTokenKind.EndOfInput:
                    return;
                case PostScriptTokenKind.ArrayStart or PostScriptTokenKind.ProcedureStart:
                    depth++;
                    break;
                case PostScriptTokenKind.ArrayEnd or PostScriptTokenKind.ProcedureEnd:
                    depth--;
                    break;
            }
        }
    }

    /// <summary>PostScript Language Reference §3.2.2: integers (optionally signed), reals with a point or an exponent, and radix numbers.</summary>
    private static bool TryParseNumber(ReadOnlySpan<byte> text, out double value, out bool isInteger)
    {
        value = 0;
        isInteger = false;
        if (text.IsEmpty)
        {
            return false;
        }

        int hash = text.IndexOf((byte)'#');
        if (hash > 0)
        {
            return TryParseRadix(text, hash, out value, out isInteger);
        }

        if (text.ContainsAnyExcept(NumberCharacters) || !text.ContainsAnyInRange((byte)'0', (byte)'9'))
        {
            return false;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value))
        {
            value = 0;
            return false;
        }

        isInteger = !text.ContainsAny((byte)'.', (byte)'e', (byte)'E') && Math.Abs(value) < 9007199254740992.0;
        return true;
    }

    private static bool TryParseRadix(ReadOnlySpan<byte> text, int hash, out double value, out bool isInteger)
    {
        value = 0;
        isInteger = false;
        int radix = 0;
        foreach (byte digit in text[..hash])
        {
            if (digit is < (byte)'0' or > (byte)'9' || radix > 36)
            {
                return false;
            }

            radix = (radix * 10) + (digit - '0');
        }

        ReadOnlySpan<byte> digits = text[(hash + 1)..];
        if (radix is < 2 or > 36 || digits.IsEmpty)
        {
            return false;
        }

        long result = 0;
        foreach (byte character in digits)
        {
            int digit = character switch
            {
                >= (byte)'0' and <= (byte)'9' => character - '0',
                >= (byte)'a' and <= (byte)'z' => character - 'a' + 10,
                >= (byte)'A' and <= (byte)'Z' => character - 'A' + 10,
                _ => 99,
            };
            if (digit >= radix)
            {
                return false;
            }

            result = (result * radix) + digit;
            if (result > uint.MaxValue)
            {
                return false;
            }
        }

        // Radix numbers are unsigned 32-bit patterns read as signed integers (PostScript Language Reference §3.2.2).
        value = (int)(uint)result;
        isInteger = true;
        return true;
    }

    private readonly int RegularLength(int start)
    {
        ReadOnlySpan<byte> rest = _data[start..];
        int index = rest.IndexOfAny(Delimiters);
        int white = rest.IndexOfAny(WhiteSpace);
        if (index < 0 || (white >= 0 && white < index))
        {
            index = white;
        }

        return index < 0 ? rest.Length : index;
    }

    private PostScriptToken ReadLiteralString(int start)
    {
        int depth = 0;
        int position = start;
        while (position < _data.Length)
        {
            byte value = _data[position++];
            if (value == (byte)'\\')
            {
                position++;
            }
            else if (value == (byte)'(')
            {
                depth++;
            }
            else if (value == (byte)')' && --depth == 0)
            {
                break;
            }
        }

        _position = Math.Min(position, _data.Length);
        return new PostScriptToken(PostScriptTokenKind.String, start, _position - start, 0);
    }

    private PostScriptToken ReadAngle(int start)
    {
        if (start + 1 < _data.Length && _data[start + 1] == (byte)'<')
        {
            _position = start + 2;
            return new PostScriptToken(PostScriptTokenKind.DictionaryDelimiter, start, 2, 0);
        }

        ReadOnlySpan<byte> terminator = start + 1 < _data.Length && _data[start + 1] == (byte)'~' ? "~>"u8 : ">"u8;
        int end = _data[(start + 1)..].IndexOf(terminator);
        _position = end < 0 ? _data.Length : start + 1 + end + terminator.Length;
        return new PostScriptToken(PostScriptTokenKind.String, start, _position - start, 0);
    }
}
