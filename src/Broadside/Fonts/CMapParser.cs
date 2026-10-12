using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>
/// Reads a CMap file (Adobe TN 5014 §7): a token-level state machine over the COS lexer that keeps the operators a PDF CMap uses
/// and skips the PostScript around them. Lenient: a malformed entry is dropped with a diagnostic and reading goes on.
/// </summary>
/// <remarks>
/// <para>
/// Each block (<c>n begincidrange … endcidrange</c>) runs to its end operator, whatever its count says (pdf.js does the same;
/// PDFBox reads <c>n</c> lines and loses the rest). A begin operator or <c>endcmap</c> inside a block closes it. An operator glued to
/// what follows it (<c>endcidchar1</c>, <c>endbfcharendcmap</c>, the FontBox <c>CMapNoWhitespace</c> case) is split, since the COS
/// lexer reads digits as regular characters.
/// </para>
/// <para>
/// Codes are hexadecimal strings of 1 to 4 bytes (TN 5014 §7.1); a literal string is accepted with a diagnostic. A range whose two
/// codes differ in length takes the upper code's length (pdf.js). CIDs are integers 0 to 65,535 (TN 5014 §7.4).
/// </para>
/// <para>
/// The same reader reads ToUnicode CMaps and the Registry-Ordering-UCS2 tables (ISO 32000-2 §9.10.3): with Unicode destinations,
/// <c>beginbfchar</c>/<c>beginbfrange</c> keep their UTF-16BE strings (and array forms) in <see cref="CMapFile.Bf"/> instead of
/// reading them as CIDs.
/// </para>
/// </remarks>
internal static class CMapParser
{
    private static readonly (byte[] Word, Operator Operator)[] Words =
    [
        ("usecmap"u8.ToArray(), Operator.UseCMap),
        ("begincodespacerange"u8.ToArray(), Operator.BeginCodespaceRange),
        ("endcodespacerange"u8.ToArray(), Operator.EndCodespaceRange),
        ("begincidrange"u8.ToArray(), Operator.BeginCidRange),
        ("endcidrange"u8.ToArray(), Operator.EndCidRange),
        ("begincidchar"u8.ToArray(), Operator.BeginCidChar),
        ("endcidchar"u8.ToArray(), Operator.EndCidChar),
        ("beginnotdefrange"u8.ToArray(), Operator.BeginNotdefRange),
        ("endnotdefrange"u8.ToArray(), Operator.EndNotdefRange),
        ("beginnotdefchar"u8.ToArray(), Operator.BeginNotdefChar),
        ("endnotdefchar"u8.ToArray(), Operator.EndNotdefChar),
        ("beginbfchar"u8.ToArray(), Operator.BeginBfChar),
        ("endbfchar"u8.ToArray(), Operator.EndBfChar),
        ("beginbfrange"u8.ToArray(), Operator.BeginBfRange),
        ("endbfrange"u8.ToArray(), Operator.EndBfRange),
        ("beginrearrangedfont"u8.ToArray(), Operator.BeginRearrangedFont),
        ("endrearrangedfont"u8.ToArray(), Operator.EndRearrangedFont),
        ("beginusematrix"u8.ToArray(), Operator.BeginUseMatrix),
        ("endusematrix"u8.ToArray(), Operator.EndUseMatrix),
        ("usefont"u8.ToArray(), Operator.UseFont),
        ("endcmap"u8.ToArray(), Operator.EndCMap),
        ("begincmap"u8.ToArray(), Operator.PostScript),
        ("def"u8.ToArray(), Operator.PostScript),
        ("begin"u8.ToArray(), Operator.PostScript),
        ("end"u8.ToArray(), Operator.PostScript),
        ("dict"u8.ToArray(), Operator.PostScript),
        ("dup"u8.ToArray(), Operator.PostScript),
        ("pop"u8.ToArray(), Operator.PostScript),
        ("currentdict"u8.ToArray(), Operator.PostScript),
        ("findresource"u8.ToArray(), Operator.PostScript),
        ("defineresource"u8.ToArray(), Operator.PostScript),
        ("true"u8.ToArray(), Operator.PostScript),
        ("false"u8.ToArray(), Operator.PostScript),
        ("null"u8.ToArray(), Operator.PostScript),
    ];

