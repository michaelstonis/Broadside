using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Filters.Jpx;

/// <summary>Where one output channel's samples come from: a codestream component, directly or through a palette column.</summary>
/// <param name="Component">The codestream component.</param>
/// <param name="Column">The palette column, or -1 for direct use.</param>
/// <param name="Depth">The channel's precision after reduction to at most 16 bits.</param>
internal readonly record struct JpxChannelSource(int Component, int Column, int Depth);

/// <summary>The colour transform the codec applies to the colour channels before handing them to PDF.</summary>
internal enum JpxColorConversion
{
    /// <summary>None: the samples are in the output colour space already.</summary>
    None,

    /// <summary>YCbCr (sYCC, e-sYCC, the YCbCr spaces) to RGB.</summary>
    Ycc,

    /// <summary>CIELab (with its range and offset parameters) to sRGB.</summary>
    Lab,

    /// <summary>CMY to RGB (inverted).</summary>
    Cmy,

    /// <summary>YCCK to CMYK: the YCC part to RGB, inverted, K kept.</summary>
    Ycck,
}

/// <summary>
/// Decides how the codestream's components become the image's channels: the JP2 component mapping and palette, the channel
/// definitions, the colour specification, the opacity channel, and the PDF rule that the image dictionary's ColorSpace wins.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.9 and Table 87; ITU-T T.800 I.5.3.3 to I.5.3.6. With an <c>Indexed</c> ColorSpace the first component is the
/// index, raw (the palette boxes are ignored). With another ColorSpace of N components, the first N colour channels (in channel
/// definition order) are the channels; the JP2 colour specification is ignored. Without one, the colour specification of highest
/// precedence and best approximation that is supported decides the colour model (sYCC, YCbCr and CIELab converted to RGB, CMY to
/// RGB, YCCK to CMYK, an ICC profile passed on), else the channel count does (1 Gray, 3 RGB, 4 CMYK). With <c>SMaskInData</c>, the
/// opacity channel (channel definition type 1 or 2, else the channel after the colours) becomes the alpha plane, premultiplied for
/// type 2 or <c>SMaskInData 2</c>.
/// </para>
/// </remarks>
internal sealed class JpxOutputPlan
{
    private static readonly CosName ColorSpaceKey = new("ColorSpace");
    private static readonly CosName SoftMaskInDataKey = new("SMaskInData");

    /// <summary>Gets the colour channels; a <see langword="null"/> entry is a channel the codestream lacks (zeros).</summary>
    public required JpxChannelSource?[] Colors { get; init; }

    /// <summary>Gets the opacity channel, or <see langword="null"/>.</summary>
    public JpxChannelSource? Alpha { get; init; }

    public bool Premultiplied { get; init; }

    /// <summary>Gets the bits per component of the colour channels (at most 16).</summary>
    public int Bits { get; init; }

    public ImageColorModel Model { get; init; }

    public byte[]? IccProfile { get; init; }

    public JpxColorConversion Conversion { get; init; }

    /// <summary>Gets the CIELab parameters (RL, OL, RA, OA, RB, OB, IL), or empty for the defaults.</summary>
    public uint[] LabParameters { get; init; } = [];

    public JpxPalette? Palette { get; init; }

    /// <summary>Gets which codestream components the output reads.</summary>
    public required bool[] UsedComponents { get; init; }

