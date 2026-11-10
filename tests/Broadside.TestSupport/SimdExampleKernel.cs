using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Broadside.TestSupport;

/// <summary>
/// A test-only <see cref="Vector128{T}"/> kernel in the shape every SIMD kernel in Broadside follows (issue #470): a scalar method, a
/// vector method that is the <see cref="Vector128{T}"/> loop plus the scalar method for the tail, and a dispatcher that picks the
/// vector method only when <see cref="Vector128.IsHardwareAccelerated"/>. The tests compare the vector method with the scalar one
/// through <c>SimdKernelAssert</c>, and the browser kernel benchmark (the Blazor sample's <c>KernelBenchmarks</c>, which links
/// this file) times both in the WebAssembly interpreter and the AOT build. It is not shipped.
/// </summary>
/// <remarks>
/// The kernel multiplies interleaved (value, alpha) byte pairs and rounds to the nearest byte, as premultiplication does:
/// <c>t = v * a + 128; (t + (t &gt;&gt; 8)) &gt;&gt; 8</c>. It uses only operations the WebAssembly interpreter maps to SIMD opcodes:
/// <c>Create</c>/<c>CopyTo</c>, <c>&amp;</c>, <c>*</c>, <c>+</c>, logical shifts and <c>Narrow</c> from 16-bit lanes, inlined by hand.
/// </remarks>
public static class SimdExampleKernel
{
    /// <summary>Multiplies each (value, alpha) pair in <paramref name="pairs"/> into one byte of <paramref name="products"/>.</summary>
    /// <param name="pairs">Interleaved value and alpha bytes; a trailing odd byte is ignored.</param>
    /// <param name="products">One byte per pair.</param>
    public static void Multiply(ReadOnlySpan<byte> pairs, Span<byte> products)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            MultiplyVector(pairs, products);
        }
        else
        {
            MultiplyScalar(pairs, products);
        }
    }

    /// <summary>The scalar path: the reference the vector path must match bit for bit.</summary>
    /// <param name="pairs">Interleaved value and alpha bytes.</param>
    /// <param name="products">One byte per pair.</param>
    public static void MultiplyScalar(ReadOnlySpan<byte> pairs, Span<byte> products)
    {
        int count = pairs.Length / 2;
        ArgumentOutOfRangeException.ThrowIfLessThan(products.Length, count);
        for (int i = 0; i < count; i++)
        {
            int t = (pairs[2 * i] * pairs[(2 * i) + 1]) + 128;
            products[i] = (byte)((t + (t >> 8)) >> 8);
        }
    }

    /// <summary>The vector path, run whether or not <see cref="Vector128.IsHardwareAccelerated"/> (so tests can reach it).</summary>
    /// <param name="pairs">Interleaved value and alpha bytes.</param>
    /// <param name="products">One byte per pair.</param>
    public static void MultiplyVector(ReadOnlySpan<byte> pairs, Span<byte> products)
    {
        int count = pairs.Length / 2;
        ArgumentOutOfRangeException.ThrowIfLessThan(products.Length, count);

        // Little-endian on every target: each 16-bit word is value | alpha << 8.
        ReadOnlySpan<ushort> words = MemoryMarshal.Cast<byte, ushort>(pairs[..(2 * count)]);
        Vector128<ushort> low = Vector128.Create((ushort)0xFF);
        Vector128<ushort> half = Vector128.Create((ushort)128);
        int i = 0;
        for (; i + 16 <= count; i += 16)
        {
            Vector128<ushort> first = Vector128.Create(words.Slice(i, 8));
            Vector128<ushort> second = Vector128.Create(words.Slice(i + 8, 8));
            Vector128<ushort> t1 = ((first & low) * Vector128.ShiftRightLogical(first, 8)) + half;
            Vector128<ushort> t2 = ((second & low) * Vector128.ShiftRightLogical(second, 8)) + half;
            t1 = Vector128.ShiftRightLogical(t1 + Vector128.ShiftRightLogical(t1, 8), 8);
            t2 = Vector128.ShiftRightLogical(t2 + Vector128.ShiftRightLogical(t2, 8), 8);
            Vector128.Narrow(t1, t2).CopyTo(products.Slice(i, 16));
        }

        MultiplyScalar(pairs[(2 * i)..(2 * count)], products[i..]);
    }
}