    private enum Operator
    {
        Unknown,
        PostScript,
        UseCMap,
        UseFont,
        EndCMap,
        BeginCodespaceRange,
        EndCodespaceRange,
        BeginCidRange,
        EndCidRange,
        BeginCidChar,
        EndCidChar,
        BeginNotdefRange,
        EndNotdefRange,
        BeginNotdefChar,
        EndNotdefChar,
        BeginBfChar,
        EndBfChar,
        BeginBfRange,
        EndBfRange,
        BeginRearrangedFont,
        EndRearrangedFont,
        BeginUseMatrix,
        EndUseMatrix,
    }

    /// <summary>The longest destination string of a ToUnicode CMap, in bytes (ISO 32000-2 §9.10.3).</summary>
    public const int MaxDestinationBytes = 512;

    /// <summary>Reads a CMap file.</summary>
    /// <param name="data">The file's bytes.</param>
    /// <param name="context">Limits and diagnostics.</param>
    /// <param name="unicodeDestinations">
    /// Whether the file is a ToUnicode CMap (or a Registry-Ordering-UCS2 table): <c>beginbfchar</c> and <c>beginbfrange</c>
    /// destinations are then kept as UTF-16 text in <see cref="CMapFile.Bf"/> (§9.10.3), and CID operators, which do not belong
    /// there, map codes to the Unicode value of their CID with a diagnostic. Otherwise bf destinations are read as CIDs
    /// (§9.7.5.4 c).
    /// </param>
    /// <returns>What the file says.</returns>
    public static CMapFile Parse(ReadOnlySpan<byte> data, CMapContext context, bool unicodeDestinations = false)
    {
        var reader = new Reader(context, unicodeDestinations);
        var lexer = new CosLexer(data);
        CosToken previous = default;
        while (true)
        {
            CosToken token = lexer.Next();
            switch (token.Kind)
            {
                case CosTokenKind.EndOfInput:
                    return reader.File;
                case CosTokenKind.Name:
                    reader.ReadNamedValue(ref lexer, token);
                    break;
                case CosTokenKind.Keyword:
                    Operator op = reader.Identify(ref lexer, token);
                    switch (op)
                    {
                        case Operator.EndCMap:
                            return reader.File;
                        case Operator.UseCMap:
                            reader.UseCMap(data, previous);
                            break;
                        case Operator.UseFont:
                            reader.UseFont(data, previous);
                            break;
                        case Operator.BeginRearrangedFont:
                        case Operator.BeginUseMatrix:
                            reader.SkipUnsupported(ref lexer, op);
                            break;
                        case Operator.BeginCodespaceRange:
                        case Operator.BeginCidRange:
                        case Operator.BeginCidChar:
                        case Operator.BeginNotdefRange:
                        case Operator.BeginNotdefChar:
                        case Operator.BeginBfChar:
                        case Operator.BeginBfRange:
                            reader.ReadBlock(ref lexer, op);
                            break;
                        case Operator.EndCodespaceRange:
                        case Operator.EndCidRange:
                        case Operator.EndCidChar:
                        case Operator.EndNotdefRange:
                        case Operator.EndNotdefChar:
                        case Operator.EndBfChar:
                        case Operator.EndBfRange:
                        case Operator.EndRearrangedFont:
                        case Operator.EndUseMatrix:
                            reader.Syntax("an end operator without its begin operator; ignored");
                            break;
                    }

                    break;
            }

            previous = token;
        }
    }

    /// <summary>Decodes a hexadecimal string token: the value of its first four bytes, big-endian, and its length in bytes.</summary>
    private static bool TryDecodeHex(ReadOnlySpan<byte> token, out uint value, out int length)
    {
        value = 0;
        length = 0;
        ReadOnlySpan<byte> digits = token[1..];
        if (!digits.IsEmpty && digits[^1] == (byte)'>')
        {
            digits = digits[..^1];
        }

        int count = 0;
        foreach (byte digit in digits)
        {
            if (CosLexer.IsWhitespace(digit))
            {
                continue;
            }

            int nibble = CosParser.HexValue(digit);
            if (nibble < 0)
            {
                return false;
            }

            if (count / 2 < 4)
            {
                value = (value << 4) | (uint)nibble;
            }

            count++;
        }

        if (count % 2 == 1)
        {
            if (count / 2 < 4)
            {
                value <<= 4;
            }

            count++;
        }

        length = count / 2;
        return length > 0;
    }

