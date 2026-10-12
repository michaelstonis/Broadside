using System.Diagnostics.CodeAnalysis;
using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Content;

/// <summary>
/// One glyph whose outline is added to the clipping path by a text object shown in a clipping rendering mode: the font, the code
/// and where the glyph was placed. The display-list builder resolves the outline through the font.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.3.6, Table 104: in modes 4 to 7 the glyph outlines of a text object are accumulated and, at <c>ET</c>, intersected
/// with the clipping path under the nonzero winding rule. Type 3 glyphs never clip.
/// </remarks>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "A record of interpreter output, never compared.")]
public readonly struct TextClipGlyph
{
    internal TextClipGlyph(PdfFont font, uint characterCode, Matrix textMatrix, Matrix ctm)
    {
        Font = font;
        CharacterCode = characterCode;
        TextMatrix = textMatrix;
        Ctm = ctm;
    }

    /// <summary>Gets the font the glyph was shown in.</summary>
    public PdfFont Font { get; }

    /// <summary>Gets the character code.</summary>
    public uint CharacterCode { get; }

    /// <summary>Gets the matrix from text space to user space for the glyph, as <see cref="GlyphEvent.TextMatrix"/>.</summary>
    public Matrix TextMatrix { get; }

    /// <summary>Gets the CTM when the glyph was shown.</summary>
    public Matrix Ctm { get; }
}
