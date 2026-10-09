using Broadside.Diagnostics;
using Broadside.Fonts.TrueType;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>
/// One engine's font program parsers: those its options registered, newest first, then the managed defaults. Immutable; built once
/// when the engine is constructed and shared by every document it opens. There is no static or global registry.
/// </summary>
/// <remarks>ISO 32000-2 §9.9, Table 124. The CFF (#51) and Type 1 (#52) parsers join the defaults as they land.</remarks>
internal sealed class FontProgramParserRegistry
{
    private static readonly CosName Type1C = new("Type1C");
    private static readonly CosName CidFontType0C = new("CIDFontType0C");
    private static readonly CosName OpenType = new("OpenType");

    private readonly IFontProgramParser[] _parsers;

    private FontProgramParserRegistry(IFontProgramParser[] parsers) => _parsers = parsers;

    /// <summary>Gets the managed default parsers, one stateless instance each, in the order they are tried.</summary>
    public static IReadOnlyList<IFontProgramParser> Defaults { get; } = [new TrueTypeFontProgramParser()];

    /// <summary>Gets the registry of the defaults only.</summary>
    public static FontProgramParserRegistry Default { get; } = new([.. Defaults]);

    /// <summary>Builds a registry trying <paramref name="registrations"/> newest first, then the defaults.</summary>
    /// <param name="registrations">The parsers registered through the options, in registration order.</param>
    /// <returns>The registry.</returns>
    public static FontProgramParserRegistry Create(IReadOnlyList<IFontProgramParser> registrations) =>
        registrations.Count == 0 ? Default : new([.. registrations.Reverse(), .. Defaults]);

    /// <summary>The format a font file stream declares: its descriptor key, and for <c>FontFile3</c> its <c>Subtype</c> (Table 124).</summary>
    /// <param name="source">The descriptor key.</param>
    /// <param name="subtype">The stream's <c>Subtype</c>.</param>
    /// <returns>The format, or <see langword="null"/> when nothing is declared or the subtype is unknown.</returns>
    public static FontProgramFormat? DeclaredFormat(FontProgramSource source, CosName? subtype) => source switch
    {
        FontProgramSource.FontFile => FontProgramFormat.Type1,
        FontProgramSource.FontFile2 => FontProgramFormat.TrueType,
        FontProgramSource.FontFile3 when Type1C.Equals(subtype) || CidFontType0C.Equals(subtype) => FontProgramFormat.Cff,
        FontProgramSource.FontFile3 when OpenType.Equals(subtype) => FontProgramFormat.OpenType,
        _ => null,
    };

    /// <summary>Picks a parser for a program and parses it; reports why when no parser applies or the parser fails.</summary>
    /// <param name="data">The decoded program.</param>
    /// <param name="context">The context, with the declared source and subtype.</param>
    /// <returns>The program, or <see langword="null"/>.</returns>
    public FontProgram? Parse(ReadOnlyMemory<byte> data, FontProgramContext context)
    {
        FontProgramFormat? declared = DeclaredFormat(context.Source, context.Subtype);
        IFontProgramParser? parser = null;
        foreach (IFontProgramParser candidate in _parsers)
        {
            if (candidate.CanParse(data.Span))
            {
                parser = candidate;
                break;
            }
        }

        if (parser is not null)
        {
            if (declared is { } format && !Handles(parser, format))
            {
                context.Report(
                    DiagnosticCodes.FontProgramFormatMismatch,
                    DiagnosticSeverity.Warning,
                    $"The font file stream declares a {format} program (ISO 32000-2 §9.9, Table 124), but its data is a {string.Join(" or ", parser.Formats)} program; it is read as such.");
            }
        }
        else if (declared is { } format)
        {
            parser = _parsers.FirstOrDefault(candidate => candidate.Formats.Contains(format));
        }

        if (parser is null)
        {
            context.Report(
                DiagnosticCodes.FontProgramUnsupported,
                DiagnosticSeverity.Information,
                declared is { } format
                    ? $"No font program parser reads {format} programs yet (ISO 32000-2 §9.9); the font's glyphs are not available."
                    : "The font program's format is not recognized (ISO 32000-2 §9.9, Table 124); the font's glyphs are not available.");
            return null;
        }

        try
        {
            return parser.Parse(data, context);
        }
        catch (Exception exception) when (exception is not DiagnosticException)
        {
            context.Report(
                DiagnosticCodes.FontProgramInvalid,
                DiagnosticSeverity.Error,
                $"The font program parser failed ({exception.GetType().Name}: {exception.Message}); the font's glyphs are not available.");
            return null;
        }
    }

    /// <summary>
    /// Whether a parser reads a declared format: an OpenType program may hold TrueType outlines (Table 124: <c>OpenType</c> with "glyf"
    /// for TrueType fonts and CIDFontType2), so a TrueType parser handles it too.
    /// </summary>
    private static bool Handles(IFontProgramParser parser, FontProgramFormat format) =>
        parser.Formats.Contains(format) || (format == FontProgramFormat.OpenType && parser.Formats.Contains(FontProgramFormat.TrueType));
}
