using System.Buffers;
using System.Globalization;

namespace Broadside.Graphics.Functions;

/// <summary>The kinds of token in a Type 4 program.</summary>
internal enum PostScriptTokenKind
{
    /// <summary>The end of the program text.</summary>
    End,

    /// <summary><c>{</c>.</summary>
    OpenBrace,

    /// <summary><c>}</c>.</summary>
    CloseBrace,

    /// <summary>An integer that fits in 32 bits.</summary>
    Integer,

    /// <summary>A real, or an integer too large for 32 bits (a real in PostScript).</summary>
    Real,

    /// <summary>A run of regular characters that is not a number: an operator name, or something unknown.</summary>
    Name,

    /// <summary>A delimiter the subset does not allow (strings, arrays, dictionaries, literal names) or a malformed number.</summary>
    Invalid,
}

/// <summary>One token of a Type 4 program.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Start">The offset of its first byte.</param>
/// <param name="Length">Its length in bytes.</param>
/// <param name="Number">The value of a number.</param>
/// <param name="HasExponent">Whether a real was written with an exponent, which PDF number syntax does not have.</param>
internal readonly record struct PostScriptToken(PostScriptTokenKind Kind, int Start, int Length, double Number = 0, bool HasExponent = false);

/// <summary>Splits the text of a Type 4 function into tokens.</summary>
/// <remarks>
/// ISO 32000-2 §7.10.5.2: operands follow PDF conventions (§7.3.3 numbers, no radix notation), white-space and comments follow §7.2.3
/// and §7.2.4, and braces delimit the program and the bodies of <c>if</c> and <c>ifelse</c>. A real written with an exponent, which
/// PostScript allows and PDF does not, is read (and flagged).
/// </remarks>
internal ref struct PostScriptLexer
{
    private static readonly SearchValues<byte> Delimiters = SearchValues.Create("\0\t\n\f\r ()<>[]{}/%"u8);

    private readonly ReadOnlySpan<byte> _text;
    private int _position;

    /// <summary>Initializes a new instance of the <see cref="PostScriptLexer"/> struct.</summary>
    /// <param name="text">The decoded stream data.</param>
    public PostScriptLexer(ReadOnlySpan<byte> text)
    {
        _text = text;
        _position = 0;
    }

    /// <summary>The bytes of a token.</summary>
    /// <param name="token">The token.</param>
    /// <returns>Its bytes.</returns>
    public readonly ReadOnlySpan<byte> TextOf(PostScriptToken token) => _text.Slice(token.Start, token.Length);

    /// <summary>Reads the next token.</summary>
    /// <returns>The token; <see cref="PostScriptTokenKind.End"/> at the end.</returns>
    public PostScriptToken Next()
    {
        SkipWhiteSpaceAndComments();
        if (_position >= _text.Length)
        {
            return new PostScriptToken(PostScriptTokenKind.End, _position, 0);
        }

        int start = _position;
        byte first = _text[_position];
        switch (first)
        {
            case (byte)'{':
                _position++;
                return new PostScriptToken(PostScriptTokenKind.OpenBrace, start, 1);
            case (byte)'}':
                _position++;
                return new PostScriptToken(PostScriptTokenKind.CloseBrace, start, 1);
            case (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'/':
                _position++;
                return new PostScriptToken(PostScriptTokenKind.Invalid, start, 1);
        }

        int length = _text[start..].IndexOfAny(Delimiters);
        length = length < 0 ? _text.Length - start : length;
        _position = start + length;
        return Classify(_text.Slice(start, length), start);
    }

    private static PostScriptToken Classify(ReadOnlySpan<byte> word, int start)
    {
        int digits = 0;
        int dots = 0;
        int exponent = -1;
        for (int i = 0; i < word.Length; i++)
        {
            byte c = word[i];
            if (c is >= (byte)'0' and <= (byte)'9')
            {
                digits++;
            }
            else if (c == '.' && exponent < 0)
            {
                dots++;
            }
            else if (c is (byte)'+' or (byte)'-' && (i == 0 || i == exponent + 1))
            {
                // A sign, at the start or right after the exponent marker.
            }
            else if (c is (byte)'e' or (byte)'E' && exponent < 0 && digits > 0)
            {
                exponent = i;
            }
            else
            {
                // Not a number. A radix number (16#FF) starts like one but is not PDF syntax.
                return new PostScriptToken(digits > 0 && word.Contains((byte)'#') ? PostScriptTokenKind.Invalid : PostScriptTokenKind.Name, start, word.Length);
            }
        }

        if (digits == 0 || dots > 1 || exponent == word.Length - 1)
        {
            return new PostScriptToken(digits == 0 && dots == 0 && exponent < 0 && word.Length > 1 ? PostScriptTokenKind.Name : PostScriptTokenKind.Invalid, start, word.Length);
        }

        if (!double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        {
            return new PostScriptToken(PostScriptTokenKind.Invalid, start, word.Length);
        }

        bool integer = dots == 0 && exponent < 0 && value is >= int.MinValue and <= int.MaxValue;
        return new PostScriptToken(integer ? PostScriptTokenKind.Integer : PostScriptTokenKind.Real, start, word.Length, value, exponent >= 0);
    }

    private void SkipWhiteSpaceAndComments()
    {
        while (_position < _text.Length)
        {
            byte c = _text[_position];
            if (c is 0 or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'\r' or (byte)' ')
            {
                _position++;
            }
            else if (c == '%')
            {
                while (_position < _text.Length && _text[_position] is not ((byte)'\r' or (byte)'\n'))
                {
                    _position++;
                }
            }
            else
            {
                return;
            }
        }
    }
}