    /// <summary>Plans the output of a codestream of <paramref name="size"/> with the JP2 boxes <paramref name="file"/>.</summary>
    public static JpxOutputPlan Create(JpxImageSize size, JpxFileHeader file, ImageFilterContext context, JpxReporter reporter)
    {
        JpxComponentInfo[] components = size.Components;
        if (context.IsMask || IsIndexed(context))
        {
            return Finish([Direct(components, 0)], null, false, ImageColorModel.Unknown, null, JpxColorConversion.None, [], null, components.Length);
        }

        List<JpxChannelSource> channels = Channels(components, file, reporter);
        int smaskInData = SoftMaskInData(context);
        bool wantsAlpha = context.WantsAlpha || smaskInData != 0;
        List<int> colorOrder;
        int alphaIndex = -1;
        bool premultiplied = smaskInData == 2;
        if (file.Channels is { } definitions)
        {
            (colorOrder, alphaIndex, bool typePremultiplied) = Classify(definitions, channels.Count, reporter);
            premultiplied |= typePremultiplied;
        }
        else
        {
            colorOrder = [.. Enumerable.Range(0, channels.Count)];
        }

        int requested = context.ColorComponents;
        if (requested > 0)
        {
            if (file.Channels is null && wantsAlpha && channels.Count > requested)
            {
                alphaIndex = requested;
            }

            var colors = new JpxChannelSource?[requested];
            for (int i = 0; i < requested; i++)
            {
                colors[i] = i < colorOrder.Count ? channels[colorOrder[i]] : null;
            }

            if (colorOrder.Count < requested)
            {
                reporter.Report(
                    DiagnosticCodes.JpxChannelCountMismatch,
                    DiagnosticSeverity.Warning,
                    $"The JPEG 2000 data has {colorOrder.Count} colour channels where the colour space needs {requested}; the missing ones are zero.");
            }

            JpxChannelSource? alpha = wantsAlpha && alphaIndex >= 0 ? channels[alphaIndex] : null;
            return Finish(colors, alpha, premultiplied, ImageColorModel.Unknown, null, JpxColorConversion.None, [], file.Palette, components.Length);
        }

        // No ColorSpace: the colour specification (ISO 32000-2 §7.4.9), else the channel count.
        JpxColorSpecification? chosen = Choose(file.Colors, file.Channels is null ? 0 : colorOrder.Count, out bool skipped);
        int count = colorOrder.Count;
        if (file.Channels is null)
        {
            int implied = chosen is null ? 0 : ComponentCount(chosen);
            if (implied > 0 && implied <= count)
            {
                count = implied;
            }
            else if (implied == 0 && wantsAlpha && count is 2 or 4)
            {
                count--;
            }

            if (wantsAlpha && channels.Count > count)
            {
                alphaIndex = count;
            }
        }

        JpxChannelSource?[] chosenColors = [.. colorOrder.Take(count).Select(i => (JpxChannelSource?)channels[i])];
        (ImageColorModel model, JpxColorConversion conversion, byte[]? profile) = Interpret(chosen, chosenColors.Length, reporter);
        if (skipped)
        {
            reporter.Report(
                DiagnosticCodes.JpxColorSpecificationUnsupported,
                DiagnosticSeverity.Information,
                "A JPEG 2000 colour specification is not supported; the next one, or the channel count, decides the colour space (ISO 32000-2 §7.4.9).");
        }

        if (chosen is null && chosenColors.Length == 3 && file.Palette is null
            && (components[chosenColors[1]!.Value.Component].Dx > 1 || components[chosenColors[1]!.Value.Component].Dy > 1))
        {
            // As pdf.js: three components with sub-sampled chroma and no colour specification are YCbCr.
            conversion = JpxColorConversion.Ycc;
            reporter.Report(
                DiagnosticCodes.JpxColorSpecificationUnsupported,
                DiagnosticSeverity.Information,
                "A JPEG 2000 image has no colour specification and sub-sampled second and third components; it is read as YCbCr.");
        }

        JpxChannelSource? alphaSource = wantsAlpha && alphaIndex >= 0 ? channels[alphaIndex] : null;
        return Finish(chosenColors, alphaSource, premultiplied, model, profile, conversion, chosen?.Parameters ?? [], file.Palette, components.Length);
    }

