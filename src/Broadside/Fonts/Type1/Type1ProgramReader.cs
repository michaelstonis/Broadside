using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Fonts.Type1;

/// <summary>
/// Reads the dictionaries of a Type 1 program from its tokens: the font dictionary entries in the clear text, and the private
/// dictionary's <c>lenIV</c>, <c>Subrs</c> and <c>CharStrings</c> in the decrypted portion, whose charstrings it decrypts into one
/// buffer.
/// </summary>
/// <remarks>
/// Type 1 Font Format chapters 2 and 5, and chapter 10 (the restricted syntax Adobe Type Manager reads, which this follows: simple
/// values are literal tokens, the <c>Encoding</c> is read from its <c>dup code /name put</c> entries, <c>Subrs</c> entries are
/// <c>dup index length RD binary NP</c> and <c>CharStrings</c> entries <c>/name length RD binary ND</c>, ended by <c>end</c>). Any
/// executable name after the length is taken as <c>RD</c> (pdf.js does the same); the <c>CharStrings</c> count is a hint. Hybrid
/// fonts (§9.2) carry a second <c>Subrs</c> and <c>CharStrings</c> for high resolutions: the first ones are used.
/// </remarks>
internal sealed class Type1ProgramReader
{
    /// <summary>The most subroutines a program may have.</summary>
    public const int MaxSubrs = 65536;

    /// <summary>The most glyphs a program may have.</summary>
    public const int MaxGlyphs = 65536;

    private readonly FontProgramContext _context;
    private readonly List<(int Start, int Length)> _subrs = [];
    private readonly List<(string Name, int Start, int Length)> _charStrings = [];
    private int _lenIV = 4;
    private bool _lenIVRead;
    private bool _subrsRead;
    private bool _charStringsRead;
    private bool _privateSeen;

    private Type1ProgramReader(FontProgramContext context) => _context = context;

    /// <summary>Gets the font name, <c>/FontName</c>.</summary>
    public string? FontName { get; private set; }

    /// <summary>Gets the six numbers of <c>/FontMatrix</c>, or <see langword="null"/>.</summary>
    public double[]? FontMatrix { get; private set; }

    /// <summary>Gets the four numbers of <c>/FontBBox</c>, or <see langword="null"/>.</summary>
    public double[]? FontBBox { get; private set; }

    /// <summary>Gets the multiple master <c>/WeightVector</c>, or <see langword="null"/>.</summary>
    public double[]? WeightVector { get; private set; }

    /// <summary>Gets the built-in encoding: 256 glyph names, <c>.notdef</c> where none is given.</summary>
    public string[] Encoding { get; } = CreateNotdefEncoding();

    /// <summary>Reads a split program.</summary>
    /// <param name="clear">The clear text.</param>
    /// <param name="plain">The decrypted portion, or <see langword="null"/> to look for the private dictionary in the clear text.</param>
    /// <param name="context">The diagnostics sink.</param>
    /// <returns>The reader with what it found.</returns>
    public static Type1ProgramReader Read(ReadOnlySpan<byte> clear, byte[]? plain, FontProgramContext context)
    {
        var reader = new Type1ProgramReader(context);
        reader.ReadClearText(clear);
        ReadOnlySpan<byte> privateText = plain ?? clear;
        reader.ReadPrivate(privateText);
        return reader;
    }

