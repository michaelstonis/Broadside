using Broadside.Caching;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside;

/// <summary>Embedded CMaps and CIDFont programs (issue #53).</summary>
public sealed partial class PdfDocument
{
    private readonly OnceCache<CMapKey, CMap?> _cmaps = new();
    private readonly OnceCache<string, CMap?> _predefinedCMaps = new(StringComparer.Ordinal);

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
                parent = FindPredefinedCMap(name.Value, context, depth);
                break;
            case null or CosNull:
                if (file.UseCMapName is { } named)
                {
                    parent = FindPredefinedCMap(named, context, depth);
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
    /// A predefined CMap by name (§9.7.5.2, Table 116), recorded as unavailable when no font resolver has it, in which case a
    /// Table 116 name still gives its codespace (<see cref="PredefinedCMapTable"/>); see <see cref="FindPredefinedCMap(string, int)"/>.
    /// </summary>
    private CMap? FindPredefinedCMap(string name, CMapContext context, int depth)
    {
        if (FindPredefinedCMap(name, depth + 1) is { } cmap)
        {
            return cmap;
        }

        CMap? fallback = PredefinedCMapTable.CreateFallback(name);
        context.Report(Parsing.DiagnosticCodes.CMapUnavailable, DiagnosticSeverity.Information, UnavailableMessage(name, fallback is not null));
        return fallback;
    }

    /// <summary>The message of <c>CMapUnavailable</c>: names the CMap, the package that has it, and what is read instead.</summary>
    internal static string UnavailableMessage(string name, bool known) => known
        ? $"The predefined CMap /{name} is not available (ISO 32000-2 §9.7.5.2, Table 116): add the Broadside.Fonts.Cmaps package and call options.UsePredefinedCMaps(), or register a font resolver that supplies it. Codes are split by its codespace and show the glyph of CID 0."
        : $"/{name} is not a predefined CMap of ISO 32000-2 Table 116 and no font resolver supplies it (§9.7.5.2); codes are read as Identity-H.";

    /// <summary>
    /// Returns a predefined CMap by name (§9.7.5.2, Table 116): Identity-H and Identity-V are built in; any other comes from the
    /// engine's font resolvers (issue #59, <see cref="FontResourceKind.CMap"/>), parsed once per document, with the CMap its
    /// <c>usecmap</c> names found the same way.
    /// </summary>
    /// <param name="name">The CMap's name.</param>
    /// <param name="depth">How many CMaps use this one through <c>usecmap</c> on the way here.</param>
    /// <returns>The CMap, or <see langword="null"/> when no resolver has it (the caller records that).</returns>
    /// <remarks>Adobe TN 5014 §7.4: usecmap nests at most five levels; deeper, or in a cycle, the CMap is not available.</remarks>
    internal CMap? FindPredefinedCMap(string name, int depth = 0)
    {
        if (CMap.FindBuiltIn(name) is { } builtIn)
        {
            return builtIn;
        }

        if (depth > CMapContext.DefaultMaxUseCMapDepth)
        {
            return null;
        }

        return _predefinedCMaps.GetOrCreate(
            name,
            (Document: this, Depth: depth),
            static (key, state) => new Created<CMap?>(state.Document.LoadPredefinedCMap(key, state.Depth)),
            static (key, state) =>
            {
                state.Document._diagnostics.Report(
                    Parsing.DiagnosticCodes.CMapUseCMapCycle,
                    DiagnosticSeverity.Error,
                    $"The predefined CMap /{key} uses itself through its usecmap chain (Adobe TN 5014 §7.4); the chain is cut there.");
                return null;
            });
    }

    /// <summary>Reads a predefined CMap a font resolver supplies (TN 5014 text syntax) and the CMap it uses.</summary>
    private CMap? LoadPredefinedCMap(string name, int depth)
    {
        if (!FontResolvers.TryResolveResource(FontResourceKind.CMap, name, out ReadOnlyMemory<byte> data))
        {
            return null;
        }

        var context = new CMapContext(_diagnostics, objectReference: null);
        CMapFile file = Fonts.CMapParser.Parse(data.Span, context);
        CMap? parent = file.UseCMapName is { } used ? FindPredefinedCMap(used, context, depth) : null;
        return CMap.Create(file, parent, file.WMode ?? 0, context);
    }

    /// <summary>A CMap stream at one version: a changed stream is parsed again.</summary>
    private readonly record struct CMapKey(CosStream Stream, int Version);
}