    /// <summary>
    /// Decodes a hexadecimal string token into bytes (an odd final digit is followed by an assumed 0, §7.3.4.3), writing at most
    /// <paramref name="bytes"/>' length and returning the full byte count.
    /// </summary>
    private static int DecodeHexBytes(ReadOnlySpan<byte> token, Span<byte> bytes, out bool valid)
    {
        valid = true;
        ReadOnlySpan<byte> digits = token[1..];
        if (!digits.IsEmpty && digits[^1] == (byte)'>')
        {
            digits = digits[..^1];
        }

        int count = 0;
        foreach (byte digit in digits)
        {
            if (CosLexer.IsWhitespace(digit))
            {
                continue;
            }

            int nibble = CosParser.HexValue(digit);
            if (nibble < 0)
            {
                valid = false;
                return 0;
            }

            int index = count / 2;
            if (index < bytes.Length)
            {
                bytes[index] = count % 2 == 0 ? (byte)(nibble << 4) : (byte)(bytes[index] | nibble);
            }

            count++;
        }

        return (count + 1) / 2;
    }

    /// <summary>The state of one parse.</summary>
    private sealed class Reader(CMapContext context, bool unicode)
    {
        private int _entries;
        private bool _limitReported;
        private bool _inlineSystemInfo;
        private string? _registry;
        private string? _ordering;
        private int _supplement;
        private bool _bfReported;
        private bool _cidReported;
        private bool _overflowReported;

        public CMapFile File { get; } = new();

        /// <summary>Identifies an operator, splitting one glued to what follows it.</summary>
        public Operator Identify(ref CosLexer lexer, CosToken token)
        {
            ReadOnlySpan<byte> word = lexer.Source.Slice(token.Start, token.Length);
            Operator best = Operator.Unknown;
            int bestLength = 0;
            foreach ((byte[] known, Operator op) in Words)
            {
                if (word.SequenceEqual(known))
                {
                    return op;
                }

                if (known.Length > bestLength && word.StartsWith(known))
                {
                    best = op;
                    bestLength = known.Length;
                }
            }

            if (best != Operator.Unknown)
            {
                lexer.Position = token.Start + bestLength;
                Syntax("an operator glued to the token after it; split");
            }

            return best;
        }

        public void ReadNamedValue(ref CosLexer lexer, CosToken token)
        {
            ReadOnlySpan<byte> name = lexer.Source.Slice(token.Start + 1, token.Length - 1);
            if (name.SequenceEqual("CMapName"u8))
            {
                if (ParseNext(ref lexer, CosTokenKind.Name) is CosName value)
                {
                    File.Name ??= value.Value;
                }
            }
            else if (name.SequenceEqual("WMode"u8))
            {
                if (ParseNext(ref lexer, CosTokenKind.Integer) is CosInteger mode)
                {
                    if (mode.Value is 0 or 1)
                    {
                        File.WMode ??= (int)mode.Value;
                    }
                    else
                    {
                        Report(DiagnosticCodes.CMapEntryInvalid, $"WMode shall be 0 or 1, not {mode.Value} (ISO 32000-2 §9.7.5.3, Table 118); read as 0.");
                        File.WMode ??= 0;
                    }
                }
            }
            else if (name.SequenceEqual("CIDSystemInfo"u8))
            {
                CosToken next = lexer.Peek();
                if (next.Kind is CosTokenKind.DictionaryStart or CosTokenKind.ArrayStart)
                {
                    var parser = new CosParser(lexer.Source, repairs: null, next.Start);
                    CosObject value = parser.ParseObject();
                    lexer.Position = parser.Position;
                    ReadSystemInfo(value is CosArray array && array.Count > 0 ? array[0] : value);
                }
                else
                {
                    _inlineSystemInfo = true;
                }
            }
            else if (name.SequenceEqual("Registry"u8) || name.SequenceEqual("Ordering"u8) || name.SequenceEqual("Supplement"u8))
            {
                CosToken next = lexer.Peek();
                if (next.Kind is CosTokenKind.LiteralString or CosTokenKind.HexString or CosTokenKind.Integer)
                {
                    var parser = new CosParser(lexer.Source, repairs: null, next.Start);
                    CosObject value = parser.ParseObject();
                    lexer.Position = parser.Position;
                    if (_inlineSystemInfo)
                    {
                        switch (value)
                        {
                            case CosString text when name[0] == (byte)'R':
                                _registry = text.DecodeText();
                                break;
                            case CosString text when name[0] == (byte)'O':
                                _ordering = text.DecodeText();
                                break;
                            case CosInteger supplement when name[0] == (byte)'S':
                                _supplement = (int)Math.Clamp(supplement.Value, int.MinValue, int.MaxValue);
                                break;
                        }

                        if (_registry is not null && _ordering is not null)
                        {
                            File.SystemInfo = new CidSystemInfo(_registry, _ordering, _supplement);
                        }
                    }
                }
            }
        }