    /// <summary>Gets the glyph names and charstrings, <c>.notdef</c> first, and the subroutines, decrypted into one buffer.</summary>
    /// <param name="plain">The text the entries were read from.</param>
    /// <param name="names">Glyph names by glyph id.</param>
    /// <param name="glyphs">Each glyph's charstring in <paramref name="pool"/>.</param>
    /// <param name="subrs">Each subroutine's charstring in <paramref name="pool"/>; a length of −1 for a missing one.</param>
    /// <param name="pool">The decrypted charstrings.</param>
    /// <returns><see langword="false"/> when the program has no <c>CharStrings</c>.</returns>
    public bool TryBuild(
        ReadOnlySpan<byte> plain,
        out string[] names,
        out (int Start, int Length)[] glyphs,
        out (int Start, int Length)[] subrs,
        out byte[] pool)
    {
        names = [];
        glyphs = [];
        subrs = [];
        pool = [];
        if (!_charStringsRead || _charStrings.Count == 0)
        {
            _context.Report(
                DiagnosticCodes.FontType1CharStringsMissing,
                DiagnosticSeverity.Error,
                "The Type 1 program has no CharStrings dictionary (Type 1 Font Format §2.6); none of its glyphs are available.");
            return false;
        }

        if (!_privateSeen)
        {
            _context.Report(
                DiagnosticCodes.FontType1PrivateMissing,
                DiagnosticSeverity.Warning,
                "The Type 1 program has no Private dictionary (Type 1 Font Format §2.5); its CharStrings are read with the default lenIV of 4.");
        }

        // Glyph order: .notdef first (Type 1 Font Format §2.6 requires it), then the CharStrings in program order; a name defined
        // twice keeps its first position and its last charstring, as def would.
        var order = new List<(string Name, int Start, int Length)>(_charStrings.Count + 1);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        order.Add((GlyphNameTable.NotDef, 0, 0));
        index[GlyphNameTable.NotDef] = 0;
        bool notdef = false;
        foreach ((string name, int start, int length) in _charStrings)
        {
            if (index.TryGetValue(name, out int existing))
            {
                order[existing] = (name, start, length);
                notdef |= existing == 0;
            }
            else
            {
                index[name] = order.Count;
                order.Add((name, start, length));
            }
        }

        if (!notdef)
        {
            _context.Report(
                DiagnosticCodes.FontType1NotdefMissing,
                DiagnosticSeverity.Warning,
                "The Type 1 program's CharStrings has no .notdef glyph, which it shall have (Type 1 Font Format §2.6); an empty one stands in for it.");
        }

        int skip = _lenIV < 0 ? 0 : _lenIV;
        long total = 0;
        foreach ((_, _, int length) in order)
        {
            total += Math.Max(length - skip, 0);
        }

        foreach ((int _, int length) in _subrs)
        {
            total += Math.Max(length - skip, 0);
        }

        pool = new byte[total];
        int position = 0;
        bool truncated = false;
        names = new string[order.Count];
        glyphs = new (int Start, int Length)[order.Count];
        for (int glyph = 0; glyph < order.Count; glyph++)
        {
            (string name, int start, int length) = order[glyph];
            names[glyph] = name;
            glyphs[glyph] = Decrypt(plain, start, length, skip, pool, ref position, ref truncated, notdef || glyph != 0);
        }

        subrs = new (int Start, int Length)[_subrs.Count];
        for (int subr = 0; subr < _subrs.Count; subr++)
        {
            (int start, int length) = _subrs[subr];
            subrs[subr] = length < 0 ? (0, -1) : Decrypt(plain, start, length, skip, pool, ref position, ref truncated, present: true);
        }

        if (truncated)
        {
            _context.Report(
                DiagnosticCodes.FontType1CharstringTruncated,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"A Type 1 charstring is shorter than lenIV ({_lenIV}) random bytes (Type 1 Font Format §7.3); it is empty."));
        }

