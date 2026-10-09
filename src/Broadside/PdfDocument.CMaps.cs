using Broadside.Caching;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside;

/// <summary>Embedded CMaps and CIDFont programs (issue #53).</summary>
public sealed partial class PdfDocument
{
    private readonly OnceCache<CMapKey, CMap?> _cmaps = new();

    /// <summary>
    /// Returns the parsed program of a font file stream for a CIDFont, as <see cref="GetFontProgram(CosStream, CosReference?, FontProgramSource, bool, PdfFont)"/>
    /// does for a simple font: parsed once per stream, with <paramref name="faceName"/> picking the font of a collection.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.9 and §9.7.4.2.</remarks>
    internal FontProgram? GetFontProgram(CosStream stream, CosReference? reference, FontProgramSource source, string? faceName) =>
        _fontPrograms.GetOrCreate(
            new FontFileKey(stream, stream.Version),
            (Document: this, Reference: reference, Source: source, FaceName: faceName),
            static (key, state) => new Created<FontProgram?>(state.Document.ParseFontProgram(key.Stream, state.Reference, state.Source, isCidFont: true, state.FaceName)),
            static (_, _) => null);

    /// <summary>
    /// Returns the CMap of an embedded CMap stream, parsed once per stream (and again after it changes) and shared by every font
    /// and thread that uses it; its <c>UseCMap</c> chain is resolved through the same cache.
    /// </summary>
    /// <param name="stream">The CMap stream.</param>
    /// <param name="reference">The reference it was reached through, which its diagnostics carry.</param>
    /// <param name="depth">How many CMaps use this one through <c>UseCMap</c> on the way here.</param>
    /// <returns>The CMap; <see langword="null"/> only when the stream is reached again through its own <c>UseCMap</c> chain.</returns>
    /// <remarks>ISO 32000-2 §9.7.5.3, Table 118, and §9.7.5.4; Adobe TN 5014 §5.4 and §7.4 (usecmap nests at most five levels).</remarks>
    internal CMap? GetCMap(CosStream stream, CosReference? reference, int depth = 0) =>
        _cmaps.GetOrCreate(
            new CMapKey(stream, stream.Version),
            (Document: this, Reference: reference, Depth: depth),
            static (key, state) => new Created<CMap?>(state.Document.ParseCMap(key.Stream, state.Reference, state.Depth)),
            static (key, state) =>
            {
                state.Document._diagnostics.Report(
                    Parsing.DiagnosticCodes.CMapUseCMapCycle,
                    DiagnosticSeverity.Error,
                    "A CMap uses itself through its UseCMap chain (ISO 32000-2 §9.7.5.3, Table 118); the chain is cut there.",
                    objectReference: state.Reference);
                return null;
            });

    /// <summary>Parses an embedded CMap stream and resolves the CMap it uses (§9.7.5.3, Table 118).</summary>
    private CMap ParseCMap(CosStream stream, CosReference? reference, int depth)
    {
        var context = new CMapContext(_diagnostics, reference);
        CMapFile file = Fonts.CMapParser.Parse(_streams.Decode(stream).Span, context);
        CosDictionary dictionary = stream.Dictionary;
        CMap? parent = null;
        CosObject? useCMap = dictionary.TryGetValue(CompositeFontNames.UseCMap, out CosObject? use) ? Resolve(use) : null;
        switch (useCMap)
        {
            case CosStream when depth >= context.MaxUseCMapDepth:
                context.Report(
                    Parsing.DiagnosticCodes.CMapUseCMapTooDeep,
                    DiagnosticSeverity.Warning,
                    $"CMaps nest through usecmap at most {context.MaxUseCMapDepth} levels (Adobe TN 5014 §7.4); the deeper CMaps are ignored.");
                break;
            case CosStream parentStream:
                parent = GetCMap(parentStream, use as CosReference, depth + 1);
                if (parent is not null && file.UseCMapName is { } inFile && parent.Name is { } parentName && inFile != parentName)
                {
                    context.Report(
                        Parsing.DiagnosticCodes.CMapUseCMapInvalid,
                        DiagnosticSeverity.Warning,
                        $"The CMap file uses /{inFile} but its UseCMap stream is /{parentName} (ISO 32000-2 §9.7.5.4 a); the UseCMap stream is used.");
                }

                break;
            case CosName name:
                parent = FindPredefinedCMap(name.Value, context);
                break;
            case null or CosNull:
                if (file.UseCMapName is { } named)
                {
                    parent = FindPredefinedCMap(named, context);
                }

                break;
            default:
                context.Report(
                    Parsing.DiagnosticCodes.CMapUseCMapInvalid,
                    DiagnosticSeverity.Warning,
                    "UseCMap shall be a name or a CMap stream (ISO 32000-2 §9.7.5.3, Table 118); ignored.");
                break;
        }

        int? dictionaryMode = dictionary.TryGetValue(CompositeFontNames.WMode, out CosObject? mode) && Resolve(mode) is CosInteger integer ? (int)Math.Clamp(integer.Value, 0, 1) : null;
        if (file.WMode is { } fileMode && dictionaryMode is { } streamMode && fileMode != streamMode)
        {
            context.Report(
                Parsing.DiagnosticCodes.CMapWritingModeMismatch,
                DiagnosticSeverity.Warning,
                "The CMap stream's WMode differs from its file's (ISO 32000-2 §9.7.5.3, Table 118); the file's is used.");
        }

        return CMap.Create(file, parent, file.WMode ?? dictionaryMode ?? 0, context);
    }

    /// <summary>
    /// A predefined CMap by name (§9.7.5.2, Table 116): Identity-H and Identity-V are built in; the others come with the CMaps package,
    /// and are recorded as unavailable without it.
    /// </summary>
    private static CMap? FindPredefinedCMap(string name, CMapContext context)
    {
        CMap? cmap = CMap.FindBuiltIn(name);
        if (cmap is null)
        {
            context.Report(
                Parsing.DiagnosticCodes.CMapUnavailable,
                DiagnosticSeverity.Information,
                $"The predefined CMap /{name} is not available (ISO 32000-2 §9.7.5.2, Table 116); it needs the CMaps package.");
        }

        return cmap;
    }

    /// <summary>A CMap stream at one version: a changed stream is parsed again.</summary>
    private readonly record struct CMapKey(CosStream Stream, int Version);
}
