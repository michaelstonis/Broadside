using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Fonts.Resolution;

/// <summary>
/// Finds the program a simple font that is not embedded is drawn with: the engine's resolvers for the font itself, then for the
/// most similar Standard 14 font, recording what was substituted or that nothing was found.
/// </summary>
/// <remarks>ISO 32000-2 §9.5 NOTE 5, §9.6.2.2, §9.8.1 (Table 120) and §9.8.2 (Table 121). ADR 0009.</remarks>
internal static class FontSubstitution
{
    /// <summary>Resolves the substitute of a font, reporting on the font's object.</summary>
    /// <param name="font">The font, which has no usable embedded program.</param>
    /// <param name="metrics">Its current metrics (for the Standard 14 match).</param>
    /// <returns>The substitute, or <see langword="null"/> when none was found.</returns>
    public static FontSubstitute? Resolve(PdfSimpleFont font, SimpleFontMetrics metrics)
    {
        FontQuery query = CreateQuery(font, metrics);
        FontResolverChain resolvers = font.Document.FontResolvers;
        FontResolution? resolution = resolvers.ResolveFont(query);
        FontMatchKind kind = resolution?.MatchKind ?? FontMatchKind.Exact;
        Standard14Font? similar = null;
        if (resolution is null && query.Standard14 is null && ChooseSimilar(query) is { } stand)
        {
            similar = stand;
            resolution = resolvers.ResolveFont(CreateStandard14Query(stand));
            kind = FontMatchKind.Similar;
        }

        string requested = font.BaseFont ?? "(no BaseFont)";
        if (resolution is null)
        {
            font.Report(
                DiagnosticCodes.FontProgramNotFound,
                DiagnosticSeverity.Information,
                $"No font program was found for the font /{requested}, which is not embedded (ISO 32000-2 §9.6.2.2, §9.8){(query.IsSymbolic && similar is null && query.Standard14 is null ? "; it is symbolic, so no text font stands in for it" : string.Empty)}. Its glyphs are not drawn; its widths and glyph names still apply. Configure the Standard 14 fonts package (PdfOptions.UseStandard14Fonts()) or a font resolver (PdfOptions.UseFontResolver).");
            return null;
        }

        FontProgram? program = font.Document.GetSubstituteProgram(resolution, font.Reference);
        if (program is null)
        {
            font.Report(
                DiagnosticCodes.FontSubstituteUnreadable,
                DiagnosticSeverity.Information,
                $"The font program {resolution.Name} found for the font /{requested} cannot be read by the engine's font program parsers; the font's glyphs are not drawn.");
            return null;
        }

        if (kind is FontMatchKind.Family or FontMatchKind.Similar)
        {
            string reason = kind == FontMatchKind.Family
                ? "a font of the same family and style"
                : $"no font of that name or family was found; standing in for {Standard14Data.PostScriptName(similar!.Value)}, chosen as {Describe(query)} from the font descriptor and name (§9.8.2)";
            font.Report(
                DiagnosticCodes.FontSubstituted,
                DiagnosticSeverity.Information,
                $"The font /{requested} is not embedded; {resolution.Name} is used in its place ({reason}). Text keeps the font's own widths (ISO 32000-2 §9.2.4).");
        }

        bool zapf = (query.Standard14 ?? similar) == Standard14Font.ZapfDingbats;
        return new FontSubstitute(program, resolution.Name, font.BaseFont, kind, query.IsSymbolic, zapf);
    }

    /// <summary>The query for a font: its name, type and descriptor facts (§9.6.2.1 Table 109, §9.8.1 Table 120).</summary>
    internal static FontQuery CreateQuery(PdfSimpleFont font, SimpleFontMetrics metrics)
    {
        PdfFontDescriptor? descriptor = font.Descriptor;
        return new FontQuery(font.BaseFont is { Length: > 0 } name ? name : metrics.Standard14 is { } std ? Standard14Data.PostScriptName(std) : "Unnamed")
        {
            FontType = font.FontType,
            Standard14 = metrics.Standard14,
            Flags = descriptor?.Flags ?? PdfFontFlags.None,
            FontFamily = descriptor?.FontFamily,
            FontWeight = descriptor?.FontWeight,
            FontStretch = descriptor?.FontStretch,
            ItalicAngle = descriptor?.ItalicAngle ?? 0,
            StemV = descriptor?.StemV ?? 0,
        };
    }

    /// <summary>The query for one of the Standard 14 fonts, as asked when no resolver has the font itself.</summary>
    internal static FontQuery CreateStandard14Query(Standard14Font font) => new(Standard14Data.PostScriptName(font))
    {
        FontType = PdfFontType.Type1,
        Standard14 = font,
        Flags = Standard14Data.Flags(font),
        ItalicAngle = Standard14Data.Metric(font, Standard14Metric.ItalicAngle),
        StemV = Standard14Data.Metric(font, Standard14Metric.StemV),
    };

    /// <summary>
    /// The Standard 14 font most like a font: Courier for fixed pitch, Times for serif, Helvetica otherwise, in its weight and slope;
    /// for a symbolic font only Symbol or ZapfDingbats when its name says so, and none otherwise (never a text font).
    /// </summary>
    internal static Standard14Font? ChooseSimilar(FontQuery query)
    {
        if (query.IsSymbolic)
        {
            string key = query.FamilyKey;
            return key.Contains("dingbat", StringComparison.Ordinal) || key.Contains("zapf", StringComparison.Ordinal) ? Standard14Font.ZapfDingbats
                : key.Contains("symbol", StringComparison.Ordinal) ? Standard14Font.Symbol
                : null;
        }

        Standard14Font family = query.IsFixedPitch ? Standard14Font.Courier : query.IsSerif ? Standard14Font.TimesRoman : Standard14Font.Helvetica;
        return family + (query.IsBold ? 1 : 0) + (query.IsItalic ? 2 : 0);
    }

    private static string Describe(FontQuery query) =>
        string.Join(
            ' ',
            new[]
            {
                query.IsFixedPitch ? "fixed pitch" : query.IsSerif ? "serif" : "sans serif",
                query.IsBold ? "bold" : null,
                query.IsItalic ? "italic" : null,
            }.OfType<string>());
}
