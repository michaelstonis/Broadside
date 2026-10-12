using System.Collections.Concurrent;
using Broadside.Caching;
using Broadside.Diagnostics;
using Broadside.Graphics.Functions;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics.Colors;

/// <summary>Why a colour space value could not be read.</summary>
internal enum ColorSpaceFailure
{
    /// <summary>Nothing went wrong.</summary>
    None,

    /// <summary>The value is not a colour space name or array.</summary>
    Invalid,

    /// <summary>The value refers back to a space that is being read.</summary>
    Cycle,

    /// <summary>Spaces nest more deeply than <see cref="ColorSpaceCache.MaxDepth"/>.</summary>
    TooDeep,
}

/// <summary>How a colour space name in a content stream was resolved.</summary>
internal enum NamedLookup
{
    /// <summary>A family name: DeviceGray, DeviceRGB, DeviceCMYK or Pattern.</summary>
    Family,

    /// <summary>A key of the ColorSpace subdictionary of the current resources.</summary>
    Resource,

    /// <summary>Not a resource, but an inline image abbreviation (G, RGB, CMYK), read as the device space.</summary>
    Abbreviation,

    /// <summary>Not found.</summary>
    Missing,
}

/// <summary>
/// A document's colour spaces: one view per colour space object, the default colour spaces per resource dictionary, and the
/// converters built from them, all created once and shared by every thread.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6. Spaces are keyed by the indirect reference they were reached through, else by the direct array, and by the
/// array's version, so a space whose array changes is read again (ADR 0004). Built through <see cref="OnceCache{TKey, TValue}"/>:
/// concurrent first reads create one view, and a space that contains itself (an Indexed base referring to the Indexed space) is
/// detected as a cycle. Names (<c>/CS0</c>) are never keys: the same name means different spaces in different resources.
/// </para>
/// <para>
/// Converters are keyed by source space, conversion and default spaces, and rebuilt when any container they were built from has
/// changed.
/// </para>
/// </remarks>
internal sealed class ColorSpaceCache
{
    /// <summary>How deeply colour spaces may nest (an Indexed base that is a Separation whose alternate is ICCBased is 3).</summary>
    public const int MaxDepth = 8;

    [ThreadStatic]
    private static int _depth;

    private readonly OnceCache<Key, PdfColorSpace?> _spaces = new();
    private readonly OnceCache<Key, PdfDefaultColorSpaces> _defaults = new();
    private readonly ConcurrentDictionary<ConverterKey, PdfColorConverter> _converters = new();

    /// <summary>Initializes a new instance of the <see cref="ColorSpaceCache"/> class.</summary>
    /// <param name="document">The document.</param>
    /// <param name="diagnostics">The document's diagnostics.</param>
    /// <param name="management">The colour management converters come from.</param>
    public ColorSpaceCache(PdfDocument document, DiagnosticSink diagnostics, IColorManagement management)
    {
        Document = document;
        Diagnostics = diagnostics;
        Management = management;
    }

    /// <summary>Gets the document.</summary>
    public PdfDocument Document { get; }

    /// <summary>Gets the document's diagnostics.</summary>
    public DiagnosticSink Diagnostics { get; }

    /// <summary>Gets the colour management of the document's engine.</summary>
    public IColorManagement Management { get; }

    /// <summary>Resolves an object through the document.</summary>
    /// <param name="value">The object.</param>
    /// <returns>The direct object; the null object for <see langword="null"/>.</returns>
    public CosObject Resolve(CosObject? value) => Document.Resolve(value);

    /// <summary>Returns the colour space a value describes; DeviceGray, with a diagnostic, when it describes none.</summary>
    /// <param name="value">A family name, a colour space array, or a reference to one.</param>
    /// <param name="owner">The nearest indirect object the value was found in, for diagnostics.</param>
    /// <returns>The space.</returns>
    /// <remarks>ISO 32000-2 §8.6.3.</remarks>
    public PdfColorSpace Get(CosObject? value, CosReference? owner = null)
    {
        PdfColorSpace? space = Find(value, owner, inline: false, out ColorSpaceFailure failure);
        if (space is null)
        {
            ReportFailure(value as CosReference ?? owner, failure, "a colour space");
        }

        return space ?? PdfDeviceGrayColorSpace.Instance;
    }

