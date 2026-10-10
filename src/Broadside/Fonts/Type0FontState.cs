using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>
/// The CMap and descendant CIDFont of a Type 0 font, resolved once from its dictionary, with the COS objects they came from and
/// their versions so that a change is noticed.
/// </summary>
/// <remarks>ISO 32000-2 §9.7.6.1, Table 119. Immutable once built.</remarks>
internal sealed class Type0FontState
{
    private readonly CosObject[] _sources;
    private readonly int[] _versions;

    private Type0FontState(CMap encoding, PdfCidFont? descendant, List<CosObject> sources)
    {
        Encoding = encoding;
        Descendant = descendant;
        _sources = [.. sources];
        _versions = [.. sources.Select(VersionOf)];
    }

    /// <summary>Gets the CMap the font reads codes with.</summary>
    public CMap Encoding { get; }

    /// <summary>Gets the descendant CIDFont, or <see langword="null"/> when the font has none.</summary>
    public PdfCidFont? Descendant { get; }

    /// <summary>Gets a value indicating whether no source has changed since the state was built.</summary>
    public bool IsCurrent
    {
        get
        {
            for (int index = 0; index < _sources.Length; index++)
            {
                if (VersionOf(_sources[index]) != _versions[index])
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Resolves the encoding and descendant of a Type 0 font, recording deviations on it.</summary>
    /// <param name="font">The font.</param>
    /// <param name="previous">The previous state, whose descendant view is kept when its dictionary is unchanged.</param>
    /// <returns>The state.</returns>
    public static Type0FontState Build(PdfType0Font font, Type0FontState? previous)
    {
        var sources = new List<CosObject> { font.Dictionary };
        PdfCidFont? descendant = ReadDescendant(font, previous, sources);
        CMap encoding = ReadEncoding(font, sources, out bool standIn);
        if (descendant is not null)
        {
            Check(font, encoding, standIn, descendant);
        }

        return new Type0FontState(encoding, descendant, sources);
    }

    private static int VersionOf(CosObject source) => source switch
    {
        CosDictionary dictionary => dictionary.Version,
        CosArray array => array.Version,
        CosStream stream => stream.Version,
        _ => 0,
    };

    /// <summary><c>Encoding</c> (Table 119): a predefined CMap name or an embedded CMap stream; Identity-H when unusable (<paramref name="standIn"/>).</summary>
    private static CMap ReadEncoding(PdfType0Font font, List<CosObject> sources, out bool standIn)
    {
        standIn = false;
        CosObject? value = font.Dictionary.TryGetValue(FontNames.Encoding, out CosObject? raw) ? font.Document.Resolve(raw) : null;
        switch (value)
        {
            case CosName name:
                if (font.Document.FindPredefinedCMap(name.Value) is { } predefined)
                {
                    return predefined;
                }

                CMap? fallback = PredefinedCMapTable.CreateFallback(name.Value);
                font.Report(DiagnosticCodes.CMapUnavailable, DiagnosticSeverity.Information, PdfDocument.UnavailableMessage(name.Value, fallback is not null));
                standIn = fallback is null;
                return fallback ?? CMap.IdentityH;
            case CosStream stream:
                sources.Add(stream);
                if (font.Document.GetCMap(stream, raw as CosReference) is { } cmap)
                {
                    return cmap;
                }

                break;
            default:
                font.Report(
                    DiagnosticCodes.Type0EncodingMissing,
                    DiagnosticSeverity.Warning,
                    "A Type 0 font's Encoding shall be a CMap name or stream (ISO 32000-2 §9.7.6.1, Table 119); codes are read as Identity-H.");
                break;
        }

        standIn = true;
        return CMap.IdentityH;
    }

    /// <summary><c>DescendantFonts</c> (Table 119): a one-element array of a CIDFont dictionary.</summary>
    private static PdfCidFont? ReadDescendant(PdfType0Font font, Type0FontState? previous, List<CosObject> sources)
    {
        CosObject? value = font.Get(CompositeFontNames.DescendantFonts);
        CosObject? element;
        switch (value)
        {
            case CosArray array when array.Count >= 1:
                sources.Add(array);
                if (array.Count > 1)
                {
                    font.Report(
                        DiagnosticCodes.Type0DescendantFontsInvalid,
                        DiagnosticSeverity.Warning,
                        "DescendantFonts shall be a one-element array (ISO 32000-2 §9.7.6.1, Table 119); the first element is used.");
                }

                element = array[0];
                break;
            case CosDictionary:
                font.Report(
                    DiagnosticCodes.Type0DescendantFontsInvalid,
                    DiagnosticSeverity.Warning,
                    "DescendantFonts shall be an array, not a dictionary (ISO 32000-2 §9.7.6.1, Table 119); the dictionary is used.");
                element = font.Dictionary[CompositeFontNames.DescendantFonts];
                break;
            default:
                font.Report(
                    DiagnosticCodes.Type0DescendantFontsInvalid,
                    DiagnosticSeverity.Warning,
                    "A Type 0 font shall have a DescendantFonts array with one CIDFont (ISO 32000-2 §9.7.6.1, Table 119); its glyphs cannot be shown.");
                return null;
        }

        if (font.Document.Resolve(element) is not CosDictionary dictionary)
        {
            font.Report(
                DiagnosticCodes.Type0DescendantFontsInvalid,
                DiagnosticSeverity.Warning,
                "The descendant of a Type 0 font shall be a CIDFont dictionary (ISO 32000-2 §9.7.6.1, Table 119); its glyphs cannot be shown.");
            return null;
        }

        sources.Add(dictionary);
        CosName? subtype = font.GetFrom(dictionary, FontNames.Subtype) as CosName;
        PdfCidFontType type = CompositeFontNames.CidFontType0.Equals(subtype) ? PdfCidFontType.CidFontType0 : PdfCidFontType.CidFontType2;
        if (previous?.Descendant is { } kept && ReferenceEquals(kept.Dictionary, dictionary) && kept.CidFontType == type)
        {
            return kept;
        }

        var reference = element as CosReference;
        if (!CompositeFontNames.CidFontType0.Equals(subtype) && !CompositeFontNames.CidFontType2.Equals(subtype))
        {
            font.Document.DiagnosticSink.Report(
                DiagnosticCodes.CidFontSubtypeInvalid,
                DiagnosticSeverity.Warning,
                $"A CIDFont's Subtype shall be CIDFontType0 or CIDFontType2, not {(subtype is null ? "absent" : "/" + subtype.Value)} (ISO 32000-2 §9.7.4.1, Table 115); read as CIDFontType2.",
                objectReference: reference ?? font.Reference);
        }

        return type == PdfCidFontType.CidFontType0
            ? new PdfCidFontType0(font, dictionary, reference)
            : new PdfCidFontType2(font, dictionary, reference);
    }

    /// <summary>
    /// The rules that relate the CMap and the CIDFont (§9.7.3, §9.7.4.1, §9.7.5.2 and the text after Table 117). The Identity rule
    /// applies to an encoding written as Identity-H or Identity-V, not to Identity-H standing in for an unusable encoding.
    /// </summary>
    private static void Check(PdfType0Font font, CMap encoding, bool standIn, PdfCidFont descendant)
    {
        CosObject? systemInfo = descendant.Get(CompositeFontNames.CidSystemInfo);
        CidSystemInfo? fontInfo = PdfCidFont.ReadSystemInfo(font.Document, systemInfo);
        if (fontInfo is null)
        {
            descendant.Report(
                DiagnosticCodes.CidSystemInfoInvalid,
                DiagnosticSeverity.Warning,
                "A CIDFont shall have a CIDSystemInfo dictionary with Registry, Ordering and Supplement (ISO 32000-2 §9.7.3, Table 114, and §9.7.4.1, Table 115).");
        }
        else if (!IsIdentityChain(encoding) && encoding.SystemInfo is { } cmapInfo && !cmapInfo.IsCompatibleWith(fontInfo))
        {
            font.Report(
                DiagnosticCodes.CidSystemInfoMismatch,
                DiagnosticSeverity.Warning,
                $"The CMap's character collection {cmapInfo.Registry}-{cmapInfo.Ordering} differs from the CIDFont's {fontInfo.Registry}-{fontInfo.Ordering} (ISO 32000-2 §9.7.3); the CIDs are used as they are.");
        }

        if (encoding.IsIdentity && !standIn && descendant.CidFontType == PdfCidFontType.CidFontType2 && !descendant.IsEmbedded)
        {
            font.Report(
                DiagnosticCodes.Type0IdentityNotEmbedded,
                DiagnosticSeverity.Warning,
                "Identity-H and Identity-V shall not be used with a TrueType CIDFont that is not embedded (ISO 32000-2 §9.7.5.2, Table 117, and §9.7.6.1).");
        }
    }

    private static bool IsIdentityChain(CMap encoding)
    {
        for (CMap? cmap = encoding; cmap is not null; cmap = cmap.Parent)
        {
            if (cmap.IsIdentity)
            {
                return true;
            }
        }

        return false;
    }
}
