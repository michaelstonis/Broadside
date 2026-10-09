namespace Broadside.Graphics.Colors;

/// <summary>A device, CIE-based or ICCBased space, converted by the colour-management extension point after clipping into range.</summary>
/// <remarks>ISO 32000-2 §8.6.4, §8.6.5; §8.6.5.6: out-of-range values are clipped to the nearest valid value.</remarks>
internal sealed class ManagedPipeline : ColorPipeline
{
    private readonly IColorConverter _converter;
    private readonly ComponentRange[] _clip;

    public ManagedPipeline(PdfColorSpace space, IColorConverter converter, int outputs)
        : base(Ranges(space), outputs)
    {
        if (converter.InputCount != space.ComponentCount || converter.OutputCount != outputs)
        {
            throw new InvalidOperationException(
                $"The colour management converter for {space.Family} takes {converter.InputCount} and gives {converter.OutputCount} components; {space.ComponentCount} and {outputs} are needed.");
        }

        _converter = converter;
        _clip = Ranges(space);
    }

    /// <summary>The component ranges of a space.</summary>
    public static ComponentRange[] Ranges(PdfColorSpace space)
    {
        var ranges = new ComponentRange[space.ComponentCount];
        for (int i = 0; i < ranges.Length; i++)
        {
            ranges[i] = space.GetComponentRange(i);
        }

        return ranges;
    }

    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        int inputs = InputCount;
        Span<float> clipped = stackalloc float[Chunk * 4];
        for (int start = 0; start < count; start += Chunk)
        {
            int n = Math.Min(Chunk, count - start);
            ReadOnlySpan<float> batch = source.Slice(start * inputs, n * inputs);
            for (int i = 0; i < batch.Length; i++)
            {
                clipped[i] = (float)_clip[i % inputs].Clamp(batch[i]);
            }

            _converter.Convert(clipped[..batch.Length], destination[(start * OutputCount)..], n);
        }
    }
}

/// <summary>An Indexed space: the table of colours is converted once; indices pick from it.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.6.3: table byte b of component j is minj + b × (maxj − minj) / 255 in the base space; an index is rounded and
/// clipped to 0..hival. A short table reads as zeros past its end.
/// </remarks>
internal sealed class IndexedPipeline : ColorPipeline
{
    private readonly float[] _palette;
    private readonly byte[] _bytes;
    private readonly int _high;

    public IndexedPipeline(PdfIndexedColorSpace space, ColorPipeline basePipeline)
        : base([new ComponentRange(0, space.HighValue)], basePipeline.OutputCount)
    {
        _high = space.HighValue;
        int m = basePipeline.InputCount;
        int entries = _high + 1;
        ReadOnlySpan<byte> lookup = space.GetLookup().Span;
        PdfColorSpace baseSpace = space.Base;
        float[] components = new float[entries * m];
        for (int i = 0; i < components.Length; i++)
        {
            byte b = i < lookup.Length ? lookup[i] : (byte)0;
            ComponentRange range = baseSpace.GetComponentRange(i % m);
            components[i] = (float)(range.Minimum + (b * (range.Maximum - range.Minimum) / 255));
        }

        _palette = new float[entries * OutputCount];
        basePipeline.Convert(components, _palette, entries);
        _bytes = new byte[_palette.Length];
        ToBytes(_palette, _bytes);
    }

    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        int outputs = OutputCount;
        for (int i = 0; i < count; i++)
        {
            float value = source[i];
            int index = value >= 0 ? (int)Math.Min(Math.Floor(value + 0.5), _high) : 0;
            _palette.AsSpan(index * outputs, outputs).CopyTo(destination.Slice(i * outputs, outputs));
        }
    }

    /// <inheritdoc/>
    /// <remarks>Here a byte is the index itself (Table 88: the default Decode of an Indexed image is [0 2^bpc − 1]).</remarks>
    public override void Convert(ReadOnlySpan<byte> source, Span<byte> destination, int count)
    {
        int outputs = OutputCount;
        for (int i = 0; i < count; i++)
        {
            int index = Math.Min(source[i], _high);
            _bytes.AsSpan(index * outputs, outputs).CopyTo(destination.Slice(i * outputs, outputs));
        }
    }
}

/// <summary>A Separation or DeviceN space painted through its alternate: tints go through the tint transform.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.6.4 and §8.6.6.5. Without a usable tint transform the tints are treated as subtractive ink: the largest tint t
/// gives gray 1 − t, RGB (1 − t) × 3, CMYK (0, 0, 0, t), and 0 in every other alternate. Outputs the transform does not give are 0.
/// </remarks>
internal sealed class TintPipeline : ColorPipeline
{
    private readonly PdfFunction? _function;
    private readonly ColorPipeline _alternate;
    private readonly PdfColorSpaceFamily _alternateFamily;

