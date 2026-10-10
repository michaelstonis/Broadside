using Broadside.Graphics;

namespace Broadside.Images;

/// <summary>
/// An image's Decode mapping, compiled once per image: raw sample values to colour component values,
/// y = Dmin + x × (Dmax − Dmin) / (2^n − 1). Immutable and thread-safe; mapping allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.5.2, Table 88. Get one from <see cref="PdfImage.CreateDecodeMap(DecodedImage)"/>, which resolves the image's
/// default array and the codec's polarity, or build one from an array with <see cref="Create"/>. Depths up to 8 bits use a lookup
/// table per component; 16-bit values are computed.
/// </para>
/// <para>
/// The results are not clamped: a Decode array may map outside a component's range (§8.9.5.2 allows Dmin and Dmax outside it), and
/// the values are clamped to the nearest allowed value when the colour is used. <see cref="Map(ReadOnlySpan{ushort}, Span{float}, ReadOnlySpan{ComponentRange})"/>
/// does both at once. For an Indexed image use <see cref="MapToIndices"/>, which rounds and clamps to the table (§8.6.6.3).
/// </para>
/// </remarks>
public sealed class ImageDecodeMap
{
    private readonly float[] _minimum;
    private readonly float[] _scale;
    private readonly float[]? _table;
    private readonly double _indexMinimum;
    private readonly double _indexScale;

    private ImageDecodeMap(ReadOnlySpan<double> decode, int components, int bitsPerComponent)
    {
        Components = components;
        BitsPerComponent = bitsPerComponent;
        _minimum = new float[components];
        _scale = new float[components];
        double top = (1 << bitsPerComponent) - 1;
        for (int c = 0; c < components; c++)
        {
            _minimum[c] = (float)decode[2 * c];
            _scale[c] = (float)((decode[(2 * c) + 1] - decode[2 * c]) / top);
        }

        IsInverted = decode[0] > decode[1];
        _indexMinimum = decode[0];
        _indexScale = (decode[1] - decode[0]) / top;
        if (bitsPerComponent <= 8)
        {
            int size = 1 << bitsPerComponent;
            _table = new float[components * size];
            for (int c = 0; c < components; c++)
            {
                for (int x = 0; x < size; x++)
                {
                    _table[(c * size) + x] = (float)(decode[2 * c] + (x * (decode[(2 * c) + 1] - decode[2 * c]) / top));
                }
            }
        }
    }

    /// <summary>Gets the number of components per sample.</summary>
    public int Components { get; }

    /// <summary>Gets the logical bits per component n: raw values run from 0 to 2^n − 1.</summary>
    public int BitsPerComponent { get; }

    /// <summary>Gets a value indicating whether the first component maps 0 above 2^n − 1 (Dmin &gt; Dmax): for an image mask, a sample 1 paints.</summary>
    /// <remarks>ISO 32000-2 §8.9.6.2: Decode <c>[1 0]</c> reverses the meaning of a stencil mask's samples.</remarks>
    public bool IsInverted { get; }

    /// <summary>Compiles a Decode array.</summary>
    /// <param name="decode">2 × <paramref name="components"/> numbers: Dmin and Dmax per component.</param>
    /// <param name="components">The number of components per sample, 1 to 32.</param>
    /// <param name="bitsPerComponent">The logical bits per component, 1 to 16.</param>
    /// <returns>The map.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An argument is out of range, or <paramref name="decode"/> is too short.</exception>
    /// <remarks>ISO 32000-2 §8.9.5.2.</remarks>
    public static ImageDecodeMap Create(ReadOnlySpan<double> decode, int components, int bitsPerComponent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(components, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(components, ImageGeometry.MaxComponents);
        ArgumentOutOfRangeException.ThrowIfLessThan(bitsPerComponent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitsPerComponent, 16);
        ArgumentOutOfRangeException.ThrowIfLessThan(decode.Length, 2 * components, nameof(decode));
        return new ImageDecodeMap(decode, components, bitsPerComponent);
    }

    /// <summary>Maps one raw value of component <paramref name="component"/>.</summary>
    /// <param name="component">The component, from 0.</param>
    /// <param name="raw">The raw value, 0 to 2^n − 1 (larger values are masked to n bits).</param>
    /// <returns>The component value.</returns>
    public float Map(int component, int raw)
    {
        int mask = (1 << BitsPerComponent) - 1;
        return _table is { } table
            ? table[(component << BitsPerComponent) + (raw & mask)]
            : _minimum[component] + ((raw & mask) * _scale[component]);
    }

    /// <summary>Maps interleaved raw values; value i belongs to component i mod <see cref="Components"/>.</summary>
    /// <param name="raw">Raw values, such as from <see cref="ImageRows.Unpack(ReadOnlySpan{byte}, int, int, Span{byte})"/>.</param>
    /// <param name="output">At least as many values.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="output"/> is too short.</exception>
    public void Map(ReadOnlySpan<byte> raw, Span<float> output)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, raw.Length, nameof(output));
        int mask = (1 << BitsPerComponent) - 1;
        if (_table is { } table)
        {
            int shift = BitsPerComponent;
            for (int i = 0, c = 0; i < raw.Length; i++)
            {
                output[i] = table[(c << shift) + (raw[i] & mask)];
                c = c + 1 == Components ? 0 : c + 1;
            }

            return;
        }

