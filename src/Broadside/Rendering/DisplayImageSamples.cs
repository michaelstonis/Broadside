using Broadside.Images;

namespace Broadside.Rendering;

/// <summary>
/// The samples of a <see cref="DisplayImage"/> at one resolution: the image's own, or a reduction of it by a power of two, decoded
/// for a render that draws the image smaller than its resolution (<see cref="DisplayImage.GetSamplesAt(int)"/>).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.3 (the sample layout: rows top to bottom, samples left to right, components interleaved, each row starting on a
/// byte boundary, 16-bit samples big-endian) and §8.9.4 (the image fills the unit square). A reduction by s = 2^<see cref="Reduction"/>
/// of the image's <see cref="SourceWidth"/> × <see cref="SourceHeight"/> samples leaves ⌈W / s⌉ × ⌈H / s⌉, each standing for the
/// s × s source samples it covers. Sample i therefore spans [i·s / W, (i + 1)·s / W) of the unit square, and the reduced samples
/// together span [0, ⌈W / s⌉·s / W], clipped at 1: never infer the geometry from <see cref="Width"/> alone.
/// </para>
/// <para>
/// The samples are raw values like the image's (<see cref="DisplayImage.DecodeMap"/> applies to them), averaged in the codec's own
/// space before any colour conversion. Immutable; safe to read from any number of threads. The document's image cache owns it: a
/// backend never disposes it, and may key a per-surface cache on the instance.
/// </para>
/// </remarks>
public sealed class DisplayImageSamples
{
    private readonly DecodedImage _samples;

    internal DisplayImageSamples(DecodedImage samples, int sourceWidth, int sourceHeight)
    {
        _samples = samples;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
    }

    /// <summary>Gets the width in samples: ⌈<see cref="SourceWidth"/> / 2^<see cref="Reduction"/>⌉.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (Width), reduced.</remarks>
    public int Width => _samples.Width;

    /// <summary>Gets the height in samples: ⌈<see cref="SourceHeight"/> / 2^<see cref="Reduction"/>⌉.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (Height), reduced.</remarks>
    public int Height => _samples.Height;

    /// <summary>Gets the image's width in samples at its own resolution.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (Width), or what the codec reported (§7.4.9).</remarks>
    public int SourceWidth { get; }

    /// <summary>Gets the image's height in samples at its own resolution.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (Height), or what the codec reported (§7.4.9).</remarks>
    public int SourceHeight { get; }

    /// <summary>Gets the reduction the samples were decoded at, as a power of two: 0 for the image's own resolution, 3 for an eighth.</summary>
    /// <remarks>What was delivered, which may be less than was asked for: a codec reduces only as far as it can.</remarks>
    public int Reduction => _samples.Reduction;

    /// <summary>Gets the number of colour components per sample.</summary>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public int Components => _samples.Components;

    /// <summary>Gets the logical bits per component n: raw values run from 0 to 2^n − 1.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (BitsPerComponent).</remarks>
    public int BitsPerComponent => _samples.BitsPerComponent;

    /// <summary>Gets the bits each component occupies in <see cref="Samples"/>: 1, 2, 4, 8 or 16.</summary>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public int StorageBits => _samples.StorageBits;

    /// <summary>Gets the number of bytes per row of <see cref="Samples"/>.</summary>
    /// <remarks>ISO 32000-2 §8.9.3: each row starts on a byte boundary.</remarks>
    public int Stride => _samples.Stride;

    /// <summary>Gets the raw samples, <see cref="Stride"/> × <see cref="Height"/> bytes in the §8.9.3 layout.</summary>
    /// <remarks>ISO 32000-2 §8.9.3. Rows the data did not reach are zero.</remarks>
    public ReadOnlySpan<byte> Samples => _samples.Samples;

    /// <summary>Gets the bytes these samples keep; what the image cache charges for them.</summary>
    internal long RetainedBytes => _samples.RetainedBytes;

    /// <summary>Gets the decoded image behind the samples.</summary>
    internal DecodedImage Decoded => _samples;

    /// <summary>Gets one row of <see cref="Samples"/>.</summary>
    /// <param name="y">The row, from 0 at the top of the image.</param>
    /// <returns>The row's <see cref="Stride"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is outside the samples.</exception>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public ReadOnlySpan<byte> GetRow(int y) => _samples.GetRow(y);
}