        public void UseCMap(ReadOnlySpan<byte> data, CosToken previous)
        {
            if (previous.Kind != CosTokenKind.Name)
            {
                Syntax("usecmap without a CMap name before it; ignored");
                return;
            }

            var parser = new CosParser(data, repairs: null, previous.Start);
            if (parser.ParseObject() is CosName name)
            {
                if (_entries > 0)
                {
                    Syntax("usecmap after mappings; it shall come before any range operator (Adobe TN 5014 §7.3); honored");
                }

                File.UseCMapName ??= name.Value;
            }
        }

        public void UseFont(ReadOnlySpan<byte> data, CosToken previous)
        {
            if (previous.Kind != CosTokenKind.Integer || !data.Slice(previous.Start, previous.Length).SequenceEqual("0"u8))
            {
                Report(DiagnosticCodes.CMapOperatorNotAllowed, "The operand of usefont shall be 0 (ISO 32000-2 §9.7.5.4 b); ignored.");
            }
        }

        public void SkipUnsupported(ref CosLexer lexer, Operator begin)
        {
            Report(
                DiagnosticCodes.CMapOperatorNotAllowed,
                "beginrearrangedfont and beginusematrix shall not be used in a PDF CMap (ISO 32000-2 §9.7.5.4 e); the block is skipped.");
            Operator end = begin == Operator.BeginRearrangedFont ? Operator.EndRearrangedFont : Operator.EndUseMatrix;
            while (true)
            {
                CosToken token = lexer.Next();
                if (token.Kind == CosTokenKind.EndOfInput || (token.Kind == CosTokenKind.Keyword && Identify(ref lexer, token) == end))
                {
                    return;
                }
            }
        }

        public void ReadBlock(ref CosLexer lexer, Operator begin)
        {
            Operator end = begin + 1;
            bool isRange = begin is Operator.BeginCodespaceRange or Operator.BeginCidRange or Operator.BeginNotdefRange or Operator.BeginBfRange;
            if (begin is Operator.BeginBfChar or Operator.BeginBfRange && !unicode && !_bfReported)
            {
                _bfReported = true;
                Report(
                    DiagnosticCodes.CMapOperatorNotAllowed,
                    "beginbfchar and beginbfrange shall not be used in a CMap used as an encoding (ISO 32000-2 §9.7.5.4 c); their destinations are read as CIDs.");
            }

            Code first = default;
            Code second = default;
            int count = 0;
            int codes = isRange ? 2 : 1;
            while (true)
            {
                CosToken token = lexer.Next();
                if (token.Kind == CosTokenKind.EndOfInput)
                {
                    Syntax("the file ends inside a block; the entries read are kept");
                    return;
                }

                if (token.Kind == CosTokenKind.Keyword)
                {
                    int start = token.Start;
                    Operator op = Identify(ref lexer, token);
                    if (op == end)
                    {
                        if (count > 0)
                        {
                            Syntax("an incomplete entry at the end of a block; dropped");
                        }

                        return;
                    }

                    if (op is Operator.EndCMap or Operator.UseCMap or Operator.BeginCodespaceRange or Operator.BeginCidRange or Operator.BeginCidChar
                        or Operator.BeginNotdefRange or Operator.BeginNotdefChar or Operator.BeginBfChar or Operator.BeginBfRange)
                    {
                        Syntax("a block without its end operator; closed");
                        lexer.Position = start;
                        return;
                    }

                    Syntax("an unexpected operator inside a block; the entry is dropped");
                    count = 0;
                    continue;
                }

                if (count < codes)
                {
                    if (!TryReadCode(lexer.Source, token, out Code code))
                    {
                        count = 0;
                        continue;
                    }

                    if (count == 0)
                    {
                        first = code;
                    }
                    else
                    {
                        second = code;
                    }

                    count++;
                    if (begin == Operator.BeginCodespaceRange && count == 2)
                    {
                        AddCodespace(first, second);
                        count = 0;
                    }

                    continue;
                }

                // The destination: a CID, or for bf operators a byte string, a name or an array of byte strings.
                count = 0;
                if (!isRange)
                {
                    second = first;
                }

                switch (begin)
                {
                    case Operator.BeginCidRange:
                    case Operator.BeginCidChar:
                    case Operator.BeginNotdefRange:
                    case Operator.BeginNotdefChar:
                        if (TryReadCid(lexer.Source, token, out int cid))
                        {
                            if (unicode)
                            {
                                AddCidAsUnicode(begin is Operator.BeginNotdefRange or Operator.BeginNotdefChar, first, second, cid);
                            }
                            else
                            {
                                AddMapping(begin is Operator.BeginNotdefRange or Operator.BeginNotdefChar, first, second, cid);
                            }
                        }
                        else if (token.Kind == CosTokenKind.HexString && TryReadCode(lexer.Source, token, out Code restart))
                        {
                            // The entry lacked its CID; the code is the start of the next entry.
                            first = restart;
                            count = 1;
                        }

                        break;
                    default:
                        if (unicode)
                        {
                            ReadUnicodeDestination(ref lexer, token, first, second, begin == Operator.BeginBfRange);
                        }
                        else
                        {
                            ReadBfDestination(ref lexer, token, first, second);
                        }

                        break;
                }
            }
        }

