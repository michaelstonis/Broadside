using System.Buffers.Binary;
using Broadside.Diagnostics;
using Broadside.Fonts.TrueType;
using Broadside.Parsing;

namespace Broadside.Fonts.Cff;

/// <summary>
/// The managed default parser for CFF programs: <c>FontFile3</c> streams of subtype <c>Type1C</c> (a bare CFF font) and
/// <c>OpenType</c> programs whose outlines are a "CFF " table. Glyph outlines are Type 2 charstrings, interpreted into cubic curves.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.9 (Tables 124 and 125) and §9.6.5.2; Adobe Technical Note #5176 (The Compact Font Format Specification) and #5177
/// (The Type 2 Charstring Format); the OpenType specification for the sfnt wrapper ("CFF ", "cmap", "name"). The charset and the
/// encoding are exposed through <see cref="FontProgram.TryGetGlyphId"/>, <see cref="FontProgram.GetGlyphName"/> and
/// <see cref="FontProgram.BuiltInEncoding"/> (StandardEncoding, ExpertEncoding, or a custom encoding with its supplements named
/// through the charset), which a Type 1 font uses to select glyphs by name (§9.6.5.2; also for OpenType programs, whose "cmap" a Type 1 font does not use, §9.9).
/// </para>
/// <para>
/// Hints are parsed and ignored; the deprecated <c>dotsection</c> is a no-op; <c>endchar</c> with four arguments composes an accented
/// character from StandardEncoding components. A CID-keyed CFF program (<c>CIDFontType0C</c>, or a CFF with ROS) is recognized; its
/// outlines need its FDArray and FDSelect and are read by a later version. CFF2 (OpenType variable fonts) is not read.
/// </para>
/// <para>
/// Leniency follows ADR 0005: a damaged INDEX, DICT, charset or encoding is read as far as it goes, with a diagnostic; a damaged
/// charstring drops its glyph (<see cref="GlyphOutlineStatus.Invalid"/>), never draws it in part. Every glyph is bounded by the
/// 48-entry argument stack, 10 levels of subroutines and <see cref="FontProgramContext.MaxCharStringOperators"/>.
/// </para>
/// </remarks>
public sealed class CffFontProgramParser : IFontProgramParser
{
    private static readonly uint Cff = SfntFile.Tag("CFF ");
    private static readonly uint Cff2 = SfntFile.Tag("CFF2");
    private static readonly uint Glyf = SfntFile.Tag("glyf");
    private static readonly uint Cmap = SfntFile.Tag("cmap");

    /// <inheritdoc/>
    public IReadOnlyList<FontProgramFormat> Formats { get; } = [FontProgramFormat.Cff, FontProgramFormat.OpenType];

    /// <inheritdoc/>
    /// <remarks>
    /// Accepts a CFF header (major version 1, header size of at least 4, offSize 1 to 4: Adobe Technical Note #5176 §6) and an
    /// OpenType program (<c>OTTO</c>) with a "CFF " or "CFF2" table and no "glyf" table (which the TrueType parser reads).
    /// </remarks>
    public bool CanParse(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 12 && BinaryPrimitives.ReadUInt32BigEndian(data) == SfntFile.OpenTypeVersion)
        {
            return !SfntFile.HasTable(data, Glyf) && (SfntFile.HasTable(data, Cff) || SfntFile.HasTable(data, Cff2));
        }

        return CffFont.IsHeader(data);
    }

    /// <inheritdoc/>
    public FontProgram? Parse(ReadOnlyMemory<byte> data, FontProgramContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (data.Length >= 12 && BinaryPrimitives.ReadUInt32BigEndian(data.Span) == SfntFile.OpenTypeVersion)
        {
            return ParseOpenType(data, context);
        }

        return CffFont.Read(data, context) is { } font ? new CffFontProgram(font, context, FontProgramFormat.Cff, null, []) : null;
    }

    private static CffFontProgram? ParseOpenType(ReadOnlyMemory<byte> data, FontProgramContext context)
    {
        if (SfntFile.Open(data, context) is not { } sfnt)
        {
            return null;
        }

        if (!sfnt.TryGetTable(Cff, out ReadOnlyMemory<byte> table))
        {
            if (sfnt.Has(Cff2))
            {
                context.Report(
                    DiagnosticCodes.FontCff2Unsupported,
                    DiagnosticSeverity.Information,
                    "The OpenType program's outlines are a \"CFF2\" table (OpenType 1.8 variable CFF), which is not read; the font's glyphs are not available.");
            }
            else
            {
                context.Report(DiagnosticCodes.FontProgramInvalid, DiagnosticSeverity.Error, "The OpenType program has neither a \"CFF \" nor a \"glyf\" table (ISO 32000-2 §9.9, Table 124); it is not usable.");
            }

            return null;
        }

        if (CffFont.Read(table, context) is not { } font)
        {
            return null;
        }

        IReadOnlyList<FontCharacterMap> maps = [];
        if (sfnt.TryGetTable(Cmap, out ReadOnlyMemory<byte> cmap))
        {
            maps = CmapSubtable.ReadAll(cmap, font.GlyphCount, context);
        }

        return new CffFontProgram(font, context, FontProgramFormat.OpenType, sfnt.ReadPostScriptName(), maps);
    }
}
