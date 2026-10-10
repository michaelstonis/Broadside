using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside;

/// <summary>Font programs (issue #50): parsing embedded programs through the engine's font program parsers.</summary>
public sealed partial class PdfDocument
{
    private PdfFont? _fallbackFont;

    /// <summary>
    /// Gets the font text is measured with when no usable font is selected (text shown before <c>Tf</c>, or a <c>Tf</c> name missing
    /// from the resources): non-embedded Helvetica with its Standard 14 metrics, as PDFBox does. It belongs to no page.
    /// </summary>
    internal PdfFont FallbackFont
    {
        get
        {
            if (_fallbackFont is { } font)
            {
                return font;
            }

            var dictionary = new CosDictionary
            {
                [Objects.KnownNames.Type] = FontNames.Font,
                [FontNames.Subtype] = FontNames.Type1,
                [FontNames.BaseFont] = new CosName("Helvetica"),
            };
            Interlocked.CompareExchange(ref _fallbackFont, PdfFont.Create(this, dictionary, reference: null), null);
            return _fallbackFont;
        }
    }

    /// <summary>Decodes a font file stream and hands it to the parser the engine picks (ISO 32000-2 §9.9, Tables 124 and 125).</summary>
    private FontProgram? ParseFontProgram(CosStream stream, CosReference? reference, FontProgramSource source, bool isCidFont, string? faceName)
    {
        CosDictionary dictionary = stream.Dictionary;
        var context = new FontProgramContext(_diagnostics, reference)
        {
            Source = source,
            Subtype = Resolve(dictionary.TryGetValue(FontNames.Subtype, out CosObject? subtype) ? subtype : null) as CosName,
            Length1 = ReadLength(dictionary, FontNames.Length1),
            Length2 = ReadLength(dictionary, FontNames.Length2),
            Length3 = ReadLength(dictionary, FontNames.Length3),
            IsCidFont = isCidFont,
            FaceName = faceName,
        };

        ReadOnlyMemory<byte> data = _streams.Decode(stream);
        return _fontProgramParsers.Parse(data, context);
    }

    private long? ReadLength(CosDictionary dictionary, CosName key) =>
        dictionary.TryGetValue(key, out CosObject? value) && Resolve(value) is CosInteger length ? length.Value : null;

    /// <summary>A font file stream at one version: a changed stream is parsed again.</summary>
    private readonly record struct FontFileKey(CosStream Stream, int Version);
}