        public void Syntax(string message) =>
            Report(DiagnosticCodes.CMapSyntaxInvalid, $"The CMap file has {message} (Adobe TN 5014 §7).");

        private void Report(string code, string message) => context.Report(code, DiagnosticSeverity.Warning, message);

        private static CosObject? ParseNext(ref CosLexer lexer, CosTokenKind kind)
        {
            CosToken next = lexer.Peek();
            if (next.Kind != kind)
            {
                return null;
            }

            var parser = new CosParser(lexer.Source, repairs: null, next.Start);
            CosObject value = parser.ParseObject();
            lexer.Position = parser.Position;
            return value;
        }

        private void ReadSystemInfo(CosObject value)
        {
            if (value is CosDictionary dictionary
                && dictionary.TryGetValue(new CosName("Registry"), out CosObject? registry) && registry is CosString registryText
                && dictionary.TryGetValue(new CosName("Ordering"), out CosObject? ordering) && ordering is CosString orderingText)
            {
                int supplement = dictionary.TryGetValue(new CosName("Supplement"), out CosObject? number) && number is CosInteger integer
                    ? (int)Math.Clamp(integer.Value, int.MinValue, int.MaxValue)
                    : 0;
                File.SystemInfo ??= new CidSystemInfo(registryText.DecodeText(), orderingText.DecodeText(), supplement);
            }
            else
            {
                Report(DiagnosticCodes.CidSystemInfoInvalid, "The CMap's CIDSystemInfo shall be a dictionary with Registry, Ordering and Supplement (ISO 32000-2 §9.7.3, Table 114); ignored.");
            }
        }

        private bool TryReadCode(ReadOnlySpan<byte> data, CosToken token, out Code code)
        {
            code = default;
            ReadOnlySpan<byte> text = data.Slice(token.Start, token.Length);
            uint value;
            int length;
            if (token.Kind == CosTokenKind.HexString)
            {
                if (!TryDecodeHex(text, out value, out length))
                {
                    Report(DiagnosticCodes.CMapEntryInvalid, "A code shall be a hexadecimal string (Adobe TN 5014 §7.1); the entry is dropped.");
                    return false;
                }
            }
            else if (token.Kind == CosTokenKind.LiteralString)
            {
                var parser = new CosParser(data, repairs: null, token.Start);
                ReadOnlySpan<byte> bytes = parser.ParseObject() is CosString literal ? literal.Bytes : [];
                length = bytes.Length;
                value = 0;
                for (int index = 0; index < Math.Min(4, length); index++)
                {
                    value = (value << 8) | bytes[index];
                }

                Syntax("a code written as a literal string; read as its bytes");
            }
            else
            {
                Syntax("a token that is not a code where a code belongs; the entry is dropped");
                return false;
            }

            if (length is < 1 or > 4)
            {
                Report(DiagnosticCodes.CMapEntryInvalid, $"A code shall be 1 to 4 bytes long, not {length} (ISO 32000-2 §9.7.6.2); the entry is dropped.");
                return false;
            }

            code = new Code(value, length);
            return true;
        }

        private bool TryReadCid(ReadOnlySpan<byte> data, CosToken token, out int cid)
        {
            cid = 0;
            ReadOnlySpan<byte> text = data.Slice(token.Start, token.Length);
            double number;
            if (token.Kind == CosTokenKind.Integer && CosParser.TryParseStrictInteger(text, out long integer))
            {
                number = integer;
            }
            else if (token.Kind is CosTokenKind.Integer or CosTokenKind.Real
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                number = Math.Truncate(number);
                Report(DiagnosticCodes.CMapEntryInvalid, "A CID shall be an integer (Adobe TN 5014 §7.4); a real is truncated.");
            }
            else
            {
                Syntax("a token that is not a CID where a CID belongs; the entry is dropped");
                return false;
            }

            if (number is < 0 or > 65535)
            {
                Report(DiagnosticCodes.CMapEntryInvalid, $"A CID shall be 0 to 65,535, not {number} (Adobe TN 5014 §7.4); the entry is dropped.");
                return false;
            }

            cid = (int)number;
            return true;
        }