    /// <summary>Returns the colour space of an inline image's <c>ColorSpace</c> entry (§8.9.7): abbreviations allowed, nothing cached.</summary>
    /// <param name="value">The entry.</param>
    /// <param name="owner">The content stream, for diagnostics.</param>
    /// <returns>The space; <see langword="null"/> when the value describes none.</returns>
    public PdfColorSpace? FindInline(CosObject value, CosReference? owner) => Find(value, owner, inline: true, out _);

    /// <summary>Returns the colour space a value describes, without recording anything when it describes none.</summary>
    /// <param name="value">A family name, a colour space array, or a reference to one.</param>
    /// <param name="owner">The nearest indirect object the value was found in, for diagnostics recorded while reading it.</param>
    /// <param name="inline">Whether inline image abbreviations (<c>G</c>, <c>RGB</c>, <c>CMYK</c>, <c>I</c>) are allowed.</param>
    /// <param name="failure">Why there is no space.</param>
    /// <returns>The space; <see langword="null"/> for a value that is not a colour space, a cycle, or nesting too deep.</returns>
    public PdfColorSpace? Find(CosObject? value, CosReference? owner, bool inline, out ColorSpaceFailure failure)
    {
        failure = ColorSpaceFailure.None;
        var reference = value as CosReference;
        switch (Resolve(value))
        {
            case CosName name:
                PdfColorSpace? named = FromName(name, inline);
                failure = named is null ? ColorSpaceFailure.Invalid : ColorSpaceFailure.None;
                return named;
            case CosArray array:
                if (_depth >= MaxDepth)
                {
                    failure = ColorSpaceFailure.TooDeep;
                    return null;
                }

                if (inline)
                {
                    return Create(array, reference, owner, inline: true);
                }

                var key = new Key((object?)reference ?? array, array.Version);
                PdfColorSpace? space = _spaces.GetOrCreate(
                    key,
                    (Cache: this, Array: array, Reference: reference, Owner: owner),
                    static (_, state) => new Created<PdfColorSpace?>(state.Cache.Create(state.Array, state.Reference, state.Owner, inline: false)),
                    static (_, _) => null);
                failure = space is null ? ColorSpaceFailure.Cycle : ColorSpaceFailure.None;
                return space;
            default:
                failure = ColorSpaceFailure.Invalid;
                return null;
        }
    }

