using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// A soft mask rendered for the software rasterizer: one 8-bit mask value per render pixel over the bounds of the mask's group, and
/// the constant value everywhere else (ISO 32000-2 §11.6.5.1: outside the group's bounding box the mask is the transfer function of
/// 0, or of the backdrop's luminosity).
/// </summary>
/// <remarks>
/// Kept by the rasterizer between paints (keyed on the mask's group list and its transform to the device) and between renders (its
/// buffer reused at its largest size), so a mask used by many paints is rendered once and a warm render allocates nothing.
/// </remarks>
internal sealed class SoftMaskPlane : IDisposable
{
    private byte[] _values = [];

    /// <summary>Gets the left edge of the rendered values, in render pixels.</summary>
    public int Left { get; private set; }

    /// <summary>Gets the top edge of the rendered values, in render pixels.</summary>
    public int Top { get; private set; }

    /// <summary>Gets the width of the rendered values.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the height of the rendered values.</summary>
    public int Height { get; private set; }

    /// <summary>Gets the mask value outside the rendered values.</summary>
    public byte Outside { get; private set; }

    /// <summary>Gets or sets the mask's group list this plane holds, or <see langword="null"/> when the plane is free.</summary>
    public DisplayList? Content { get; set; }

    /// <summary>Gets or sets the transform from mask space to render pixels the plane was rendered with.</summary>
    public Matrix ToDevice { get; set; }

    /// <summary>Gets or sets when the plane was last used, for evicting the least recently used one.</summary>
    public long LastUse { get; set; }

    /// <summary>Gets or sets how many groups being painted composite through the plane (it must not be evicted while they do).</summary>
    public int Pins { get; set; }

    /// <summary>Gets the 256-entry transfer table the values were mapped through.</summary>
    public byte[] Transfer { get; } = new byte[256];

    /// <summary>Starts the plane over a rectangle of render pixels with a value outside it, growing the buffer when it is too small.</summary>
    public void Open(int left, int top, int width, int height, byte outside)
    {
        (Left, Top, Width, Height, Outside) = (left, top, Math.Max(width, 0), Math.Max(height, 0), outside);
        Pooled.Ensure(ref _values, Width * Height);
    }

    /// <summary>Returns the row of values at render row <paramref name="row"/>, which must lie within the plane.</summary>
    public Span<byte> Row(int row) => _values.AsSpan((row - Top) * Width, Width);

    /// <summary>Returns the mask value at a render pixel.</summary>
    public byte At(int row, int x) =>
        row >= Top && row < Top + Height && x >= Left && x < Left + Width ? _values[((row - Top) * Width) + x - Left] : Outside;

    /// <summary>
    /// Returns where the run of equal mask values that starts at render pixel (<paramref name="x"/>, <paramref name="row"/>) ends,
    /// at most <paramref name="end"/>, and the value of the run.
    /// </summary>
    public int Run(int row, int x, int end, out byte value)
    {
        if (row < Top || row >= Top + Height || x >= Left + Width)
        {
            value = Outside;
            return end;
        }

        if (x < Left)
        {
            value = Outside;
            return Math.Min(end, Left);
        }

        int limit = Math.Min(end, Left + Width);
        ReadOnlySpan<byte> line = _values.AsSpan(((row - Top) * Width) + x - Left, limit - x);
        value = line[0];
        int length = line[1..].IndexOfAnyExcept(value);
        return length < 0 ? limit : x + 1 + length;
    }

    /// <summary>Returns the buffer.</summary>
    public void Dispose()
    {
        Content = null;
        Pooled.Return(ref _values);
    }
}