        private void ReadBfDestination(ref CosLexer lexer, CosToken token, Code low, Code high)
        {
            ReadOnlySpan<byte> data = lexer.Source;
            switch (token.Kind)
            {
                case CosTokenKind.HexString when TryDecodeHex(data.Slice(token.Start, token.Length), out uint value, out int length):
                    AddMapping(notdef: false, low, high, BfCid(value, length));
                    break;
                case CosTokenKind.Integer when CosParser.TryParseStrictInteger(data.Slice(token.Start, token.Length), out long number) && number is >= 0 and <= 65535:
                    AddMapping(notdef: false, low, high, (int)number);
                    break;
                case CosTokenKind.ArrayStart:
                    uint offset = 0;
                    while (true)
                    {
                        CosToken element = lexer.Next();
                        if (element.Kind is CosTokenKind.ArrayEnd or CosTokenKind.EndOfInput)
                        {
                            break;
                        }

                        if (element.Kind == CosTokenKind.HexString
                            && TryDecodeHex(data.Slice(element.Start, element.Length), out uint value, out int length)
                            && offset <= high.Value - low.Value)
                        {
                            var code = new Code(low.Value + offset, low.Length);
                            AddMapping(notdef: false, code, code, BfCid(value, length));
                        }

                        offset++;
                    }

                    break;
                default:
                    // A glyph name destination selects no CID.
                    break;
            }
        }

        /// <summary>A bf destination read as a CID: its first two bytes, big-endian (pdf.js, PDFBox).</summary>
        private static int BfCid(uint value, int length) => length switch
        {
            1 => (int)value,
            2 => (int)value,
            3 => (int)(value >> 8),
            _ => (int)(value >> 16),
        };

        /// <summary>A destination of a ToUnicode CMap (§9.10.3): a UTF-16BE string, or for a range an array of them.</summary>
        private void ReadUnicodeDestination(ref CosLexer lexer, CosToken token, Code low, Code high, bool isRange)
        {
            ReadOnlySpan<byte> data = lexer.Source;
            int start = File.BfText.Count;
            switch (token.Kind)
            {
                case CosTokenKind.HexString:
                case CosTokenKind.LiteralString:
                    if (AppendString(data, token))
                    {
                        AddBf(low, high, isArray: false, start, File.BfText.Count - start, increment: isRange);
                    }

                    break;
                case CosTokenKind.Name:
                    AppendName(data, token);
                    AddBf(low, high, isArray: false, start, File.BfText.Count - start, increment: isRange);
                    break;
                case CosTokenKind.Integer when AppendInteger(data, token):
                    AddBf(low, high, isArray: false, start, File.BfText.Count - start, increment: isRange);
                    break;
                case CosTokenKind.ArrayStart:
                    ReadUnicodeArray(ref lexer, low, high, isRange);
                    break;
                default:
                    Syntax("a token that is not a destination where a destination belongs; the entry is dropped");
                    break;
            }
        }

        private void ReadUnicodeArray(ref CosLexer lexer, Code low, Code high, bool isRange)
        {
            ReadOnlySpan<byte> data = lexer.Source;
            int first = File.BfArrayElements.Count;
            bool skipped = false;
            while (true)
            {
                CosToken element = lexer.Next();
                if (element.Kind is CosTokenKind.ArrayEnd or CosTokenKind.EndOfInput)
                {
                    break;
                }

                int start = File.BfText.Count;
                bool read = element.Kind switch
                {
                    CosTokenKind.HexString or CosTokenKind.LiteralString => AppendString(data, element),
                    CosTokenKind.Name => AppendName(data, element),
                    CosTokenKind.Integer => AppendInteger(data, element),
                    _ => false,
                };
                if (read)
                {
                    File.BfArrayElements.Add((start, File.BfText.Count - start));
                }
                else
                {
                    skipped = true;
                }
            }

            if (skipped)
            {
                Destination("an array element that is not a string; it is skipped");
            }

            int count = File.BfArrayElements.Count - first;
            if (!isRange)
            {
                Destination("an array after beginbfchar, where only beginbfrange allows one; its first element is used");
                count = Math.Min(count, 1);
            }

            (Code alignedLow, Code alignedHigh) = Align(low, high);
            if (isRange && alignedHigh.Value >= alignedLow.Value && count != (long)alignedHigh.Value - alignedLow.Value + 1)
            {
                Destination($"an array of {count} strings for a range of {(long)alignedHigh.Value - alignedLow.Value + 1} codes (m = srcCode2 - srcCode1 + 1); the codes with a string are mapped");
            }

            if (count > 0)
            {
                AddBf(low, high, isArray: true, first, count, increment: false);
            }
        }

