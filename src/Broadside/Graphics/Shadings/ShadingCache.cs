using System.Runtime.CompilerServices;
using Broadside.Caching;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics.Shadings;

/// <summary>
/// A document's shadings and patterns: one model per object, built once and shared by every caller and thread, built again only
/// after the object changes.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.7. Keyed by the resolved object's instance (the object cache gives one instance per indirect reference) and its
/// <c>Version</c>, through <see cref="OnceCache{TKey, TValue}"/>. Lookups that hit allocate nothing.
/// </remarks>
internal sealed class ShadingCache
{
    private readonly PdfDocument _document;
    private readonly OnceCache<Key, PdfShading?> _shadings = new();
    private readonly OnceCache<Key, PdfPattern?> _patterns = new();

    public ShadingCache(PdfDocument document) => _document = document;

    /// <summary>Returns the model of a shading dictionary or stream.</summary>
    /// <param name="value">The shading, or a reference to it.</param>
    /// <param name="owner">The nearest indirect object it was found in, for diagnostics when it is direct.</param>
    /// <returns>The model; <see langword="null"/>, with a diagnostic, when the object is not a shading of Type 1 to 7.</returns>
    public PdfShading? GetShading(CosObject? value, CosReference? owner)
    {
        CosObject resolved = _document.Resolve(value);
        CosDictionary? dictionary = DictionaryOf(resolved);
        var reference = value as CosReference;
        if (dictionary is null)
        {
            Report(DiagnosticCodes.ShadingTypeInvalid, "A shading is not a dictionary or a stream.", reference ?? owner);
            return null;
        }

        return _shadings.GetOrCreate(
            KeyOf(resolved, dictionary),
            (Cache: this, Resolved: resolved, Dictionary: dictionary, Reference: reference, Owner: owner),
            static (_, state) => new Created<PdfShading?>(state.Cache.CreateShading(state.Resolved, state.Dictionary, state.Reference, state.Owner)),
            static (_, _) => null);
    }

    /// <summary>Returns the model of a pattern dictionary or stream.</summary>
    /// <param name="value">The pattern, or a reference to it.</param>
    /// <param name="owner">The nearest indirect object it was found in, for diagnostics when it is direct.</param>
    /// <returns>The model; <see langword="null"/>, with a diagnostic, when the object is not a pattern of Type 1 or 2.</returns>
    public PdfPattern? GetPattern(CosObject? value, CosReference? owner)
    {
        CosObject resolved = _document.Resolve(value);
        CosDictionary? dictionary = DictionaryOf(resolved);
        var reference = value as CosReference;
        if (dictionary is null)
        {
            Report(DiagnosticCodes.PatternTypeInvalid, "A pattern is not a dictionary or a stream.", reference ?? owner);
            return null;
        }

        return _patterns.GetOrCreate(
            KeyOf(resolved, dictionary),
            (Cache: this, Resolved: resolved, Dictionary: dictionary, Reference: reference, Owner: owner),
            static (_, state) => new Created<PdfPattern?>(state.Cache.CreatePattern(state.Resolved, state.Dictionary, state.Reference, state.Owner)),
            static (_, _) => null);
    }

    private static CosDictionary? DictionaryOf(CosObject value) => value switch
    {
        CosStream stream => stream.Dictionary,
        CosDictionary dictionary => dictionary,
        _ => null,
    };

    private static Key KeyOf(CosObject resolved, CosDictionary dictionary) =>
        new(resolved, dictionary.Version, resolved is CosStream stream ? stream.Version : 0);

    private PdfShading? CreateShading(CosObject resolved, CosDictionary dictionary, CosReference? reference, CosReference? owner)
    {
        var reader = new ShadingReader(_document, resolved, dictionary, reference, owner);
        int? type = reader.Integer(ShadingNames.ShadingType);
        if (type is not (>= 1 and <= 7))
        {
            reader.Invalid(DiagnosticCodes.ShadingTypeInvalid, "A shading's ShadingType is missing or not 1 to 7.");
            return null;
        }

        var shadingType = (PdfShadingType)type.Value;
        return shadingType switch
        {
            PdfShadingType.FunctionBased => new PdfFunctionShading(reader),
            PdfShadingType.Axial => new PdfAxialShading(reader),
            PdfShadingType.Radial => new PdfRadialShading(reader),
            PdfShadingType.FreeFormTriangleMesh or PdfShadingType.LatticeFormTriangleMesh => new PdfTriangleMeshShading(reader, shadingType),
            _ => new PdfPatchMeshShading(reader, shadingType),
        };
    }

    private PdfPattern? CreatePattern(CosObject resolved, CosDictionary dictionary, CosReference? reference, CosReference? owner)
    {
        var reader = new ShadingReader(_document, resolved, dictionary, reference, owner);
        switch (reader.Integer(ShadingNames.PatternType))
        {
            case 1:
                return new PdfTilingPattern(reader);
            case 2:
                return new PdfShadingPattern(reader);
            default:
                reader.Invalid(DiagnosticCodes.PatternTypeInvalid, "A pattern's PatternType is missing or not 1 or 2.");
                return null;
        }
    }

    private void Report(string code, string message, CosReference? reference) =>
        _document.DiagnosticSink.Report(code, DiagnosticSeverity.Error, message, offset: null, reference);

    /// <summary>The object's identity and the versions of what the model was read from.</summary>
    private readonly record struct Key(CosObject Identity, int Version, int StreamVersion)
    {
        public bool Equals(Key other) =>
            ReferenceEquals(Identity, other.Identity) && Version == other.Version && StreamVersion == other.StreamVersion;

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Identity), Version, StreamVersion);
    }
}