        for (int i = 0, c = 0; i < raw.Length; i++)
        {
            output[i] = _minimum[c] + ((raw[i] & mask) * _scale[c]);
            c = c + 1 == Components ? 0 : c + 1;
        }
    }

    /// <summary>Maps interleaved raw values of any depth; value i belongs to component i mod <see cref="Components"/>.</summary>
    /// <param name="raw">Raw values, such as from <see cref="ImageRows.Unpack(ReadOnlySpan{byte}, int, int, Span{ushort})"/>.</param>
    /// <param name="output">At least as many values.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="output"/> is too short.</exception>
    public void Map(ReadOnlySpan<ushort> raw, Span<float> output)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, raw.Length, nameof(output));
        int mask = (1 << BitsPerComponent) - 1;
        if (_table is { } table)
        {
            int shift = BitsPerComponent;
            for (int i = 0, c = 0; i < raw.Length; i++)
            {
                output[i] = table[(c << shift) + (raw[i] & mask)];
                c = c + 1 == Components ? 0 : c + 1;
            }

            return;
        }

        for (int i = 0, c = 0; i < raw.Length; i++)
        {
            output[i] = _minimum[c] + ((raw[i] & mask) * _scale[c]);
            c = c + 1 == Components ? 0 : c + 1;
        }
    }

    /// <summary>Maps interleaved raw values and clamps each result to its component's range.</summary>
    /// <param name="raw">Raw values.</param>
    /// <param name="output">At least as many values.</param>
    /// <param name="ranges">The range of each component (<see cref="PdfColorSpace.GetComponentRange"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException">A span is too short.</exception>
    /// <remarks>ISO 32000-2 §8.9.5.2: "the nearest allowed value".</remarks>
    public void Map(ReadOnlySpan<ushort> raw, Span<float> output, ReadOnlySpan<ComponentRange> ranges)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ranges.Length, Components, nameof(ranges));
        Map(raw, output);
        for (int i = 0, c = 0; i < raw.Length; i++)
        {
            output[i] = (float)ranges[c].Clamp(output[i]);
            c = c + 1 == Components ? 0 : c + 1;
        }
    }

    /// <summary>Maps the raw values of a one-component Indexed image to table indices: rounded (half up) and clamped to 0 through <paramref name="highValue"/>.</summary>
    /// <param name="raw">Raw values.</param>
    /// <param name="indices">At least as many bytes.</param>
    /// <param name="highValue">The colour table's highest index (<see cref="PdfIndexedColorSpace.HighValue"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException">A span is too short, or <paramref name="highValue"/> is not 0 to 255.</exception>
    /// <remarks>ISO 32000-2 §8.6.6.3: "rounded to the nearest integer ... clipped to the range 0 to hival".</remarks>
    public void MapToIndices(ReadOnlySpan<ushort> raw, Span<byte> indices, int highValue)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(indices.Length, raw.Length, nameof(indices));
        ArgumentOutOfRangeException.ThrowIfNegative(highValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(highValue, 255);
        for (int i = 0; i < raw.Length; i++)
        {
            int mask = (1 << BitsPerComponent) - 1;
            double index = Math.Floor(_indexMinimum + ((raw[i] & mask) * _indexScale) + 0.5);
            indices[i] = (byte)Math.Clamp(index, 0, highValue);
        }
    }
}
