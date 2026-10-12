using Broadside.Graphics.Colors;
using Broadside.Graphics.Functions;

namespace Broadside.Graphics;

/// <summary>
/// Converts colours of any colour space to a device colour model: the special spaces expanded, default colour spaces applied, and the
/// rest through the engine's colour management. Get one from <see cref="PdfDocument.GetColorConverter(PdfColorSpace, PdfDefaultColorSpaces?)"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6 and §10.3 to §10.4. An Indexed colour is looked up in its table, converted once; a Separation or DeviceN tint
/// goes through its tint transform to the alternate space (a display always uses the alternate), except the colourant <c>All</c>
/// (1 − tint on every additive component) and <c>None</c> (<see cref="PaintsNothing"/>); a Pattern colour converts through the
/// underlying space of an uncoloured pattern. Device spaces, including a nested Indexed base, Pattern underlying space and
/// Separation or DeviceN alternate, are replaced by the given defaults (§8.6.5.6). What remains, a device, CIE-based or ICCBased
/// space, goes to the <see cref="IColorManagement"/> the engine was configured with, every component clipped into its range first.
/// </para>
/// <para>
/// A converter is built once per document, source, conversion and defaults, and again when a COS object it was built from changes.
/// It is immutable, safe to use from several threads at once, and allocates nothing per call.
/// </para>
/// </remarks>
public sealed class PdfColorConverter
{
    private readonly ColorPipeline _pipeline;
    private readonly FunctionDependency[] _dependencies;

    private PdfColorConverter(PdfColorSpace source, ColorConversion conversion, ColorPipeline pipeline, FunctionDependency[] dependencies)
    {
        Source = source;
        Conversion = conversion;
        _pipeline = pipeline;
        _dependencies = dependencies;
    }

    /// <summary>Gets the colour space converted from, as selected (before default colour spaces apply).</summary>
    public PdfColorSpace Source { get; }

    /// <summary>Gets the conversion: target model, rendering intent and device controls.</summary>
    public ColorConversion Conversion { get; }

    /// <summary>Gets the number of components of a source colour.</summary>
    public int InputCount => _pipeline.InputCount;

    /// <summary>Gets the number of components of a device colour: 1, 3 or 4.</summary>
    public int OutputCount => _pipeline.OutputCount;

    /// <summary>Gets a value indicating whether colours of the source space paint nothing (Separation <c>None</c>, an all-None DeviceN).</summary>
    /// <remarks>ISO 32000-2 §8.6.6.4 and §8.6.6.5. Converted colours are then no ink: white, or CMYK 0 0 0 0.</remarks>
    public bool PaintsNothing => _pipeline.PaintsNothing;

    /// <summary>Gets a value indicating whether nothing the converter was built from has changed since.</summary>
    internal bool IsCurrent
    {
        get
        {
            foreach (FunctionDependency dependency in _dependencies)
            {
                if (!dependency.IsCurrent)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Converts one colour.</summary>
    /// <param name="color">The colour; its components beyond <see cref="InputCount"/> are ignored and missing ones read as 0.</param>
    /// <param name="destination">At least <see cref="OutputCount"/> values; receives the device colour, each 0 to 1.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    public void Convert(in PdfColor color, Span<float> destination) => Convert(color.Components, destination);

    /// <summary>Converts one colour given as its components.</summary>
    /// <param name="components">The components; values beyond <see cref="InputCount"/> are ignored and missing ones read as 0.</param>
    /// <param name="destination">At least <see cref="OutputCount"/> values; receives the device colour, each 0 to 1.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    public void Convert(ReadOnlySpan<float> components, Span<float> destination)
    {
        if (destination.Length < OutputCount)
        {
            throw new ArgumentException("There is less room than the device colour needs.", nameof(destination));
        }

        int inputs = InputCount;
        Span<float> input = stackalloc float[Math.Max(inputs, 1)];
        input.Clear();
        components[..Math.Min(components.Length, inputs)].CopyTo(input);
        _pipeline.Convert(input[..inputs], destination, 1);
    }

    /// <summary>Converts <paramref name="count"/> colours stored one after the other.</summary>
    /// <param name="source">At least <paramref name="count"/> × <see cref="InputCount"/> component values.</param>
    /// <param name="destination">At least <paramref name="count"/> × <see cref="OutputCount"/> values; receives the device colours.</param>
    /// <param name="count">The number of colours.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A span is too short for <paramref name="count"/> colours.</exception>
    public void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(source.Length, (long)count * InputCount, nameof(source));
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, (long)count * OutputCount, nameof(destination));
        _pipeline.Convert(source, destination, count);
    }

    /// <summary>Converts <paramref name="count"/> colours of 8-bit samples, as an image row holds them, to 8-bit device colours.</summary>
    /// <param name="source">
    /// At least <paramref name="count"/> × <see cref="InputCount"/> samples. Each maps linearly from 0..255 onto its component's
    /// range (the default <c>Decode</c> of Table 88), except in an Indexed space, where a sample is the index.
    /// </param>
    /// <param name="destination">At least <paramref name="count"/> × <see cref="OutputCount"/> bytes; receives the device colours, 0 to 255.</param>
    /// <param name="count">The number of colours.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A span is too short for <paramref name="count"/> colours.</exception>
    /// <remarks>ISO 32000-2 §8.9.5.2. One-component spaces convert through a table of 256 colours built once.</remarks>
    public void Convert(ReadOnlySpan<byte> source, Span<byte> destination, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(source.Length, (long)count * InputCount, nameof(source));
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, (long)count * OutputCount, nameof(destination));
        _pipeline.Convert(source, destination, count);
    }

