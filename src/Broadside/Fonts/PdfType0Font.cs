using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>A composite (Type 0) font, whose glyphs come from a descendant CIDFont: a live view over its font dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.7. A Type 0 font has no font descriptor of its own (§9.8.1); its CIDFont has one. Its CMap (<see cref="Encoding"/>)
/// splits a shown string into codes of one to four bytes and maps each to a CID; the CIDFont (<see cref="DescendantFont"/>) gives the
/// CID's glyph and metrics. <see cref="ReadGlyph"/> does both for one code.
/// </para>
/// <para>
/// The CMap and the CIDFont are resolved on first use and again when the font dictionary, its <c>DescendantFonts</c> array, the
/// CIDFont dictionary or the CMap stream changes. Deviations are recorded once, on the font's object; in strict mode the call that
/// finds one throws.
/// </para>
/// </remarks>
public sealed class PdfType0Font : PdfFont
{
    private volatile Type0FontState? _state;
    private volatile bool _invalidCodeReported;

    internal PdfType0Font(PdfDocument document, CosDictionary dictionary, CosReference? reference)
        : base(document, dictionary, reference, PdfFontType.Type0)
    {
    }

    /// <summary>
    /// Gets the CMap that maps the font's character codes to CIDs: a built-in Identity CMap, a predefined CMap from the engine's
    /// font resolvers (the Broadside.Fonts.Cmaps package, <c>options.UsePredefinedCMaps()</c>), or an embedded CMap stream. A
    /// predefined CMap of Table 116 that no resolver supplies is recorded as unavailable and stood in for by a CMap with its
    /// codespace, character collection and writing mode but no mappings (every code selects CID 0). When the <c>Encoding</c> entry
    /// is missing, malformed, or names a CMap that is not in Table 116 and that no resolver supplies, codes are read as
    /// <see cref="CMap.IdentityH"/>, with a diagnostic.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.7.5, §9.7.5.2 (Table 116) and §9.7.6.1, Table 119 (<c>Encoding</c>).</remarks>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found in the font, its CMap or its CIDFont.</exception>
    public CMap Encoding => State.Encoding;

    /// <summary>Gets the writing mode of the font's CMap.</summary>
    /// <remarks>ISO 32000-2 §9.7.4.3 and §9.7.5.1: it selects horizontal (<c>W</c>, <c>DW</c>) or vertical (<c>W2</c>, <c>DW2</c>) metrics.</remarks>
    public WritingMode WritingMode => State.Encoding.WritingMode;

    /// <summary>Gets the descendant CIDFont, or <see langword="null"/> when the font has none it can use.</summary>
    /// <remarks>ISO 32000-2 §9.7.6.1, Table 119 (<c>DescendantFonts</c>), and §9.7.4.</remarks>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found in the font, its CMap or its CIDFont.</exception>
    public PdfCidFont? DescendantFont => State.Descendant;

    /// <summary>Gets the CMap and descendant, rebuilt when an object they come from has changed.</summary>
    internal Type0FontState State
    {
        get
        {
            Type0FontState? state = _state;
            if (state is null || !state.IsCurrent)
            {
                state = Type0FontState.Build(this, state);
                _state = state;
            }

            return state;
        }
    }

    /// <summary>Reads the glyph of the first character code of <paramref name="text"/>.</summary>
    /// <param name="text">The rest of a string shown with this font.</param>
    /// <returns>
    /// The code (advance by its <see cref="CharacterCode.Length"/>), its CID, the glyph id and the CID's metrics. Without a
    /// descendant CIDFont, the glyph id is 0 and the metrics are the defaults (width 1000, <c>DW2</c> <c>[880 −1000]</c>).
    /// </returns>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found, including a code outside the codespace.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §9.7.6.2: the CMap splits the code off (per-byte codespace matching) and maps it to a CID. §9.7.6.3: an invalid code,
    /// or one without a mapping, selects CID 0; when the CIDFont has no glyph for the CID, the code's notdef mapping is tried, then
    /// the glyph of CID 0. §9.7.4.2: the CID the code maps to gives the metrics even when another glyph is shown.
    /// </para>
    /// <para>Allocates nothing once the font's tables are built. An invalid code is recorded once per font.</para>
    /// </remarks>
    public CidGlyph ReadGlyph(ReadOnlySpan<byte> text)
    {
        Type0FontState state = State;
        CMap cmap = state.Encoding;
        CharacterCode code = cmap.ReadCode(text);
        if (!code.IsValid && code.Length > 0)
        {
            ReportInvalidCode(code);
        }

        int cid = cmap.GetCid(code);
        if (state.Descendant is not { } descendant)
        {
            return new CidGlyph(code, cid, 0, 1000, new CidVerticalMetrics(-1000, 500, 880));
        }

        if (!descendant.TryGetGlyphId(cid, out int glyphId)
            && !(cmap.TryGetNotdefCid(code, out int substitute) && descendant.TryGetGlyphId(substitute, out glyphId)))
        {
            glyphId = descendant.GetGlyphId(0);
        }

        CidFontMetrics metrics = descendant.Metrics;
        return new CidGlyph(code, cid, glyphId, metrics.GetWidth(cid), metrics.GetVerticalMetrics(cid));
    }

    /// <inheritdoc/>
    internal override bool IsVertical => WritingMode == WritingMode.Vertical;

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §9.7.4.3: w0 is the CID's width; in vertical writing w1 is its vertical advance and the glyph's horizontal origin
    /// lies at the position vector v from the current point. Word spacing applies only to the single-byte code 32 (§9.3.3).
    /// </remarks>
    internal override ShownGlyph ReadShownGlyph(ReadOnlySpan<byte> text)
    {
        CidGlyph glyph = ReadGlyph(text);
        int length = Math.Max(1, glyph.Code.Length);
        if (WritingMode != WritingMode.Vertical)
        {
            return new ShownGlyph(glyph.Code.Value, length, glyph.Width / 1000, 0, default, glyph.AppliesWordSpacing);
        }

        CidVerticalMetrics vertical = glyph.VerticalMetrics;
        return new ShownGlyph(
            glyph.Code.Value,
            length,
            glyph.Width / 1000,
            vertical.VerticalAdvance / 1000,
            new Graphics.PathPoint(vertical.PositionX / 1000, vertical.PositionY / 1000),
            glyph.AppliesWordSpacing);
    }

    private void ReportInvalidCode(CharacterCode code)
    {
        if (_invalidCodeReported && !Document.DiagnosticSink.IsStrict)
        {
            return;
        }

        _invalidCodeReported = true;
        Report(
            DiagnosticCodes.CMapCodeInvalid,
            DiagnosticSeverity.Warning,
            $"A shown string has a code (0x{code.Value:X}) in no codespace range of the font's CMap (ISO 32000-2 §9.7.6.3); it takes {code.Length} byte(s) and shows the glyph of CID 0.");
    }
}
