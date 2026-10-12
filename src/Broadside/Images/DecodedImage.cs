using System.Buffers;

namespace Broadside.Images;

/// <summary>
/// The decoded samples of an image: the buffer every image codec fills and the renderer reads. Owns pooled memory; dispose it to
/// return the memory.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.3 sample layout, exactly: <see cref="Height"/> rows of <see cref="Stride"/> bytes, each row starting on a byte
/// boundary; within a row the <see cref="Components"/> components of each sample are interleaved, packed most significant bit first
/// at <see cref="StorageBits"/> bits each (1, 2, 4, 8 or 16; 16-bit values big-endian); the padding bits at the end of a row are
/// meaningless. <see cref="Stride"/> is ⌈<see cref="Width"/> × <see cref="Components"/> × <see cref="StorageBits"/> / 8⌉ with no
/// further alignment, so a filter's output is adopted as it is.
/// </para>
/// <para>
/// The values are the raw samples, before the image's <c>Decode</c> array (§8.9.5.2): <see cref="ImageDecodeMap"/> applies it.
/// <see cref="BitsPerComponent"/> is the logical depth, which sets the largest raw value (2^n − 1); a depth the layout cannot store
/// directly (3, 5, 6, 7, 9 to 15, a JPEG 2000 precision) is stored in the next wider unit without scaling.
/// </para>
/// <para>
/// Built by a <see cref="DecodedImageBuilder"/>, then immutable: safe for concurrent readers until disposed. Disposing while another
/// thread reads is a caller error.
/// </para>
/// </remarks>
public sealed class DecodedImage : IDisposable
{
    private byte[]? _buffer;
    private readonly int _length;

    internal DecodedImage(DecodedImageBuilder builder, byte[] buffer, int length, DecodedImage? alpha)
    {
        _buffer = buffer;
        _length = length;
        Width = builder.Width;
        Height = builder.Height;
        Components = builder.Components;
        BitsPerComponent = builder.BitsPerComponent;
        StorageBits = builder.StorageBits;
        Stride = builder.Stride;
        DecodedRows = builder.DecodedRows;
        SamplesInverted = builder.SamplesInverted;
        ColorModel = builder.ColorModel;
        IccProfile = builder.IccProfile;
        Palette = builder.Palette;
        AdobeTransform = builder.AdobeTransform;
        ColorTransformApplied = builder.ColorTransformApplied;
        Alpha = alpha;
        AlphaPremultiplied = alpha is not null && builder.AlphaPremultiplied;
    }

    /// <summary>Gets the number of samples in each row.</summary>
    /// <remarks>ISO 32000-2 §8.9.2.</remarks>
    public int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    /// <remarks>ISO 32000-2 §8.9.2.</remarks>
    public int Height { get; }

    /// <summary>Gets the number of colour components in each sample (1 for an image mask or a soft mask).</summary>
    /// <remarks>ISO 32000-2 §8.9.2.</remarks>
    public int Components { get; }

    /// <summary>Gets the logical number of bits per component: raw values run from 0 to 2^n − 1.</summary>
    /// <remarks>ISO 32000-2 §8.9.2, §8.9.5.2 (the Decode formula divides by 2^n − 1).</remarks>
    public int BitsPerComponent { get; }

    /// <summary>Gets the number of bits each component occupies in <see cref="Samples"/>: 1, 2, 4, 8 or 16.</summary>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public int StorageBits { get; }

    /// <summary>Gets the number of bytes in each row of <see cref="Samples"/>.</summary>
    /// <remarks>ISO 32000-2 §8.9.3: rows start on byte boundaries.</remarks>
    public int Stride { get; }

    /// <summary>Gets the number of rows the data actually held; the rows after them are zero.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.3. Less than <see cref="Height"/> only for truncated data, which a lenient reader decodes as far as it goes
    /// and records as a diagnostic (ADR 0005). An image mask's missing rows paint nothing.
    /// </remarks>
    public int DecodedRows { get; }

    /// <summary>Gets all rows: <see cref="Stride"/> × <see cref="Height"/> bytes.</summary>
    /// <exception cref="ObjectDisposedException">The image was disposed.</exception>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public ReadOnlySpan<byte> Samples => Buffer.AsSpan(0, _length);

    /// <summary>
    /// Gets a value indicating whether the codec delivered samples of the opposite polarity to the PDF convention (a JBIG2 codec that
    /// leaves 1 as black, say) and left inverting them to the image layer, which folds the inversion into the <c>Decode</c> mapping.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.4.7 and §8.9.5.2. <see cref="PdfImage.CreateDecodeMap(DecodedImage)"/> and the stencil helpers take it into
    /// account; colour key masks (§8.9.6.4) compare the samples as the PDF file defines them, so a codec that sets it must not be
    /// used for colour-keyed images.
    /// </remarks>
    public bool SamplesInverted { get; }

    /// <summary>Gets the colour model the codec reports, or <see cref="ImageColorModel.Unknown"/>.</summary>
    /// <remarks>ISO 32000-2 §7.4.9: used only when the image dictionary has no <c>ColorSpace</c>.</remarks>
    public ImageColorModel ColorModel { get; }

    /// <summary>Gets the ICC profile the codestream carries (a JPEG 2000 <c>colr</c> box), or empty.</summary>
    /// <remarks>ISO 32000-2 §7.4.9.</remarks>
    public ReadOnlyMemory<byte> IccProfile { get; }

    /// <summary>Gets the palette the codec left unapplied because the image's colour space is Indexed, as base-space bytes, or empty.</summary>
    /// <remarks>ISO 32000-2 §7.4.9 and §8.6.6.3.</remarks>
    public ReadOnlyMemory<byte> Palette { get; }

    /// <summary>Gets the transform code of a JPEG Adobe APP14 marker (0, 1 or 2), or <see langword="null"/> when there is none.</summary>
    /// <remarks>ISO 32000-2 §7.4.8, Table 13; Adobe Technical Note 5116.</remarks>
    public int? AdobeTransform { get; }

    /// <summary>Gets a value indicating whether the codec converted YCbCr to RGB or YCCK to CMYK.</summary>
    /// <remarks>ISO 32000-2 §7.4.8, Table 13 (<c>ColorTransform</c>).</remarks>
    public bool ColorTransformApplied { get; }

    /// <summary>Gets the opacity the codec decoded with the colour (a JPEG 2000 opacity channel), one component, same size; or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.4.9 and §8.9.5.1 Table 87 (<c>SMaskInData</c>). Disposed with this image.</remarks>
    public DecodedImage? Alpha { get; }

    /// <summary>Gets a value indicating whether the colour samples were premultiplied by <see cref="Alpha"/> (<c>SMaskInData 2</c>).</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1 Table 87.</remarks>
    public bool AlphaPremultiplied { get; }

    private byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(DecodedImage));

    /// <summary>Returns row <paramref name="y"/>: <see cref="Stride"/> bytes, top row first.</summary>
    /// <param name="y">The row, from 0 at the top (§8.9.4: image space has its origin at the upper-left corner).</param>
    /// <returns>The row's bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is not a row.</exception>
    /// <exception cref="ObjectDisposedException">The image was disposed.</exception>
    /// <remarks>ISO 32000-2 §8.9.3, §8.9.4.</remarks>
    public ReadOnlySpan<byte> GetRow(int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return Buffer.AsSpan(y * Stride, Stride);
    }

    /// <summary>Returns the pooled memory, and the alpha image's.</summary>
    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        Alpha?.Dispose();
    }
}