    /// <summary>Builds the converter.</summary>
    internal static PdfColorConverter Create(ColorSpaceCache cache, PdfColorSpace source, ColorConversion conversion, PdfDefaultColorSpaces defaults)
    {
        var dependencies = new List<FunctionDependency>();
        ColorPipeline pipeline = Build(cache, source, conversion, defaults, dependencies, depth: 0);
        return new PdfColorConverter(source, conversion, pipeline, [.. dependencies]);
    }

    private static ColorPipeline Build(ColorSpaceCache cache, PdfColorSpace space, ColorConversion conversion, PdfDefaultColorSpaces defaults, List<FunctionDependency> dependencies, int depth)
    {
        int outputs = DeviceConverter.Outputs(conversion.Target);
        PdfColorSpace remapped = defaults.Remap(space);
        if (!ReferenceEquals(remapped, space))
        {
            // §8.6.5.6: a default is not itself remapped.
            space = remapped;
            defaults = PdfDefaultColorSpaces.None;
        }

        space.AddDependencies(dependencies);
        if (depth > 2 * ColorSpaceCache.MaxDepth)
        {
            return Build(cache, PdfDeviceGrayColorSpace.Instance, conversion, PdfDefaultColorSpaces.None, dependencies, 0);
        }

        switch (space)
        {
            case PdfIndexedColorSpace indexed:
                return new IndexedPipeline(indexed, Build(cache, indexed.Base, conversion, defaults, dependencies, depth + 1));
            case PdfSeparationColorSpace separation when separation.IsNone:
                return new NoColorantPipeline(1, conversion.Target, outputs);
            case PdfSeparationColorSpace separation when separation.IsAll:
                return new AllColorantsPipeline(conversion.Target, outputs);
            case PdfSeparationColorSpace separation:
                return Tint(1, separation.TintTransform, separation.Alternate);
            case PdfDeviceNColorSpace deviceN when deviceN.AreAllNone:
                return new NoColorantPipeline(deviceN.ComponentCount, conversion.Target, outputs);
            case PdfDeviceNColorSpace deviceN:
                return Tint(deviceN.ComponentCount, deviceN.TintTransform, deviceN.Alternate);
            case PdfPatternColorSpace { Underlying: { } underlying }:
                return Build(cache, underlying, conversion, defaults, dependencies, depth + 1);
            case PdfPatternColorSpace:
                return new PatternPipeline(conversion.Target, outputs);
            default:
                return new ManagedPipeline(space, cache.Management.CreateConverter(space, conversion), outputs);
        }

        ColorPipeline Tint(int inputs, PdfFunction? function, PdfColorSpace alternate)
        {
            if (function is not null)
            {
                dependencies.Add(new FunctionDependency(function.CosObject, FunctionDependency.VersionOf(function.CosObject)));
            }

            return new TintPipeline(inputs, function, alternate, Build(cache, alternate, conversion, defaults, dependencies, depth + 1));
        }
    }
}