    private static JpxOutputPlan Finish(
        JpxChannelSource?[] colors,
        JpxChannelSource? alpha,
        bool premultiplied,
        ImageColorModel model,
        byte[]? profile,
        JpxColorConversion conversion,
        uint[] lab,
        JpxPalette? palette,
        int componentCount)
    {
        if (colors.Length == 0)
        {
            colors = [null];
        }
        else if (colors.Length > ImageGeometry.MaxComponents)
        {
            // §8.9.3 images have at most 32 components (the DeviceN limit); the rest are not output.
            colors = colors[..ImageGeometry.MaxComponents];
        }

        int bits = 1;
        bool[] used = new bool[componentCount];
        foreach (JpxChannelSource? color in colors)
        {
            if (color is { } source)
            {
                bits = Math.Max(bits, source.Depth);
                used[source.Component] = true;
            }
        }

        if (alpha is { } opacity)
        {
            used[opacity.Component] = true;
        }

        if (conversion == JpxColorConversion.Lab)
        {
            bits = Math.Max(bits, 8);
        }

        return new JpxOutputPlan
        {
            Colors = colors,
            Alpha = alpha,
            Premultiplied = alpha is not null && premultiplied,
            Bits = bits,
            Model = model,
            IccProfile = profile,
            Conversion = conversion,
            LabParameters = lab,
            Palette = palette,
            UsedComponents = used,
        };
    }

    private static JpxChannelSource Direct(JpxComponentInfo[] components, int c) => new(c, -1, Math.Min(components[c].Depth, 16));

    /// <summary>The JP2 channels: through the component mapping box (and palette), else one per component (I.5.3.4, I.5.3.5).</summary>
    private static List<JpxChannelSource> Channels(JpxComponentInfo[] components, JpxFileHeader file, JpxReporter reporter)
    {
        var channels = new List<JpxChannelSource>();
        if (file.Palette is { } palette && file.Mapping is { } mapping)
        {
            foreach (JpxComponentMapping entry in mapping)
            {
                if (entry.Component >= components.Length || (entry.Type == 1 && entry.Column >= palette.Columns))
                {
                    reporter.Report(DiagnosticCodes.JpxBoxInvalid, DiagnosticSeverity.Warning, "A JPEG 2000 component mapping names a component or palette column that does not exist; that channel is dropped.");
                    continue;
                }

                channels.Add(entry.Type == 1
                    ? new JpxChannelSource(entry.Component, entry.Column, Math.Min(palette.Depths[entry.Column], 16))
                    : Direct(components, entry.Component));
            }

            if (channels.Count > 0)
            {
                return channels;
            }
        }
        else if (file.Palette is not null || file.Mapping is not null)
        {
            reporter.Report(DiagnosticCodes.JpxBoxInvalid, DiagnosticSeverity.Warning, "A JPEG 2000 palette needs a component mapping box and the reverse (I.5.3.4); the components are used directly.");
        }

        for (int c = 0; c < components.Length; c++)
        {
            channels.Add(Direct(components, c));
        }

        return channels;
    }

    /// <summary>The colour channels in association order and the opacity channel of a channel definition box (I.5.3.6).</summary>
    private static (List<int> Colors, int Alpha, bool Premultiplied) Classify(JpxChannelDefinition[] definitions, int channelCount, JpxReporter reporter)
    {
        var colors = new List<(int Association, int Order, int Channel)>();
        int alpha = -1;
        bool premultiplied = false;
        bool ambiguous = false;
        for (int i = 0; i < definitions.Length; i++)
        {
            JpxChannelDefinition definition = definitions[i];
            if (definition.Channel >= channelCount)
            {
                continue;
            }

            switch (definition.Type)
            {
                case 0:
                    colors.Add((definition.Association is 0 or 65535 ? int.MaxValue : definition.Association, i, definition.Channel));
                    break;
                case 1 or 2:
                    if (alpha >= 0 || definition.Association != 0)
                    {
                        ambiguous = true;
                    }

                    if (alpha < 0)
                    {
                        alpha = definition.Channel;
                        premultiplied = definition.Type == 2;
                    }

                    break;
            }
        }

        if (ambiguous)
        {
            reporter.Report(
                DiagnosticCodes.JpxOpacityChannelAmbiguous,
                DiagnosticSeverity.Warning,
                "A JPEG 2000 image has more than one opacity channel, or one for a single colour; PDF uses one opacity channel for all colours, the first (ISO 32000-2 §7.4.9).");
        }

        return ([.. colors.OrderBy(c => c.Association).ThenBy(c => c.Order).Select(c => c.Channel)], alpha, premultiplied);
    }

