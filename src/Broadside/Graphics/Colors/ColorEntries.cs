using Broadside.Objects;

namespace Broadside.Graphics.Colors;

/// <summary>Reads the numeric entries of colour space dictionaries (ISO 32000-2 Tables 62 to 72) as they are now.</summary>
internal static class ColorEntries
{
    /// <summary>Reads an array of exactly <paramref name="destination"/>'s length of finite numbers, without allocating.</summary>
    /// <param name="cache">The document's colour spaces, for resolving.</param>
    /// <param name="dictionary">The dictionary, or <see langword="null"/>.</param>
    /// <param name="key">The key.</param>
    /// <param name="destination">Receives the numbers.</param>
    /// <returns>Whether the entry is such an array.</returns>
    public static bool TryReadNumbers(ColorSpaceCache cache, CosDictionary? dictionary, CosName key, Span<double> destination)
    {
        if (dictionary is null || !dictionary.TryGetValue(key, out CosObject? value) || cache.Resolve(value) is not CosArray array || array.Count != destination.Length)
        {
            return false;
        }

        for (int i = 0; i < destination.Length; i++)
        {
            if (cache.Resolve(array[i]) is not CosNumber number || !double.IsFinite(number.ToDouble()))
            {
                return false;
            }

            destination[i] = number.ToDouble();
        }

        return true;
    }

    /// <summary>Reads an array of numbers.</summary>
    /// <param name="cache">The document's colour spaces, for resolving.</param>
    /// <param name="dictionary">The dictionary, or <see langword="null"/>.</param>
    /// <param name="key">The key.</param>
    /// <returns>The numbers; <see langword="null"/> when absent, not an array or holding something that is not a finite number.</returns>
    public static double[]? Numbers(ColorSpaceCache cache, CosDictionary? dictionary, CosName key) =>
        dictionary is not null && dictionary.TryGetValue(key, out CosObject? value) ? Numbers(cache, value) : null;

    /// <summary>Reads an array of numbers.</summary>
    /// <param name="cache">The document's colour spaces, for resolving.</param>
    /// <param name="value">The value.</param>
    /// <returns>The numbers; <see langword="null"/> when not an array of finite numbers.</returns>
    public static double[]? Numbers(ColorSpaceCache cache, CosObject? value)
    {
        if (cache.Resolve(value) is not CosArray array)
        {
            return null;
        }

        double[] numbers = new double[array.Count];
        for (int i = 0; i < numbers.Length; i++)
        {
            if (cache.Resolve(array[i]) is not CosNumber number || !double.IsFinite(number.ToDouble()))
            {
                return null;
            }

            numbers[i] = number.ToDouble();
        }

        return numbers;
    }

    /// <summary>Reads a number.</summary>
    /// <param name="cache">The document's colour spaces, for resolving.</param>
    /// <param name="value">The value.</param>
    /// <returns>The number; <see langword="null"/> when not a finite number.</returns>
    public static double? Number(ColorSpaceCache cache, CosObject? value) =>
        cache.Resolve(value) is CosNumber number && double.IsFinite(number.ToDouble()) ? number.ToDouble() : null;

    /// <summary>Reads a number entry.</summary>
    /// <param name="cache">The document's colour spaces, for resolving.</param>
    /// <param name="dictionary">The dictionary, or <see langword="null"/>.</param>
    /// <param name="key">The key.</param>
    /// <returns>The number; <see langword="null"/> when absent or not a finite number.</returns>
    public static double? Number(ColorSpaceCache cache, CosDictionary? dictionary, CosName key) =>
        dictionary is not null && dictionary.TryGetValue(key, out CosObject? value) ? Number(cache, value) : null;

    /// <summary>Reads a white point: three numbers, X and Z positive, Y 1.</summary>
    /// <param name="cache">The document's colour spaces.</param>
    /// <param name="dictionary">The CIE dictionary.</param>
    /// <returns>The white point; <see langword="null"/> when absent or invalid.</returns>
    /// <remarks>ISO 32000-2 Tables 62 to 64: "XW and ZW shall be positive, and YW shall be equal to 1.0".</remarks>
    public static CieXyz? WhitePoint(ColorSpaceCache cache, CosDictionary? dictionary) =>
        Numbers(cache, dictionary, ColorSpaceNames.WhitePoint) is [double x, double y, double z] && x > 0 && z > 0 && Math.Abs(y - 1) < 1e-3
            ? new CieXyz(x, 1, z)
            : null;

    /// <summary>Reads a black point: three non-negative numbers.</summary>
    /// <param name="cache">The document's colour spaces.</param>
    /// <param name="dictionary">The CIE dictionary.</param>
    /// <param name="present">Whether the entry is there at all.</param>
    /// <returns>The black point; <see langword="null"/> when absent or invalid.</returns>
    /// <remarks>ISO 32000-2 Tables 62 to 64: default [0 0 0]; "all three of these numbers shall be non-negative".</remarks>
    public static CieXyz? BlackPoint(ColorSpaceCache cache, CosDictionary? dictionary, out bool present)
    {
        present = dictionary is not null && dictionary.ContainsKey(ColorSpaceNames.BlackPoint);
        return Numbers(cache, dictionary, ColorSpaceNames.BlackPoint) is [double x, double y, double z] && x >= 0 && y >= 0 && z >= 0
            ? new CieXyz(x, y, z)
            : null;
    }
}
