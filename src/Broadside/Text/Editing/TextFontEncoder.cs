using System.Text;
using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside.Text.Editing;

/// <summary>A character code found for a character in an existing font.</summary>
/// <param name="Value">The code, high byte first in the content.</param>
/// <param name="Length">The number of bytes of the code.</param>
/// <param name="NeedsToUnicode">Whether the font's ToUnicode CMap must gain an entry for the code to read back as the character.</param>
internal readonly record struct EncodedCode(uint Value, int Length, bool NeedsToUnicode)
{
    /// <summary>Writes the code's bytes, high byte first.</summary>
    public void Write(List<byte> bytes)
    {
        for (int shift = Length - 1; shift >= 0; shift--)
        {
            bytes.Add((byte)(Value >> (8 * shift)));
        }
    }
}

/// <summary>
/// Finds, for a character of a replacement, a code of an existing font that shows it (issue #641): the codes the page already shows it
/// with, then the font's encoding reversed (glyph names and the ToUnicode CMap for a simple font, the ToUnicode CMap for a Type 0 font
/// with an Identity CMap), then, for a Type 0 font over an embedded TrueType program, the program's "cmap" and the CIDToGIDMap.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.6.5 (simple font encodings), §9.7.6.2 (CMaps), §9.7.4.2 (CIDToGIDMap), §9.6.4 (Type 3 <c>CharProcs</c>), §9.10.2 and
/// §9.10.3 (ToUnicode). A code is usable only when the font can draw it: an embedded program has a glyph with an outline for it (a
/// white-space character may have an empty one), a Type 3 font a glyph procedure; a font without an embedded program is drawn by a
/// substitute, which has every character its encoding names. A subset program usually has no glyph for the characters it did not
/// show, so a check on the encoding alone is not enough.
/// </para>
/// <para>Each character is worked out once per edit. Mutation-side: single-threaded.</para>
/// </remarks>
internal sealed class TextFontEncoder
{
    private readonly PdfFont _font;
    private readonly Dictionary<int, EncodedCode?> _cache = [];
    private readonly Dictionary<int, EncodedCode> _seeded = [];
    private readonly GlyphOutline _outline = new();
    private Dictionary<int, uint>? _reverse;

    public TextFontEncoder(PdfFont font, TextPage? text)
    {
        _font = font;
        if (text is null)
        {
            return;
        }

        foreach (TextGlyph glyph in text.Glyphs)
        {
            if (!ReferenceEquals(glyph.Font, font) || glyph.TextLength is < 1 or > 2 || (glyph.Flags & (TextGlyphFlags.ActualText | TextGlyphFlags.AlternateDescription | TextGlyphFlags.Expansion)) != 0
                || glyph.UnicodeSource == UnicodeSource.Unmapped)
            {
                continue;
            }

            ReadOnlySpan<char> shown = text.GetGlyphText(glyph);
            if (Rune.DecodeFromUtf16(shown, out Rune rune, out int consumed) == System.Buffers.OperationStatus.Done && consumed == shown.Length)
            {
                _seeded.TryAdd(rune.Value, new EncodedCode(glyph.CharacterCode, glyph.CodeLength, NeedsToUnicode: false));
            }
        }
    }

    /// <summary>Gets the font.</summary>
    public PdfFont Font => _font;

    /// <summary>Finds a usable code for a character.</summary>
    /// <param name="scalar">The character.</param>
    /// <param name="code">The code.</param>
    /// <returns>Whether the font has one.</returns>
    public bool TryEncode(int scalar, out EncodedCode code)
    {
        if (!_cache.TryGetValue(scalar, out EncodedCode? found))
        {
            found = Find(scalar);
            _cache[scalar] = found;
        }

        code = found.GetValueOrDefault();
        return found is not null;
    }

    private EncodedCode? Find(int scalar)
    {
        if (_seeded.TryGetValue(scalar, out EncodedCode seeded) && IsUsable(seeded, scalar))
        {
            return seeded;
        }

        if (Reverse().TryGetValue(scalar, out uint value))
        {
            var reversed = new EncodedCode(value, _font is PdfType0Font ? 2 : 1, NeedsToUnicode: false);
            if (IsUsable(reversed, scalar))
            {
                return reversed;
            }
        }

        return FromCharacterMap(scalar);
    }