        /// <summary>Appends a string token's bytes as UTF-16BE: an odd byte count gets a leading zero byte, unpaired surrogates become U+FFFD.</summary>
        private bool AppendString(ReadOnlySpan<byte> data, CosToken token)
        {
            Span<byte> bytes = stackalloc byte[MaxDestinationBytes + 1];
            int count;
            if (token.Kind == CosTokenKind.HexString)
            {
                count = DecodeHexBytes(data.Slice(token.Start, token.Length), bytes, out bool valid);
                if (!valid)
                {
                    Report(DiagnosticCodes.CMapEntryInvalid, "A destination shall be a hexadecimal string (Adobe TN 5014 §7.1); the entry is dropped.");
                    return false;
                }
            }
            else
            {
                var parser = new CosParser(data, repairs: null, token.Start);
                ReadOnlySpan<byte> literal = parser.ParseObject() is CosString text ? text.Bytes : [];
                count = Math.Min(literal.Length, bytes.Length);
                literal[..count].CopyTo(bytes);
                Destination("a literal string; its bytes are read as UTF-16BE");
            }

            if (count > MaxDestinationBytes)
            {
                Destination($"a string longer than {MaxDestinationBytes} bytes (ISO 32000-2 §9.10.3); it is cut there");
                count = MaxDestinationBytes;
            }

            if (count == 0)
            {
                Destination("an empty string; the code maps to no text");
                return true;
            }

            int offset = 0;
            if (count % 2 == 1)
            {
                // A one-byte <41> means U+0041 (pdf.js, and PDFBox reads one byte as ISO 8859-1, which is the same).
                Destination("an odd number of bytes; a leading zero byte is assumed");
                File.BfText.Add((char)bytes[0]);
                offset = 1;
            }

            bool unpaired = false;
            for (int index = offset; index + 1 < count; index += 2)
            {
                char unit = (char)((bytes[index] << 8) | bytes[index + 1]);
                if (char.IsHighSurrogate(unit) && index + 3 < count)
                {
                    char next = (char)((bytes[index + 2] << 8) | bytes[index + 3]);
                    if (char.IsLowSurrogate(next))
                    {
                        File.BfText.Add(unit);
                        File.BfText.Add(next);
                        index += 2;
                        continue;
                    }
                }

                if (char.IsSurrogate(unit))
                {
                    unpaired = true;
                    unit = '�';
                }

                File.BfText.Add(unit);
            }

            if (unpaired)
            {
                Destination("an unpaired surrogate, which is not UTF-16BE; it is replaced by U+FFFD");
            }

            return true;
        }

        /// <summary>A glyph name destination (Adobe TN 5014 §7.4 allows names for base fonts): its Adobe Glyph List text.</summary>
        private bool AppendName(ReadOnlySpan<byte> data, CosToken token)
        {
            var parser = new CosParser(data, repairs: null, token.Start);
            string name = parser.ParseObject() is CosName value ? value.Value : string.Empty;
            Destination($"the glyph name /{name} instead of a UTF-16BE string (ISO 32000-2 §9.10.3); its Adobe Glyph List value is used");
            Span<char> text = stackalloc char[MaxDestinationBytes / 2];
            int written = AdobeGlyphList.MapName(name, zapfDingbats: false, text, out _);
            foreach (char unit in text[..written])
            {
                File.BfText.Add(unit);
            }

            return true;
        }

