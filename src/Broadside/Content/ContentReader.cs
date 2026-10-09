using System.Buffers;
using System.Globalization;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>Deviations the <see cref="ContentReader"/> repaired while reading the last operator; the interpreter reports them.</summary>
[Flags]
internal enum ReaderIssues
{
    None = 0,

    /// <summary>A keyword that is not an operator was split into operators and numbers written without white-space between them.</summary>
    GluedTokens = 1,

    /// <summary>A token that cannot appear here (a lone <c>)</c> or <c>&gt;</c>, an unbalanced <c>]</c> or <c>&gt;&gt;</c>, a malformed number, string or name) was skipped or repaired.</summary>
    SyntaxInvalid = 2,

    /// <summary>An array or dictionary operand was still open when an operator came; it was closed there.</summary>
    ContainerUnclosed = 4,

    /// <summary>An inline image had no <c>ID</c> or no <c>EI</c>, or a dictionary entry that is not a value.</summary>
    InlineImageInvalid = 8,
}

/// <summary>One operator read by <see cref="ContentReader"/>, with byte ranges in the content (its operands are in the arena).</summary>
/// <param name="Code">The operator.</param>
/// <param name="Start">The offset of its first operand, or of the keyword when it has none.</param>
/// <param name="KeywordStart">The offset of the keyword.</param>
/// <param name="KeywordLength">The length of the keyword.</param>
/// <param name="End">The offset one past the operator: past the keyword, or past <c>EI</c> for an inline image.</param>
/// <param name="DataStart">For an inline image, the offset of its data.</param>
/// <param name="DataLength">For an inline image, the length of its data.</param>
internal readonly record struct ReadOperator(ContentOperatorCode Code, int Start, int KeywordStart, int KeywordLength, int End, int DataStart = 0, int DataLength = 0);

