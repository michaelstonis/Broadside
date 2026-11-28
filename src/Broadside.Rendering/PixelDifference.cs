using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Broadside.Rendering;

/// <summary>
/// The per-pixel difference of two rows of 32-bit pixels for <see cref="VisualComparison"/>: for each pixel, the largest absolute
/// difference of its four channel bytes. A scalar method, a <see cref="Vector128{T}"/> method (the vector loop, then the scalar method
/// for the tail) and a dispatcher, as every SIMD kernel in Broadside (the <c>adding-a-benchmark</c> skill, "SIMD kernels").
/// </summary>
/// <remarks>
/// Self-contained (BCL only), so the browser kernel benchmark links it. The vector path uses <c>LoadUnsafe</c>/<c>StoreUnsafe</c>,
/// <c>Min</c>/<c>Max</c>, <c>-</c>, <c>|</c>, the <c>&gt;&gt;&gt;</c> operator and <c>Shuffle</c> with constant masks, which are fast
/// under the JIT, Native AOT and both browser builds.
/// </remarks>
internal static class PixelDifference
{
    /// <summary>Writes, for each pixel of <paramref name="first"/> and <paramref name="second"/>, its largest channel difference.</summary>
    /// <param name="first">The first row, four bytes per pixel; a partial trailing pixel is ignored.</param>
    /// <param name="second">The second row, at least as long as <paramref name="first"/>.</param>
    /// <param name="differences">One byte per pixel.</param>
    public static void MaxChannelDifference(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> differences)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            MaxChannelDifferenceVector(first, second, differences);
        }
        else
        {
            MaxChannelDifferenceScalar(first, second, differences);
        }
    }

    /// <summary>The scalar path: the reference the vector path matches bit for bit.</summary>
    /// <param name="first">The first row.</param>
    /// <param name="second">The second row.</param>
    /// <param name="differences">One byte per pixel.</param>
    public static void MaxChannelDifferenceScalar(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> differences)
    {
        int count = first.Length / 4;
        ArgumentOutOfRangeException.ThrowIfLessThan(second.Length, count * 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(differences.Length, count);
        for (int i = 0; i < count; i++)
        {
            int o = 4 * i;
            int d0 = Math.Abs(first[o] - second[o]);
            int d1 = Math.Abs(first[o + 1] - second[o + 1]);
            int d2 = Math.Abs(first[o + 2] - second[o + 2]);
            int d3 = Math.Abs(first[o + 3] - second[o + 3]);
            differences[i] = (byte)Math.Max(Math.Max(d0, d1), Math.Max(d2, d3));
        }
    }

    /// <summary>The vector path, run whether or not <see cref="Vector128.IsHardwareAccelerated"/> (so tests can reach it).</summary>
    /// <param name="first">The first row.</param>
    /// <param name="second">The second row.</param>
    /// <param name="differences">One byte per pixel.</param>
    public static void MaxChannelDifferenceVector(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> differences)
    {
        int count = first.Length / 4;
        ArgumentOutOfRangeException.ThrowIfLessThan(second.Length, count * 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(differences.Length, count);

        // The loop reads first and second [4i .. 4i + 64) and writes differences [i .. i + 16), inside the lengths checked above.
        ref byte a = ref MemoryMarshal.GetReference(first);
        ref byte b = ref MemoryMarshal.GetReference(second);
        ref byte destination = ref MemoryMarshal.GetReference(differences);
        int i = 0;
        for (; i + 16 <= count; i += 16)
        {
            nuint o = (nuint)(4 * i);
            Vector128<byte> a0 = Vector128.LoadUnsafe(ref a, o), b0 = Vector128.LoadUnsafe(ref b, o);
            Vector128<byte> a1 = Vector128.LoadUnsafe(ref a, o + 16), b1 = Vector128.LoadUnsafe(ref b, o + 16);
            Vector128<byte> a2 = Vector128.LoadUnsafe(ref a, o + 32), b2 = Vector128.LoadUnsafe(ref b, o + 32);
            Vector128<byte> a3 = Vector128.LoadUnsafe(ref a, o + 48), b3 = Vector128.LoadUnsafe(ref b, o + 48);
            Vector128<uint> d0 = (Vector128.Max(a0, b0) - Vector128.Min(a0, b0)).AsUInt32();
            Vector128<uint> d1 = (Vector128.Max(a1, b1) - Vector128.Min(a1, b1)).AsUInt32();
            Vector128<uint> d2 = (Vector128.Max(a2, b2) - Vector128.Min(a2, b2)).AsUInt32();
            Vector128<uint> d3 = (Vector128.Max(a3, b3) - Vector128.Min(a3, b3)).AsUInt32();

            // Folds each pixel's four bytes into its lowest: the larger of bytes (0, 2) and (1, 3), then of the two.
            d0 = Vector128.Max(d0.AsByte(), (d0 >>> 16).AsByte()).AsUInt32();
            d1 = Vector128.Max(d1.AsByte(), (d1 >>> 16).AsByte()).AsUInt32();
            d2 = Vector128.Max(d2.AsByte(), (d2 >>> 16).AsByte()).AsUInt32();
            d3 = Vector128.Max(d3.AsByte(), (d3 >>> 16).AsByte()).AsUInt32();
            d0 = Vector128.Max(d0.AsByte(), (d0 >>> 8).AsByte()).AsUInt32();
            d1 = Vector128.Max(d1.AsByte(), (d1 >>> 8).AsByte()).AsUInt32();
            d2 = Vector128.Max(d2.AsByte(), (d2 >>> 8).AsByte()).AsUInt32();
            d3 = Vector128.Max(d3.AsByte(), (d3 >>> 8).AsByte()).AsUInt32();

            // Gathers the lowest byte of each pixel, four vectors into one (index 255 selects zero).
            Vector128<byte> packed =
                Vector128.Shuffle(d0.AsByte(), Vector128.Create((byte)0, 4, 8, 12, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255))
                | Vector128.Shuffle(d1.AsByte(), Vector128.Create((byte)255, 255, 255, 255, 0, 4, 8, 12, 255, 255, 255, 255, 255, 255, 255, 255))
                | Vector128.Shuffle(d2.AsByte(), Vector128.Create((byte)255, 255, 255, 255, 255, 255, 255, 255, 0, 4, 8, 12, 255, 255, 255, 255))
                | Vector128.Shuffle(d3.AsByte(), Vector128.Create((byte)255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 0, 4, 8, 12));
            packed.StoreUnsafe(ref destination, (nuint)i);
        }

        MaxChannelDifferenceScalar(first[(4 * i)..(4 * count)], second[(4 * i)..], differences[i..]);
    }
}
