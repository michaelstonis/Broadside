using System.Buffers.Binary;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Fonts.TrueType;

/// <summary>
/// The managed default parser for TrueType programs: <c>FontFile2</c> streams, and <c>FontFile3</c> streams of subtype
/// <c>OpenType</c> whose outlines are "glyf" data. The first default of the font program parser extension point.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.6.3 and §9.9 (Tables 124 and 125); Apple TrueType Reference Manual and the OpenType specification for the tables:
/// "head", "hhea", "hmtx", "maxp", "loca", "glyf" (simple and composite glyphs), "cmap" (formats 0, 2, 4, 6, 12, 13) and "post". A
/// TrueType collection is read too, the font chosen by <see cref="FontProgramContext.FaceName"/> or
/// <see cref="FontProgramContext.FaceIndex"/>.
/// </para>
/// <para>
/// Outlines are quadratic, unhinted: glyph instructions, "fpgm", "prep" and "cvt " are skipped, never executed, so glyphs whose
/// components only assemble through instructions (a few CJK fonts) draw scrambled. Each outline is translated by the difference
/// between its "hmtx" left side bearing and its bounding box's left edge, as TrueType rasterizers place glyphs.
/// </para>
/// <para>
/// Leniency follows ADR 0005: a missing "head", "hhea", "hmtx" or "maxp" is replaced by defaults with a diagnostic; only a program
/// without "glyf" and "loca" is unusable. A damaged glyph is dropped (<see cref="GlyphOutlineStatus.Invalid"/>), never drawn in part.
/// </para>
/// </remarks>
public sealed class TrueTypeFontProgramParser : IFontProgramParser
{
    private static readonly uint Glyf = SfntFile.Tag("glyf");
    private static readonly uint Loca = SfntFile.Tag("loca");
    private static readonly uint Head = SfntFile.Tag("head");
    private static readonly uint Hhea = SfntFile.Tag("hhea");
    private static readonly uint Hmtx = SfntFile.Tag("hmtx");
    private static readonly uint Maxp = SfntFile.Tag("maxp");
    private static readonly uint Cmap = SfntFile.Tag("cmap");
    private static readonly uint Post = SfntFile.Tag("post");
    private static readonly uint Cff = SfntFile.Tag("CFF ");

    /// <inheritdoc/>
    public IReadOnlyList<FontProgramFormat> Formats { get; } = [FontProgramFormat.TrueType];

    /// <inheritdoc/>
    /// <remarks>
    /// Accepts the TrueType sfnt versions <c>00 01 00 00</c> and <c>true</c>, a TrueType collection (<c>ttcf</c>), and an OpenType
    /// program (<c>OTTO</c>) only when it has a "glyf" table: ISO 32000-2 Table 124 allows "glyf" outlines in <c>OpenType</c> streams.
    /// </remarks>
    public bool CanParse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
        {
            return false;
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(data);
        return version switch
        {
            SfntFile.TrueTypeVersion or SfntFile.AppleTrueTypeVersion or SfntFile.CollectionTag => SfntFile.HasTable(data, Glyf) || !SfntFile.HasTable(data, Cff),
            SfntFile.OpenTypeVersion => SfntFile.HasTable(data, Glyf),
            _ => false,
        };
    }

    /// <inheritdoc/>
    public FontProgram? Parse(ReadOnlyMemory<byte> data, FontProgramContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Length1 is { } length1 && length1 > data.Length)
        {
            context.Report(
                DiagnosticCodes.FontProgramTruncated,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The font file stream's Length1 is {length1}, but its data is {data.Length} bytes (ISO 32000-2 §9.9, Table 125); the bytes present are read."));
        }

        if (SfntFile.Open(data, context) is not { } sfnt)
        {
            return null;
        }

        if (!sfnt.TryGetTable(Glyf, out ReadOnlyMemory<byte> glyf) || !sfnt.TryGetTable(Loca, out ReadOnlyMemory<byte> loca))
        {
            context.Report(
                DiagnosticCodes.FontProgramInvalid,
                DiagnosticSeverity.Error,
                sfnt.Has(Cff)
                    ? "The font program has CFF outlines, not the \"glyf\" and \"loca\" tables of TrueType outlines (ISO 32000-2 §9.9, Table 124); it is not usable as TrueType."
                    : "The font program has no \"glyf\" or no \"loca\" table, which a TrueType program shall have (ISO 32000-2 §9.9, Table 124); it is not usable.");
            return null;
        }

        return TrueTypeFontProgram.Create(sfnt, glyf, loca, context, Read(sfnt, Head), Read(sfnt, Hhea), Read(sfnt, Hmtx), Read(sfnt, Maxp), Read(sfnt, Cmap), Read(sfnt, Post));
    }

    private static ReadOnlyMemory<byte>? Read(SfntFile sfnt, uint tag) => sfnt.TryGetTable(tag, out ReadOnlyMemory<byte> table) ? table : default(ReadOnlyMemory<byte>?);
}
