using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>A Type 3 font, whose glyphs are content streams: a live view over its font dictionary.</summary>
/// <remarks>
/// ISO 32000-2 §9.6.4. Its encoding is entirely defined by its <c>Encoding</c> entry (§9.6.5.3): the <c>Differences</c> of the
/// encoding dictionary, over the predefined encoding its <c>BaseEncoding</c> names, if any. Its widths are in its glyph space,
/// which the font matrix maps to text space; it never takes Standard 14 metrics.
/// </remarks>
public sealed class PdfType3Font : PdfSimpleFont
{
    private static readonly CosName FontMatrixKey = new("FontMatrix");
    private static readonly CosName CharProcsKey = new("CharProcs");

    internal PdfType3Font(PdfDocument document, CosDictionary dictionary, CosReference? reference)
        : base(document, dictionary, reference, PdfFontType.Type3)
    {
    }

    /// <summary>Gets the font matrix, which maps glyph space to text space; <c>[0.001 0 0 0.001 0 0]</c> when absent or malformed.</summary>
    /// <remarks>ISO 32000-2 §9.6.4, Table 110 (<c>FontMatrix</c>, required).</remarks>
    public Matrix FontMatrix => AnnotationValues.ReadMatrix(Document, Get(FontMatrixKey)) ?? ThousandthMatrix;

    /// <summary>Gets the resources the glyph descriptions use, or <see langword="null"/> when the font has none of its own.</summary>
    /// <remarks>
    /// ISO 32000-2 §9.6.4, Table 110 (<c>Resources</c>): without them, names resolve in the resources of the page or form that shows
    /// the glyph (normative in PDF 2.0).
    /// </remarks>
    public CosDictionary? Resources => Get(KnownNames.Resources) as CosDictionary;

    /// <inheritdoc/>
    internal override bool CanUseStandard14 => false;

    /// <inheritdoc/>
    internal override Matrix GlyphSpaceMatrix => FontMatrix;

    /// <summary>Returns the glyph description of a character code: the <c>CharProcs</c> stream its glyph name selects, or <see langword="null"/>.</summary>
    /// <param name="code">The character code.</param>
    /// <returns>The content stream, or <see langword="null"/> when the encoding or <c>CharProcs</c> has none.</returns>
    /// <remarks>ISO 32000-2 §9.6.4, Table 110 (<c>CharProcs</c>).</remarks>
    public CosStream? GetCharProc(byte code)
    {
        if (Get(CharProcsKey) is not CosDictionary procedures)
        {
            return null;
        }

        string name = GetGlyphName(code);
        for (int index = 0; index < procedures.Count; index++)
        {
            KeyValuePair<CosName, CosObject> pair = procedures.GetAt(index);
            if (pair.Key.Value == name)
            {
                return Document.Resolve(pair.Value) as CosStream;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §9.6.4: the width is in glyph space; the font matrix maps it to text space.</remarks>
    internal override double GetHorizontalDisplacement(byte code) => FontMatrix.TransformVector(Metrics.Widths[code], 0).X;
}
