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
/// <c>t = v * a + 128; (t + (t &gt;&gt; 8)) &gt;&gt; 8</c>. It uses only operations that are fast in both browser builds as well as
/// under the JIT and Native AOT (issue #471): <c>LoadUnsafe</c>/<c>StoreUnsafe</c>, <c>&amp;</c>, <c>*</c>, <c>+</c>, the <c>&gt;&gt;&gt;</c>
/// operator (not the <c>ShiftRightLogical</c> method, which the browser AOT build does not compile to a SIMD shift) and
/// <c>Narrow</c> from 16-bit lanes, inlined by hand.
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

        // The loop reads pairs[2i .. 2i + 32) and writes products[i .. i + 16), both inside the lengths checked above.
        ref byte source = ref MemoryMarshal.GetReference(pairs);
        ref byte destination = ref MemoryMarshal.GetReference(products);
        Vector128<ushort> low = Vector128.Create((ushort)0xFF);
        Vector128<ushort> half = Vector128.Create((ushort)128);
        int i = 0;
        for (; i + 16 <= count; i += 16)
        {
            // Little-endian on every target: each 16-bit word is value | alpha << 8.
            Vector128<ushort> first = Vector128.LoadUnsafe(ref source, (nuint)(2 * i)).AsUInt16();
            Vector128<ushort> second = Vector128.LoadUnsafe(ref source, (nuint)((2 * i) + 16)).AsUInt16();
            Vector128<ushort> t1 = ((first & low) * (first >>> 8)) + half;
            Vector128<ushort> t2 = ((second & low) * (second >>> 8)) + half;
            t1 = (t1 + (t1 >>> 8)) >>> 8;
            t2 = (t2 + (t2 >>> 8)) >>> 8;
            Vector128.Narrow(t1, t2).StoreUnsafe(ref destination, (nuint)i);
        }

        MultiplyScalar(pairs[(2 * i)..(2 * count)], products[i..]);
    }
}
