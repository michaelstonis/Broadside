using System.Globalization;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>Reads the tokens of file structure (§7.5): unsigned integers and keywords, from a <see cref="CosLexer"/>.</summary>
internal static class StructureTokens
{
    /// <summary>
    /// Returns whether the token after <paramref name="position"/> starts and ends inside <paramref name="window"/>, so what was
    /// parsed up to <paramref name="position"/> cannot depend on bytes past the window (issue #45: windowed sources).
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="position">Where the parse ended.</param>
    /// <returns><see langword="true"/> when a whole token follows inside the window.</returns>
    public static bool NextTokenEndsInside(ReadOnlySpan<byte> window, int position)
    {
        CosToken next = new CosLexer(window, position).Next();
        return next.Kind != CosTokenKind.EndOfInput && next.End < window.Length;
    }

    /// <summary>Reads the next token as an unsigned decimal integer.</summary>
    /// <param name="lexer">The lexer; advanced past the token whether or not it is an integer.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when the token is digits only and fits a <see cref="long"/>.</returns>
    public static bool TryReadUnsigned(ref CosLexer lexer, out long value)
    {
        CosToken token = lexer.Next();
        return TryParseUnsigned(lexer.Source.Slice(token.Start, token.Length), token.Kind, out value);
    }

    /// <summary>Reads the next token and returns whether it is the keyword <paramref name="keyword"/>.</summary>
    /// <param name="lexer">The lexer; advanced past the token.</param>
    /// <param name="keyword">The expected keyword.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool TryReadKeyword(ref CosLexer lexer, ReadOnlySpan<byte> keyword)
    {
        CosToken token = lexer.Next();
        return IsKeyword(lexer.Source, token, keyword);
    }

    /// <summary>Returns whether <paramref name="token"/> is the keyword <paramref name="keyword"/>.</summary>
    /// <param name="source">The bytes the token is in.</param>
    /// <param name="token">The token.</param>
    /// <param name="keyword">The keyword.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsKeyword(ReadOnlySpan<byte> source, CosToken token, ReadOnlySpan<byte> keyword) =>
        token.Kind == CosTokenKind.Keyword && source.Slice(token.Start, token.Length).SequenceEqual(keyword);

    private static bool TryParseUnsigned(ReadOnlySpan<byte> text, CosTokenKind kind, out long value)
    {
        value = 0;
        return kind == CosTokenKind.Integer
            && text.Length > 0
            && text[0] is >= (byte)'0' and <= (byte)'9'
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
