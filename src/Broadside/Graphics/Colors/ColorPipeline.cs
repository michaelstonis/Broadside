namespace Broadside.Graphics.Colors;

/// <summary>
/// One step of a colour conversion as <see cref="PdfColorConverter"/> runs it: from a colour space's components to device colours.
/// Immutable after construction (the 8-bit table is built once, lazily) and safe to run from several threads at once.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.6.6: the special spaces are expanded here (an Indexed table, a tint transform, All and None), and the device,
/// CIE-based and ICCBased spaces go to the colour-management extension point. Runs of colours go through one call; nothing is
/// allocated per colour.
/// </remarks>
internal abstract class ColorPipeline
{
    /// <summary>How many colours are converted per batch through stack scratch space.</summary>
    protected const int Chunk = 64;

    /// <summary>The most input components per batch held on the stack.</summary>
    protected const int ScratchLength = Chunk * PdfColor.MaxComponents;

    private readonly ComponentRange[] _ranges;
    private byte[]? _byteTable;

    // Whether the RGB byte table maps every sample b to (b, b, b), so the lookup is a replication (GrayExpansion). Written before the
    // table is published (Volatile.Write) and read after it is seen (Volatile.Read), so a reader that has the table has the flag.
    private bool _tableHoldsGrays;

    /// <summary>Initializes the shared part of a pipeline.</summary>
    /// <param name="ranges">The range of each input component: what 8-bit samples 0 to 255 map onto.</param>
    /// <param name="outputs">The number of device components.</param>
    protected ColorPipeline(ComponentRange[] ranges, int outputs)
    {
        _ranges = ranges;
        OutputCount = outputs;
    }

    /// <summary>Gets the range of each input component.</summary>
    public ComponentRange[] InputRanges => _ranges;

    /// <summary>Gets the number of input components.</summary>
    public int InputCount => _ranges.Length;

    /// <summary>Gets the number of device components.</summary>
    public int OutputCount { get; }

    /// <summary>Gets a value indicating whether colours in this space paint nothing (Separation <c>None</c>, an all-None DeviceN).</summary>
    public virtual bool PaintsNothing => false;

    /// <summary>
    /// Gets how far image samples may be averaged before this conversion rather than after it (<see cref="ImageReductions"/>):
    /// without limit for an affine conversion, less for a curved one, not at all for a table lookup.
    /// </summary>
    public virtual int MaxImageReduction => ImageReductions.Curved;

    /// <summary>Converts <paramref name="count"/> colours of <see cref="InputCount"/> components.</summary>
    /// <param name="source">The components, colour after colour.</param>
    /// <param name="destination">Receives the device colours, colour after colour.</param>
    /// <param name="count">The number of colours.</param>
    public abstract void Convert(ReadOnlySpan<float> source, Span<float> destination, int count);

    /// <summary>Converts 8-bit samples: each byte maps linearly from 0..255 onto its component's range.</summary>
    /// <param name="source">The samples, colour after colour.</param>
    /// <param name="destination">Receives 8-bit device colours.</param>
    /// <param name="count">The number of colours.</param>
    public virtual void Convert(ReadOnlySpan<byte> source, Span<byte> destination, int count)
    {
        int inputs = InputCount;
        int outputs = OutputCount;
        if (inputs == 1)
        {
            ReadOnlySpan<byte> table = ByteTable();
            if (outputs == 3 && _tableHoldsGrays)
            {
                // r = g = b = the sample: the lookup is a replication (issue #472).
                GrayExpansion.ToRgb(source[..count], destination[..(3 * count)]);
            }
            else if (outputs == 3)
            {
                for (int i = 0; i < count; i++)
                {
                    int entry = source[i] * 3;
                    destination[3 * i] = table[entry];
                    destination[(3 * i) + 1] = table[entry + 1];
                    destination[(3 * i) + 2] = table[entry + 2];
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    table.Slice(source[i] * outputs, outputs).CopyTo(destination.Slice(i * outputs, outputs));
                }
            }

            return;
        }

        // A DeviceN space may have many colourants: the batch shrinks so the scratch space stays bounded.
        int chunk = Math.Clamp(ScratchLength / inputs, 1, Chunk);
        Span<float> components = stackalloc float[ScratchLength];
        Span<float> colors = stackalloc float[Chunk * 4];
        for (int start = 0; start < count; start += chunk)
        {
            int n = Math.Min(chunk, count - start);
            ReadOnlySpan<byte> samples = source.Slice(start * inputs, n * inputs);
            for (int i = 0; i < samples.Length; i++)
            {
                ComponentRange range = _ranges[i % inputs];
                components[i] = (float)(range.Minimum + (samples[i] * (range.Maximum - range.Minimum) / 255));
            }

            Convert(components[..(n * inputs)], colors, n);
            ToBytes(colors[..(n * outputs)], destination.Slice(start * outputs, n * outputs));
        }
    }

    /// <summary>Converts device colours 0..1 to bytes, rounding to nearest.</summary>
    /// <param name="colors">The colours.</param>
    /// <param name="destination">The bytes.</param>
    protected static void ToBytes(ReadOnlySpan<float> colors, Span<byte> destination)
    {
        for (int i = 0; i < colors.Length; i++)
        {
            destination[i] = (byte)((ColorMath.Clamp01(colors[i]) * 255) + 0.5);
        }
    }

    /// <summary>Returns the 256 device colours of a one-component space, built on first use.</summary>
    /// <returns>256 × <see cref="OutputCount"/> bytes.</returns>
    private byte[] ByteTable()
    {
        byte[]? table = Volatile.Read(ref _byteTable);
        if (table is not null)
        {
            return table;
        }

        ComponentRange range = _ranges[0];
        float[] components = new float[256];
        for (int b = 0; b < 256; b++)
        {
            components[b] = (float)(range.Minimum + (b * (range.Maximum - range.Minimum) / 255));
        }

        float[] colors = new float[256 * OutputCount];
        Convert(components, colors, 256);
        table = new byte[colors.Length];
        ToBytes(colors, table);
        _tableHoldsGrays = OutputCount == 3 && HoldsGrays(table);
        Volatile.Write(ref _byteTable, table);
        return table;
    }

    /// <summary>Whether an RGB byte table holds the grays: entry b is (b, b, b) for every b (§10.4.2.2, DeviceGray on an RGB device).</summary>
    private static bool HoldsGrays(ReadOnlySpan<byte> table)
    {
        for (int b = 0; b < 256; b++)
        {
            if (table[3 * b] != b || table[(3 * b) + 1] != b || table[(3 * b) + 2] != b)
            {
                return false;
            }
        }

        return true;
    }
}
