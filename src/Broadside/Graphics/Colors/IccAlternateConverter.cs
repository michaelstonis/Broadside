namespace Broadside.Graphics.Colors;

/// <summary>Converts ICCBased colours through the alternate space: clipped into the ICC range, then into the alternate's ranges.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.5.5, Table 65: the alternate is used "without any intermediate conversion"; colour values are constrained to the
/// ICC Range and then clipped into the alternate's ranges. The alternate conversion comes from the same colour management.
/// </remarks>
internal sealed class IccAlternateConverter : IColorConverter
{
    private const int Chunk = 64;

    private readonly IColorConverter _alternate;
    private readonly ComponentRange[] _ranges;

    public IccAlternateConverter(PdfIccBasedColorSpace space, PdfColorSpace alternate, IColorConverter converter)
    {
        _alternate = converter;
        InputCount = space.ComponentCount;
        OutputCount = converter.OutputCount;
        _ranges = new ComponentRange[InputCount];
        for (int i = 0; i < InputCount; i++)
        {
            ComponentRange icc = space.GetComponentRange(i);
            ComponentRange target = alternate.GetComponentRange(i);
            _ranges[i] = new ComponentRange(
                Math.Max(icc.Minimum, target.Minimum),
                Math.Max(Math.Max(icc.Minimum, target.Minimum), Math.Min(icc.Maximum, target.Maximum)));
        }
    }

    /// <inheritdoc/>
    public int InputCount { get; }

    /// <summary>Gets the alternate space's converter, which does all the converting after the clip.</summary>
    public IColorConverter Alternate => _alternate;

    /// <inheritdoc/>
    public int OutputCount { get; }

    /// <summary>Gets a value indicating whether every component is clipped to 0..1 (the ICC and alternate ranges both 0..1).</summary>
    public bool ClipsToUnit => Array.TrueForAll(_ranges, static range => range == new ComponentRange(0, 1));

    /// <inheritdoc/>
    public void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        int inputs = InputCount;
        Span<float> clipped = stackalloc float[Chunk * 4];
        for (int start = 0; start < count; start += Chunk)
        {
            int n = Math.Min(Chunk, count - start);
            ReadOnlySpan<float> batch = source.Slice(start * inputs, n * inputs);
            for (int c = 0; c < inputs; c++)
            {
                ComponentRange range = _ranges[c];
                for (int i = c; i < batch.Length; i += inputs)
                {
                    clipped[i] = (float)range.Clamp(batch[i]);
                }
            }

            _alternate.Convert(clipped[..(n * inputs)], destination[(start * OutputCount)..], n);
        }
    }
}
