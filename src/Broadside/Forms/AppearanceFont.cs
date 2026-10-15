using System.Text;
using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside.Forms;

/// <summary>A font an appearance stream shows variable text with: how text encodes in it, how wide it is, and its vertical extent.</summary>
/// <remarks>ISO 32000-2 §12.7.4.3 (the default appearance font) and §9.2.4 (glyph widths in thousandths of text space).</remarks>
internal abstract class AppearanceFont
{
    /// <summary>Gets the font object the appearance's <c>Font</c> resources hold (a reference or a dictionary).</summary>
    public abstract CosObject FontObject { get; }

    /// <summary>Gets the ascent above the baseline, in thousandths of the font size.</summary>
    public abstract double Ascent { get; }

    /// <summary>Gets the descent below the baseline (negative), in thousandths of the font size.</summary>
    public abstract double Descent { get; }

    /// <summary>Gets a value indicating whether the shown string is written as hexadecimal (two-byte codes).</summary>
    public virtual bool IsHexadecimal => false;

    /// <summary>Whether every character of <paramref name="text"/> has a code and a glyph in the font.</summary>
    public abstract bool CanEncode(string text);

    /// <summary>The string that shows <paramref name="text"/>; every character must be encodable (<see cref="CanEncode"/>).</summary>
    public abstract byte[] Encode(string text);

    /// <summary>The width of <paramref name="text"/> in thousandths of the font size (§9.2.4).</summary>
    public abstract double Measure(string text);

    /// <summary>Reads the ascent and descent of a font from its descriptor, else its bounding box, else 800 and -200.</summary>
    private protected static (double Ascent, double Descent) Extent(PdfFont font)
    {
        PdfFontDescriptor? descriptor = font.Descriptor;
        double ascent = descriptor?.Ascent ?? 0;
        double descent = descriptor?.Descent ?? 0;
        if (ascent <= 0 && descriptor is not null)
        {
            ascent = descriptor.FontBBox.Top;
            descent = descriptor.FontBBox.Bottom;
        }

        if (ascent <= 0)
        {
            return (800, -200);
        }

        return (ascent, descent > 0 ? -descent : descent);
    }
}

/// <summary>A simple font of the default resources (Type 1, TrueType): one byte per character, through its encoding's glyph names.</summary>
/// <remarks>ISO 32000-2 §9.6.5 (character encoding) and Annex D; a character maps to the lowest code whose glyph name stands for it.</remarks>
internal sealed class SimpleAppearanceFont : AppearanceFont
{
    private readonly PdfSimpleFont _font;
    private readonly Dictionary<int, byte> _codes = [];

    public SimpleAppearanceFont(PdfSimpleFont font, CosObject fontObject)
    {
        _font = font;
        FontObject = fontObject;
        bool zapf = font.Standard14 == Standard14Font.ZapfDingbats;
        for (int code = 0; code < 256; code++)
        {
            string? name = font.GetGlyphName((byte)code);
            if (!string.IsNullOrEmpty(name) && name != ".notdef" && AdobeGlyphList.TryGetScalar(name, zapf, out int scalar))
            {
                _codes.TryAdd(scalar, (byte)code);
            }
        }

        (Ascent, Descent) = Extent(font);
    }

    public override CosObject FontObject { get; }

    public override double Ascent { get; }

    public override double Descent { get; }

    public override bool CanEncode(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!_codes.ContainsKey(rune.Value))
            {
                return false;
            }
        }

        return true;
    }

    public override byte[] Encode(string text)
    {
        var bytes = new List<byte>(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            bytes.Add(_codes[rune.Value]);
        }

        return [.. bytes];
    }

    public override double Measure(string text)
    {
        double width = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            width += _font.GetWidth(_codes[rune.Value]);
        }

        return width;
    }
}

/// <summary>
/// A canvas font an appearance shows text with: for text the default appearance font cannot encode, the TrueType program the engine's
/// font resolvers find, embedded through the same <see cref="EmbeddedCanvasFont"/> (and the same shared subset) a canvas uses.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.7 (Type 0 fonts, CIDFontType2, <c>W</c>, <c>CIDToGIDMap</c>), §9.8 (descriptor, <c>FontFile2</c>), §9.9.2 (subset
/// tag) and §9.10.3 (<c>ToUnicode</c>). Every appearance of a document that needs the same program shows it through one subset, grown
/// as needed (issues #150, #285).
/// </remarks>
internal sealed class CanvasAppearanceFont : AppearanceFont
{
    private readonly PdfCanvasFont _font;

    private CanvasAppearanceFont(PdfCanvasFont font) => _font = font;

    public override CosObject FontObject => _font.Reference;

    public override double Ascent => _font.Ascent;

    public override double Descent => _font.Descent;

    public override bool IsHexadecimal => _font.CodeLength > 1;

    /// <summary>
    /// Finds a TrueType program for <paramref name="text"/> through the document's font resolvers (one like <paramref name="like"/>,
    /// then Helvetica's stand-in), and adds the text's glyphs to the document's shared subset of that program, adding the subset font
    /// to the document when it has none yet.
    /// </summary>
    /// <param name="document">The document the font is added to.</param>
    /// <param name="like">The default appearance font, whose name and style choose the program; or <see langword="null"/>.</param>
    /// <param name="text">Every text the font will show.</param>
    /// <param name="missing">When no program has glyphs for all of it, the first character none has; else <see langword="null"/>.</param>
    /// <returns>The font, or <see langword="null"/> when no resolver found a TrueType program covering the text.</returns>
    public static CanvasAppearanceFont? CreateEmbedded(PdfDocument document, PdfFont? like, IReadOnlyList<string> text, out string? missing)
    {
        missing = null;
        var queries = new List<FontQuery>();
        Standard14Font? standard14 = (like as PdfSimpleFont)?.Standard14;
        if (like?.BaseFont is { Length: > 0 } baseFont)
        {
            queries.Add(new FontQuery(baseFont) { FontType = PdfFontType.TrueType, Standard14 = standard14 });
        }

        if (standard14 != Standard14Font.Helvetica)
        {
            queries.Add(new FontQuery("Helvetica") { FontType = PdfFontType.TrueType, Standard14 = Standard14Font.Helvetica });
        }

        foreach (FontQuery query in queries)
        {
            if (document.FontResolvers.ResolveFont(query) is not { } resolution)
            {
                continue;
            }

            if (EmbeddedCanvasFont.Get(document, resolution, text, out string? uncovered, out _) is { } font)
            {
                missing = null;
                return new CanvasAppearanceFont(font);
            }

            missing ??= uncovered;
        }

        return null;
    }

    public override bool CanEncode(string text) => _font.CanEncode(text);

    public override byte[] Encode(string text)
    {
        var bytes = new List<byte>(text.Length * _font.CodeLength);
        int index = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            CanvasGlyph glyph = _font.GlyphOrThrow(rune, index, nameof(text));
            for (int shift = 8 * (_font.CodeLength - 1); shift >= 0; shift -= 8)
            {
                bytes.Add((byte)(glyph.Code >> shift));
            }

            _font.OnShown(glyph);
            index += rune.Utf16SequenceLength;
        }

        return [.. bytes];
    }

    public override double Measure(string text) => _font.MeasureWidth(text, 1000);
}