        /// <summary>An integer destination (pdf.js reads it as a character code): that Unicode value.</summary>
        private bool AppendInteger(ReadOnlySpan<byte> data, CosToken token)
        {
            if (!CosParser.TryParseStrictInteger(data.Slice(token.Start, token.Length), out long number)
                || number is < 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
            {
                return false;
            }

            Destination("an integer; read as that Unicode value");
            AppendScalar((int)number);
            return true;
        }

        private void AppendScalar(int scalar)
        {
            Span<char> units = stackalloc char[2];
            int count = new System.Text.Rune(scalar).EncodeToUtf16(units);
            for (int index = 0; index < count; index++)
            {
                File.BfText.Add(units[index]);
            }
        }

        /// <summary>A cidchar/cidrange in a ToUnicode CMap: the code maps to the Unicode value of its CID (pdf.js; PDFBox reads such a CMap as Identity-H).</summary>
        private void AddCidAsUnicode(bool notdef, Code low, Code high, int cid)
        {
            if (!_cidReported)
            {
                _cidReported = true;
                Report(
                    DiagnosticCodes.ToUnicodeCidMapping,
                    "A ToUnicode CMap shall map codes with beginbfchar and beginbfrange (ISO 32000-2 §9.10.3); its CID mappings map each code to the Unicode value of its CID, and notdef mappings are ignored.");
            }

            if (notdef)
            {
                return;
            }

            int start = File.BfText.Count;
            File.BfText.Add(char.IsSurrogate((char)cid) ? '�' : (char)cid);
            AddBf(low, high, isArray: false, start, 1, increment: false);
        }

        private void AddBf(Code low, Code high, bool isArray, int start, int count, bool increment)
        {
            if (!Admit())
            {
                return;
            }

            (low, high) = Align(low, high);
            if (low.Value > high.Value)
            {
                Report(DiagnosticCodes.CMapEntryInvalid, "A range's lower code shall not exceed its upper code (Adobe TN 5014 §7.4); the range is dropped.");
                return;
            }

            if (increment && count > 0 && !_overflowReported && (File.BfText[start + count - 1] & 0xFF) + (long)(high.Value - low.Value) > 255)
            {
                _overflowReported = true;
                Report(
                    DiagnosticCodes.ToUnicodeBfRangeOverflow,
                    "The last byte of a bfrange destination shall not exceed 255 - (srcCode2 - srcCode1) (ISO 32000-2 §9.10.3); the destination's last character is incremented as a whole.");
            }

            int entry = File.BfEntries.Count;
            File.BfEntries.Add(new CMapFile.BfEntry(low.Value, isArray, start, count));
            File.Bf[high.Length - 1].Add(new IntervalTable.Interval(low.Value, high.Value, entry));
        }

        private void Destination(string problem) =>
            Report(DiagnosticCodes.ToUnicodeDestinationInvalid, $"A ToUnicode destination is {problem} (ISO 32000-2 §9.10.3).");

        private void AddCodespace(Code low, Code high)
        {
            if (!Admit())
            {
                return;
            }

            (low, high) = Align(low, high);
            var range = new CodespaceRange(high.Length, low.Value, high.Value);
            for (int index = 0; index < range.Length; index++)
            {
                int shift = 8 * index;
                if (((low.Value >> shift) & 0xFF) > ((high.Value >> shift) & 0xFF))
                {
                    Report(DiagnosticCodes.CMapEntryInvalid, "A codespace range's lower code shall not exceed its upper code in any byte (Adobe TN 5014 §7.4); the range is dropped.");
                    return;
                }
            }

            File.Codespace.Add(range);
        }

        private void AddMapping(bool notdef, Code low, Code high, int cid)
        {
            if (!Admit())
            {
                return;
            }

            (low, high) = Align(low, high);
            if (low.Value > high.Value)
            {
                Report(DiagnosticCodes.CMapEntryInvalid, "A range's lower code shall not exceed its upper code (Adobe TN 5014 §7.4); the range is dropped.");
                return;
            }

            if (!notdef && cid + (long)(high.Value - low.Value) > 65535)
            {
                Report(DiagnosticCodes.CMapEntryInvalid, "A range maps codes past CID 65,535 (Adobe TN 5014 §7.4); those codes select CID 0.");
            }

            (notdef ? File.Notdefs : File.Cids)[high.Length - 1].Add(new IntervalTable.Interval(low.Value, high.Value, cid));
        }

        private (Code Low, Code High) Align(Code low, Code high)
        {
            if (low.Length == high.Length)
            {
                return (low, high);
            }

            Report(DiagnosticCodes.CMapEntryInvalid, "A range's two codes shall have the same length (Adobe TN 5014 §7.4); the upper code's length is used.");
            uint mask = high.Length == 4 ? uint.MaxValue : (1u << (8 * high.Length)) - 1;
            return (new Code(low.Value & mask, high.Length), high);
        }

        private bool Admit()
        {
            if (++_entries <= context.MaxEntries)
            {
                return true;
            }

            if (!_limitReported)
            {
                _limitReported = true;
                Report(DiagnosticCodes.CMapEntryLimitExceeded, $"The CMap has more than {context.MaxEntries} entries; the rest are dropped.");
            }

            return false;
        }
    }

    /// <summary>A code operand: its value and its length in bytes.</summary>
    private readonly record struct Code(uint Value, int Length);
}