    /// <summary>
    /// The font's codes by the character they read as, each character its lowest code: every code of a simple font that has a glyph name
    /// or a ToUnicode entry; every two-byte code of a Type 0 font with an Identity CMap that its ToUnicode CMap maps.
    /// </summary>
    private Dictionary<int, uint> Reverse()
    {
        if (_reverse is not null)
        {
            return _reverse;
        }

        _reverse = [];
        Span<char> buffer = stackalloc char[FontUnicode.MaxLength];
        switch (_font)
        {
            case PdfSimpleFont simple:
                {
                    ToUnicodeMap? map = simple.ToUnicodeMap;
                    for (int code = 0; code < 256; code++)
                    {
                        string name = simple.GetGlyphName((byte)code);
                        bool mapped = map is not null && map.TryMap((uint)code, 1, buffer, out _, out _);
                        if (!mapped && (string.IsNullOrEmpty(name) || name == ".notdef"))
                        {
                            continue;
                        }

                        int written = simple.GetUnicode(new CharacterCode((uint)code, 1, true), buffer, out UnicodeSource source);
                        if (source != UnicodeSource.Unmapped && Single(buffer[..written]) is int scalar)
                        {
                            _reverse.TryAdd(scalar, (uint)code);
                        }
                    }

                    break;
                }

            case PdfType0Font type0 when type0.Encoding.IsIdentity && type0.ToUnicodeMap is { } map:
                for (uint code = 0; code <= 0xFFFF; code++)
                {
                    if (map.TryMap(code, 2, buffer, out int written, out _) && Single(buffer[..written]) is int scalar)
                    {
                        _reverse.TryAdd(scalar, code);
                    }
                }

                break;
        }

        return _reverse;
    }

    /// <summary>
    /// For a Type 0 font with an Identity CMap over an embedded TrueType program whose CIDToGIDMap is the identity: the program's
    /// Unicode "cmap" gives the glyph, the glyph is the CID and the CID the code (§9.7.4.2, §9.7.6.2). The code then needs a ToUnicode
    /// entry unless the CMap already maps it to the character.
    /// </summary>
    private EncodedCode? FromCharacterMap(int scalar)
    {
        if (_font is not PdfType0Font { Encoding.IsIdentity: true } type0 || type0.DescendantFont is not PdfCidFontType2 descendant
            || descendant.Program is not { } program)
        {
            return null;
        }

        int glyph = program.CharacterMapSelection.GetGlyphIdForUnicode(scalar, out _);
        if (glyph is <= 0 or > 0xFFFF || !descendant.TryGetGlyphId(glyph, out int mapped) || mapped != glyph)
        {
            return null;
        }

        bool needs = true;
        if (type0.ToUnicodeMap is { } map)
        {
            Span<char> buffer = stackalloc char[FontUnicode.MaxLength];
            needs = !(map.TryMap((uint)glyph, 2, buffer, out int written, out _) && Single(buffer[..written]) == scalar);
        }

        var code = new EncodedCode((uint)glyph, 2, needs);
        return IsUsable(code, scalar) ? code : null;
    }

    /// <summary>Whether the font draws the code: see the class remarks.</summary>
    private bool IsUsable(EncodedCode code, int scalar)
    {
        bool blank = Rune.IsWhiteSpace(new Rune(scalar));
        switch (_font)
        {
            case PdfType3Font type3:
                return code.Length == 1 && type3.GetCharProc((byte)code.Value) is not null;
            case PdfType0Font type0:
                {
                    if (type0.DescendantFont is not { } descendant)
                    {
                        return false;
                    }

                    if (descendant.Program is not { } program)
                    {
                        return true;
                    }

                    byte[] bytes = Bytes(code);
                    int cid = type0.Encoding.GetCid(type0.Encoding.ReadCode(bytes));
                    return descendant.TryGetGlyphId(cid, out int glyph) && HasOutline(program, glyph, blank);
                }

            case PdfTrueTypeFont trueType:
                return trueType.Program is not { } trueTypeProgram || HasOutline(trueTypeProgram, trueType.GetGlyphId((byte)code.Value), blank);
            case PdfType1Font type1:
                return type1.Program is not { } type1Program || HasOutline(type1Program, type1.GetGlyphId((byte)code.Value), blank);
            default:
                return _font.Program is null;
        }
    }

    private bool HasOutline(FontProgram program, int glyph, bool blank)
    {
        if (glyph <= 0 || glyph >= program.GlyphCount)
        {
            return false;
        }

        if (blank)
        {
            return true;
        }

        _outline.Clear();
        return program.GetOutline(glyph, _outline) == GlyphOutlineStatus.Complete && !_outline.IsEmpty;
    }

    private static byte[] Bytes(EncodedCode code)
    {
        byte[] bytes = new byte[code.Length];
        for (int index = 0; index < code.Length; index++)
        {
            bytes[index] = (byte)(code.Value >> (8 * (code.Length - 1 - index)));
        }

        return bytes;
    }

    private static int? Single(ReadOnlySpan<char> text) =>
        Rune.DecodeFromUtf16(text, out Rune rune, out int consumed) == System.Buffers.OperationStatus.Done && consumed == text.Length && rune.Value != FontUnicode.Replacement
            ? rune.Value
            : null;
}