    /// <summary>Looks a name up in a subdictionary of a resource dictionary without allocating.</summary>
    /// <param name="resources">The resource dictionary, or <see langword="null"/>.</param>
    /// <param name="category">The subdictionary's key, such as <c>ColorSpace</c> or <c>Pattern</c>.</param>
    /// <param name="name">The name's bytes.</param>
    /// <param name="key">The name as stored in the dictionary.</param>
    /// <returns>The value as stored (possibly a reference); <see langword="null"/> when absent.</returns>
    /// <remarks>ISO 32000-2 §7.8.3, Table 34.</remarks>
    public CosObject? FindResource(CosDictionary? resources, CosName category, ReadOnlySpan<byte> name, out CosName? key)
    {
        key = null;
        if (resources is null || !resources.TryGetValue(category, out CosObject? entry) || Resolve(entry) is not CosDictionary dictionary)
        {
            return null;
        }

        for (int i = 0; i < dictionary.Count; i++)
        {
            KeyValuePair<CosName, CosObject> pair = dictionary.GetAt(i);
            if (pair.Key.Bytes.SequenceEqual(name))
            {
                key = pair.Key;
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>Returns the colour space a name operand of <c>CS</c> or <c>cs</c> names, without allocating once the space is cached.</summary>
    /// <param name="resources">The current resource dictionary, or <see langword="null"/>.</param>
    /// <param name="name">The name's bytes.</param>
    /// <param name="owner">The content stream, for diagnostics recorded while reading the space.</param>
    /// <param name="lookup">How the name was resolved.</param>
    /// <returns>The space; <see langword="null"/> when the name is neither a family name nor a resource.</returns>
    /// <remarks>
    /// ISO 32000-2 §8.6.8, Table 73: DeviceGray, DeviceRGB, DeviceCMYK and Pattern always name those spaces and never resources; any
    /// other name is a key of the <c>ColorSpace</c> subdictionary of the current resources. The inline image abbreviations G, RGB and
    /// CMYK are accepted as a last resort (pdf.js does), after the resources.
    /// </remarks>
    public PdfColorSpace? FindNamed(CosDictionary? resources, ReadOnlySpan<byte> name, CosReference? owner, out NamedLookup lookup)
    {
        lookup = NamedLookup.Family;
        if (name.SequenceEqual("DeviceGray"u8))
        {
            return PdfDeviceGrayColorSpace.Instance;
        }

        if (name.SequenceEqual("DeviceRGB"u8))
        {
            return PdfDeviceRgbColorSpace.Instance;
        }

        if (name.SequenceEqual("DeviceCMYK"u8))
        {
            return PdfDeviceCmykColorSpace.Instance;
        }

        if (name.SequenceEqual("Pattern"u8))
        {
            return PdfPatternColorSpace.Colored;
        }

        if (FindResource(resources, ColorSpaceNames.ColorSpace, name, out _) is { } value)
        {
            lookup = NamedLookup.Resource;
            return Get(value, value as CosReference ?? owner);
        }

        lookup = NamedLookup.Abbreviation;
        if (name.SequenceEqual("G"u8))
        {
            return PdfDeviceGrayColorSpace.Instance;
        }

        if (name.SequenceEqual("RGB"u8))
        {
            return PdfDeviceRgbColorSpace.Instance;
        }

        if (name.SequenceEqual("CMYK"u8))
        {
            return PdfDeviceCmykColorSpace.Instance;
        }

        lookup = NamedLookup.Missing;
        return null;
    }

    /// <summary>Returns the default colour spaces of a resource dictionary.</summary>
    /// <param name="resources">The resource dictionary, or <see langword="null"/>.</param>
    /// <returns>The defaults; <see cref="PdfDefaultColorSpaces.None"/> when it has none.</returns>
    /// <remarks>ISO 32000-2 §8.6.5.6.</remarks>
    public PdfDefaultColorSpaces GetDefaults(CosDictionary? resources)
    {
        if (resources is null
            || !resources.TryGetValue(ColorSpaceNames.ColorSpace, out CosObject? entry)
            || Resolve(entry) is not CosDictionary spaces
            || !(spaces.ContainsKey(ColorSpaceNames.DefaultGray) || spaces.ContainsKey(ColorSpaceNames.DefaultRgb) || spaces.ContainsKey(ColorSpaceNames.DefaultCmyk)))
        {
            return PdfDefaultColorSpaces.None;
        }

        return _defaults.GetOrCreate(
            new Key(spaces, spaces.Version),
            (Cache: this, Spaces: spaces, Owner: entry as CosReference),
            static (_, state) => new Created<PdfDefaultColorSpaces>(state.Cache.CreateDefaults(state.Spaces, state.Owner)),
            static (_, _) => PdfDefaultColorSpaces.None);
    }

    /// <summary>Returns the converter from <paramref name="source"/> under <paramref name="conversion"/> with <paramref name="defaults"/>.</summary>
    /// <param name="source">The source space.</param>
    /// <param name="conversion">The conversion.</param>
    /// <param name="defaults">The default colour spaces in effect.</param>
    /// <returns>The converter, built once and rebuilt when what it was built from changes.</returns>
    public PdfColorConverter GetConverter(PdfColorSpace source, ColorConversion conversion, PdfDefaultColorSpaces defaults)
    {
        var key = new ConverterKey(source, conversion, defaults);
        if (_converters.TryGetValue(key, out PdfColorConverter? converter) && converter.IsCurrent)
        {
            return converter;
        }

        converter = PdfColorConverter.Create(this, source, conversion, defaults);
        _converters[key] = converter;
        return converter;
    }

    /// <summary>Records why a nested or top-level value is not a colour space.</summary>
    /// <param name="space">The space that holds the value.</param>
    /// <param name="failure">What went wrong.</param>
    /// <param name="what">What the value was meant to be, for the message.</param>
    public static void ReportFailure(PdfColorSpace space, ColorSpaceFailure failure, string what) =>
        space.Cache!.ReportFailure(space.DiagnosticReference, failure, what);

    /// <summary>Records why a value is not a colour space, against <paramref name="reference"/>.</summary>
    /// <param name="reference">The object to record it against.</param>
    /// <param name="failure">What went wrong.</param>
    /// <param name="what">What the value was meant to be, for the message.</param>
    public void ReportFailure(CosReference? reference, ColorSpaceFailure failure, string what)
    {
        (string code, DiagnosticSeverity severity, string message) = failure switch
        {
            ColorSpaceFailure.Cycle => (DiagnosticCodes.ColorSpaceCycle, DiagnosticSeverity.Error, $"The value of {what} refers back to the colour space being read; DeviceGray is used."),
            ColorSpaceFailure.TooDeep => (DiagnosticCodes.ColorSpaceTooDeep, DiagnosticSeverity.Error, $"Colour spaces nest more than {MaxDepth} deep at {what}; DeviceGray is used."),
            _ => (DiagnosticCodes.ColorSpaceInvalid, DiagnosticSeverity.Warning, $"The value of {what} is not a colour space name or array; DeviceGray is used."),
        };
        Diagnostics.Report(code, severity, message, offset: null, reference);
    }

    /// <summary>Returns the space a family name stands for, or <see langword="null"/> for a name that is not one.</summary>
    private static PdfColorSpace? FromName(CosName name, bool inline)
    {
        if (name.Equals(ColorSpaceNames.DeviceGray) || (inline && name.Equals(ColorSpaceNames.G)))
        {
            return PdfDeviceGrayColorSpace.Instance;
        }

        if (name.Equals(ColorSpaceNames.DeviceRgb) || (inline && name.Equals(ColorSpaceNames.Rgb)))
        {
            return PdfDeviceRgbColorSpace.Instance;
        }

        if (name.Equals(ColorSpaceNames.DeviceCmyk) || name.Equals(ColorSpaceNames.CalCmyk) || (inline && name.Equals(ColorSpaceNames.Cmyk)))
        {
            return PdfDeviceCmykColorSpace.Instance;
        }

        return name.Equals(ColorSpaceNames.Pattern) && !inline ? PdfPatternColorSpace.Colored : null;
    }

    /// <summary>Reads a colour space array into its view, checks it, and repairs it to DeviceGray when it cannot be used.</summary>
    private PdfColorSpace Create(CosArray array, CosReference? reference, CosReference? owner, bool inline)
    {
        _depth++;
        try
        {
            CosReference? diagnostics = reference ?? owner;
            if (array.Count == 0 || Resolve(array[0]) is not CosName family)
            {
                Diagnostics.Report(DiagnosticCodes.ColorSpaceInvalid, DiagnosticSeverity.Warning, "A colour space array is empty or does not start with a family name; DeviceGray is used.", offset: null, diagnostics);
                return PdfDeviceGrayColorSpace.Instance;
            }

            if (FromName(family, inline) is { } device && family.Equals(ColorSpaceNames.Pattern) is false)
            {
                // §8.6.3: a family with no parameters is named alone; [/DeviceRGB] is read anyway (CalCMYK, §8.6.5.1, silently).
                if (!family.Equals(ColorSpaceNames.CalCmyk))
                {
                    Diagnostics.Report(DiagnosticCodes.ColorSpaceEntryInvalid, DiagnosticSeverity.Warning, $"The device colour space {family.Value} is written as an array; it is read as the name.", offset: null, diagnostics);
                }

                return device;
            }

            PdfColorSpace? space = family switch
            {
                _ when family.Equals(ColorSpaceNames.CalGray) => new PdfCalGrayColorSpace(this, array, reference) { OwnerReference = owner, IsInline = inline },
                _ when family.Equals(ColorSpaceNames.CalRgb) => new PdfCalRgbColorSpace(this, array, reference) { OwnerReference = owner, IsInline = inline },
                _ when family.Equals(ColorSpaceNames.Lab) => new PdfLabColorSpace(this, array, reference) { OwnerReference = owner, IsInline = inline },
                _ when family.Equals(ColorSpaceNames.IccBased) && array.Count > 1 && Resolve(array[1]) is CosStream =>
                    new PdfIccBasedColorSpace(this, array, reference) { OwnerReference = owner, IsInline = inline },
                _ when family.Equals(ColorSpaceNames.Indexed) || (inline && family.Equals(ColorSpaceNames.I)) =>
                    new PdfIndexedColorSpace(this, array, reference) { OwnerReference = owner, IsInline = inline },
                _ when family.Equals(ColorSpaceNames.Separation) => new PdfSeparationColorSpace(this, array, reference) { OwnerReference = owner, IsInline = inline },
                _ when family.Equals(ColorSpaceNames.DeviceN) => new PdfDeviceNColorSpace(this, array, reference) { OwnerReference = owner, IsInline = inline },
                _ when family.Equals(ColorSpaceNames.Pattern) => array.Count == 1
                    ? PdfPatternColorSpace.Colored
                    : new PdfPatternColorSpace(this, array, reference) { OwnerReference = owner, IsInline = inline },
                _ => null,
            };

            if (space is null)
            {
                Diagnostics.Report(DiagnosticCodes.ColorSpaceInvalid, DiagnosticSeverity.Warning, $"The colour space family {family.Value} is unknown, or its parameters are missing; DeviceGray is used.", offset: null, diagnostics);
                return PdfDeviceGrayColorSpace.Instance;
            }

            return space.Cache is null || space.Validate() ? space : PdfDeviceGrayColorSpace.Instance;
        }
        finally
        {
            _depth--;
        }
    }

    private PdfDefaultColorSpaces CreateDefaults(CosDictionary spaces, CosReference? owner)
    {
        return new PdfDefaultColorSpaces(
            Default(ColorSpaceNames.DefaultGray, 1),
            Default(ColorSpaceNames.DefaultRgb, 3),
            Default(ColorSpaceNames.DefaultCmyk, 4));

        PdfColorSpace? Default(CosName key, int components)
        {
            if (!spaces.TryGetValue(key, out CosObject? value))
            {
                return null;
            }

            PdfColorSpace space = Get(value, owner);
            if (space.ComponentCount == components && space.Family is not (PdfColorSpaceFamily.Lab or PdfColorSpaceFamily.Indexed or PdfColorSpaceFamily.Pattern))
            {
                return space;
            }

            Diagnostics.Report(
                DiagnosticCodes.DefaultColorSpaceInvalid,
                DiagnosticSeverity.Warning,
                $"{key.Value} is a {space.Family} space of {space.ComponentCount} components; a default colour space shall have {components} and not be Lab, Indexed or Pattern. It is ignored.",
                offset: null,
                value as CosReference ?? owner);
            return null;
        }
    }

    /// <summary>A colour space object's identity (its reference, else the direct container) and the container's version.</summary>
    private readonly record struct Key(object Identity, int Version);

    /// <summary>What a converter is built from.</summary>
    private readonly record struct ConverterKey(PdfColorSpace Source, ColorConversion Conversion, PdfDefaultColorSpaces Defaults);
}
