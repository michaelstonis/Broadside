namespace Broadside.Images;

/// <summary>Sizes of the §8.9.3 sample layout, computed without overflow.</summary>
internal static class ImageGeometry
{
    /// <summary>The most components a sample may have (a DeviceN space's limit, Annex C).</summary>
    public const int MaxComponents = 32;

    /// <summary>Returns the storage width for a logical depth: the depth itself for 1, 2, 4, 8 and 16; 8 for 3, 5, 6 and 7; else 16.</summary>
    public static int StorageBits(int bitsPerComponent) => bitsPerComponent switch
    {
        1 or 2 or 4 or 8 or 16 => bitsPerComponent,
        < 8 => 8,
        _ => 16,
    };

    /// <summary>Returns ⌈width × components × bits / 8⌉.</summary>
    public static long Stride(long width, int components, int bits) => ((width * components * bits) + 7) >> 3;

    /// <summary>
    /// Checks an image's size against the limits: pixels, bytes at the storage depth, and the largest array .NET can allocate.
    /// </summary>
    /// <returns><see langword="null"/> when within the limits, else why not.</returns>
    public static string? CheckLimits(int width, int height, int components, int bitsPerComponent, long maxPixels, long maxBytes)
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
}
