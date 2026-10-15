namespace Broadside.Graphics.Colors;

/// <summary>
/// The transfer function applied to the device colours another pipeline produces: each device component through its own function,
/// in additive form.
/// </summary>
/// <remarks>
/// ISO 32000-2 §10.5. The function of each component is chosen once: a single function for every component; of four, red, green and
/// blue for RGB, gray (the fourth) for a gray device, all four for CMYK. A subtractive (CMYK) component c goes through as 1 − c and
/// the result is converted back. A DeviceGray colour converted to CMYK passes only its black through the gray function; its cyan,
/// magenta and yellow are rendered directly. The 8-bit path applies 256-entry tables built once, after the inner pipeline's own
/// 8-bit conversion. Nothing is allocated per colour.
/// </remarks>
internal sealed class TransferPipeline : ColorPipeline
{
    private readonly ColorPipeline _inner;
    private readonly PdfFunction?[] _functions;
    private readonly bool _subtractive;
    private byte[]? _tables;

    /// <summary>Wraps <paramref name="inner"/>.</summary>
    /// <param name="inner">The conversion to device colours.</param>
    /// <param name="transfer">The transfer function; not the identity.</param>
    /// <param name="target">The device colour model <paramref name="inner"/> produces.</param>
    /// <param name="grayOnBlackOnly">Whether only the black component is transferred (a DeviceGray source on a CMYK device).</param>
    public TransferPipeline(ColorPipeline inner, PdfTransferFunction transfer, DeviceColorModel target, bool grayOnBlackOnly)
        : base(inner.InputRanges, inner.OutputCount)
    {
        _inner = inner;
        _subtractive = target == DeviceColorModel.Cmyk;
        IReadOnlyList<PdfFunction> functions = transfer.Functions;
        _functions = new PdfFunction?[inner.OutputCount];
        for (int component = 0; component < _functions.Length; component++)
        {
            // A single function serves every component; of four, a gray device uses the fourth (§10.5).
            int index = functions.Count == 1 ? 0 : target == DeviceColorModel.Gray ? 3 : component;
            PdfFunction function = functions[Math.Min(index, functions.Count - 1)];
            bool skipped = (grayOnBlackOnly && component < 3) || function.InputCount != 1 || function.OutputCount != 1;
            _functions[component] = skipped ? null : function;
        }
    }

    /// <inheritdoc/>
    public override bool PaintsNothing => _inner.PaintsNothing;

    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        _inner.Convert(source, destination, count);
        int outputs = OutputCount;
        Span<float> values = stackalloc float[Chunk];
        Span<float> results = stackalloc float[Chunk];
        for (int component = 0; component < outputs; component++)
        {
            if (_functions[component] is not { } function)
            {
                continue;
            }

            for (int start = 0; start < count; start += Chunk)
            {
                int n = Math.Min(Chunk, count - start);
                for (int i = 0; i < n; i++)
                {
                    float value = (float)ColorMath.Clamp01(destination[((start + i) * outputs) + component]);
                    values[i] = _subtractive ? 1 - value : value;
                }

                function.Evaluate(values[..n], results, n);
                for (int i = 0; i < n; i++)
                {
                    float value = (float)ColorMath.Clamp01(results[i]);
                    destination[((start + i) * outputs) + component] = _subtractive ? 1 - value : value;
                }
            }
        }
    }

    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<byte> source, Span<byte> destination, int count)
    {
        _inner.Convert(source, destination, count);
        ReadOnlySpan<byte> tables = Tables();
        int outputs = OutputCount;
        for (int component = 0; component < outputs; component++)
        {
            if (_functions[component] is null)
            {
                continue;
            }

            ReadOnlySpan<byte> table = tables.Slice(component * 256, 256);
            for (int i = component; i < count * outputs; i += outputs)
            {
                destination[i] = table[destination[i]];
            }
        }
    }

    /// <summary>Returns a table of 256 bytes per component, built on first use: the transferred value of each 8-bit value.</summary>
    private byte[] Tables()
    {
        byte[]? tables = Volatile.Read(ref _tables);
        if (tables is not null)
        {
            return tables;
        }

        int outputs = OutputCount;
        tables = new byte[outputs * 256];
        float[] levels = new float[256];
        float[] results = new float[256];
        for (int component = 0; component < outputs; component++)
        {
            if (_functions[component] is not { } function)
            {
                continue;
            }

            for (int b = 0; b < 256; b++)
            {
                levels[b] = _subtractive ? 1 - (b / 255f) : b / 255f;
            }

            function.Evaluate(levels, results, 256);
            for (int b = 0; b < 256; b++)
            {
                float value = (float)ColorMath.Clamp01(results[b]);
                tables[(component * 256) + b] = (byte)(((_subtractive ? 1 - value : value) * 255) + 0.5);
            }
        }

        Volatile.Write(ref _tables, tables);
        return tables;
    }
}
