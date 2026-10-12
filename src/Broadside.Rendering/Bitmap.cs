using System.Buffers;

namespace Broadside.Rendering;

/// <summary>
/// Pixels the software rasterizer draws into: premultiplied BGRA, 8 bits per channel, rows top to bottom, in a buffer the caller
/// provides or one rented from a pool and returned on <see cref="Dispose"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each pixel is four bytes in memory order blue, green, red, alpha; colour channels are premultiplied by alpha (each is at most the
/// alpha), the layout of <c>BGRA8888</c> premultiplied surfaces on Windows, Apple platforms, Skia and the HTML canvas after a swizzle.
/// A pixel covers the half-open square of device space from its integer coordinates (ISO 32000-2 §10.7.4).
/// </para>
/// <para>Not thread-safe: one writer at a time. A bitmap over caller memory never frees it; disposing only detaches it.</para>
/// </remarks>
public sealed class Bitmap : IDisposable
{
    private byte[]? _rented;
    private Memory<byte> _pixels;

    /// <summary>Initializes a new instance of the <see cref="Bitmap"/> class over a pooled buffer, every pixel transparent black.</summary>
    /// <param name="width">The width in pixels, at least 1.</param>
    /// <param name="height">The height in pixels, at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive, or the bitmap would exceed the largest array.</exception>
    public Bitmap(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        long length = (long)width * height * 4;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, Array.MaxLength, nameof(width));
        _rented = ArrayPool<byte>.Shared.Rent((int)length);
        _pixels = _rented.AsMemory(0, (int)length);
        _pixels.Span.Clear();
        Width = width;
        Height = height;
        Stride = width * 4;
    }

    /// <summary>Initializes a new instance of the <see cref="Bitmap"/> class over memory the caller owns; its contents are kept.</summary>
    /// <param name="pixels">At least <c>(height − 1) × stride + width × 4</c> bytes.</param>
    /// <param name="width">The width in pixels, at least 1.</param>
    /// <param name="height">The height in pixels, at least 1.</param>
    /// <param name="stride">The bytes from the start of one row to the start of the next, at least <c>width × 4</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive, the stride is too small, or the memory too short.</exception>
    public Bitmap(Memory<byte> pixels, int width, int height, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan((long)stride, (long)width * 4, nameof(stride));
        ArgumentOutOfRangeException.ThrowIfLessThan((long)pixels.Length, ((long)(height - 1) * stride) + ((long)width * 4), nameof(pixels));
        _pixels = pixels;
        Width = width;
        Height = height;
        Stride = stride;
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the bytes from the start of one row to the start of the next.</summary>
    public int Stride { get; }

    /// <summary>Gets every row, <see cref="Stride"/> bytes apart, premultiplied BGRA.</summary>
    /// <exception cref="ObjectDisposedException">The bitmap was disposed.</exception>
    public Span<byte> Pixels
    {
        get
        {
            ObjectDisposedException.ThrowIf(_pixels.IsEmpty, this);
            return _pixels.Span;
        }
    }

    /// <summary>Returns the <see cref="Width"/> × 4 bytes of one row.</summary>
    /// <param name="y">The row, 0 at the top.</param>
    /// <returns>The row's pixels.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is outside the bitmap.</exception>
    public Span<byte> GetRow(int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return Pixels.Slice(y * Stride, Width * 4);
    }

    /// <summary>Writes the bitmap as a PNG image: 8-bit RGB when every pixel is opaque, otherwise 8-bit RGBA with alpha un-premultiplied.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <remarks>
    /// PNG (ISO/IEC 15948, W3C Portable Network Graphics Specification, third edition): IHDR, one zlib-compressed IDAT with an
    /// adaptive filter per row, IEND. A colour channel <c>c</c> of alpha <c>a</c> is written as <c>round(c × 255 / a)</c>, which
    /// premultiplying again with rounding returns exactly.
    /// </remarks>
    public void SavePng(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PngEncoder.Write(this, stream);
    }

    /// <summary>Returns a pooled buffer to its pool. A bitmap over caller memory leaves the memory alone.</summary>
    public void Dispose()
    {
        _pixels = Memory<byte>.Empty;
        if (_rented is { } rented)
        {
            _rented = null;
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
