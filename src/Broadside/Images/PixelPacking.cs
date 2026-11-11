using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Broadside.Images;

/// <summary>
/// Packs a converted image row (three color bytes per pixel) and its folded opacity into premultiplied 8-bit pixels, in BGRA byte
/// order (the software rasterizer and the Skia, CoreGraphics, Direct2D and Android backends) or RGBA (WebGL): each color byte
/// becomes <c>round(c × a / 255)</c>, the alpha byte <c>a</c>; with no opacity row every pixel is opaque and the colors pass
/// through. Issue #472: a <see cref="Vector128{T}"/> path for 16 pixels per iteration, bit-exact with the scalar one, which finishes
/// the row.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §11.6.5.2 and §8.9.6: an image's soft mask, explicit mask or color key folded into its alpha; premultiplied, as every
/// backend's surface stores it. The rounding division is <c>(v + 128 + ((v + 128) &gt;&gt; 8)) &gt;&gt; 8</c>, exact for
/// <c>v ≤ 255 × 255</c>, and it returns <c>c</c> for <c>a = 255</c>, so the vector path multiplies every pixel without testing for
/// opaque ones, and the alpha byte comes out of the same multiply (255 × a).
/// </para>
/// <para>
/// The vector path loads the 48 color bytes of 16 pixels as four overlapping vectors (from bytes 0, 12, 24 and 32), so one constant
/// byte shuffle per four pixels places them in the output order with zero in the alpha byte; an OR sets that byte to 255. With an
/// opacity row a second shuffle repeats each pixel's opacity across its four bytes, and the two multiply in 16-bit lanes
/// (<c>WidenLower</c>/<c>WidenUpper</c>, <c>*</c>, <c>+</c>, the <c>&gt;&gt;&gt;</c> operator) and narrow back to bytes. Only operations
/// that are fast in every build Broadside runs in (adding-a-benchmark skill, "SIMD kernels"): <c>LoadUnsafe</c>/<c>StoreUnsafe</c>,
/// bounded by the slices at the top; constant shuffles; the shift operator, never the <c>Vector128.ShiftRightLogical</c> method;
/// <c>Narrow</c> from 16-bit lanes. Byte order only, so it is the same on either endianness. The backends reach it through
/// <c>ImageRows.PackBgra</c> and <c>ImageRows.PackRgba</c>; the source is BCL-only otherwise, so the Blazor sample's kernel
/// benchmark compiles it in.
/// </para>
/// </remarks>
internal static class PixelPacking
{
    /// <summary>Packs <paramref name="rgb"/> and <paramref name="alpha"/> into premultiplied BGRA bytes, through the vector path when it is accelerated.</summary>
    /// <param name="rgb">Three bytes per pixel, red, green, blue; at least 3 × the pixel count.</param>
    /// <param name="alpha">One opacity byte per pixel, or empty for opaque pixels.</param>
    /// <param name="bgra">Four bytes per pixel; its length sets the pixel count.</param>
    public static void PackBgra(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> alpha, Span<byte> bgra)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            PackBgraVector(rgb, alpha, bgra);
        }
        else
        {
            PackBgraScalar(rgb, alpha, bgra);
        }
    }

    /// <summary>Packs <paramref name="rgb"/> and <paramref name="alpha"/> into premultiplied RGBA bytes, through the vector path when it is accelerated.</summary>
    /// <param name="rgb">Three bytes per pixel, red, green, blue; at least 3 × the pixel count.</param>
    /// <param name="alpha">One opacity byte per pixel, or empty for opaque pixels.</param>
    /// <param name="rgba">Four bytes per pixel; its length sets the pixel count.</param>
    public static void PackRgba(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> alpha, Span<byte> rgba)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            PackRgbaVector(rgb, alpha, rgba);
        }
        else
        {
            PackRgbaScalar(rgb, alpha, rgba);
        }
    }

    /// <summary>The scalar path of <see cref="PackBgra"/>: the reference.</summary>
    /// <param name="rgb">Three bytes per pixel.</param>
    /// <param name="alpha">One byte per pixel, or empty.</param>
    /// <param name="bgra">Four bytes per pixel.</param>
    public static void PackBgraScalar(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> alpha, Span<byte> bgra)
    {
        int width = bgra.Length / 4;
        bool opaque = alpha.IsEmpty;
        for (int x = 0; x < width; x++)
        {
            int r = rgb[3 * x];
            int g = rgb[(3 * x) + 1];
            int b = rgb[(3 * x) + 2];
            int a = opaque ? 255 : alpha[x];
            if (a != 255)
            {
                r = Divide255(r * a);
                g = Divide255(g * a);
                b = Divide255(b * a);
            }

            bgra[4 * x] = (byte)b;
            bgra[(4 * x) + 1] = (byte)g;
            bgra[(4 * x) + 2] = (byte)r;
            bgra[(4 * x) + 3] = (byte)a;
        }
    }

    /// <summary>The scalar path of <see cref="PackRgba"/>: the reference.</summary>
    /// <param name="rgb">Three bytes per pixel.</param>
    /// <param name="alpha">One byte per pixel, or empty.</param>
    /// <param name="rgba">Four bytes per pixel.</param>
    public static void PackRgbaScalar(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> alpha, Span<byte> rgba)
    {
        int width = rgba.Length / 4;
        bool opaque = alpha.IsEmpty;
        for (int x = 0; x < width; x++)
        {
            int r = rgb[3 * x];
            int g = rgb[(3 * x) + 1];
            int b = rgb[(3 * x) + 2];
            int a = opaque ? 255 : alpha[x];
            if (a != 255)
            {
                r = Divide255(r * a);
                g = Divide255(g * a);
                b = Divide255(b * a);
            }

            rgba[4 * x] = (byte)r;
            rgba[(4 * x) + 1] = (byte)g;
            rgba[(4 * x) + 2] = (byte)b;
            rgba[(4 * x) + 3] = (byte)a;
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="PackBgra"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="rgb">Three bytes per pixel.</param>
    /// <param name="alpha">One byte per pixel, or empty.</param>
    /// <param name="bgra">Four bytes per pixel.</param>
    public static void PackBgraVector(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> alpha, Span<byte> bgra)
    {
        int width = bgra.Length / 4;
        rgb = rgb[..(3 * width)];
        ref byte rgbRef = ref MemoryMarshal.GetReference(rgb);
        ref byte outRef = ref MemoryMarshal.GetReference(bgra);
        Vector128<byte> opaque = Vector128.Create((byte)0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);
        int x = 0;
        if (alpha.IsEmpty)
        {
            // Every access is inside the row: color bytes 3x .. 3x + 47 and output bytes 4x .. 4x + 63, x + 16 ≤ width.
            for (; x <= width - 16; x += 16)
            {
                nuint i = (nuint)(3 * x);
                nuint o = (nuint)(4 * x);
                (Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i), Vector128.Create((byte)2, 1, 0, 255, 5, 4, 3, 255, 8, 7, 6, 255, 11, 10, 9, 255)) | opaque).StoreUnsafe(ref outRef, o);
                (Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i + 12), Vector128.Create((byte)2, 1, 0, 255, 5, 4, 3, 255, 8, 7, 6, 255, 11, 10, 9, 255)) | opaque).StoreUnsafe(ref outRef, o + 16);
                (Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i + 24), Vector128.Create((byte)2, 1, 0, 255, 5, 4, 3, 255, 8, 7, 6, 255, 11, 10, 9, 255)) | opaque).StoreUnsafe(ref outRef, o + 32);
                (Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i + 32), Vector128.Create((byte)6, 5, 4, 255, 9, 8, 7, 255, 12, 11, 10, 255, 15, 14, 13, 255)) | opaque).StoreUnsafe(ref outRef, o + 48);
            }

            PackBgraScalar(rgb[(3 * x)..], alpha, bgra[(4 * x)..]);
            return;
        }

        alpha = alpha[..width];
        ref byte alphaRef = ref MemoryMarshal.GetReference(alpha);
        Vector128<ushort> half = Vector128.Create((ushort)128);
        for (; x <= width - 16; x += 16)
        {
            // Every access is inside the row: color bytes 3x .. 3x + 47, opacity bytes x .. x + 15, output bytes 4x .. 4x + 63.
            nuint i = (nuint)(3 * x);
            nuint o = (nuint)(4 * x);
            Vector128<byte> a = Vector128.LoadUnsafe(ref alphaRef, (nuint)x);

            Vector128<byte> c = Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i), Vector128.Create((byte)2, 1, 0, 255, 5, 4, 3, 255, 8, 7, 6, 255, 11, 10, 9, 255)) | opaque;
            Vector128<byte> m = Vector128.Shuffle(a, Vector128.Create((byte)0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3));
            Vector128<ushort> lo = (Vector128.WidenLower(c) * Vector128.WidenLower(m)) + half;
            Vector128<ushort> hi = (Vector128.WidenUpper(c) * Vector128.WidenUpper(m)) + half;
            Vector128.Narrow((lo + (lo >>> 8)) >>> 8, (hi + (hi >>> 8)) >>> 8).StoreUnsafe(ref outRef, o);

            c = Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i + 12), Vector128.Create((byte)2, 1, 0, 255, 5, 4, 3, 255, 8, 7, 6, 255, 11, 10, 9, 255)) | opaque;
            m = Vector128.Shuffle(a, Vector128.Create((byte)4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7));
            lo = (Vector128.WidenLower(c) * Vector128.WidenLower(m)) + half;
            hi = (Vector128.WidenUpper(c) * Vector128.WidenUpper(m)) + half;
            Vector128.Narrow((lo + (lo >>> 8)) >>> 8, (hi + (hi >>> 8)) >>> 8).StoreUnsafe(ref outRef, o + 16);

            c = Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i + 24), Vector128.Create((byte)2, 1, 0, 255, 5, 4, 3, 255, 8, 7, 6, 255, 11, 10, 9, 255)) | opaque;
            m = Vector128.Shuffle(a, Vector128.Create((byte)8, 8, 8, 8, 9, 9, 9, 9, 10, 10, 10, 10, 11, 11, 11, 11));
            lo = (Vector128.WidenLower(c) * Vector128.WidenLower(m)) + half;
            hi = (Vector128.WidenUpper(c) * Vector128.WidenUpper(m)) + half;
            Vector128.Narrow((lo + (lo >>> 8)) >>> 8, (hi + (hi >>> 8)) >>> 8).StoreUnsafe(ref outRef, o + 32);

            c = Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i + 32), Vector128.Create((byte)6, 5, 4, 255, 9, 8, 7, 255, 12, 11, 10, 255, 15, 14, 13, 255)) | opaque;
            m = Vector128.Shuffle(a, Vector128.Create((byte)12, 12, 12, 12, 13, 13, 13, 13, 14, 14, 14, 14, 15, 15, 15, 15));
            lo = (Vector128.WidenLower(c) * Vector128.WidenLower(m)) + half;
            hi = (Vector128.WidenUpper(c) * Vector128.WidenUpper(m)) + half;
            Vector128.Narrow((lo + (lo >>> 8)) >>> 8, (hi + (hi >>> 8)) >>> 8).StoreUnsafe(ref outRef, o + 48);
        }

        PackBgraScalar(rgb[(3 * x)..], alpha[x..], bgra[(4 * x)..]);
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="PackRgba"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="rgb">Three bytes per pixel.</param>
    /// <param name="alpha">One byte per pixel, or empty.</param>
    /// <param name="rgba">Four bytes per pixel.</param>
    public static void PackRgbaVector(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> alpha, Span<byte> rgba)
    {
        int width = rgba.Length / 4;
        rgb = rgb[..(3 * width)];
        ref byte rgbRef = ref MemoryMarshal.GetReference(rgb);
        ref byte outRef = ref MemoryMarshal.GetReference(rgba);
        Vector128<byte> opaque = Vector128.Create((byte)0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);
        int x = 0;
        if (alpha.IsEmpty)
        {
            // Every access is inside the row: color bytes 3x .. 3x + 47 and output bytes 4x .. 4x + 63, x + 16 ≤ width.
            for (; x <= width - 16; x += 16)
            {
                nuint i = (nuint)(3 * x);
                nuint o = (nuint)(4 * x);
                (Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i), Vector128.Create((byte)0, 1, 2, 255, 3, 4, 5, 255, 6, 7, 8, 255, 9, 10, 11, 255)) | opaque).StoreUnsafe(ref outRef, o);
                (Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i + 12), Vector128.Create((byte)0, 1, 2, 255, 3, 4, 5, 255, 6, 7, 8, 255, 9, 10, 11, 255)) | opaque).StoreUnsafe(ref outRef, o + 16);
                (Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i + 24), Vector128.Create((byte)0, 1, 2, 255, 3, 4, 5, 255, 6, 7, 8, 255, 9, 10, 11, 255)) | opaque).StoreUnsafe(ref outRef, o + 32);
                (Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i + 32), Vector128.Create((byte)4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255, 13, 14, 15, 255)) | opaque).StoreUnsafe(ref outRef, o + 48);
            }

            PackRgbaScalar(rgb[(3 * x)..], alpha, rgba[(4 * x)..]);
            return;
        }

        alpha = alpha[..width];
        ref byte alphaRef = ref MemoryMarshal.GetReference(alpha);
        Vector128<ushort> half = Vector128.Create((ushort)128);
        for (; x <= width - 16; x += 16)
        {
            // Every access is inside the row: color bytes 3x .. 3x + 47, opacity bytes x .. x + 15, output bytes 4x .. 4x + 63.
            nuint i = (nuint)(3 * x);
            nuint o = (nuint)(4 * x);
            Vector128<byte> a = Vector128.LoadUnsafe(ref alphaRef, (nuint)x);

            Vector128<byte> c = Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i), Vector128.Create((byte)0, 1, 2, 255, 3, 4, 5, 255, 6, 7, 8, 255, 9, 10, 11, 255)) | opaque;
            Vector128<byte> m = Vector128.Shuffle(a, Vector128.Create((byte)0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3));
            Vector128<ushort> lo = (Vector128.WidenLower(c) * Vector128.WidenLower(m)) + half;
            Vector128<ushort> hi = (Vector128.WidenUpper(c) * Vector128.WidenUpper(m)) + half;
            Vector128.Narrow((lo + (lo >>> 8)) >>> 8, (hi + (hi >>> 8)) >>> 8).StoreUnsafe(ref outRef, o);

            c = Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i + 12), Vector128.Create((byte)0, 1, 2, 255, 3, 4, 5, 255, 6, 7, 8, 255, 9, 10, 11, 255)) | opaque;
            m = Vector128.Shuffle(a, Vector128.Create((byte)4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7));
            lo = (Vector128.WidenLower(c) * Vector128.WidenLower(m)) + half;
            hi = (Vector128.WidenUpper(c) * Vector128.WidenUpper(m)) + half;
            Vector128.Narrow((lo + (lo >>> 8)) >>> 8, (hi + (hi >>> 8)) >>> 8).StoreUnsafe(ref outRef, o + 16);

            c = Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i + 24), Vector128.Create((byte)0, 1, 2, 255, 3, 4, 5, 255, 6, 7, 8, 255, 9, 10, 11, 255)) | opaque;
            m = Vector128.Shuffle(a, Vector128.Create((byte)8, 8, 8, 8, 9, 9, 9, 9, 10, 10, 10, 10, 11, 11, 11, 11));
            lo = (Vector128.WidenLower(c) * Vector128.WidenLower(m)) + half;
            hi = (Vector128.WidenUpper(c) * Vector128.WidenUpper(m)) + half;
            Vector128.Narrow((lo + (lo >>> 8)) >>> 8, (hi + (hi >>> 8)) >>> 8).StoreUnsafe(ref outRef, o + 32);

            c = Vector128.Shuffle(Vector128.LoadUnsafe(ref rgbRef, i + 32), Vector128.Create((byte)4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255, 13, 14, 15, 255)) | opaque;
            m = Vector128.Shuffle(a, Vector128.Create((byte)12, 12, 12, 12, 13, 13, 13, 13, 14, 14, 14, 14, 15, 15, 15, 15));
            lo = (Vector128.WidenLower(c) * Vector128.WidenLower(m)) + half;
            hi = (Vector128.WidenUpper(c) * Vector128.WidenUpper(m)) + half;
            Vector128.Narrow((lo + (lo >>> 8)) >>> 8, (hi + (hi >>> 8)) >>> 8).StoreUnsafe(ref outRef, o + 48);
        }

        PackRgbaScalar(rgb[(3 * x)..], alpha[x..], rgba[(4 * x)..]);
    }

    /// <summary>Rounds <paramref name="value"/> / 255 for 0 ≤ value ≤ 255 × 255.</summary>
    private static int Divide255(int value)
    {
        value += 128;
        return (value + (value >> 8)) >> 8;
    }
}
