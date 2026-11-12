using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Broadside.Filters.Jpx;

/// <summary>
/// The <see cref="Vector128{T}"/> paths of <see cref="JpxLevelShift"/> (issue #503): eight samples per iteration, two vectors of
/// 32-bit lanes packed into one of 16-bit samples, bit-exact with the scalar methods, which finish each row's last zero to seven samples.
/// </summary>
/// <remarks>
/// <para>
/// The 9/7 path rounds without a rounding instruction: NaN lanes are masked to +0 (a NaN is the only value not equal to itself), the
/// values are clamped to <c>[-shift, max - shift]</c> (at most 2^16 in magnitude), and adding 1.5 x 2^23 leaves the sum in
/// [2^23, 2^24), where a float's unit in the last place is 1: the addition itself rounds to the nearest integer with ties to even,
/// IEEE 754's default and the rounding of <see cref="MathF.Round(float)"/>, and that integer is the sum's bits less
/// <c>0x4B400000</c>. Clamped values are integers apart from the fraction rounded, so clamping first matches the scalar method.
/// </para>
/// <para>
/// The 32-bit results (0 to 65,535) are packed with two constant byte <c>Shuffle</c>s and an <c>|</c>, not <c>Narrow</c> from 32-bit
/// lanes (slow in the browser interpreter). Only operations that are fast in every build (adding-a-benchmark skill, "SIMD kernels"):
/// <c>LoadUnsafe</c>/<c>StoreUnsafe</c> bounded by the slices at the top of each method and a loop condition that keeps every access
/// inside them, <c>+</c>, <c>-</c>, <c>&amp;</c>, <c>|</c>, <c>Equals</c>, <c>Min</c>, <c>Max</c> and constant <c>Shuffle</c>s.
/// </para>
/// </remarks>
internal static partial class JpxLevelShift
{
    /// <summary>The bits of 1.5 x 2^23, the float the 9/7 path adds to round.</summary>
    private const int RoundingBits = 0x4B400000;

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="Shift"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="source">The reconstructed samples.</param>
    /// <param name="target">The stored samples.</param>
    /// <param name="shift">The level shift.</param>
    /// <param name="max">The largest sample.</param>
    public static void ShiftVector(ReadOnlySpan<int> source, Span<ushort> target, int shift, int max)
    {
        int n = source.Length;
        target = target[..n];
        ref int s = ref MemoryMarshal.GetReference(source);
        ref ushort t = ref MemoryMarshal.GetReference(target);
        Vector128<int> low = Vector128.Create(-shift);
        Vector128<int> high = Vector128.Create(max - shift);
        Vector128<int> offset = Vector128.Create(shift);
        int i = 0;
        for (; i <= n - 8; i += 8)
        {
            Vector128<int> a = Vector128.Min(Vector128.Max(Vector128.LoadUnsafe(ref s, (nuint)i), low), high) + offset;
            Vector128<int> b = Vector128.Min(Vector128.Max(Vector128.LoadUnsafe(ref s, (nuint)(i + 4)), low), high) + offset;
            Vector128<byte> packed = Vector128.Shuffle(a.AsByte(), Vector128.Create((byte)0, 1, 4, 5, 8, 9, 12, 13, 255, 255, 255, 255, 255, 255, 255, 255))
                | Vector128.Shuffle(b.AsByte(), Vector128.Create((byte)255, 255, 255, 255, 255, 255, 255, 255, 0, 1, 4, 5, 8, 9, 12, 13));
            packed.AsUInt16().StoreUnsafe(ref t, (nuint)i);
        }

        if (i < n)
        {
            ShiftScalar(source[i..], target[i..], shift, max);
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="RoundShift"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="source">The reconstructed values.</param>
    /// <param name="target">The stored samples.</param>
    /// <param name="shift">The level shift.</param>
    /// <param name="max">The largest sample.</param>
    public static void RoundShiftVector(ReadOnlySpan<float> source, Span<ushort> target, int shift, int max)
    {
        int n = source.Length;
        target = target[..n];
        ref float s = ref MemoryMarshal.GetReference(source);
        ref ushort t = ref MemoryMarshal.GetReference(target);
        Vector128<float> low = Vector128.Create((float)-shift);
        Vector128<float> high = Vector128.Create((float)(max - shift));
        Vector128<float> rounding = Vector128.Create(BitConverter.Int32BitsToSingle(RoundingBits));
        Vector128<int> offset = Vector128.Create(RoundingBits - shift);
        int i = 0;
        for (; i <= n - 8; i += 8)
        {
            Vector128<float> x = Vector128.LoadUnsafe(ref s, (nuint)i);
            Vector128<float> y = Vector128.LoadUnsafe(ref s, (nuint)(i + 4));
            x = Vector128.Min(Vector128.Max(x & Vector128.Equals(x, x), low), high);
            y = Vector128.Min(Vector128.Max(y & Vector128.Equals(y, y), low), high);
            Vector128<int> a = (x + rounding).AsInt32() - offset;
            Vector128<int> b = (y + rounding).AsInt32() - offset;
            Vector128<byte> packed = Vector128.Shuffle(a.AsByte(), Vector128.Create((byte)0, 1, 4, 5, 8, 9, 12, 13, 255, 255, 255, 255, 255, 255, 255, 255))
                | Vector128.Shuffle(b.AsByte(), Vector128.Create((byte)255, 255, 255, 255, 255, 255, 255, 255, 0, 1, 4, 5, 8, 9, 12, 13));
            packed.AsUInt16().StoreUnsafe(ref t, (nuint)i);
        }

        if (i < n)
        {
            RoundShiftScalar(source[i..], target[i..], shift, max);
        }
    }
}
