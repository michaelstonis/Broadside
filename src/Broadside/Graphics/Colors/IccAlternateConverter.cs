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

    /// <inheritdoc/>
    public int OutputCount { get; }

    /// <inheritdoc/>
    public void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        int inputs = InputCount;
        Span<float> clipped = stackalloc float[Chunk * 4];
        for (int start = 0; start < count; start += Chunk)
        {
            int n = Math.Min(Chunk, count - start);
            for (int i = 0; i < n * inputs; i++)
            {
                clipped[i] = (float)_ranges[i % inputs].Clamp(source[(start * inputs) + i]);
            }

            _alternate.Convert(clipped[..(n * inputs)], destination[(start * OutputCount)..], n);
        }
    }
}
