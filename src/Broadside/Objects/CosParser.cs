using System.Buffers;
using System.Globalization;

namespace Broadside.Objects;

/// <summary>Builds COS objects from the tokens of a <see cref="CosLexer"/>.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.3. The parser is lenient and never throws on any input: every deviation from the syntax is repaired and reported to
/// the <see cref="CosRepairLog"/> (ADR 0005); a caller that wants strictness checks the log. Nesting deeper than
/// <see cref="MaxDepth"/> is skipped, so hostile input cannot exhaust the stack.
/// </para>
/// <para>
/// The parser reads objects only. The <c>N G obj</c> … <c>endobj</c> wrapper, cross-reference sections and the trailer are file
/// structure (§7.5) and belong to the file reader, which positions this parser at each object.
/// </para>
/// </remarks>
internal ref struct CosParser
{
    /// <summary>The deepest nesting of arrays and dictionaries the parser builds; deeper containers are skipped and read as null.</summary>
    public const int MaxDepth = 256;

    private static readonly SearchValues<byte> DigitsAndPeriod = SearchValues.Create("0123456789."u8);

    private CosLexer _lexer;
    private readonly CosRepairLog? _repairs;
    private readonly IStreamLengthResolver? _lengthResolver;

    /// <summary>Initializes a new instance of the <see cref="CosParser"/> struct.</summary>
    /// <param name="source">The bytes to parse.</param>
    /// <param name="repairs">Where to report repairs, or <see langword="null"/> to discard them.</param>
    /// <param name="position">The offset to start at.</param>
    /// <param name="lengthResolver">
    /// Resolves a stream's <c>Length</c> entry when it is an indirect reference (§7.3.8.2), or <see langword="null"/> when there is no
    /// file to resolve it in; the data then ends at <c>endstream</c> without a repair.
    /// </param>
    public CosParser(ReadOnlySpan<byte> source, CosRepairLog? repairs, int position = 0, IStreamLengthResolver? lengthResolver = null)
    {
        _lexer = new CosLexer(source, position);
        _repairs = repairs;
        _lengthResolver = lengthResolver;
    }

    /// <summary>Gets or sets the offset of the next byte to read.</summary>
    public int Position
    {
        readonly get => _lexer.Position;
        set => _lexer.Position = value;
    }

    /// <summary>Returns whether only white-space and comments remain.</summary>
    public bool IsAtEnd()
    {
        _lexer.SkipWhitespaceAndComments();
        return _lexer.Position >= _lexer.Source.Length;
    }

    /// <summary>Reports a repair when anything other than white-space and comments remains.</summary>
    public void ExpectEndOfInput()
    {
        if (!IsAtEnd())
        {
            Report(CosRepairCodes.TrailingContent, _lexer.Position, "Expected the end of the input after the object.");
        }
    }

    /// <summary>Reads one object. At the end of input, or on a token that cannot start an object, reports a repair and returns null.</summary>
    public CosObject ParseObject() => ParseObject(depth: 0);

    private CosObject ParseObject(int depth)
    {
        CosToken token = _lexer.Next();
        switch (token.Kind)
        {
            case CosTokenKind.EndOfInput:
                Report(CosRepairCodes.UnexpectedEndOfInput, token.Start, "Expected an object.");
                return CosNull.Instance;
            case CosTokenKind.Integer:
                return ParseIntegerOrReference(token);
            case CosTokenKind.Real:
                return ParseNumber(token);
            case CosTokenKind.LiteralString:
                return ParseLiteralString(token);
            case CosTokenKind.HexString:
                return ParseHexString(token);
            case CosTokenKind.Name:
                return ParseName(token);
            case CosTokenKind.ArrayStart:
                return depth >= MaxDepth ? SkipTooDeep(token) : ParseArray(token, depth + 1);
            case CosTokenKind.DictionaryStart:
                return depth >= MaxDepth ? SkipTooDeep(token) : ParseDictionaryOrStream(token, depth + 1);
            case CosTokenKind.Keyword:
                ReadOnlySpan<byte> keyword = Bytes(token);
                if (keyword.SequenceEqual("true"u8))
                {
                    return CosBoolean.True;
                }

                if (keyword.SequenceEqual("false"u8))
                {
                    return CosBoolean.False;
                }

                if (keyword.SequenceEqual("null"u8))
                {
                    return CosNull.Instance;
                }

                break;
        }

        ReportUnexpected(token);
        return CosNull.Instance;
    }

    private CosObject ParseIntegerOrReference(CosToken first)
    {
        CosNumber number = ParseNumber(first);
        if (number is not CosInteger { Value: var objectNumber })
        {
            return number;
        }

        int afterFirst = _lexer.Position;
        CosToken second = _lexer.Next();
        if (second.Kind == CosTokenKind.Integer)
        {
            CosToken third = _lexer.Next();
            if (third.Kind == CosTokenKind.Keyword && Bytes(third).SequenceEqual("R"u8))
            {
                bool validGeneration = TryParseStrictInteger(Bytes(second), out long generation);
                if (objectNumber is >= 1 and <= int.MaxValue && validGeneration && generation is >= 0 and <= CosReference.MaxGeneration)
                {
                    return new CosReference((int)objectNumber, (int)generation);
                }

                Report(
                    CosRepairCodes.ReferenceInvalid,
                    first.Start,
                    "An indirect reference needs a positive object number and a generation number from 0 to 65535; read as null.");
                return CosNull.Instance;
            }
        }

        _lexer.Position = afterFirst;
        return number;
    }

    private readonly CosNumber ParseNumber(CosToken token)
    {
        ReadOnlySpan<byte> text = Bytes(token);
        if (token.Kind == CosTokenKind.Integer && TryParseStrictInteger(text, out long integer))
        {
            return new CosInteger(integer);
        }

        if (!IsWellFormedNumber(text))
        {
            Report(CosRepairCodes.NumberMalformed, token.Start, "A number must be digits with an optional sign and at most one period.");
            Span<byte> scratch = text.Length < 256 ? stackalloc byte[256] : new byte[text.Length + 2];
            ReadOnlySpan<byte> prefix = WellFormedPrefix(text, scratch);
            return !prefix.Contains((byte)'.') && TryParseStrictInteger(prefix, out integer)
                ? new CosInteger(integer)
                : ParseReal(prefix, token);
        }

        // A well-formed real, or an integer too large for 64 bits (Annex C: read as a real).
        return ParseReal(text, token);
    }

    private readonly CosReal ParseReal(ReadOnlySpan<byte> text, CosToken token)
    {
        double value = double.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (!double.IsFinite(value))
        {
            Report(CosRepairCodes.NumberOutOfRange, token.Start, "The number is too large for a double; clamped to the largest finite value.");
            value = double.IsNegative(value) ? double.MinValue : double.MaxValue;
        }

        return new CosReal(value);
    }

    private readonly CosString ParseLiteralString(CosToken token)
    {
        ReadOnlySpan<byte> text = Bytes(token);
        byte[]? rented = null;
        Span<byte> output = text.Length <= 256 ? stackalloc byte[256] : (rented = ArrayPool<byte>.Shared.Rent(text.Length));
        int written = 0;
        int depth = 1;
        int index = 1;
        while (index < text.Length)
        {
            byte current = text[index];
            switch (current)
            {
                case (byte)'\\':
                    index = ReadEscape(text, index, output, ref written);
                    continue;
                case (byte)'(':
                    depth++;
                    break;
                case (byte)')':
                    depth--;
                    if (depth == 0)
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
            Report(CosRepairCodes.StringUnterminated, token.Start, "The literal string has no closing parenthesis; it runs to the end of the input.");
        }

        var result = CosString.FromOwnedBytes(output[..written].ToArray(), hexadecimal: false);
        if (rented is not null)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        return result;
    }

    /// <summary>Reads the escape sequence at <paramref name="text"/>[<paramref name="index"/>] (a backslash) and returns the index after it (§7.3.4.2, Table 3).</summary>
    private static int ReadEscape(ReadOnlySpan<byte> text, int index, Span<byte> output, ref int written)
    {
        if (index + 1 >= text.Length)
        {
            // A backslash as the last byte of an unterminated string: nothing follows it to escape.
            return text.Length;
        }

        byte escaped = text[index + 1];
        switch (escaped)
        {
            case (byte)'n':
                output[written++] = (byte)'\n';
                return index + 2;
            case (byte)'r':
                output[written++] = (byte)'\r';
                return index + 2;
            case (byte)'t':
                output[written++] = (byte)'\t';
                return index + 2;
            case (byte)'b':
                output[written++] = 0x08;
                return index + 2;
            case (byte)'f':
                output[written++] = 0x0C;
                return index + 2;
            case (byte)'(' or (byte)')' or (byte)'\\':
                output[written++] = escaped;
                return index + 2;
            case (byte)'\r':
                // A backslash before an end-of-line marker continues the string on the next line; both are dropped.
                return index + 2 < text.Length && text[index + 2] == (byte)'\n' ? index + 3 : index + 2;
            case (byte)'\n':
                return index + 2;
            case >= (byte)'0' and <= (byte)'7':
                int code = 0;
                int digit = index + 1;
                int end = Math.Min(index + 4, text.Length);
                while (digit < end && text[digit] is >= (byte)'0' and <= (byte)'7')
                {
                    code = (code * 8) + (text[digit] - '0');
                    digit++;
                }

                // High-order overflow of a three-digit code is ignored.
                output[written++] = (byte)code;
                return digit;
            default:
                // Not an escape sequence: the backslash is ignored and the byte after it is read normally.
                return index + 1;
        }
    }

    private readonly CosString ParseHexString(CosToken token)
    {
        ReadOnlySpan<byte> text = Bytes(token);
        bool terminated = text.Length >= 2 && text[^1] == (byte)'>';
        ReadOnlySpan<byte> digits = terminated ? text[1..^1] : text[1..];
        byte[] output = new byte[(digits.Length + 1) / 2];
        int written = 0;
        int high = -1;
        bool reportedInvalid = false;
        foreach (byte character in digits)
        {
            int nibble = HexValue(character);
            if (nibble < 0)
            {
                if (!CosLexer.IsWhitespace(character) && !reportedInvalid)
                {
                    Report(CosRepairCodes.HexStringInvalidDigit, token.Start, "The hexadecimal string contains a byte that is neither a hexadecimal digit nor white-space; ignored.");
                    reportedInvalid = true;
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
            Report(CosRepairCodes.HexStringUnterminated, token.Start, "The hexadecimal string has no closing angle bracket; it runs to the end of the input.");
        }

        return CosString.FromOwnedBytes(written == output.Length ? output : output[..written], hexadecimal: true);
    }

    private readonly CosName ParseName(CosToken token)
    {
        ReadOnlySpan<byte> text = Bytes(token)[1..];
        if (!text.Contains((byte)'#'))
        {
            return CosName.FromOwnedBytes(text.ToArray());
        }

        Span<byte> output = text.Length <= 256 ? stackalloc byte[256] : new byte[text.Length];
        int written = 0;
        for (int index = 0; index < text.Length; index++)
        {
            byte current = text[index];
            if (current == (byte)'#')
            {
                int high = index + 1 < text.Length ? HexValue(text[index + 1]) : -1;
                int low = index + 2 < text.Length ? HexValue(text[index + 2]) : -1;
                if (high < 0 || low < 0)
                {
                    Report(CosRepairCodes.NameInvalidEscape, token.Start, "A number sign in a name is not followed by two hexadecimal digits; read as a literal number sign.");
                }
                else
                {
                    index += 2;
                    current = (byte)((high << 4) | low);
                    if (current == 0)
                    {
                        Report(CosRepairCodes.NameContainsNull, token.Start, "A name cannot contain the byte 0 (#00); dropped.");
                        continue;
                    }
                }
            }

            output[written++] = current;
        }

        return CosName.FromOwnedBytes(output[..written].ToArray());
    }

    private CosArray ParseArray(CosToken start, int depth)
    {
        var items = new List<CosObject>();
        while (true)
        {
            CosToken next = _lexer.Peek();
            switch (next.Kind)
            {
                case CosTokenKind.ArrayEnd:
                    _lexer.Position = next.End;
                    return CosArray.FromOwnedList(items);
                case CosTokenKind.EndOfInput or CosTokenKind.DictionaryEnd:
                    Report(CosRepairCodes.ArrayUnterminated, start.Start, "The array has no closing bracket; it ends here.");
                    return CosArray.FromOwnedList(items);
                case CosTokenKind.Keyword when IsStructuralKeyword(Bytes(next)):
                    Report(CosRepairCodes.ArrayUnterminated, start.Start, "The array has no closing bracket; it ends at the next keyword.");
                    return CosArray.FromOwnedList(items);
                case CosTokenKind.Invalid:
                    _lexer.Position = next.End;
                    ReportUnexpected(next);
                    continue;
                default:
                    CosObject item = ParseObject(depth);

                    // An unknown keyword (reported by ParseObject) is skipped; the keyword null is a real element.
                    if (next.Kind != CosTokenKind.Keyword || item is not CosNull || Bytes(next).SequenceEqual("null"u8))
                    {
                        items.Add(item);
                    }

                    continue;
            }
        }
    }

    private CosObject ParseDictionaryOrStream(CosToken start, int depth)
    {
        var entries = new OrderedDictionary<CosName, CosObject>();
        while (true)
        {
            CosToken next = _lexer.Peek();
            if (next.Kind == CosTokenKind.DictionaryEnd)
            {
                _lexer.Position = next.End;
                break;
            }

            if (EndsContainer(next))
            {
                Report(CosRepairCodes.DictionaryUnterminated, start.Start, "The dictionary has no closing angle brackets; it ends here.");
                return CosDictionary.FromOwnedEntries(entries);
            }

            if (next.Kind != CosTokenKind.Name)
            {
                Report(CosRepairCodes.DictionaryKeyNotName, next.Start, "A dictionary key must be a name; the object is skipped.");
                if (next.Kind is CosTokenKind.Invalid or CosTokenKind.Keyword)
                {
                    _lexer.Position = next.End;
                }
                else
                {
                    _ = ParseObject(depth);
                }

                continue;
            }

            _lexer.Position = next.End;
            CosName key = ParseName(next);
            CosToken valueToken = _lexer.Peek();
            if (valueToken.Kind == CosTokenKind.DictionaryEnd || EndsContainer(valueToken))
            {
                Report(CosRepairCodes.DictionaryValueMissing, next.Start, "The last dictionary key has no value; the key is dropped.");
                continue;
            }

            CosObject value = ParseObject(depth);
            if (entries.Remove(key))
            {
                Report(CosRepairCodes.DictionaryDuplicateKey, next.Start, "The dictionary has two entries with the same key; the last one is kept.");
            }

            // §7.3.7: an entry whose value is null is the same as an absent entry.
            if (value is not CosNull)
            {
                entries.Add(key, value);
            }
        }

        var dictionary = CosDictionary.FromOwnedEntries(entries);
        CosToken after = _lexer.Peek();
        return after.Kind == CosTokenKind.Keyword && Bytes(after).SequenceEqual("stream"u8)
            ? ParseStreamData(dictionary, after)
            : dictionary;
    }

    /// <summary>Reads the data of a stream whose <c>stream</c> keyword is <paramref name="keyword"/>, through <c>endstream</c> (§7.3.8).</summary>
    private CosStream ParseStreamData(CosDictionary dictionary, CosToken keyword)
    {
        ReadOnlySpan<byte> source = _lexer.Source;
        int dataStart = SkipStreamKeywordEndOfLine(keyword);
        dictionary.TryGetValue(KnownNames.Length, out CosObject? lengthEntry);
        long declared = lengthEntry switch
        {
            CosInteger { Value: >= 0 } length => length.Value,
            CosReference when _lengthResolver?.ResolveLength(lengthEntry) is >= 0 and long resolved => resolved,
            _ => -1,
        };

        if (declared >= 0 && declared <= source.Length - dataStart && TryMatchEndstream(source, dataStart + (int)declared, out int afterEndstream))
        {
            _lexer.Position = afterEndstream;
            return new CosStream(dictionary, source.Slice(dataStart, (int)declared).ToArray());
        }

        int found = source[dataStart..].IndexOf("endstream"u8);
        if (found >= 0)
        {
            int dataEnd = dataStart + found;
            dataEnd -= EndOfLineLengthBefore(source, dataStart, dataEnd);
            // Without a resolver an indirect Length cannot be checked, so finding the data by endstream is not a repair.
            if (lengthEntry is not CosReference || _lengthResolver is not null)
            {
                Report(
                    CosRepairCodes.StreamLengthInvalid,
                    keyword.Start,
                    string.Create(CultureInfo.InvariantCulture, $"The stream's Length is missing or wrong; the data runs to endstream ({dataEnd - dataStart} bytes)."));
            }

            _lexer.Position = dataStart + found + "endstream"u8.Length;
            return new CosStream(dictionary, source[dataStart..dataEnd].ToArray());
        }

        // No endstream: the data ends at the stated Length, or at endobj when that comes first (as PDFBox does), or at the end.
        int end = declared >= 0 && declared <= source.Length - dataStart ? dataStart + (int)declared : source.Length;
        int endobj = source[dataStart..].IndexOf("endobj"u8);
        if (endobj >= 0 && dataStart + endobj < end)
        {
            end = dataStart + endobj;
            end -= EndOfLineLengthBefore(source, dataStart, end);
        }

        Report(CosRepairCodes.EndstreamMissing, keyword.Start, "The stream has no endstream keyword.");
        _lexer.Position = end;
        return new CosStream(dictionary, source[dataStart..end].ToArray());
    }

    /// <summary>
    /// Skips the end-of-line marker after the <c>stream</c> keyword, which shall be CRLF or LF (§7.3.8.1), and returns where the data
    /// starts. A CR alone, or spaces before the marker, are repaired.
    /// </summary>
    private readonly int SkipStreamKeywordEndOfLine(CosToken keyword)
    {
        ReadOnlySpan<byte> source = _lexer.Source;
        int position = keyword.End;
        if (position < source.Length && source[position] == (byte)'\n')
        {
            return position + 1;
        }

        if (position + 1 < source.Length && source[position] == (byte)'\r' && source[position + 1] == (byte)'\n')
        {
            return position + 2;
        }

        while (position < source.Length && source[position] is (byte)' ' or (byte)'\t')
        {
            position++;
        }

        if (position + 1 < source.Length && source[position] == (byte)'\r' && source[position + 1] == (byte)'\n')
        {
            position += 2;
        }
        else if (position < source.Length && source[position] is (byte)'\r' or (byte)'\n')
        {
            position++;
        }

        Report(CosRepairCodes.StreamKeywordEndOfLine, keyword.Start, "The stream keyword must be followed by CRLF or LF.");
        return position;
    }

    /// <summary>Whether white-space and then the <c>endstream</c> keyword follow <paramref name="position"/>.</summary>
    private static bool TryMatchEndstream(ReadOnlySpan<byte> source, int position, out int afterEndstream)
    {
        var lexer = new CosLexer(source, position);
        CosToken token = lexer.Next();
        afterEndstream = token.End;
        return token.Kind == CosTokenKind.Keyword && source.Slice(token.Start, token.Length).SequenceEqual("endstream"u8);
    }

    /// <summary>The length of the end-of-line marker (CRLF, LF or CR) that ends just before <paramref name="end"/>, which is not part of the data (§7.3.8.1).</summary>
    private static int EndOfLineLengthBefore(ReadOnlySpan<byte> source, int start, int end)
    {
        if (end - start >= 2 && source[end - 2] == (byte)'\r' && source[end - 1] == (byte)'\n')
        {
            return 2;
        }

        return end - start >= 1 && source[end - 1] is (byte)'\r' or (byte)'\n' ? 1 : 0;
    }

    /// <summary>Skips an array or dictionary nested deeper than <see cref="MaxDepth"/>, without recursion, and reads it as null.</summary>
    private CosNull SkipTooDeep(CosToken start)
    {
        Report(CosRepairCodes.NestingTooDeep, start.Start, string.Create(CultureInfo.InvariantCulture, $"Arrays and dictionaries nest deeper than {MaxDepth} levels; the inner container is read as null."));
        int depth = 1;
        while (depth > 0)
        {
            CosToken token = _lexer.Next();
            switch (token.Kind)
            {
                case CosTokenKind.EndOfInput:
                    return CosNull.Instance;
                case CosTokenKind.ArrayStart or CosTokenKind.DictionaryStart:
                    depth++;
                    break;
                case CosTokenKind.ArrayEnd or CosTokenKind.DictionaryEnd:
                    depth--;
                    break;
            }
        }

        return CosNull.Instance;
    }

    private readonly bool EndsContainer(CosToken token) =>
        token.Kind is CosTokenKind.EndOfInput or CosTokenKind.ArrayEnd
        || (token.Kind == CosTokenKind.Keyword && IsStructuralKeyword(Bytes(token)));

    /// <summary>Keywords that end an object in a file (§7.3.8, §7.3.10, §7.5); meeting one inside a container means the container was not closed.</summary>
    private static bool IsStructuralKeyword(ReadOnlySpan<byte> keyword) =>
        keyword.SequenceEqual("endobj"u8)
        || keyword.SequenceEqual("obj"u8)
        || keyword.SequenceEqual("stream"u8)
        || keyword.SequenceEqual("endstream"u8)
        || keyword.SequenceEqual("xref"u8)
        || keyword.SequenceEqual("trailer"u8)
        || keyword.SequenceEqual("startxref"u8);

    private readonly void ReportUnexpected(CosToken token) =>
        Report(CosRepairCodes.UnexpectedToken, token.Start, "This token cannot start an object; skipped.");

    private readonly void Report(string code, int offset, string message) => _repairs?.Report(code, offset, message);

    private readonly ReadOnlySpan<byte> Bytes(CosToken token) => _lexer.Source.Slice(token.Start, token.Length);

    private static int HexValue(byte character) => character switch
    {
        >= (byte)'0' and <= (byte)'9' => character - '0',
        >= (byte)'A' and <= (byte)'F' => character - 'A' + 10,
        >= (byte)'a' and <= (byte)'f' => character - 'a' + 10,
        _ => -1,
    };

    /// <summary>Parses <c>[+-]?[0-9]+</c> into a <see cref="long"/>; fails on any other text or on overflow.</summary>
    private static bool TryParseStrictInteger(ReadOnlySpan<byte> text, out long value) =>
        long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    /// <summary>Whether <paramref name="text"/> is <c>[+-]?</c>, then digits with at most one period, with at least one digit (§7.3.3).</summary>
    private static bool IsWellFormedNumber(ReadOnlySpan<byte> text)
    {
        if (text.Length > 0 && text[0] is (byte)'+' or (byte)'-')
        {
            text = text[1..];
        }

        int period = text.IndexOf((byte)'.');
        if (period >= 0 && text[(period + 1)..].Contains((byte)'.'))
        {
            return false;
        }

        return !text.ContainsAnyExcept(DigitsAndPeriod) && text.ContainsAnyInRange((byte)'0', (byte)'9');
    }

    /// <summary>
    /// The lenient reading of a malformed number: leading signs collapse to one (negative if any is a minus), then digits and at most
    /// one period, stopping at the first byte that does not fit. Always has at least one digit.
    /// </summary>
    private static ReadOnlySpan<byte> WellFormedPrefix(ReadOnlySpan<byte> text, Span<byte> scratch)
    {
        int index = 0;
        bool negative = false;
        while (index < text.Length && text[index] is (byte)'+' or (byte)'-')
        {
            negative |= text[index] == (byte)'-';
            index++;
        }

        int length = 0;
        scratch[length++] = negative ? (byte)'-' : (byte)'+';
        bool sawPeriod = false;
        bool sawDigit = false;
        for (; index < text.Length && length < scratch.Length; index++)
        {
            byte current = text[index];
            if (current == (byte)'.' && !sawPeriod)
            {
                sawPeriod = true;
            }
            else if (current is >= (byte)'0' and <= (byte)'9')
            {
                sawDigit = true;
            }
            else
            {
                break;
            }

            scratch[length++] = current;
        }

        if (!sawDigit)
        {
            length = 1;
            scratch[length++] = (byte)'0';
        }

        return scratch[..length];
    }
}