    /// <summary>The supported colour specification of highest precedence, then best approximation (ISO 32000-2 §7.4.9).</summary>
    private static JpxColorSpecification? Choose(List<JpxColorSpecification> specifications, int colorChannels, out bool skipped)
    {
        skipped = false;
        foreach (JpxColorSpecification specification in specifications
            .Select((s, i) => (s, i))
            .OrderByDescending(p => p.s.Precedence)
            .ThenBy(p => p.s.Approximation == 0 ? 5 : p.s.Approximation)
            .ThenBy(p => p.i)
            .Select(p => p.s))
        {
            int count = ComponentCount(specification);
            if (count > 0 && (colorChannels == 0 || count == colorChannels))
            {
                return specification;
            }

            skipped = true;
        }

        return null;
    }

    /// <summary>The number of colour components a supported specification describes, or 0 when it is not supported.</summary>
    private static int ComponentCount(JpxColorSpecification specification) => specification.Method switch
    {
        1 => specification.Enumerated switch
        {
            17 => 1,
            16 or 18 or 14 or 20 or 21 or 24 or 1 or 3 or 4 or 11 => 3,
            12 or 13 => 4,
            _ => 0,
        },
        2 or 3 when specification.Profile is { } profile && IccProfileHeader.Parse(profile) is { IsSupportedForPdf: true, ComponentCount: 1 or 3 or 4 } header => header.ComponentCount,
        _ => 0,
    };

    private static (ImageColorModel Model, JpxColorConversion Conversion, byte[]? Profile) Interpret(JpxColorSpecification? specification, int count, JpxReporter reporter)
    {
        ImageColorModel byCount = count switch
        {
            1 => ImageColorModel.Gray,
            3 => ImageColorModel.Rgb,
            4 => ImageColorModel.Cmyk,
            _ => ImageColorModel.Unknown,
        };

        if (specification is null)
        {
            return (byCount, JpxColorConversion.None, null);
        }

        if (specification.Method != 1)
        {
            return (byCount, JpxColorConversion.None, specification.Profile);
        }

        switch (specification.Enumerated)
        {
            case 17:
                return (ImageColorModel.Gray, JpxColorConversion.None, null);
            case 16:
                return (ImageColorModel.Rgb, JpxColorConversion.None, null);
            case 18 or 24 or 1 or 3 or 4:
                return (ImageColorModel.Rgb, JpxColorConversion.Ycc, null);
            case 14:
                return (ImageColorModel.Rgb, JpxColorConversion.Lab, null);
            case 11:
                return (ImageColorModel.Rgb, JpxColorConversion.Cmy, null);
            case 12:
                return (ImageColorModel.Cmyk, JpxColorConversion.None, null);
            case 13:
                return (ImageColorModel.Cmyk, JpxColorConversion.Ycck, null);
            default:
                reporter.Report(
                    DiagnosticCodes.JpxColorSpecificationUnsupported,
                    DiagnosticSeverity.Information,
                    $"The JPEG 2000 enumerated colour space {specification.Enumerated} is read as RGB, its closest approximation (ISO 32000-2 §7.4.9).");
                return (ImageColorModel.Rgb, JpxColorConversion.None, null);
        }
    }

    /// <summary>Whether the image dictionary's ColorSpace is Indexed (§7.4.9: the codestream then holds the indices).</summary>
    private static bool IsIndexed(ImageFilterContext context)
    {
        if (context.Filter.StreamDictionary is not { } dictionary || !dictionary.TryGetValue(ColorSpaceKey, out CosObject? value))
        {
            return false;
        }

        return context.Filter.Resolve(value) is CosArray { Count: > 0 } array && context.Filter.Resolve(array[0]) is CosName { Value: "Indexed" or "I" };
    }

    private static int SoftMaskInData(ImageFilterContext context) =>
        context.Filter.StreamDictionary is { } dictionary && dictionary.TryGetValue(SoftMaskInDataKey, out CosObject? value)
            && context.Filter.Resolve(value) is CosInteger { Value: 1 or 2 } number
            ? (int)number.Value
            : 0;
}
