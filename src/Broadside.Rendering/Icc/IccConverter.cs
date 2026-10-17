using Broadside.Graphics;

namespace Broadside.Rendering.Icc;

/// <summary>Converts an ICCBased space's colours through an ICC transform: to the RGB profile, then, for a gray or CMYK target, through the fallback's DeviceRGB conversion; or straight to an output intent's device.</summary>
/// <remarks>
/// ISO 32000-2 §10.3 (CIE-based to device colour through the profile) and §10.4.2 (RGB to gray and CMYK, by whatever DeviceRGB
/// conversion the wrapped colour management makes, so black generation and undercolour removal apply). Allocates nothing per call.
/// </remarks>
internal sealed class IccConverter : IColorConverter
{
    private const int Chunk = 64;

    private readonly IccTransform _transform;
    private readonly IColorConverter? _fromRgb;

    public IccConverter(IccTransform transform, IColorConverter? fromRgb)
    {
        _transform = transform;
        _fromRgb = fromRgb;
        OutputCount = fromRgb?.OutputCount ?? transform.OutputCount;
    }

    /// <inheritdoc/>
    public int InputCount => _transform.InputCount;

    /// <inheritdoc/>
    public int OutputCount { get; }

    /// <inheritdoc/>
    public void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        if (_fromRgb is null)
        {
            _transform.Convert(source, destination, count);
            return;
        }

        int inputs = InputCount;
        int outputs = OutputCount;
        Span<float> rgb = stackalloc float[Chunk * 3];
        for (int start = 0; start < count; start += Chunk)
        {
            int n = Math.Min(Chunk, count - start);
            _transform.Convert(source.Slice(start * inputs, n * inputs), rgb, n);
            _fromRgb.Convert(rgb[..(n * 3)], destination.Slice(start * outputs, n * outputs), n);
        }
    }
}
