using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Broadside.Filters.Dct;

/// <summary>
/// The <see cref="Vector128{T}"/> paths of the 8-bit row transforms (issue #471): 16 samples per iteration, bit-exact with
/// <see cref="YccToRgbScalar"/> and <see cref="YcckToCmykScalar"/>, the scalar methods finishing the row.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.8, Table 13; Adobe Technical Note #5116 §13. The arithmetic is libjpeg's 16-bit fixed point, so it needs 32-bit
/// lanes: each chroma term is <c>(K * c + bias) &gt;&gt; 16</c> with the table's <c>- 128 K</c> and rounding folded into the bias.
/// Every 16-bit lane of the widened chroma splits into an even and an odd 32-bit lane (<c>&amp; 0xFFFF</c> and <c>&gt;&gt;&gt; 16</c>); the
/// even result's high half (<c>&gt;&gt;&gt; 16</c>) and the odd result's high half in place (<c>&amp; 0xFFFF0000</c>) OR back into 16-bit
/// lanes in sample order, which are the terms the tables hold modulo 2^16. The luminance is added in 16-bit lanes, clamped with
/// <c>Min</c>/<c>Max</c>, narrowed to bytes and interleaved with constant byte shuffles. YCCK to CMYK is 255 minus the YCbCr to
/// RGB result (the clamp is symmetric), K copied.
/// </para>
/// <para>
/// Only operations that are fast in every build Broadside runs in, measured (adding-a-benchmark skill, "SIMD kernels"): the JIT and
/// Native AOT on x64 and Arm64, and the browser's interpreter and AOT builds. Loads and stores are <c>LoadUnsafe</c>/<c>StoreUnsafe</c>
/// (the span forms <c>Create(ReadOnlySpan)</c>/<c>CopyTo</c> cost the interpreter 3.4x a vector), bounded by the slices at the top;
/// shifts are the <c>&gt;&gt;&gt;</c> operator (the browser AOT build does not compile the <c>Vector128.ShiftRightLogical</c> method to a
/// SIMD shift: about 8x the cost of the operator). The source is BCL-only, so the Blazor sample's kernel benchmark links it.
/// </para>
/// </remarks>
internal static partial class JpegColor
{
    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="YccToRgb"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="y">The luminance samples.</param>
    /// <param name="cb">The blue-difference samples.</param>
    /// <param name="cr">The red-difference samples.</param>
    /// <param name="rgb">Three bytes per sample.</param>
    public static void YccToRgbVector(ReadOnlySpan<byte> y, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, Span<byte> rgb)
    {
        int width = rgb.Length / 3;
        y = y[..width];
        cb = cb[..width];
        cr = cr[..width];
        ref byte yRef = ref MemoryMarshal.GetReference(y);
        ref byte cbRef = ref MemoryMarshal.GetReference(cb);
        ref byte crRef = ref MemoryMarshal.GetReference(cr);
        ref byte rgbRef = ref MemoryMarshal.GetReference(rgb);
        Vector128<int> even = Vector128.Create(0xFFFF);
        Vector128<int> odd = Vector128.Create(unchecked((int)0xFFFF0000));
        Vector128<int> crToRed = Vector128.Create(91881);
        Vector128<int> cbToBlue = Vector128.Create(116130);
        Vector128<int> cbToGreen = Vector128.Create(-22554);
        Vector128<int> crToGreen = Vector128.Create(-46802);
        Vector128<int> redBias = Vector128.Create(-11728000); // -128 * 91881 + 2^15
        Vector128<int> blueBias = Vector128.Create(-14831872); // -128 * 116130 + 2^15
        Vector128<int> greenBias = Vector128.Create(8910336); // 128 * (22554 + 46802) + 2^15
        Vector128<short> zero = Vector128<short>.Zero;
        Vector128<short> max = Vector128.Create((short)255);
        int x = 0;
        for (; x <= width - 16; x += 16)
        {
            Vector128<byte> yv = Vector128.LoadUnsafe(ref yRef, (nuint)x);
            Vector128<byte> cbv = Vector128.LoadUnsafe(ref cbRef, (nuint)x);
            Vector128<byte> crv = Vector128.LoadUnsafe(ref crRef, (nuint)x);

            // Samples 0-7.
            Vector128<int> cb32 = Vector128.WidenLower(cbv).AsInt32();
            Vector128<int> cr32 = Vector128.WidenLower(crv).AsInt32();
            Vector128<int> cbEven = cb32 & even, cbOdd = cb32 >>> 16;
            Vector128<int> crEven = cr32 & even, crOdd = cr32 >>> 16;
            Vector128<short> luma = Vector128.WidenLower(yv).AsInt16();
            Vector128<short> r0 = luma + ((((crEven * crToRed) + redBias) >>> 16) | (((crOdd * crToRed) + redBias) & odd)).AsInt16();
            Vector128<short> g0 = luma + ((((cbEven * cbToGreen) + (crEven * crToGreen) + greenBias) >>> 16) | (((cbOdd * cbToGreen) + (crOdd * crToGreen) + greenBias) & odd)).AsInt16();
            Vector128<short> b0 = luma + ((((cbEven * cbToBlue) + blueBias) >>> 16) | (((cbOdd * cbToBlue) + blueBias) & odd)).AsInt16();

            // Samples 8-15.
            cb32 = Vector128.WidenUpper(cbv).AsInt32();
            cr32 = Vector128.WidenUpper(crv).AsInt32();
            cbEven = cb32 & even;
            cbOdd = cb32 >>> 16;
            crEven = cr32 & even;
            crOdd = cr32 >>> 16;
            luma = Vector128.WidenUpper(yv).AsInt16();
            Vector128<short> r1 = luma + ((((crEven * crToRed) + redBias) >>> 16) | (((crOdd * crToRed) + redBias) & odd)).AsInt16();
            Vector128<short> g1 = luma + ((((cbEven * cbToGreen) + (crEven * crToGreen) + greenBias) >>> 16) | (((cbOdd * cbToGreen) + (crOdd * crToGreen) + greenBias) & odd)).AsInt16();
            Vector128<short> b1 = luma + ((((cbEven * cbToBlue) + blueBias) >>> 16) | (((cbOdd * cbToBlue) + blueBias) & odd)).AsInt16();

            Vector128<byte> red = Vector128.Narrow(Vector128.Min(Vector128.Max(r0, zero), max).AsUInt16(), Vector128.Min(Vector128.Max(r1, zero), max).AsUInt16());
            Vector128<byte> green = Vector128.Narrow(Vector128.Min(Vector128.Max(g0, zero), max).AsUInt16(), Vector128.Min(Vector128.Max(g1, zero), max).AsUInt16());
            Vector128<byte> blue = Vector128.Narrow(Vector128.Min(Vector128.Max(b0, zero), max).AsUInt16(), Vector128.Min(Vector128.Max(b1, zero), max).AsUInt16());

            // Interleave: index 255 selects zero.
            nuint o = (nuint)(3 * x);
            (Vector128.Shuffle(red, Vector128.Create((byte)0, 255, 255, 1, 255, 255, 2, 255, 255, 3, 255, 255, 4, 255, 255, 5))
                | Vector128.Shuffle(green, Vector128.Create((byte)255, 0, 255, 255, 1, 255, 255, 2, 255, 255, 3, 255, 255, 4, 255, 255))
                | Vector128.Shuffle(blue, Vector128.Create((byte)255, 255, 0, 255, 255, 1, 255, 255, 2, 255, 255, 3, 255, 255, 4, 255))).StoreUnsafe(ref rgbRef, o);
            (Vector128.Shuffle(red, Vector128.Create((byte)255, 255, 6, 255, 255, 7, 255, 255, 8, 255, 255, 9, 255, 255, 10, 255))
                | Vector128.Shuffle(green, Vector128.Create((byte)5, 255, 255, 6, 255, 255, 7, 255, 255, 8, 255, 255, 9, 255, 255, 10))
                | Vector128.Shuffle(blue, Vector128.Create((byte)255, 5, 255, 255, 6, 255, 255, 7, 255, 255, 8, 255, 255, 9, 255, 255))).StoreUnsafe(ref rgbRef, o + 16);
            (Vector128.Shuffle(red, Vector128.Create((byte)255, 11, 255, 255, 12, 255, 255, 13, 255, 255, 14, 255, 255, 15, 255, 255))
                | Vector128.Shuffle(green, Vector128.Create((byte)255, 255, 11, 255, 255, 12, 255, 255, 13, 255, 255, 14, 255, 255, 15, 255))
                | Vector128.Shuffle(blue, Vector128.Create((byte)10, 255, 255, 11, 255, 255, 12, 255, 255, 13, 255, 255, 14, 255, 255, 15))).StoreUnsafe(ref rgbRef, o + 32);
        }

        YccToRgbScalar(y[x..], cb[x..], cr[x..], rgb[(3 * x)..]);
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="YcckToCmyk"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="y">The luminance samples.</param>
    /// <param name="cb">The blue-difference samples.</param>
    /// <param name="cr">The red-difference samples.</param>
    /// <param name="k">The black samples.</param>
    /// <param name="cmyk">Four bytes per sample.</param>
    /// <remarks>Adobe Technical Note #5116 §13.2.</remarks>
    public static void YcckToCmykVector(ReadOnlySpan<byte> y, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, ReadOnlySpan<byte> k, Span<byte> cmyk)
    {
        int width = cmyk.Length / 4;
        y = y[..width];
        cb = cb[..width];
        cr = cr[..width];
        k = k[..width];
        ref byte yRef = ref MemoryMarshal.GetReference(y);
        ref byte cbRef = ref MemoryMarshal.GetReference(cb);
        ref byte crRef = ref MemoryMarshal.GetReference(cr);
        ref byte kRef = ref MemoryMarshal.GetReference(k);
        ref byte cmykRef = ref MemoryMarshal.GetReference(cmyk);
        Vector128<int> even = Vector128.Create(0xFFFF);
        Vector128<int> odd = Vector128.Create(unchecked((int)0xFFFF0000));
        Vector128<int> crToRed = Vector128.Create(91881);
        Vector128<int> cbToBlue = Vector128.Create(116130);
        Vector128<int> cbToGreen = Vector128.Create(-22554);
        Vector128<int> crToGreen = Vector128.Create(-46802);
        Vector128<int> redBias = Vector128.Create(-11728000);
        Vector128<int> blueBias = Vector128.Create(-14831872);
        Vector128<int> greenBias = Vector128.Create(8910336);
        Vector128<short> zero = Vector128<short>.Zero;
        Vector128<short> max = Vector128.Create((short)255);
        Vector128<byte> ones = Vector128.Create((byte)255);
        int x = 0;
        for (; x <= width - 16; x += 16)
        {
            Vector128<byte> yv = Vector128.LoadUnsafe(ref yRef, (nuint)x);
            Vector128<byte> cbv = Vector128.LoadUnsafe(ref cbRef, (nuint)x);
            Vector128<byte> crv = Vector128.LoadUnsafe(ref crRef, (nuint)x);
            Vector128<byte> black = Vector128.LoadUnsafe(ref kRef, (nuint)x);

            // Samples 0-7.
            Vector128<int> cb32 = Vector128.WidenLower(cbv).AsInt32();
            Vector128<int> cr32 = Vector128.WidenLower(crv).AsInt32();
            Vector128<int> cbEven = cb32 & even, cbOdd = cb32 >>> 16;
            Vector128<int> crEven = cr32 & even, crOdd = cr32 >>> 16;
            Vector128<short> luma = Vector128.WidenLower(yv).AsInt16();
            Vector128<short> r0 = luma + ((((crEven * crToRed) + redBias) >>> 16) | (((crOdd * crToRed) + redBias) & odd)).AsInt16();
            Vector128<short> g0 = luma + ((((cbEven * cbToGreen) + (crEven * crToGreen) + greenBias) >>> 16) | (((cbOdd * cbToGreen) + (crOdd * crToGreen) + greenBias) & odd)).AsInt16();
            Vector128<short> b0 = luma + ((((cbEven * cbToBlue) + blueBias) >>> 16) | (((cbOdd * cbToBlue) + blueBias) & odd)).AsInt16();

            // Samples 8-15.
            cb32 = Vector128.WidenUpper(cbv).AsInt32();
            cr32 = Vector128.WidenUpper(crv).AsInt32();
            cbEven = cb32 & even;
            cbOdd = cb32 >>> 16;
            crEven = cr32 & even;
            crOdd = cr32 >>> 16;
            luma = Vector128.WidenUpper(yv).AsInt16();
            Vector128<short> r1 = luma + ((((crEven * crToRed) + redBias) >>> 16) | (((crOdd * crToRed) + redBias) & odd)).AsInt16();
            Vector128<short> g1 = luma + ((((cbEven * cbToGreen) + (crEven * crToGreen) + greenBias) >>> 16) | (((cbOdd * cbToGreen) + (crOdd * crToGreen) + greenBias) & odd)).AsInt16();
            Vector128<short> b1 = luma + ((((cbEven * cbToBlue) + blueBias) >>> 16) | (((cbOdd * cbToBlue) + blueBias) & odd)).AsInt16();

            // 255 minus the clamped RGB.
            Vector128<byte> cyan = ones ^ Vector128.Narrow(Vector128.Min(Vector128.Max(r0, zero), max).AsUInt16(), Vector128.Min(Vector128.Max(r1, zero), max).AsUInt16());
            Vector128<byte> magenta = ones ^ Vector128.Narrow(Vector128.Min(Vector128.Max(g0, zero), max).AsUInt16(), Vector128.Min(Vector128.Max(g1, zero), max).AsUInt16());
            Vector128<byte> yellow = ones ^ Vector128.Narrow(Vector128.Min(Vector128.Max(b0, zero), max).AsUInt16(), Vector128.Min(Vector128.Max(b1, zero), max).AsUInt16());

            // Interleave: index 255 selects zero.
            nuint o = (nuint)(4 * x);
            (Vector128.Shuffle(cyan, Vector128.Create((byte)0, 255, 255, 255, 1, 255, 255, 255, 2, 255, 255, 255, 3, 255, 255, 255))
                | Vector128.Shuffle(magenta, Vector128.Create((byte)255, 0, 255, 255, 255, 1, 255, 255, 255, 2, 255, 255, 255, 3, 255, 255))
                | Vector128.Shuffle(yellow, Vector128.Create((byte)255, 255, 0, 255, 255, 255, 1, 255, 255, 255, 2, 255, 255, 255, 3, 255))
                | Vector128.Shuffle(black, Vector128.Create((byte)255, 255, 255, 0, 255, 255, 255, 1, 255, 255, 255, 2, 255, 255, 255, 3))).StoreUnsafe(ref cmykRef, o);
            (Vector128.Shuffle(cyan, Vector128.Create((byte)4, 255, 255, 255, 5, 255, 255, 255, 6, 255, 255, 255, 7, 255, 255, 255))
                | Vector128.Shuffle(magenta, Vector128.Create((byte)255, 4, 255, 255, 255, 5, 255, 255, 255, 6, 255, 255, 255, 7, 255, 255))
                | Vector128.Shuffle(yellow, Vector128.Create((byte)255, 255, 4, 255, 255, 255, 5, 255, 255, 255, 6, 255, 255, 255, 7, 255))
                | Vector128.Shuffle(black, Vector128.Create((byte)255, 255, 255, 4, 255, 255, 255, 5, 255, 255, 255, 6, 255, 255, 255, 7))).StoreUnsafe(ref cmykRef, o + 16);
            (Vector128.Shuffle(cyan, Vector128.Create((byte)8, 255, 255, 255, 9, 255, 255, 255, 10, 255, 255, 255, 11, 255, 255, 255))
                | Vector128.Shuffle(magenta, Vector128.Create((byte)255, 8, 255, 255, 255, 9, 255, 255, 255, 10, 255, 255, 255, 11, 255, 255))
                | Vector128.Shuffle(yellow, Vector128.Create((byte)255, 255, 8, 255, 255, 255, 9, 255, 255, 255, 10, 255, 255, 255, 11, 255))
                | Vector128.Shuffle(black, Vector128.Create((byte)255, 255, 255, 8, 255, 255, 255, 9, 255, 255, 255, 10, 255, 255, 255, 11))).StoreUnsafe(ref cmykRef, o + 32);
            (Vector128.Shuffle(cyan, Vector128.Create((byte)12, 255, 255, 255, 13, 255, 255, 255, 14, 255, 255, 255, 15, 255, 255, 255))
                | Vector128.Shuffle(magenta, Vector128.Create((byte)255, 12, 255, 255, 255, 13, 255, 255, 255, 14, 255, 255, 255, 15, 255, 255))
                | Vector128.Shuffle(yellow, Vector128.Create((byte)255, 255, 12, 255, 255, 255, 13, 255, 255, 255, 14, 255, 255, 255, 15, 255))
                | Vector128.Shuffle(black, Vector128.Create((byte)255, 255, 255, 12, 255, 255, 255, 13, 255, 255, 255, 14, 255, 255, 255, 15))).StoreUnsafe(ref cmykRef, o + 48);
        }

        YcckToCmykScalar(y[x..], cb[x..], cr[x..], k[x..], cmyk[(4 * x)..]);
    }
}
