using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Broadside.Rendering;

/// <summary>
/// The compositing rows of the software rasterizer (issue #473): premultiplied source-over of a row of pixels at one coverage, and of
/// one opaque color over a run at partial coverage, each as a scalar method (the reference), a <see cref="Vector128{T}"/> method that
/// matches it bit for bit, and a dispatcher that takes the vector method where <see cref="Vector128.IsHardwareAccelerated"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §11.3.3 and §11.3.6 in premultiplied form (§11.3.6 NOTE 1) for the Normal blend mode: d = s × c + d × (1 − αs × c),
/// per channel, in integers with exact /255 rounding (<see cref="Divide255"/>). <see cref="Compositor"/> calls these for every paint
/// that composites in Normal.
/// </para>
/// <para>
/// The vector methods work on four pixels (16 bytes) at a time in 16-bit lanes, two pixels per vector after <c>WidenLower</c> and
/// <c>WidenUpper</c>. <see cref="Divide255"/> is exact there because every value it takes is at most 65,152 (65,407 once rounded), and
/// a sum that could pass 65,535 (a source channel above its alpha, which a premultiplied pixel never has but the scalar method clamps)
/// is capped at 65,152 before the division, which then gives 255 as the scalar <c>Min</c> does. At full coverage, the case of a
/// transparency group compositing onto its parent, the source term needs no division: the result is s + d × (255 − a) / 255 with a
/// saturating byte add.
/// </para>
/// <para>
/// Only operations that are fast in every build Broadside runs in (adding-a-benchmark skill, "SIMD kernels"): <c>LoadUnsafe</c> and
/// <c>StoreUnsafe</c> inside bounds the slices at the top prove, the <c>&gt;&gt;&gt;</c> operator (not the <c>ShiftRightLogical</c>
/// method), 16-bit <c>+</c>, <c>-</c>, <c>*</c> and <c>Min</c>, byte <c>~</c>, <c>+</c>, <c>|</c> and <c>LessThan</c>, <c>WidenLower</c>/<c>WidenUpper</c>,
/// <c>Narrow</c> from 16-bit lanes and a constant byte <c>Shuffle</c>. The file is BCL-only, so the Blazor sample's kernel benchmark
/// links it.
/// </para>
/// </remarks>
internal static class CompositingRows
{
    /// <summary>Rounds <paramref name="value"/> / 255 for 0 ≤ value ≤ 2 × 255 × 255.</summary>
    public static int Divide255(int value)
    {
        value += 128;
        return (value + (value >> 8)) >> 8;
    }

    /// <summary>Premultiplied source-over with the source scaled by <paramref name="coverage"/>: d = s·c + d·(1 − a·c).</summary>
    /// <param name="source">The source pixel, premultiplied BGRA.</param>
    /// <param name="destination">The backdrop pixel, premultiplied BGRA.</param>
    /// <param name="coverage">The source's coverage, 0 to 255.</param>
    /// <returns>The result pixel.</returns>
    public static uint Over(uint source, uint destination, int coverage)
    {
        uint alpha = source >> 24;
        if (coverage == 255 && alpha == 255)
        {
            return source;
        }

        if (coverage <= 0)
        {
            return destination;
        }

        int keep = 255 - Divide255((int)alpha * coverage);
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int s = (int)((source >> shift) & 0xFF);
            int d = (int)((destination >> shift) & 0xFF);
            result |= (uint)Math.Min(Divide255((s * coverage) + (d * keep)), 255) << shift;
        }

