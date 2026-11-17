using System.Runtime.CompilerServices;
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

    /// <summary>Rounds <paramref name="difference"/> × <paramref name="amount"/> / 255 for a difference of either sign.</summary>
    /// <param name="difference">The difference, −255 to 255.</param>
    /// <param name="amount">0 to 255.</param>
    /// <returns>The scaled difference.</returns>
    public static int Scaled(int difference, int amount) =>
        difference >= 0 ? Divide255(difference * amount) : -Divide255(-difference * amount);

    /// <summary>
    /// Mixes two premultiplied pixels: <paramref name="from"/> + (<paramref name="to"/> − <paramref name="from"/>) × amount / 255,
    /// per channel, rounded.
    /// </summary>
    /// <param name="from">The pixel at amount 0.</param>
    /// <param name="to">The pixel at amount 255.</param>
    /// <param name="amount">0 to 255.</param>
    /// <returns>The mixed pixel.</returns>
    public static uint Mix(uint from, uint to, int amount)
    {
        if (amount >= 255 || from == to)
        {
            return to;
        }

        if (amount <= 0)
        {
            return from;
        }

        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int f = (int)((from >> shift) & 0xFF);
            int t = (int)((to >> shift) & 0xFF);
            result |= (uint)Math.Clamp(f + Scaled(t - f, amount), 0, 255) << shift;
        }

        return result;
    }

    /// <summary>Replaces each pixel of <paramref name="target"/> by its <see cref="Mix"/> with the pixel of <paramref name="to"/> at the same index.</summary>
    /// <param name="target">The pixels at amount 0, replaced by the result.</param>
    /// <param name="to">The pixels at amount 255, at least as many as <paramref name="target"/>.</param>
    /// <param name="amount">0 to 255, one amount for the row.</param>
    public static void MixRow(Span<uint> target, ReadOnlySpan<uint> to, int amount)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            MixRowVector(target, to, amount);
        }
        else
        {
            MixRowScalar(target, to, amount);
        }
    }

    /// <summary>The scalar path of <see cref="MixRow"/>: <see cref="Mix"/> per pixel, the reference the vector path matches.</summary>
    /// <param name="target">The pixels at amount 0, replaced by the result.</param>
    /// <param name="to">The pixels at amount 255.</param>
    /// <param name="amount">The amount.</param>
    public static void MixRowScalar(Span<uint> target, ReadOnlySpan<uint> to, int amount)
    {
        to = to[..target.Length];
        for (int i = 0; i < target.Length; i++)
        {
            target[i] = Mix(target[i], to[i], amount);
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="MixRow"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="target">The pixels at amount 0, replaced by the result.</param>
    /// <param name="to">The pixels at amount 255.</param>
    /// <param name="amount">The amount.</param>
    public static void MixRowVector(Span<uint> target, ReadOnlySpan<uint> to, int amount)
    {
        if (amount is <= 0 or >= 255)
        {
            MixRowScalar(target, to, amount);
            return;
        }

        int count = target.Length;
        to = to[..count];
        ref byte f = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(target));
        ref byte t = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(to));
        Vector128<ushort> a = Vector128.Create((ushort)amount);
        Vector128<ushort> half = Vector128.Create((ushort)128);
        int i = 0;

        // Four pixels a step, reading and writing bytes [4i, 4i + 16) of both rows, inside the count sliced above.
        for (; i <= count - 4; i += 4)
        {
            nuint offset = (nuint)(4 * i);
            Vector128<byte> fv = Vector128.LoadUnsafe(ref f, offset);
            Vector128<byte> tv = Vector128.LoadUnsafe(ref t, offset);

            // |t − f| × amount / 255 per channel, rounded (at most 255 × 254 + 128 + its high byte: 16 bits), added to f where t is
            // above it and subtracted where it is below (the result lies between f and t, so neither wraps).
            Vector128<byte> difference = Vector128.Max(fv, tv) - Vector128.Min(fv, tv);
            Vector128<ushort> p0 = (Vector128.WidenLower(difference) * a) + half;
            Vector128<ushort> p1 = (Vector128.WidenUpper(difference) * a) + half;
            Vector128<byte> step = Vector128.Narrow((p0 + (p0 >>> 8)) >>> 8, (p1 + (p1 >>> 8)) >>> 8);
            Vector128<byte> below = Vector128.LessThan(tv, fv);
            ((~below & (fv + step)) | (below & (fv - step))).StoreUnsafe(ref f, offset);
        }

        MixRowScalar(target[i..], to[i..], amount);
    }

    /// <summary>
    /// The knockout of one paint in a knockout group, premultiplied: (1 − shape) × <paramref name="previous"/> + <paramref name="composited"/> −
    /// (1 − shape) × <paramref name="backdrop"/>, per channel, with the colors held to the result's alpha.
    /// </summary>
    /// <param name="previous">What the group held at the pixel.</param>
    /// <param name="composited">The paint composited over <paramref name="backdrop"/>.</param>
    /// <param name="backdrop">The group's initial backdrop at the pixel.</param>
    /// <param name="shape">The paint's shape, 0 to 255.</param>
    /// <returns>The pixel.</returns>
    public static uint KnockOut(uint previous, uint composited, uint backdrop, int shape)
    {
        if (shape >= 255)
        {
            return composited;
        }

        if (shape <= 0 || previous == backdrop)
        {
            return shape <= 0 ? previous : composited;
        }

        int keep = 255 - shape;
        int alpha = Math.Clamp((int)(composited >> 24) + Scaled((int)(previous >> 24) - (int)(backdrop >> 24), keep), 0, 255);
        uint result = (uint)alpha << 24;
        for (int shift = 0; shift < 24; shift += 8)
        {
            int c = (int)((composited >> shift) & 0xFF) + Scaled((int)((previous >> shift) & 0xFF) - (int)((backdrop >> shift) & 0xFF), keep);
            result |= (uint)Math.Clamp(c, 0, alpha) << shift;
        }

        return result;
    }

    /// <summary>Replaces each pixel of <paramref name="composited"/> by its <see cref="KnockOut"/> against the pixels of <paramref name="previous"/> and <paramref name="backdrop"/> at the same index.</summary>
    /// <param name="previous">What the group held, at least as many pixels as <paramref name="composited"/>.</param>
    /// <param name="composited">The paint composited over the backdrop, replaced by the result.</param>
    /// <param name="backdrop">The group's initial backdrop, at least as many pixels as <paramref name="composited"/>.</param>
    /// <param name="shape">The paint's shape over the row, 0 to 255.</param>
    public static void KnockOutRow(ReadOnlySpan<uint> previous, Span<uint> composited, ReadOnlySpan<uint> backdrop, int shape)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            KnockOutRowVector(previous, composited, backdrop, shape);
        }
        else
        {
            KnockOutRowScalar(previous, composited, backdrop, shape);
        }
    }

    /// <summary>The scalar path of <see cref="KnockOutRow"/>: <see cref="KnockOut"/> per pixel, the reference the vector path matches.</summary>
    /// <param name="previous">What the group held.</param>
    /// <param name="composited">The paint composited over the backdrop, replaced by the result.</param>
    /// <param name="backdrop">The group's initial backdrop.</param>
    /// <param name="shape">The shape.</param>
    public static void KnockOutRowScalar(ReadOnlySpan<uint> previous, Span<uint> composited, ReadOnlySpan<uint> backdrop, int shape)
    {
        previous = previous[..composited.Length];
        backdrop = backdrop[..composited.Length];
        for (int i = 0; i < composited.Length; i++)
        {
            composited[i] = KnockOut(previous[i], composited[i], backdrop[i], shape);
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="KnockOutRow"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="previous">What the group held.</param>
    /// <param name="composited">The paint composited over the backdrop, replaced by the result.</param>
    /// <param name="backdrop">The group's initial backdrop.</param>
    /// <param name="shape">The shape.</param>
    public static void KnockOutRowVector(ReadOnlySpan<uint> previous, Span<uint> composited, ReadOnlySpan<uint> backdrop, int shape)
    {
        if (shape is <= 0 or >= 255)
        {
            KnockOutRowScalar(previous, composited, backdrop, shape);
            return;
        }

        int count = composited.Length;
        previous = previous[..count];
        backdrop = backdrop[..count];
        ref byte p = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(previous));
        ref byte c = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(composited));
        ref byte b = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(backdrop));
        Vector128<ushort> keep = Vector128.Create((ushort)(255 - shape));
        Vector128<ushort> half = Vector128.Create((ushort)128);
        Vector128<byte> alphaOfPixel = Vector128.Create((byte)3, 3, 3, 3, 7, 7, 7, 7, 11, 11, 11, 11, 15, 15, 15, 15);
        int i = 0;

        // Four pixels a step, reading bytes [4i, 4i + 16) of the three rows and writing them to the composited one.
        for (; i <= count - 4; i += 4)
        {
            nuint offset = (nuint)(4 * i);
            Vector128<byte> pv = Vector128.LoadUnsafe(ref p, offset);
            Vector128<byte> cv = Vector128.LoadUnsafe(ref c, offset);
            Vector128<byte> bv = Vector128.LoadUnsafe(ref b, offset);

            // Per channel, c + (p − b) × (255 − shape) / 255 rounded: the step is |p − b| × keep / 255, added where p is at least b
            // (saturating at 255, which the alpha cap below lowers anyway) and subtracted, stopping at 0, where it is below.
            Vector128<byte> difference = Vector128.Max(pv, bv) - Vector128.Min(pv, bv);
            Vector128<ushort> q0 = (Vector128.WidenLower(difference) * keep) + half;
            Vector128<ushort> q1 = (Vector128.WidenUpper(difference) * keep) + half;
            Vector128<byte> step = Vector128.Narrow((q0 + (q0 >>> 8)) >>> 8, (q1 + (q1 >>> 8)) >>> 8);
            Vector128<byte> below = Vector128.LessThan(pv, bv);
            Vector128<byte> up = cv + step;
            up |= Vector128.LessThan(up, cv);
            Vector128<byte> sum = (~below & up) | (below & (Vector128.Max(cv, step) - step));

            // The colors may not pass the pixel's own (new) alpha, which is the alpha lane of the same sum.
            Vector128<byte> result = Vector128.Min(sum, Vector128.Shuffle(sum, alphaOfPixel));

            // A backdrop equal to what the group held leaves the composited pixel as it is, even a color above its alpha.
            Vector128<byte> same = Vector128.Equals(pv.AsUInt32(), bv.AsUInt32()).AsByte();
            ((same & cv) | (~same & result)).StoreUnsafe(ref c, offset);
        }

        KnockOutRowScalar(previous[i..], composited[i..], backdrop[i..], shape);
    }

    /// <summary>
    /// Removes the backdrop from a non-isolated group's complete result, premultiplied: the alpha becomes <paramref name="groupAlpha"/> and
    /// each color is <paramref name="complete"/> − <paramref name="backdrop"/> × (1 − groupAlpha), held between 0 and the alpha.
    /// </summary>
    /// <param name="complete">The group's complete result at the pixel.</param>
    /// <param name="backdrop">The group's initial backdrop.</param>
    /// <param name="groupAlpha">The group alpha, 0 to 255.</param>
    /// <returns>The group's color and alpha.</returns>
    public static uint RemoveBackdrop(uint complete, uint backdrop, int groupAlpha)
    {
        if (groupAlpha <= 0)
        {
            return 0;
        }

        int keep = 255 - groupAlpha;
        uint result = (uint)groupAlpha << 24;
        for (int shift = 0; shift < 24; shift += 8)
        {
            int c = (int)((complete >> shift) & 0xFF) - Divide255((int)((backdrop >> shift) & 0xFF) * keep);
            result |= (uint)Math.Clamp(c, 0, groupAlpha) << shift;
        }

        return result;
    }

    /// <summary>Sets each pixel of <paramref name="destination"/> to the <see cref="RemoveBackdrop"/> of the pixels of <paramref name="complete"/> and <paramref name="backdrop"/> at the same index and the group alpha at that index.</summary>
    /// <param name="complete">The group's complete result, at least as many pixels as <paramref name="destination"/>.</param>
    /// <param name="backdrop">The group's initial backdrop, at least as many pixels as <paramref name="destination"/>.</param>
    /// <param name="groupAlpha">The group alpha of each pixel, at least as many as <paramref name="destination"/>.</param>
    /// <param name="destination">The result.</param>
    public static void RemoveBackdropRow(ReadOnlySpan<uint> complete, ReadOnlySpan<uint> backdrop, ReadOnlySpan<byte> groupAlpha, Span<uint> destination)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            RemoveBackdropRowVector(complete, backdrop, groupAlpha, destination);
        }
        else
        {
            RemoveBackdropRowScalar(complete, backdrop, groupAlpha, destination);
        }
    }

    /// <summary>The scalar path of <see cref="RemoveBackdropRow"/>: <see cref="RemoveBackdrop"/> per pixel, the reference the vector path matches.</summary>
    /// <param name="complete">The group's complete result.</param>
    /// <param name="backdrop">The group's initial backdrop.</param>
    /// <param name="groupAlpha">The group alpha of each pixel.</param>
    /// <param name="destination">The result.</param>
    public static void RemoveBackdropRowScalar(ReadOnlySpan<uint> complete, ReadOnlySpan<uint> backdrop, ReadOnlySpan<byte> groupAlpha, Span<uint> destination)
    {
        complete = complete[..destination.Length];
        backdrop = backdrop[..destination.Length];
        groupAlpha = groupAlpha[..destination.Length];
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = RemoveBackdrop(complete[i], backdrop[i], groupAlpha[i]);
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="RemoveBackdropRow"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="complete">The group's complete result.</param>
    /// <param name="backdrop">The group's initial backdrop.</param>
    /// <param name="groupAlpha">The group alpha of each pixel.</param>
    /// <param name="destination">The result.</param>
    public static void RemoveBackdropRowVector(ReadOnlySpan<uint> complete, ReadOnlySpan<uint> backdrop, ReadOnlySpan<byte> groupAlpha, Span<uint> destination)
    {
        int count = destination.Length;
        complete = complete[..count];
        backdrop = backdrop[..count];
        groupAlpha = groupAlpha[..count];
        ref byte c = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(complete));
        ref byte b = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(backdrop));
        ref byte g = ref MemoryMarshal.GetReference(groupAlpha);
        ref byte d = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(destination));
        Vector128<ushort> half = Vector128.Create((ushort)128);
        Vector128<ushort> full = Vector128.Create((ushort)255);
        Vector128<ushort> alphaLane = Vector128.Create((ushort)0, 0, 0, 0xFFFF, 0, 0, 0, 0xFFFF);
        Vector128<byte> lowAlpha = Vector128.Create((byte)0, 255, 0, 255, 0, 255, 0, 255, 1, 255, 1, 255, 1, 255, 1, 255);
        Vector128<byte> highAlpha = Vector128.Create((byte)2, 255, 2, 255, 2, 255, 2, 255, 3, 255, 3, 255, 3, 255, 3, 255);
        int i = 0;

        // Four pixels a step: bytes [4i, 4i + 16) of the three pixel rows and [i, i + 4) of the alpha row, inside the count sliced above.
        for (; i <= count - 4; i += 4)
        {
            nuint offset = (nuint)(4 * i);
            Vector128<byte> cv = Vector128.LoadUnsafe(ref c, offset);
            Vector128<byte> bv = Vector128.LoadUnsafe(ref b, offset);

            // The four group alphas, each in the four 16-bit lanes of its pixel.
            Vector128<byte> alphas = Vector128.Create(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref g, (nint)i))).AsByte();
            Vector128<ushort> a0 = Vector128.Shuffle(alphas, lowAlpha).AsUInt16();
            Vector128<ushort> a1 = Vector128.Shuffle(alphas, highAlpha).AsUInt16();

            // c − b × (1 − a) / 255 rounded, held between 0 and a; the alpha lane is a itself (a of 0 gives 0 in every lane).
            Vector128<ushort> q0 = (Vector128.WidenLower(bv) * (full - a0)) + half;
            Vector128<ushort> q1 = (Vector128.WidenUpper(bv) * (full - a1)) + half;
            Vector128<short> r0 = Vector128.Min(Vector128.Max(Vector128.WidenLower(cv).AsInt16() - ((q0 + (q0 >>> 8)) >>> 8).AsInt16(), Vector128<short>.Zero), a0.AsInt16());
            Vector128<short> r1 = Vector128.Min(Vector128.Max(Vector128.WidenUpper(cv).AsInt16() - ((q1 + (q1 >>> 8)) >>> 8).AsInt16(), Vector128<short>.Zero), a1.AsInt16());
            Vector128<ushort> s0 = (r0.AsUInt16() & ~alphaLane) | (a0 & alphaLane);
            Vector128<ushort> s1 = (r1.AsUInt16() & ~alphaLane) | (a1 & alphaLane);
            Vector128.Narrow(s0, s1).StoreUnsafe(ref d, offset);
        }

        RemoveBackdropRowScalar(complete[i..], backdrop[i..], groupAlpha[i..], destination[i..]);
    }

    /// <summary>
    /// Scales a row of coverage (or mask) bytes: each byte of <paramref name="source"/> × <paramref name="coverage"/> / 255, rounded,
    /// in <paramref name="target"/>.
    /// </summary>
    /// <param name="source">The values.</param>
    /// <param name="target">The scaled values, as many as <paramref name="source"/> has.</param>
    /// <param name="coverage">0 to 255.</param>
    public static void ScaleRow(ReadOnlySpan<byte> source, Span<byte> target, int coverage)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            ScaleRowVector(source, target, coverage);
        }
        else
        {
            ScaleRowScalar(source, target, coverage);
        }
    }

    /// <summary>The scalar path of <see cref="ScaleRow"/>, the reference the vector path matches.</summary>
    /// <param name="source">The values.</param>
    /// <param name="target">The scaled values.</param>
    /// <param name="coverage">The coverage.</param>
    public static void ScaleRowScalar(ReadOnlySpan<byte> source, Span<byte> target, int coverage)
    {
        source = source[..target.Length];
        for (int i = 0; i < target.Length; i++)
        {
            target[i] = (byte)Divide255(source[i] * coverage);
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="ScaleRow"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="source">The values.</param>
    /// <param name="target">The scaled values.</param>
    /// <param name="coverage">The coverage.</param>
    public static void ScaleRowVector(ReadOnlySpan<byte> source, Span<byte> target, int coverage)
    {
        if (coverage is < 0 or > 255)
        {
            ScaleRowScalar(source, target, coverage);
            return;
        }

        int count = target.Length;
        source = source[..count];
        ref byte s = ref MemoryMarshal.GetReference(source);
        ref byte d = ref MemoryMarshal.GetReference(target);
        Vector128<ushort> c = Vector128.Create((ushort)coverage);
        Vector128<ushort> half = Vector128.Create((ushort)128);
        int i = 0;

        // Sixteen bytes a step, [i, i + 16) of both rows.
        for (; i <= count - 16; i += 16)
        {
            Vector128<byte> sv = Vector128.LoadUnsafe(ref s, (nuint)i);
            Vector128<ushort> t0 = (Vector128.WidenLower(sv) * c) + half;
            Vector128<ushort> t1 = (Vector128.WidenUpper(sv) * c) + half;
            Vector128.Narrow((t0 + (t0 >>> 8)) >>> 8, (t1 + (t1 >>> 8)) >>> 8).StoreUnsafe(ref d, (nuint)i);
        }

        ScaleRowScalar(source[i..], target[i..], coverage);
    }
}
