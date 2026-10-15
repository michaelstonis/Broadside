using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside;

/// <summary>Writing text (issue #149): the fonts a <see cref="PdfCanvas"/> shows text with.</summary>
public sealed partial class PdfDocument
{
    /// <summary>Returns a non-embedded Standard 14 font a <see cref="PdfCanvas"/> of this document can show text with, adding its font dictionary the first time.</summary>
    /// <param name="font">The font.</param>
    /// <returns>
    /// The font: the twelve Latin fonts in <c>WinAnsiEncoding</c>, Symbol and ZapfDingbats in their built-in encodings. Every call for
    /// the same font uses one font dictionary of the document (with <c>Widths</c> and a <c>FontDescriptor</c> from the AFM metrics,
    /// and no font program).
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="font"/> is not a defined font.</exception>
    /// <remarks>
    /// ISO 32000-2 §9.6.2.2 (Standard 14 fonts), §9.6.2.1 (Table 109) and Annex D (WinAnsiEncoding, the Symbol and ZapfDingbats
    /// encodings). A font that is not embedded is drawn by the reader's own copy of the font or a substitute; PDF/A and PDF/UA require
    /// embedded fonts. Text the font's encoding has no code for needs an embedded font. A mutation (the first call for a font).
    /// </remarks>
    public PdfCanvasFont GetStandard14Font(Standard14Font font)
    {
        if (!Enum.IsDefined(font))
        {
            throw new ArgumentOutOfRangeException(nameof(font), font, "Not a Standard 14 font.");
        }

        return Standard14CanvasFont.Get(this, font);
    }
}
