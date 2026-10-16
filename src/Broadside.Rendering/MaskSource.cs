using Broadside.Images;

namespace Broadside.Rendering;

/// <summary>
/// A mask converted to opacity bytes at its own resolution, ready to be resampled to the device: an image mask (stencil or explicit)
/// becomes 255 where it paints and 0 elsewhere, a soft mask its Decode-mapped value times 255.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.6.2 and §8.9.6.3 (image masks: 1 bit per sample, Decode <c>[1 0]</c> reversing them), §11.6.5.2 (a soft-mask
/// image's values, through its own Decode array, are opacities from 0 to 1). A mask is sampled at its own resolution when it is
/// drawn, never resampled to its image's size first (§8.9.6.3: both map onto the unit square).
/// </para>
/// <para>
/// One per rasterizer, reused for every mask: its buffers are pooled and grow to the largest mask seen, so converting allocates
/// nothing once warm. The last mask converted is kept until the next one, across renders (tiles of one list convert it once).
/// </para>
/// </remarks>
internal sealed class MaskSource : IDisposable
{
    private byte[] _alpha = [];
    private ushort[] _raw = [];
    private float[] _values = [];
    private object? _mask;

    /// <summary>Gets the pooled array of opacities, <see cref="Width"/> × <see cref="Height"/>, rows top to bottom, possibly longer.</summary>
    public byte[] Buffer => _alpha;

    /// <summary>Gets the width of the mask converted last.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the height of the mask converted last.</summary>
    public int Height { get; private set; }

    /// <summary>Converts an image mask, unless it is the mask of the last call.</summary>
    /// <param name="mask">The mask; at most <see cref="Array.MaxLength"/> samples.</param>
    public void Prepare(DisplayImageMask mask)
    {
        if (ReferenceEquals(mask, _mask))
        {
            return;
        }

        Pooled.Ensure(ref _alpha, mask.Width * mask.Height);
        for (int y = 0; y < mask.Height; y++)
        {
            StencilRow(mask, y, _alpha.AsSpan(y * mask.Width, mask.Width));
        }

        Remember(mask, mask.Width, mask.Height);
    }

    /// <summary>Converts a soft mask, unless it is the mask of the last call.</summary>
    /// <param name="mask">The soft mask, one component; at most <see cref="Array.MaxLength"/> samples.</param>
    public void Prepare(DisplayImage mask)
    {
        if (ReferenceEquals(mask, _mask))
        {
            return;
        }

        Pooled.Ensure(ref _alpha, mask.Width * mask.Height);
        Pooled.Ensure(ref _raw, mask.Width);
        Pooled.Ensure(ref _values, mask.Width);
        for (int y = 0; y < mask.Height; y++)
        {
            SoftRow(mask, y, _alpha.AsSpan(y * mask.Width, mask.Width), _raw.AsSpan(0, mask.Width), _values.AsSpan(0, mask.Width));
        }

        Remember(mask, mask.Width, mask.Height);
    }

    /// <summary>Writes the opacity of each sample of row <paramref name="y"/> of an image mask: 255 where it paints, 0 elsewhere.</summary>
    /// <remarks>ISO 32000-2 §8.9.6.2 and §8.9.6.3.</remarks>
    public static void StencilRow(DisplayImageMask mask, int y, Span<byte> alpha) =>
        ImageRows.Stencil(mask.GetRow(y), mask.Width, mask.IsInverted, alpha);

    /// <summary>Writes the opacity of each sample of row <paramref name="y"/> of a soft mask, 0 to 255.</summary>
    /// <param name="mask">The soft mask.</param>
    /// <param name="y">The row.</param>
    /// <param name="alpha">One byte per sample.</param>
    /// <param name="raw">Scratch, one value per sample.</param>
    /// <param name="values">Scratch, one value per sample.</param>
    /// <remarks>ISO 32000-2 §11.6.5.2 and Table 143: the samples through the soft mask's Decode array, clamped to 0 to 1.</remarks>
    public static void SoftRow(DisplayImage mask, int y, Span<byte> alpha, Span<ushort> raw, Span<float> values)
    {
        int width = mask.Width;
        ImageDecodeMap map = mask.DecodeMap;
        int top = (1 << map.BitsPerComponent) - 1;
        if (mask.StorageBits <= 8 && map.Map(0, 0) == 0f && map.Map(0, top) == 1f)
        {
            ImageRows.UnpackScaled(mask.GetRow(y), mask.StorageBits, mask.BitsPerComponent, width, alpha);
            return;
        }

        ImageRows.Unpack(mask.GetRow(y), mask.StorageBits, width, raw);
        map.Map(raw[..width], values);
        for (int x = 0; x < width; x++)
        {
            alpha[x] = (byte)((Math.Clamp(values[x], 0f, 1f) * 255f) + 0.5f);
        }
    }

    /// <summary>Forgets the mask converted last, so that nothing it belongs to is kept alive; the buffers stay.</summary>
    public void Forget() => _mask = null;

    /// <summary>Returns the pooled buffers.</summary>
    public void Dispose()
    {
        Forget();
        Pooled.Return(ref _alpha);
        Pooled.Return(ref _raw);
        Pooled.Return(ref _values);
        Width = 0;
        Height = 0;
    }

    private void Remember(object mask, int width, int height)
    {
        _mask = mask;
        Width = width;
        Height = height;
    }
}