        return result;
    }

    /// <summary>
    /// Composites each pixel of <paramref name="source"/> over the pixel of <paramref name="target"/> at the same index, at one
    /// <paramref name="coverage"/>: the vector path where <see cref="Vector128"/> is accelerated, else the scalar one.
    /// </summary>
    /// <param name="source">The source pixels, premultiplied BGRA, at least as many as <paramref name="target"/>.</param>
    /// <param name="target">The backdrop pixels, premultiplied BGRA, replaced by the result.</param>
    /// <param name="coverage">The coverage of every source pixel, 0 to 255.</param>
    public static void OverRow(ReadOnlySpan<uint> source, Span<uint> target, int coverage)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            OverRowVector(source, target, coverage);
        }
        else
        {
            OverRowScalar(source, target, coverage);
        }
    }

    /// <summary>The scalar path of <see cref="OverRow"/>: <see cref="Over"/> per pixel, the reference the vector path matches.</summary>
    /// <param name="source">The source pixels.</param>
    /// <param name="target">The backdrop pixels, replaced by the result.</param>
    /// <param name="coverage">The coverage of every source pixel.</param>
    public static void OverRowScalar(ReadOnlySpan<uint> source, Span<uint> target, int coverage)
    {
        source = source[..target.Length];
        for (int i = 0; i < target.Length; i++)
        {
            target[i] = Over(source[i], target[i], coverage);
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="OverRow"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="source">The source pixels.</param>
    /// <param name="target">The backdrop pixels, replaced by the result.</param>
    /// <param name="coverage">The coverage of every source pixel.</param>
    public static void OverRowVector(ReadOnlySpan<uint> source, Span<uint> target, int coverage)
    {
        if (coverage is <= 0 or > 255)
        {
            OverRowScalar(source, target, coverage);
            return;
        }

        int count = target.Length;
        source = source[..count];
        ref byte s = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(source));
        ref byte d = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(target));
        Vector128<ushort> half = Vector128.Create((ushort)128);
        Vector128<byte> lowAlpha = Vector128.Create((byte)3, 255, 3, 255, 3, 255, 3, 255, 7, 255, 7, 255, 7, 255, 7, 255);
        Vector128<byte> highAlpha = Vector128.Create((byte)11, 255, 11, 255, 11, 255, 11, 255, 15, 255, 15, 255, 15, 255, 15, 255);
        int i = 0;

        // Both loops take four pixels a step, reading and writing bytes [4i, 4i + 16) of both rows, inside the count sliced above.
        if (coverage == 255)
        {
            // Full coverage, as a group composites onto its parent: s × 255 / 255 is s exactly, so the result is s + d × (255 − a) / 255,
            // clamped to 255 (the same as the scalar rounding of the whole sum, checked for every s and every product). The sum is
            // taken in bytes, saturated where it wraps (only a source channel above its alpha can wrap it).
            for (; i <= count - 4; i += 4)
            {
                nuint offset = (nuint)(4 * i);
                Vector128<byte> sv = Vector128.LoadUnsafe(ref s, offset);
                Vector128<byte> dv = Vector128.LoadUnsafe(ref d, offset);

                // 255 − a of each pixel in its four 16-bit lanes (index 255 selects zero).
                Vector128<byte> transparency = ~sv;
                Vector128<ushort> t0 = (Vector128.WidenLower(dv) * Vector128.Shuffle(transparency, lowAlpha).AsUInt16()) + half;
                Vector128<ushort> t1 = (Vector128.WidenUpper(dv) * Vector128.Shuffle(transparency, highAlpha).AsUInt16()) + half;
                Vector128<byte> sum = sv + Vector128.Narrow((t0 + (t0 >>> 8)) >>> 8, (t1 + (t1 >>> 8)) >>> 8);
                (sum | Vector128.LessThan(sum, sv)).StoreUnsafe(ref d, offset);
            }
        }
        else
        {
            Vector128<ushort> c = Vector128.Create((ushort)coverage);
            Vector128<ushort> full = Vector128.Create((ushort)255);
            Vector128<ushort> limit = Vector128.Create((ushort)65152);
            for (; i <= count - 4; i += 4)
            {
                nuint offset = (nuint)(4 * i);
                Vector128<byte> sv = Vector128.LoadUnsafe(ref s, offset);
                Vector128<byte> dv = Vector128.LoadUnsafe(ref d, offset);

                // keep = 255 − a × c / 255 for each pixel in its four 16-bit lanes.
                Vector128<ushort> a0 = (Vector128.Shuffle(sv, lowAlpha).AsUInt16() * c) + half;
                Vector128<ushort> a1 = (Vector128.Shuffle(sv, highAlpha).AsUInt16() * c) + half;
                Vector128<ushort> q0 = Vector128.WidenLower(dv) * (full - ((a0 + (a0 >>> 8)) >>> 8));
                Vector128<ushort> q1 = Vector128.WidenUpper(dv) * (full - ((a1 + (a1 >>> 8)) >>> 8));

                // Min(s × c + d × keep, 65,152) without the sum wrapping (both terms are at most 65,025), then /255: 65,152 gives
                // 255, as the scalar Min does for any larger sum (only a source channel above its alpha gives one).
                Vector128<ushort> t0 = Vector128.Min(Vector128.WidenLower(sv) * c, limit - q0) + q0 + half;
                Vector128<ushort> t1 = Vector128.Min(Vector128.WidenUpper(sv) * c, limit - q1) + q1 + half;
                Vector128.Narrow((t0 + (t0 >>> 8)) >>> 8, (t1 + (t1 >>> 8)) >>> 8).StoreUnsafe(ref d, offset);
            }
        }

        OverRowScalar(source[i..], target[i..], coverage);
    }

    /// <summary>
    /// Composites one opaque <paramref name="color"/> over every pixel of <paramref name="target"/> at a partial
    /// <paramref name="coverage"/>: the vector path where <see cref="Vector128"/> is accelerated, else the scalar one.
    /// </summary>
    /// <param name="target">The backdrop pixels, premultiplied BGRA, replaced by the result.</param>
    /// <param name="color">The color, opaque premultiplied BGRA.</param>
    /// <param name="coverage">The coverage, 1 to 254 (0 leaves the row, 255 replaces it: callers handle both without compositing).</param>
    public static void FillRow(Span<uint> target, uint color, int coverage)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            FillRowVector(target, color, coverage);
        }
        else
        {
            FillRowScalar(target, color, coverage);
        }
    }

    /// <summary>The scalar path of <see cref="FillRow"/>: d = s × c + d × (1 − c), per channel, rounded; the reference.</summary>
    /// <param name="target">The backdrop pixels, replaced by the result.</param>
    /// <param name="color">The color.</param>
    /// <param name="coverage">The coverage, 0 to 255.</param>
    public static void FillRowScalar(Span<uint> target, uint color, int coverage)
    {
        int inverse = 255 - coverage;
        Span<byte> bytes = MemoryMarshal.AsBytes(target);
        int s0 = (int)(color & 0xFF) * coverage;
        int s1 = (int)((color >> 8) & 0xFF) * coverage;
        int s2 = (int)((color >> 16) & 0xFF) * coverage;
        int s3 = (int)(color >> 24) * coverage;
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i] = (byte)Divide255(s0 + (bytes[i] * inverse));
            bytes[i + 1] = (byte)Divide255(s1 + (bytes[i + 1] * inverse));
            bytes[i + 2] = (byte)Divide255(s2 + (bytes[i + 2] * inverse));
            bytes[i + 3] = (byte)Divide255(s3 + (bytes[i + 3] * inverse));
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="FillRow"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="target">The backdrop pixels, replaced by the result.</param>
    /// <param name="color">The color.</param>
    /// <param name="coverage">The coverage, 0 to 255.</param>
    public static void FillRowVector(Span<uint> target, uint color, int coverage)
    {
        if (coverage is < 0 or > 255)
        {
            FillRowScalar(target, color, coverage);
            return;
        }

        int count = target.Length;
        ref byte d = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(target));

        // s × c + 128 per channel for two pixels; with d × (255 − c) every sum is at most 255 × 255 + 128, so /255 is exact in 16 bits.
        ushort b = (ushort)(((color & 0xFF) * coverage) + 128);
        ushort g = (ushort)((((color >> 8) & 0xFF) * coverage) + 128);
        ushort r = (ushort)((((color >> 16) & 0xFF) * coverage) + 128);
        ushort a = (ushort)(((color >> 24) * coverage) + 128);
        Vector128<ushort> paint = Vector128.Create(b, g, r, a, b, g, r, a);
        Vector128<ushort> inverse = Vector128.Create((ushort)(255 - coverage));
        int i = 0;

        // Four pixels a step; the loop reads and writes bytes [4i, 4i + 16) of the row, inside its length.
        for (; i <= count - 4; i += 4)
        {
            nuint offset = (nuint)(4 * i);
            Vector128<byte> dv = Vector128.LoadUnsafe(ref d, offset);
            Vector128<ushort> t0 = (Vector128.WidenLower(dv) * inverse) + paint;
            Vector128<ushort> t1 = (Vector128.WidenUpper(dv) * inverse) + paint;
            Vector128.Narrow((t0 + (t0 >>> 8)) >>> 8, (t1 + (t1 >>> 8)) >>> 8).StoreUnsafe(ref d, offset);
        }

        FillRowScalar(target[i..], color, coverage);
    }
}
