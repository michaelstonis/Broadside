using System.Runtime.Intrinsics;

namespace Broadside.Filters.Jpx;

/// <summary>
/// The last step of a JPEG 2000 tile-component row (issue #503): the inverse DC level shift and the clipping to the component's
/// range, stored as 16-bit samples, with the 9/7 path's rounding to integers fused in. Each has a scalar method (the reference), a
/// <see cref="Vector128{T}"/> method that matches it bit for bit, and a dispatcher that takes the vector method where
/// <see cref="Vector128.IsHardwareAccelerated"/>.
/// </summary>
/// <remarks>
/// <para>
/// ITU-T T.800 G.1.2 (equation G-2, the inverse DC level shift) of a component of at most 16 bits, after the inverse transforms of
/// Annex F and G.2 or G.3. A sample becomes <c>clamp(I + shift, 0, max)</c> with <c>shift = 2^(depth - 1)</c> and
/// <c>max = 2^depth - 1</c>, clamped first as <c>clamp(I, -shift, max - shift) + shift</c> so the sum cannot overflow.
/// </para>
/// <para>
/// The 9/7 path reconstructs single-precision values, rounded to the nearest integer with ties to even (<see cref="MathF.Round(float)"/>),
/// NaN to 0, and values beyond the 32-bit range saturated, before the shift. Rounding is monotone and leaves integers unchanged, so
/// clamping to the integer bounds before rounding gives the same sample as rounding first. The source is BCL-only, so the Blazor
/// sample's kernel benchmark links it.
/// </para>
/// </remarks>
internal static partial class JpxLevelShift
{
    /// <summary>
    /// G-2 for a row of integers (the 5/3 path): <c>target[i] = clamp(source[i], -shift, max - shift) + shift</c>; the vector path
    /// where <see cref="Vector128"/> is accelerated.
    /// </summary>
    /// <param name="source">The reconstructed samples; its length is the row's.</param>
    /// <param name="target">The stored samples, at least as long as <paramref name="source"/>.</param>
    /// <param name="shift">The level shift, <c>2^(depth - 1)</c>, depth 1 to 16.</param>
    /// <param name="max">The largest sample, <c>2^depth - 1</c>.</param>
    public static void Shift(ReadOnlySpan<int> source, Span<ushort> target, int shift, int max)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            ShiftVector(source, target, shift, max);
        }
        else
        {
            ShiftScalar(source, target, shift, max);
        }
    }

    /// <summary>The scalar <see cref="Shift"/>: the reference.</summary>
    /// <param name="source">The reconstructed samples.</param>
    /// <param name="target">The stored samples.</param>
    /// <param name="shift">The level shift.</param>
    /// <param name="max">The largest sample.</param>
    public static void ShiftScalar(ReadOnlySpan<int> source, Span<ushort> target, int shift, int max)
    {
        target = target[..source.Length];
        int low = -shift;
        int high = max - shift;
        for (int i = 0; i < source.Length; i++)
        {
            target[i] = (ushort)(Math.Clamp(source[i], low, high) + shift);
        }
    }

    /// <summary>
    /// G-2 for a row of single-precision values (the 9/7 path), rounded to integers first: <c>target[i] = round(clamp(source[i],
    /// -shift, max - shift)) + shift</c>, NaN as 0; the vector path where <see cref="Vector128"/> is accelerated.
    /// </summary>
    /// <param name="source">The reconstructed values; its length is the row's.</param>
    /// <param name="target">The stored samples, at least as long as <paramref name="source"/>.</param>
    /// <param name="shift">The level shift, <c>2^(depth - 1)</c>, depth 1 to 16.</param>
    /// <param name="max">The largest sample, <c>2^depth - 1</c>.</param>
    public static void RoundShift(ReadOnlySpan<float> source, Span<ushort> target, int shift, int max)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            RoundShiftVector(source, target, shift, max);
        }
        else
        {
            RoundShiftScalar(source, target, shift, max);
        }
    }

    /// <summary>The scalar <see cref="RoundShift"/>: the reference.</summary>
    /// <param name="source">The reconstructed values.</param>
    /// <param name="target">The stored samples.</param>
    /// <param name="shift">The level shift.</param>
    /// <param name="max">The largest sample.</param>
    public static void RoundShiftScalar(ReadOnlySpan<float> source, Span<ushort> target, int shift, int max)
    {
        target = target[..source.Length];
        float low = -shift;
        float high = max - shift;
        for (int i = 0; i < source.Length; i++)
        {
            float value = source[i];
            int rounded = float.IsNaN(value) ? 0 : (int)MathF.Round(Math.Clamp(value, low, high));
            target[i] = (ushort)(rounded + shift);
        }
    }

    /// <summary>
    /// Rounds single-precision values to integers in place, for the components deeper than 16 bits: ties to even, NaN to 0, values
    /// beyond the 32-bit range saturated.
    /// </summary>
    /// <param name="values">The values, read as floats and written back as integers.</param>
    public static void RoundToIntegers(Span<int> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            float value = BitConverter.Int32BitsToSingle(values[i]);
            values[i] = value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : float.IsNaN(value) ? 0 : (int)MathF.Round(value);
        }
    }
}
