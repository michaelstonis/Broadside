using Broadside.Caching;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>ToUnicode CMaps and the Registry-Ordering-UCS2 tables (issue #58).</summary>
public sealed partial class PdfDocument
{
    private readonly OnceCache<CMapKey, ToUnicodeMap?> _toUnicodeMaps = new();
    private readonly OnceCache<string, ToUnicodeMap?> _cidToUnicodeMaps = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the map of a font's <c>ToUnicode</c> entry: a ToUnicode CMap stream, parsed once per stream (and again after it
    /// changes) and shared by every font and thread that uses it, or the name <c>Identity-H</c>/<c>Identity-V</c> (with a
    /// diagnostic); <see langword="null"/> when the entry is absent or unusable.
    /// </summary>
    /// <param name="value">The entry, resolved.</param>
    /// <param name="reference">The reference the entry holds, which the stream's diagnostics carry.</param>
    /// <param name="font">The font, which diagnostics about the entry itself carry.</param>
    /// <remarks>ISO 32000-2 §9.10.3; Table 109 and Table 119 (<c>ToUnicode</c>, a stream).</remarks>
    internal ToUnicodeMap? GetToUnicode(CosObject? value, CosReference? reference, PdfFont font)
    {
        switch (value)
        {
            case null or CosNull:
                return null;
            case CosStream stream:
                return GetToUnicode(stream, reference, depth: 0);
            case CosName { Value: "Identity-H" or "Identity-V" } name:
                font.Report(
                    DiagnosticCodes.ToUnicodeIdentityName,
                    DiagnosticSeverity.Warning,
                    $"ToUnicode shall be a CMap stream, not the name /{name.Value} (ISO 32000-2 §9.10.3); each code is read as its own UTF-16 value, as viewers do.");
                return ToUnicodeMap.Identity;
            default:
                font.Report(
                    DiagnosticCodes.ToUnicodeInvalid,
                    DiagnosticSeverity.Warning,
                    "ToUnicode shall be a CMap stream (ISO 32000-2 §9.10.3, Table 109); ignored.");
                return null;
        }
    }

    /// <summary>
    /// Returns the Registry-Ordering-UCS2 table of a character collection (§9.10.2 step d), from the engine's font resolvers
    /// (<see cref="FontResourceKind.CidToUnicode"/>, the Broadside.Fonts.Cmaps package), parsed once per document.
    /// </summary>
    /// <param name="name">The table's name, such as <c>Adobe-Japan1-UCS2</c>.</param>
    /// <returns>The table, or <see langword="null"/> when no resolver has it.</returns>
    internal ToUnicodeMap? FindCidToUnicode(string name) =>
        _cidToUnicodeMaps.GetOrCreate(
            name,
            this,
            static (key, document) => new Created<ToUnicodeMap?>(document.LoadCidToUnicode(key)),
            static (_, _) => null);

    private ToUnicodeMap? GetToUnicode(CosStream stream, CosReference? reference, int depth) =>
        _toUnicodeMaps.GetOrCreate(
            new CMapKey(stream, stream.Version),
            (Document: this, Reference: reference, Depth: depth),
            static (key, state) => new Created<ToUnicodeMap?>(state.Document.ParseToUnicode(key.Stream, state.Reference, state.Depth)),
            static (_, state) =>
            {
                state.Document._diagnostics.Report(
                    DiagnosticCodes.CMapUseCMapCycle,
                    DiagnosticSeverity.Error,
                    "A ToUnicode CMap uses itself through its UseCMap chain (ISO 32000-2 §9.10.3); the chain is cut there.",
                    objectReference: state.Reference);
                return null;
            });

    /// <summary>Parses a ToUnicode CMap stream and the ToUnicode CMap it uses (§9.10.3: <c>UseCMap</c> is its only pertinent entry).</summary>
    private ToUnicodeMap ParseToUnicode(CosStream stream, CosReference? reference, int depth)
    {
        var context = new CMapContext(_diagnostics, reference);
        CMapFile file = CMapParser.Parse(_streams.Decode(stream).Span, context, unicodeDestinations: true);
        ToUnicodeMap? parent = null;
        CosObject? useCMap = stream.Dictionary.TryGetValue(CompositeFontNames.UseCMap, out CosObject? use) ? Resolve(use) : null;
        switch (useCMap)
        {
            case CosStream when depth >= context.MaxUseCMapDepth:
                context.Report(
                    DiagnosticCodes.CMapUseCMapTooDeep,
                    DiagnosticSeverity.Warning,
                    $"CMaps nest through usecmap at most {context.MaxUseCMapDepth} levels (Adobe TN 5014 §7.4); the deeper CMaps are ignored.");
                break;
            case CosStream parentStream:
                parent = GetToUnicode(parentStream, use as CosReference, depth + 1);
                break;
            case CosName name:
                parent = FindUsedToUnicode(name.Value, context);
                break;
            case null or CosNull:
                if (file.UseCMapName is { } named)
                {
                    parent = FindUsedToUnicode(named, context);
                }

                break;
            default:
                context.Report(DiagnosticCodes.CMapUseCMapInvalid, DiagnosticSeverity.Warning, "UseCMap shall be a name or a CMap stream (ISO 32000-2 §9.7.5.3, Table 118); ignored.");
                break;
        }

        return ToUnicodeMap.Create(file, parent);
    }

    private static ToUnicodeMap? FindUsedToUnicode(string name, CMapContext context)
    {
        if (name is "Identity-H" or "Identity-V")
        {
            return ToUnicodeMap.Identity;
        }

        context.Report(
            DiagnosticCodes.CMapUseCMapInvalid,
            DiagnosticSeverity.Warning,
            $"A ToUnicode CMap may use only another ToUnicode CMap stream (ISO 32000-2 §9.10.3), not /{name}; read without it.");
        return null;
    }

    private ToUnicodeMap? LoadCidToUnicode(string name)
    {
        if (!FontResolvers.TryResolveResource(FontResourceKind.CidToUnicode, name, out ReadOnlyMemory<byte> data))
        {
            return null;
        }

        // The table comes from the machine, not the file: its deviations are summarized as information, never thrown.
        var context = new CMapContext();
        ToUnicodeMap map = ToUnicodeMap.Parse(data.Span, context);
        if (context.Diagnostics.Count > 0)
        {
            _diagnostics.Report(
                DiagnosticCodes.CidToUnicodeResourceInvalid,
                DiagnosticSeverity.Information,
                $"The font resolver's {name} table deviates from the CMap file format ({context.Diagnostics[0].Message}); what could be read is used.");
        }

        return map;
    }
}