    public TintPipeline(int inputs, PdfFunction? function, PdfColorSpace alternate, ColorPipeline alternatePipeline)
        : base(Unit(inputs), alternatePipeline.OutputCount)
    {
        _function = function is { IsValid: true } && function.InputCount <= PdfColor.MaxComponents ? function : null;
        _alternate = alternatePipeline;
        _alternateFamily = alternate.Family;
    }

    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        int inputs = InputCount;
        int m = _alternate.InputCount;
        int chunk = Math.Clamp(ScratchLength / Math.Max(inputs, m), 1, Chunk);
        Span<float> components = stackalloc float[ScratchLength];
        Span<float> input = stackalloc float[PdfColor.MaxComponents];
        Span<float> output = stackalloc float[PdfColor.MaxComponents];
        for (int start = 0; start < count; start += chunk)
        {
            int n = Math.Min(chunk, count - start);
            Span<float> alternate = components[..(n * m)];
            for (int i = 0; i < n; i++)
            {
                ReadOnlySpan<float> tints = source.Slice((start + i) * inputs, inputs);
                Span<float> target = alternate.Slice(i * m, m);
                if (_function is { } function)
                {
                    // The function takes its own number of inputs (it clips them to its domain) and its outputs fill the alternate.
                    int k = function.InputCount;
                    input[..k].Clear();
                    tints[..Math.Min(k, inputs)].CopyTo(input);
                    function.Evaluate(input[..k], output);
                    target.Clear();
                    output[..Math.Min(function.OutputCount, m)].CopyTo(target);
                }
                else
                {
                    Subtractive(tints, target);
                }
            }

            _alternate.Convert(alternate, destination.Slice(start * OutputCount, n * OutputCount), n);
        }
    }

    private static ComponentRange[] Unit(int count)
    {
        var ranges = new ComponentRange[count];
        Array.Fill(ranges, new ComponentRange(0, 1));
        return ranges;
    }

    private void Subtractive(ReadOnlySpan<float> tints, Span<float> target)
    {
        float tint = 0;
        foreach (float value in tints)
        {
            tint = Math.Max(tint, Math.Clamp(value, 0, 1));
        }

        target.Clear();
        switch (_alternateFamily)
        {
            case PdfColorSpaceFamily.DeviceGray or PdfColorSpaceFamily.CalGray:
                target[0] = 1 - tint;
                break;
            case PdfColorSpaceFamily.DeviceRgb or PdfColorSpaceFamily.CalRgb:
                target[0] = target[1] = target[2] = 1 - tint;
                break;
            case PdfColorSpaceFamily.DeviceCmyk:
                target[3] = tint;
                break;
        }
    }
}

/// <summary>Separation <c>All</c>: every colourant at the tint; on an additive device, 1 − tint on every component.</summary>
/// <remarks>ISO 32000-2 §8.6.6.4.</remarks>
internal sealed class AllColorantsPipeline(DeviceColorModel target, int outputs) : ColorPipeline([new ComponentRange(0, 1)], outputs)
{
    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        int outputs = OutputCount;
        for (int i = 0; i < count; i++)
        {
            float tint = (float)ColorMath.Clamp01(source[i]);
            destination.Slice(i * outputs, outputs).Fill(target == DeviceColorModel.Cmyk ? tint : 1 - tint);
        }
    }
}

/// <summary>Separation <c>None</c>, or DeviceN whose colourants are all <c>None</c>: nothing is painted.</summary>
/// <remarks>ISO 32000-2 §8.6.6.4 and §8.6.6.5. The colour written is no ink (white, or CMYK 0 0 0 0); painters check <see cref="PaintsNothing"/>.</remarks>
internal sealed class NoColorantPipeline(int inputs, DeviceColorModel target, int outputs) : ColorPipeline(Unit(inputs), outputs)
{
    /// <inheritdoc/>
    public override bool PaintsNothing => true;

    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<float> source, Span<float> destination, int count) =>
        destination[..(count * OutputCount)].Fill(target == DeviceColorModel.Cmyk ? 0 : 1);

    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<byte> source, Span<byte> destination, int count) =>
        destination[..(count * OutputCount)].Fill(target == DeviceColorModel.Cmyk ? (byte)0 : (byte)255);

    private static ComponentRange[] Unit(int count)
    {
        var ranges = new ComponentRange[count];
        Array.Fill(ranges, new ComponentRange(0, 1));
        return ranges;
    }
}

/// <summary>A Pattern space without an underlying space: its colours have no components; the pattern itself paints (§8.7).</summary>
/// <remarks>ISO 32000-2 §8.6.6.2. The colour written is black.</remarks>
internal sealed class PatternPipeline(DeviceColorModel target, int outputs) : ColorPipeline([], outputs)
{
    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Black(destination.Slice(i * OutputCount, OutputCount));
        }
    }

    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<byte> source, Span<byte> destination, int count)
    {
        Span<float> black = stackalloc float[OutputCount];
        Black(black);
        for (int i = 0; i < count; i++)
        {
            ToBytes(black, destination.Slice(i * OutputCount, OutputCount));
        }
    }

    private void Black(Span<float> color)
    {
        color.Clear();
        if (target == DeviceColorModel.Cmyk)
        {
            color[3] = 1;
        }
    }
}