        return true;
    }

    private (int Start, int Length) Decrypt(ReadOnlySpan<byte> plain, int start, int length, int skip, byte[] pool, ref int position, ref bool truncated, bool present)
    {
        if (!present)
        {
            return (position, 0);
        }

        ReadOnlySpan<byte> cipher = plain.Slice(start, length);
        int size = Math.Max(length - skip, 0);
        truncated |= length < skip;
        if (_lenIV < 0)
        {
            // lenIV −1: the charstrings are not encrypted (accepted by Adobe's tools, pdf.js and FontBox).
            cipher.CopyTo(pool.AsSpan(position));
        }
        else
        {
            Type1Layout.Decrypt(cipher, Type1Layout.CharstringKey, skip, pool.AsSpan(position, size));
        }

        (int Start, int Length) entry = (position, size);
        position += size;
        return entry;
    }

    private static string[] CreateNotdefEncoding()
    {
        var names = new string[256];
        Array.Fill(names, GlyphNameTable.NotDef);
        return names;
    }

    private static string NameOf(ReadOnlySpan<byte> text) => System.Text.Encoding.Latin1.GetString(text);

    /// <summary>Type 1 Font Format §2.2-2.4: the font dictionary entries a reader needs; the first of each key counts.</summary>
    private void ReadClearText(ReadOnlySpan<byte> clear)
    {
        var tokenizer = new PostScriptTokenizer(clear);
        bool encodingRead = false;
        while (true)
        {
            PostScriptToken token = tokenizer.Next();
            if (token.Kind == PostScriptTokenKind.EndOfInput || tokenizer.IsName(token, "eexec"u8))
            {
                break;
            }

            if (token.Kind != PostScriptTokenKind.LiteralName)
            {
                continue;
            }

            ReadOnlySpan<byte> key = tokenizer.Text(token);
            if (key.SequenceEqual("FontName"u8) && FontName is null)
            {
                if (tokenizer.Peek() is { Kind: PostScriptTokenKind.LiteralName } name)
                {
                    tokenizer.Next();
                    FontName = NameOf(tokenizer.Text(name));
                }
            }
            else if (key.SequenceEqual("FontMatrix"u8) && FontMatrix is null)
            {
                FontMatrix = ReadNumbers(ref tokenizer, 6, 6);
                if (FontMatrix is null)
                {
                    _context.Report(
                        DiagnosticCodes.FontType1FontMatrixInvalid,
                        DiagnosticSeverity.Warning,
                        "The Type 1 program's FontMatrix is not an array of six numbers (Type 1 Font Format §2.3); [0.001 0 0 0.001 0 0] is used.");
                    FontMatrix = [];
                }
            }
            else if (key.SequenceEqual("FontBBox"u8) && FontBBox is null)
            {
                FontBBox = ReadNumbers(ref tokenizer, 4, 4);
            }
            else if (key.SequenceEqual("WeightVector"u8) && WeightVector is null)
            {
                WeightVector = ReadNumbers(ref tokenizer, 1, 16);
            }
            else if (key.SequenceEqual("Encoding"u8) && !encodingRead)
            {
                encodingRead = true;
                ReadEncoding(ref tokenizer);
            }
            else if (key.SequenceEqual("Private"u8))
            {
                break;
            }
        }

        if (FontMatrix is { Length: 0 })
        {
            FontMatrix = null;
        }
    }

    /// <summary>An array or procedure of numbers; <see langword="null"/> when it holds anything else or a count out of range (the value is skipped).</summary>
    private static double[]? ReadNumbers(ref PostScriptTokenizer tokenizer, int minimum, int maximum)
    {
        PostScriptToken open = tokenizer.Peek();
        if (open.Kind is not (PostScriptTokenKind.ArrayStart or PostScriptTokenKind.ProcedureStart))
        {
            return null;
        }

        tokenizer.Next();
        Span<double> values = stackalloc double[16];
        int count = 0;
        while (true)
        {
            PostScriptToken token = tokenizer.Next();
            if (token.Kind is PostScriptTokenKind.ArrayEnd or PostScriptTokenKind.ProcedureEnd)
            {
                break;
            }

            if (!token.IsNumber || count == values.Length)
            {
                if (token.Kind is PostScriptTokenKind.ArrayStart or PostScriptTokenKind.ProcedureStart)
                {
                    tokenizer.SkipToClose();
                }

                if (token.Kind != PostScriptTokenKind.EndOfInput)
                {
                    tokenizer.SkipToClose();
                }

                return null;
            }

            values[count++] = token.Value;
        }

        return count >= minimum && count <= maximum ? values[..count].ToArray() : null;
    }

    /// <summary>
    /// Type 1 Font Format §2.3 and §10.3: <c>StandardEncoding</c> or <c>ISOLatin1Encoding</c> (PostScript Language Reference
    /// Appendix E.7) by name, or an array filled by <c>dup code /name put</c> entries up to <c>def</c> (the <c>for</c> loop that
    /// fills it with <c>.notdef</c> is skipped).
    /// </summary>
    private void ReadEncoding(ref PostScriptTokenizer tokenizer)
    {
        PostScriptToken first = tokenizer.Next();
        if (first.Kind == PostScriptTokenKind.Name)
        {
            ReadOnlySpan<byte> name = tokenizer.Text(first);
            BuiltInEncoding named = BuiltInEncoding.Standard;
            if (name.SequenceEqual("ISOLatin1Encoding"u8))
            {
                named = BuiltInEncoding.ISOLatin1;
            }
            else if (!name.SequenceEqual("StandardEncoding"u8))
            {
                _context.Report(
                    DiagnosticCodes.FontType1EncodingInvalid,
                    DiagnosticSeverity.Information,
                    $"The Type 1 program's Encoding is the named encoding {NameOf(name)}, which is not read; StandardEncoding is used.");
            }

            ReadOnlySpan<short> table = GlyphNameTable.Table(named);
            for (int code = 0; code < 256; code++)
            {
                Encoding[code] = table[code] >= 0 ? GlyphNameTable.Names[table[code]] : GlyphNameTable.NotDef;
            }

            return;
        }

        bool reported = false;
        while (true)
        {
            PostScriptToken token = tokenizer.Peek();
            if (token.Kind is PostScriptTokenKind.EndOfInput or PostScriptTokenKind.LiteralName || tokenizer.IsName(token, "eexec"u8))
            {
                return;
            }

            tokenizer.Next();
            if (token.Kind == PostScriptTokenKind.ProcedureStart)
            {
                tokenizer.SkipToClose();
            }
            else if (tokenizer.IsName(token, "def"u8))
            {
                return;
            }
            else if (tokenizer.IsName(token, "dup"u8))
            {
                PostScriptToken code = tokenizer.Next();
                PostScriptToken name = tokenizer.Peek();
                if (name.Kind != PostScriptTokenKind.LiteralName)
                {
                    continue;
                }

                tokenizer.Next();
                if (code.TryGetInt32(out int value) && value is >= 0 and <= 255)
                {
                    // An empty name (a lone slash) names no glyph: the code keeps .notdef.
                    Encoding[value] = name.Length == 0 ? GlyphNameTable.NotDef : NameOf(tokenizer.Text(name));
                }
                else if (!reported)
                {
                    reported = true;
                    _context.Report(
                        DiagnosticCodes.FontType1EncodingInvalid,
                        DiagnosticSeverity.Warning,
                        "An entry of the Type 1 program's Encoding has a code that is not an integer from 0 to 255 (Type 1 Font Format §2.3); it is ignored.");
                }
            }
        }
    }

    /// <summary>Type 1 Font Format §2.5, chapter 5 and §10.4-10.5: <c>lenIV</c>, <c>Subrs</c>, <c>CharStrings</c>.</summary>
    private void ReadPrivate(ReadOnlySpan<byte> text)
    {
        var tokenizer = new PostScriptTokenizer(text);
        while (!(_charStringsRead && _subrsRead))
        {
            PostScriptToken token = tokenizer.Next();
            if (token.Kind == PostScriptTokenKind.EndOfInput)
            {
                break;
            }

            if (token.Kind != PostScriptTokenKind.LiteralName)
            {
                continue;
            }

            ReadOnlySpan<byte> key = tokenizer.Text(token);
            if (key.SequenceEqual("Private"u8))
            {
                _privateSeen = true;
            }
            else if (key.SequenceEqual("lenIV"u8) && !_lenIVRead)
            {
                if (tokenizer.Peek().TryGetInt32(out int lenIV))
                {
                    tokenizer.Next();
                    _lenIVRead = true;
                    _lenIV = Math.Clamp(lenIV, -1, 65535);
                }
            }
            else if (key.SequenceEqual("OtherSubrs"u8))
            {
                tokenizer.SkipValue();
            }
            else if (key.SequenceEqual("Subrs"u8) && !_subrsRead)
            {
                if (tokenizer.Peek().IsNumber)
                {
                    _subrsRead = true;
                    ReadSubrs(ref tokenizer);
                }
            }
            else if (key.SequenceEqual("CharStrings"u8) && !_charStringsRead)
            {
                if (tokenizer.Peek().IsNumber)
                {
                    _charStringsRead = true;
                    ReadCharStrings(ref tokenizer);
                }
            }
        }
    }

    /// <summary><c>n array</c>, then <c>dup index length RD binary NP</c> entries.</summary>
    private void ReadSubrs(ref PostScriptTokenizer tokenizer)
    {
        tokenizer.Next(); // the count: a hint only
        if (tokenizer.IsName(tokenizer.Peek(), "array"u8))
        {
            tokenizer.Next();
        }

        while (true)
        {
            PostScriptToken token = tokenizer.Peek();
            if (tokenizer.IsName(token, "dup"u8))
            {
                tokenizer.Next();
                PostScriptToken index = tokenizer.Next();
                PostScriptToken length = tokenizer.Next();
                PostScriptToken rd = tokenizer.Next();
                if (!index.TryGetInt32(out int subr) || !length.TryGetInt32(out int count) || count < 0 || rd.Kind != PostScriptTokenKind.Name)
                {
                    ReportEntry("a Subrs entry is not \"dup index length RD\"; the rest of Subrs is ignored");
                    return;
                }

                bool complete = tokenizer.ReadBinary(count, out int start, out int present);
                if (!complete)
                {
                    ReportEntry("a Subrs entry is longer than the data left; it is read up to the end");
                }

                if (subr is < 0 or >= MaxSubrs)
                {
                    ReportEntry(string.Create(CultureInfo.InvariantCulture, $"Subrs entry {subr} is outside 0 to {MaxSubrs - 1}; it is ignored"));
                    continue;
                }

                while (_subrs.Count <= subr)
                {
                    _subrs.Add((0, -1));
                }

                _subrs[subr] = (start, present);
            }
            else if (token.Kind == PostScriptTokenKind.Name && IsTerminator(tokenizer.Text(token)))
            {
                tokenizer.Next();
            }
            else
            {
                return;
            }
        }
    }

    /// <summary><c>n dict dup begin</c>, then <c>/name length RD binary ND</c> entries up to <c>end</c>.</summary>
    private void ReadCharStrings(ref PostScriptTokenizer tokenizer)
    {
        tokenizer.Next(); // the count: a hint only
        while (true)
        {
            PostScriptToken token = tokenizer.Next();
            if (token.Kind == PostScriptTokenKind.EndOfInput || tokenizer.IsName(token, "end"u8))
            {
                return;
            }

            if (token.Kind != PostScriptTokenKind.LiteralName)
            {
                continue;
            }

            PostScriptToken length = tokenizer.Peek();
            if (!length.TryGetInt32(out int count) || count < 0)
            {
                continue;
            }

            tokenizer.Next();
            PostScriptToken rd = tokenizer.Next();
            if (rd.Kind != PostScriptTokenKind.Name)
            {
                ReportEntry("a CharStrings entry is not \"/name length RD\"; it is ignored");
                continue;
            }

            if (!tokenizer.ReadBinary(count, out int start, out int present))
            {
                ReportEntry("a CharStrings entry is longer than the data left; it is read up to the end");
            }

            if (_charStrings.Count >= MaxGlyphs)
            {
                ReportEntry(string.Create(CultureInfo.InvariantCulture, $"CharStrings has more than {MaxGlyphs} glyphs; the rest are ignored"));
                return;
            }

            _charStrings.Add((NameOf(tokenizer.Text(token)), start, present));
        }
    }

    private static bool IsTerminator(ReadOnlySpan<byte> name) =>
        name.SequenceEqual("NP"u8) || name.SequenceEqual("|"u8) || name.SequenceEqual("ND"u8) || name.SequenceEqual("|-"u8)
        || name.SequenceEqual("noaccess"u8) || name.SequenceEqual("put"u8) || name.SequenceEqual("def"u8)
        || name.SequenceEqual("readonly"u8) || name.SequenceEqual("executeonly"u8);

    private void ReportEntry(string problem) => _context.Report(
        DiagnosticCodes.FontType1EntryInvalid,
        DiagnosticSeverity.Warning,
        $"The Type 1 program's private dictionary deviates from Type 1 Font Format §10.4-10.5: {problem}.");
}