/// <summary>
/// Reads a decoded content stream as a sequence of operators, each preceded by its operands in an <see cref="OperandArena"/>, without
/// allocating: no object per operand, no string per keyword.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.8.2: content is PDF object syntax (§7.2, §7.3) in postfix order. Tokens come from <see cref="CosLexer"/>; numbers
/// are read leniently as the object parser reads them (<c>--5</c>, <c>5.</c>, <c>1.2.3</c>).
/// </para>
/// <para>
/// A keyword that is not an operator but splits completely into operators and numbers (<c>q1</c>, <c>Qq</c>, <c>0cm</c>,
/// <c>BTq</c>) is read as those tokens, as other readers do; any other keyword is an unknown operator. An inline image
/// (§8.9.7) is read whole: <c>BI</c>, its dictionary as one dictionary operand, and the data between <c>ID</c> and <c>EI</c> as
/// a byte range that is never tokenized. The end of the data is found from the dictionary's <c>L</c>/<c>Length</c> when it points
/// at <c>EI</c>, else by looking for <c>EI</c> between white-space followed by text that is not binary.
/// </para>
/// </remarks>
internal ref struct ContentReader
{
    private const int MaxGluedLength = 32;
    private const int InlineImageLookahead = 64;

    private static readonly SearchValues<byte> Whitespace = SearchValues.Create("\0\t\n\f\r "u8);
    private static readonly SearchValues<byte> NumberCharacters = SearchValues.Create("+-.0123456789"u8);

    private readonly OperandArena _arena;
    private CosLexer _lexer;

    public ContentReader(ReadOnlySpan<byte> source, OperandArena arena)
    {
        _lexer = new CosLexer(source);
        _arena = arena;
    }

    /// <summary>Gets the deviations repaired since they were last cleared.</summary>
    public ReaderIssues Issues { get; private set; }

    /// <summary>Gets the offset of the first deviation since they were last cleared.</summary>
    public int IssueOffset { get; private set; }

    /// <summary>Gets the content.</summary>
    public readonly ReadOnlySpan<byte> Source => _lexer.Source;

    /// <summary>Forgets the recorded deviations.</summary>
    public void ClearIssues() => Issues = ReaderIssues.None;

    /// <summary>
    /// Reads operands into the arena up to the next operator. Returns false at the end of the content, leaving any operands read
    /// since the last operator in the arena.
    /// </summary>
    public bool Next(out ReadOperator op)
    {
        int first = -1;
        while (true)
        {
            CosToken token = _lexer.Next();
            if (token.Kind == CosTokenKind.EndOfInput)
            {
                op = default;
                return false;
            }

            if (first < 0 || _arena.Count == 0 && _arena.OpenDepth == 0)
            {
                first = token.Start;
            }

            if (token.Kind != CosTokenKind.Keyword)
            {
                AddOperand(token);
                continue;
            }

            ReadOnlySpan<byte> keyword = Bytes(token);
            if (TryAddKeywordValue(keyword))
            {
                continue;
            }

            int length = keyword.Length;
            ContentOperatorCode code = OperatorTable.Lookup(keyword);
            if (code == ContentOperatorCode.Unknown && SplitGlued(keyword) is > 0 and var piece)
            {
                Issue(ReaderIssues.GluedTokens, token.Start);
                _lexer.Position = token.Start + piece;
                if (NumberCharacters.Contains(keyword[0]))
                {
                    AddNumber(keyword[..piece], token.Start);
                    continue;
                }

                length = piece;
                code = OperatorTable.Lookup(keyword[..piece]);
            }

            if (_arena.OpenDepth > 0)
            {
                if (code == ContentOperatorCode.Unknown)
                {
                    // A stray keyword inside an array or dictionary operand: not a value, so it is skipped.
                    Issue(ReaderIssues.SyntaxInvalid, token.Start);
                    continue;
                }

                _arena.CloseAll();
                Issue(ReaderIssues.ContainerUnclosed, token.Start);
            }

            if (code == ContentOperatorCode.BeginInlineImage)
            {
                op = ReadInlineImage(first, token.Start);
                return true;
            }

            op = new ReadOperator(code, first, token.Start, length, token.Start + length);
            return true;
        }
    }

    /// <summary>
    /// Finds where the data of an inline image ends, as the offset of its <c>EI</c>: the first <c>EI</c> after <paramref name="start"/>
    /// with white-space before it and white-space or the end after it, followed by text that is not binary. -1 when there is none.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.9.7. The same heuristic as pdf.js (<c>findDefaultInlineStreamEnd</c>), for data without a usable <c>L</c>.</remarks>
    internal static int FindInlineImageEnd(ReadOnlySpan<byte> source, int start)
    {
        int position = start;
        while (position < source.Length)
        {
            int found = source[position..].IndexOf("EI"u8);
            if (found < 0)
            {
                return -1;
            }

            int candidate = position + found;
            position = candidate + 1;
            bool before = candidate == start || CosLexer.IsWhitespace(source[candidate - 1]);
            bool after = candidate + 2 == source.Length || CosLexer.IsWhitespace(source[candidate + 2]);
            if (before && after && !LooksBinary(source.Slice(candidate + 2, Math.Min(InlineImageLookahead, source.Length - candidate - 2))))
            {
                return candidate;
            }
        }

        return -1;
    }

    /// <summary>
    /// For a keyword that is not an operator: the length of its first piece when the whole keyword splits into known operators and
    /// numbers (longest operator first), else 0.
    /// </summary>
    private static int SplitGlued(ReadOnlySpan<byte> keyword)
    {
        if (keyword.Length > MaxGluedLength)
        {
            return 0;
        }

        int first = 0;
        int position = 0;
        while (position < keyword.Length)
        {
            int piece = 0;
            if (NumberCharacters.Contains(keyword[position]))
            {
                ReadOnlySpan<byte> rest = keyword[position..];
                int run = rest.IndexOfAnyExcept(NumberCharacters);
                run = run < 0 ? rest.Length : run;
                piece = rest[..run].ContainsAnyInRange((byte)'0', (byte)'9') ? run : 0;
            }
            else
            {
                for (int length = Math.Min(OperatorTable.MaxKeywordLength, keyword.Length - position); length > 0 && piece == 0; length--)
                {
                    piece = OperatorTable.Lookup(keyword.Slice(position, length)) != ContentOperatorCode.Unknown ? length : 0;
                }
            }

            if (piece == 0)
            {
                return 0;
            }

            first = first == 0 ? piece : first;
            position += piece;
        }

        return first;
    }

    private static bool LooksBinary(ReadOnlySpan<byte> text)
    {
        foreach (byte value in text)
        {
            if (value is < 0x09 or (> 0x0D and < 0x20) or >= 0x7F)
            {
                return true;
            }
        }

        return false;
    }

    private readonly ReadOnlySpan<byte> Bytes(CosToken token) => _lexer.Source.Slice(token.Start, token.Length);

    private void Issue(ReaderIssues issue, int offset)
    {
        if (Issues == ReaderIssues.None)
        {
            IssueOffset = offset;
        }

        Issues |= issue;
    }

    private readonly bool TryAddKeywordValue(ReadOnlySpan<byte> keyword)
    {
        if (keyword.SequenceEqual("true"u8) || keyword.SequenceEqual("false"u8))
        {
            _arena.AddBoolean(keyword[0] == (byte)'t');
            return true;
        }

        if (keyword.SequenceEqual("null"u8))
        {
            _arena.AddNull();
            return true;
        }

        return false;
    }

    /// <summary>Adds a token that is not a keyword as an operand, or repairs it.</summary>
    private void AddOperand(CosToken token)
    {
        switch (token.Kind)
        {
            case CosTokenKind.Integer or CosTokenKind.Real:
                AddNumber(Bytes(token), token.Start);
                break;
            case CosTokenKind.LiteralString:
                AddLiteralString(token);
                break;
            case CosTokenKind.HexString:
                AddHexString(token);
                break;
            case CosTokenKind.Name:
                AddName(token);
                break;
            case CosTokenKind.ArrayStart:
                _arena.BeginContainer(ContentOperandKind.Array);
                break;
            case CosTokenKind.DictionaryStart:
                _arena.BeginContainer(ContentOperandKind.Dictionary);
                break;
            case CosTokenKind.ArrayEnd:
                if (!_arena.EndContainer(ContentOperandKind.Array))
                {
                    Issue(ReaderIssues.SyntaxInvalid, token.Start);
                }

                break;
            case CosTokenKind.DictionaryEnd:
                if (!_arena.EndContainer(ContentOperandKind.Dictionary))
                {
                    Issue(ReaderIssues.SyntaxInvalid, token.Start);
                }

                break;
            default:
                Issue(ReaderIssues.SyntaxInvalid, token.Start);
                break;
        }
    }

    private void AddNumber(ReadOnlySpan<byte> text, int offset)
    {
        if (CosParser.TryParseStrictInteger(text, out long integer))
        {
            _arena.AddNumber(integer, isInteger: true);
            return;
        }

        Span<byte> scratch = stackalloc byte[256];
        scoped ReadOnlySpan<byte> number = text;
        if (!CosParser.IsWellFormedNumber(text))
        {
            Issue(ReaderIssues.SyntaxInvalid, offset);
            number = CosParser.WellFormedPrefix(text, scratch);
            if (!number.Contains((byte)'.') && CosParser.TryParseStrictInteger(number, out integer))
            {
                _arena.AddNumber(integer, isInteger: true);
                return;
            }
        }

        double value = double.Parse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (!double.IsFinite(value))
        {
            Issue(ReaderIssues.SyntaxInvalid, offset);
            value = double.IsNegative(value) ? double.MinValue : double.MaxValue;
        }

        // An integer too large for 64 bits reads as a real (Annex C).
        _arena.AddNumber(value, isInteger: false);
    }

    private void AddLiteralString(CosToken token)
    {
        ReadOnlySpan<byte> text = Bytes(token);
        Span<byte> output = _arena.ReserveBytes(text.Length);
        int written = 0;
        int depth = 1;
        int index = 1;
        while (index < text.Length)
        {
            byte current = text[index];
            switch (current)
            {
                case (byte)'\\':
                    index = CosParser.ReadEscape(text, index, output, ref written);
                    continue;
                case (byte)'(':
                    depth++;
                    break;
                case (byte)')':
                    if (--depth == 0)
                    {
                        index = text.Length;
                        continue;
                    }

                    break;
                case (byte)'\r':
                    // §7.3.4.2: an unescaped end-of-line marker is read as a single LINE FEED.
                    current = (byte)'\n';
                    if (index + 1 < text.Length && text[index + 1] == (byte)'\n')
                    {
                        index++;
                    }

                    break;
            }

            output[written++] = current;
            index++;
        }

        if (depth != 0)
        {
            Issue(ReaderIssues.SyntaxInvalid, token.Start);
        }

        _arena.CommitBytes(ContentOperandKind.String, written);
    }

    private void AddHexString(CosToken token)
    {
        ReadOnlySpan<byte> text = Bytes(token);
        bool terminated = text.Length >= 2 && text[^1] == (byte)'>';
        ReadOnlySpan<byte> digits = terminated ? text[1..^1] : text[1..];
        Span<byte> output = _arena.ReserveBytes((digits.Length + 1) / 2);
        int written = 0;
        int high = -1;
        foreach (byte character in digits)
        {
            int nibble = CosParser.HexValue(character);
            if (nibble < 0)
            {
                if (!CosLexer.IsWhitespace(character))
                {
                    Issue(ReaderIssues.SyntaxInvalid, token.Start);
                }

                continue;
            }

            if (high < 0)
            {
                high = nibble;
            }
            else
            {
                output[written++] = (byte)((high << 4) | nibble);
                high = -1;
            }
        }

        if (high >= 0)
        {
            // §7.3.4.3: a missing final digit is read as 0.
            output[written++] = (byte)(high << 4);
        }

        if (!terminated)
        {
            Issue(ReaderIssues.SyntaxInvalid, token.Start);
        }

        _arena.CommitBytes(ContentOperandKind.String, written, hexadecimal: true);
    }

    private void AddName(CosToken token)
    {
        ReadOnlySpan<byte> text = Bytes(token)[1..];
        Span<byte> output = _arena.ReserveBytes(text.Length);
        int written = 0;
        for (int index = 0; index < text.Length; index++)
        {
            byte current = text[index];
            if (current == (byte)'#')
            {
                int high = index + 1 < text.Length ? CosParser.HexValue(text[index + 1]) : -1;
                int low = index + 2 < text.Length ? CosParser.HexValue(text[index + 2]) : -1;
                if (high < 0 || low < 0)
                {
                    Issue(ReaderIssues.SyntaxInvalid, token.Start);
                }
                else
                {
                    index += 2;
                    current = (byte)((high << 4) | low);
                    if (current == 0)
                    {
                        Issue(ReaderIssues.SyntaxInvalid, token.Start);
                        continue;
                    }
                }
            }

            output[written++] = current;
        }

        _arena.CommitBytes(ContentOperandKind.Name, written);
    }

    /// <summary>Reads an inline image after its <c>BI</c>: the dictionary into the arena, then the data range up to <c>EI</c>.</summary>
    private ReadOperator ReadInlineImage(int first, int keywordStart)
    {
        _arena.BeginContainer(ContentOperandKind.Dictionary);
        while (true)
        {
            CosToken token = _lexer.Next();
            if (token.Kind == CosTokenKind.EndOfInput)
            {
                _arena.CloseAll();
                Issue(ReaderIssues.InlineImageInvalid, keywordStart);
                return new ReadOperator(ContentOperatorCode.BeginInlineImage, first, keywordStart, 2, Source.Length);
            }

            if (token.Kind != CosTokenKind.Keyword)
            {
                AddOperand(token);
                continue;
            }

            ReadOnlySpan<byte> keyword = Bytes(token);
            if (TryAddKeywordValue(keyword))
            {
                continue;
            }

            _arena.CloseAll();
            if (!keyword.SequenceEqual("ID"u8))
            {
                // Not a value and not ID: the image is abandoned here and the keyword is read again as the next operator.
                Issue(ReaderIssues.InlineImageInvalid, token.Start);
                _lexer.Position = token.Start;
                return new ReadOperator(ContentOperatorCode.BeginInlineImage, first, keywordStart, 2, token.Start);
            }

            break;
        }

        ReadOnlySpan<byte> source = Source;
        int dataStart = _lexer.Position;
        if (dataStart < source.Length && CosLexer.IsWhitespace(source[dataStart]))
        {
            // §8.9.7: one white-space character separates ID from the data.
            dataStart++;
        }

        int end = DeclaredInlineImageEnd(source, dataStart, out int dataEnd);
        if (end < 0)
        {
            end = FindInlineImageEnd(source, dataStart);
            dataEnd = end > dataStart && CosLexer.IsWhitespace(source[end - 1]) ? end - 1 : end;
        }

        if (end < 0)
        {
            Issue(ReaderIssues.InlineImageInvalid, keywordStart);
            _lexer.Position = source.Length;
            return new ReadOperator(ContentOperatorCode.BeginInlineImage, first, keywordStart, 2, source.Length, dataStart, source.Length - dataStart);
        }

        _lexer.Position = end + 2;
        return new ReadOperator(ContentOperatorCode.BeginInlineImage, first, keywordStart, 2, end + 2, dataStart, Math.Max(0, dataEnd - dataStart));
    }

    /// <summary>The offset of <c>EI</c> when the dictionary's <c>L</c> or <c>Length</c> leads to it (optional white-space between), else -1.</summary>
    private readonly int DeclaredInlineImageEnd(ReadOnlySpan<byte> source, int dataStart, out int dataEnd)
    {
        dataEnd = -1;
        if (_arena.Count == 0 || _arena.Last(1)[0].Kind != ContentOperandKind.Dictionary)
        {
            return -1;
        }

        ContentOperands entries = _arena.Last(1)[0].Items;
        for (int index = 0; index + 1 < entries.Count; index += 2)
        {
            ContentOperand key = entries[index];
            ContentOperand value = entries[index + 1];
            if ((key.IsName("L"u8) || key.IsName("Length"u8)) && value.Kind == ContentOperandKind.Integer && value.Number >= 0
                && value.Number <= source.Length - dataStart)
            {
                int position = dataStart + (int)value.Number;
                int after = source[position..].IndexOfAnyExcept(Whitespace);
                position = after < 0 ? source.Length : position + after;
                if (source[position..].StartsWith("EI"u8) && (position + 2 == source.Length || !CosLexer.IsRegular(source[position + 2])))
                {
                    dataEnd = dataStart + (int)value.Number;
                    return position;
                }
            }
        }

        return -1;
    }
}
