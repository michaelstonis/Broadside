namespace Broadside.Images;

/// <summary>An image's geometry: how many samples it has across and down, how many components each sample has, and how many bits each component takes.</summary>
/// <param name="Width">The number of samples in each row.</param>
/// <param name="Height">The number of rows.</param>
/// <param name="Components">The number of components in each sample.</param>
/// <param name="BitsPerComponent">The logical bits per component: the precision, not the storage width (see <see cref="StorageBits(int)"/>).</param>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.3 (sample representation) and §8.9.5.1, Table 87 (<c>Width</c>, <c>Height</c>, <c>BitsPerComponent</c>; the
/// components come from the <c>ColorSpace</c>). The one value that travels through <see cref="Filters.ImageHeader"/>,
/// <see cref="Filters.ImageFilterContext"/>, <see cref="DecodedImage"/> and <see cref="DecodedImageBuilder"/> and the display
/// list's image, wherever the four numbers used to travel apart.
/// </para>
/// <para>
/// Which of the four a value holds depends on where it came from: a context's geometry is the image dictionary's as read, 0 where an
/// entry is absent; a decoded image's is what was delivered, complete.
/// </para>
/// </remarks>
public readonly record struct ImageGeometry(int Width, int Height, int Components, int BitsPerComponent)
{
    /// <summary>The most components a sample may have (a DeviceN space's limit, Annex C).</summary>
    internal const int MaxComponents = 32;

    /// <summary>The largest reduction a decode may be asked for, as a power of two: 1/2^15.</summary>
    internal const int MaxReduction = 15;

    /// <summary>Returns the geometry of this image reduced by 2^<paramref name="reduction"/>: ⌈Width / 2^n⌉ × ⌈Height / 2^n⌉, the components and depth unchanged.</summary>
    /// <param name="reduction">The reduction, as a power of two, 0 to 15.</param>
    /// <returns>The reduced geometry.</returns>
    /// <remarks>ISO 32000-2 §8.9.5 (the samples may be fewer than the image has; each stands for the source samples it covers).</remarks>
    public ImageGeometry AtReduction(int reduction) => this with { Width = Reduce(Width, reduction), Height = Reduce(Height, reduction) };

    /// <summary>Returns the number of samples a reduction by 2^<paramref name="reduction"/> leaves of <paramref name="size"/>: ⌈size / 2^reduction⌉.</summary>
    internal static int Reduce(int size, int reduction) => (int)(((long)size + (1L << reduction) - 1) >> reduction);

    /// <summary>Returns the storage width for a logical depth: the depth itself for 1, 2, 4, 8 and 16; 8 for 3, 5, 6 and 7; else 16.</summary>
    internal static int StorageBits(int bitsPerComponent) => bitsPerComponent switch
    {
        1 or 2 or 4 or 8 or 16 => bitsPerComponent,
        < 8 => 8,
        _ => 16,
    };

    /// <summary>Returns ⌈width × components × bits / 8⌉.</summary>
    internal static long Stride(long width, int components, int bits) => ((width * components * bits) + 7) >> 3;

    /// <summary>
    /// Checks an image's size against the limits: pixels, bytes at the storage depth, and the largest array .NET can allocate.
    /// </summary>
    /// <returns><see langword="null"/> when within the limits, else why not.</returns>
    internal static string? CheckLimits(int width, int height, int components, int bitsPerComponent, long maxPixels, long maxBytes)
    {
        long pixels = (long)width * height;
        if (pixels > maxPixels)
        {
            return FormattableString.Invariant($"{width} x {height} = {pixels} pixels is more than the limit of {maxPixels}");
        }

        long stride = Stride(width, components, StorageBits(bitsPerComponent));
        Int128 bytes = (Int128)stride * height;
        if (bytes > maxBytes || bytes > Array.MaxLength || stride > int.MaxValue)
        {
            return FormattableString.Invariant($"{bytes} bytes of samples is more than the limit of {Math.Min(maxBytes, Array.MaxLength)}");
        }

        return null;
    }

    /// <summary>Checks this geometry against the limits; see <see cref="CheckLimits(int, int, int, int, long, long)"/>.</summary>
    internal string? CheckLimits(long maxPixels, long maxBytes) => CheckLimits(Width, Height, Components, BitsPerComponent, maxPixels, maxBytes);
}
