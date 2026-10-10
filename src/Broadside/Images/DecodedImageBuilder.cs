using System.Buffers;

namespace Broadside.Images;

/// <summary>
/// Fills a <see cref="DecodedImage"/>: a codec writes rows into <see cref="Samples"/> (or <see cref="GetRow"/>), sets what it learned
/// about the samples, then calls <see cref="Build"/>. Get one from <see cref="Filters.ImageFilterContext.TryCreateImage"/>, which
/// checks the engine's image limits before any memory is rented.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.3: the layout is <see cref="DecodedImage"/>'s. The memory starts zeroed, so rows a codec never writes (truncated
/// data) read as zero; set <see cref="DecodedRows"/> to the rows actually decoded.
/// </para>
/// <para>One codec call on one thread uses a builder. Dispose it if <see cref="Build"/> is never called.</para>
/// </remarks>
public sealed class DecodedImageBuilder : IDisposable
{
    private byte[]? _buffer;
    private readonly int _length;
    private DecodedImageBuilder? _alpha;
    private int _decodedRows;

    internal DecodedImageBuilder(int width, int height, int components, int bitsPerComponent)
    {
        Width = width;
        Height = height;
        Components = components;
        BitsPerComponent = bitsPerComponent;
        StorageBits = ImageGeometry.StorageBits(bitsPerComponent);
        Stride = (int)ImageGeometry.Stride(width, components, StorageBits);
        _length = checked(Stride * height);
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, _length));
        _buffer.AsSpan(0, _length).Clear();
        _decodedRows = height;
    }

    /// <summary>Gets the number of samples in each row.</summary>
    /// <remarks>ISO 32000-2 §8.9.2.</remarks>
    public int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    /// <remarks>ISO 32000-2 §8.9.2.</remarks>
    public int Height { get; }

    /// <summary>Gets the number of components in each sample.</summary>
    /// <remarks>ISO 32000-2 §8.9.2.</remarks>
    public int Components { get; }

    /// <summary>Gets the logical bits per component.</summary>
    /// <remarks>ISO 32000-2 §8.9.2, §8.9.5.2 (the Decode formula divides by 2^n − 1).</remarks>
    public int BitsPerComponent { get; }

    /// <summary>Gets the bits each component occupies in the buffer: 1, 2, 4, 8 or 16.</summary>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public int StorageBits { get; }

    /// <summary>Gets the number of bytes in each row.</summary>
    /// <remarks>ISO 32000-2 §8.9.3: rows start on byte boundaries.</remarks>
    public int Stride { get; }

    /// <summary>Gets the whole buffer, for a codec that writes all rows at once.</summary>
    /// <exception cref="InvalidOperationException">The builder was built or disposed.</exception>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public Span<byte> Samples => Buffer.AsSpan(0, _length);

    /// <summary>Gets or sets the number of rows actually decoded; the default is <see cref="Height"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than <see cref="Height"/>.</exception>
    /// <remarks>ISO 32000-2 §8.9.3: the rows after these are zero.</remarks>
    public int DecodedRows
    {
        get => _decodedRows;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Height);
            _decodedRows = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether the samples have the opposite polarity to the PDF convention (see <see cref="DecodedImage.SamplesInverted"/>).</summary>
    /// <remarks>ISO 32000-2 §8.9.5.2: the image layer folds an inversion into the Decode mapping.</remarks>
    public bool SamplesInverted { get; set; }

    /// <summary>Gets or sets the colour model of the samples.</summary>
    /// <remarks>ISO 32000-2 §7.4.9: used only when the image dictionary has no <c>ColorSpace</c>.</remarks>
    public ImageColorModel ColorModel { get; set; }

    /// <summary>Gets or sets the ICC profile the codestream carries.</summary>
    /// <remarks>ISO 32000-2 §7.4.9.</remarks>
    public ReadOnlyMemory<byte> IccProfile { get; set; }

    /// <summary>Gets or sets the palette left unapplied.</summary>
    /// <remarks>ISO 32000-2 §7.4.9 and §8.6.6.3.</remarks>
    public ReadOnlyMemory<byte> Palette { get; set; }

    /// <summary>Gets or sets the Adobe APP14 transform code.</summary>
    /// <remarks>ISO 32000-2 §7.4.8, Table 13; Adobe Technical Note 5116.</remarks>
    public int? AdobeTransform { get; set; }

    /// <summary>Gets or sets a value indicating whether a YCbCr or YCCK transform was applied.</summary>
    /// <remarks>ISO 32000-2 §7.4.8, Table 13 (<c>ColorTransform</c>).</remarks>
    public bool ColorTransformApplied { get; set; }

    /// <summary>Gets a value indicating whether the colour was premultiplied by the alpha created with <see cref="TryCreateAlpha"/>.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1 Table 87 (<c>SMaskInData</c>).</remarks>
    public bool AlphaPremultiplied { get; private set; }

    /// <summary>Gets the alpha builder created with <see cref="TryCreateAlpha"/>, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.4.9 and §8.9.5.1 Table 87 (<c>SMaskInData</c>).</remarks>
    public DecodedImageBuilder? Alpha => _alpha;

    private byte[] Buffer => _buffer ?? throw new InvalidOperationException("The image was already built or disposed.");

    /// <summary>Returns row <paramref name="y"/> to write.</summary>
    /// <param name="y">The row, from 0 at the top.</param>
    /// <returns>The row's <see cref="Stride"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is not a row.</exception>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public Span<byte> GetRow(int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return Buffer.AsSpan(y * Stride, Stride);
    }

    /// <summary>Creates the alpha plane: one component, the same width and height, at <paramref name="bitsPerComponent"/>.</summary>
    /// <param name="bitsPerComponent">The alpha's bits per component, 1 to 16.</param>
    /// <param name="premultiplied">Whether the colour samples are premultiplied by it (<c>SMaskInData 2</c>).</param>
    /// <param name="alpha">The alpha builder, filled like this one and built with it.</param>
    /// <returns><see langword="false"/> when an alpha plane exists already.</returns>
    /// <remarks>ISO 32000-2 §7.4.9, §8.9.5.1 Table 87 (<c>SMaskInData</c>).</remarks>
    public bool TryCreateAlpha(int bitsPerComponent, bool premultiplied, out DecodedImageBuilder alpha)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bitsPerComponent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitsPerComponent, 16);
        if (_alpha is not null)
        {
            alpha = _alpha;
            return false;
        }

        alpha = _alpha = new DecodedImageBuilder(Width, Height, 1, bitsPerComponent);
        AlphaPremultiplied = premultiplied;
        return true;
    }

    /// <summary>Freezes the samples into a <see cref="DecodedImage"/>, which takes over the memory.</summary>
    /// <returns>The image.</returns>
    /// <exception cref="InvalidOperationException">The builder was built or disposed.</exception>
    public DecodedImage Build()
    {
        byte[] buffer = Buffer;
        DecodedImage? alpha = _alpha?.Build();
        _buffer = null;
        _alpha = null;
        return new DecodedImage(this, buffer, _length, alpha);
    }

    /// <summary>Returns the memory when the image was never built.</summary>
    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        _alpha?.Dispose();
        _alpha = null;
    }
}
