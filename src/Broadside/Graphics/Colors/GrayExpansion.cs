using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Broadside.Graphics.Colors;

/// <summary>
/// Expands 8-bit gray samples to RGB bytes, each sample repeated three times: the byte path of a one-component space whose 256 colors
/// are the grays themselves (DeviceGray, and a calibrated or ICC gray that converts to them, on an RGB device). Issue #472: a
/// <see cref="Vector128{T}"/> path for 16 samples per iteration, bit-exact with the scalar one, which finishes the row.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §10.4.2.2 (DeviceGray to DeviceRGB: r = g = b = gray). The pipeline proves the replication on its 256-entry byte table
/// before choosing this path (<c>ColorPipeline</c>), so it is the table lookup it replaces.
/// </para>
/// <para>
/// The vector path loads 16 samples and writes 48 bytes with three constant byte shuffles: only <c>LoadUnsafe</c>/<c>StoreUnsafe</c>
/// (bounded by the slices at the top) and <c>Shuffle</c>, fast in every build (adding-a-benchmark skill, "SIMD kernels"). The source
/// is BCL-only, so the Blazor sample's kernel benchmark compiles it in.
/// </para>
/// </remarks>
internal static class GrayExpansion
{
    /// <summary>Expands <paramref name="gray"/> into <paramref name="rgb"/>, through the vector path when it is accelerated.</summary>
    /// <param name="gray">One byte per sample, at least a third of <paramref name="rgb"/>.</param>
    /// <param name="rgb">Three bytes per sample; its length sets the count.</param>
    public static void ToRgb(ReadOnlySpan<byte> gray, Span<byte> rgb)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            ToRgbVector(gray, rgb);
        }
        else
        {
            ToRgbScalar(gray, rgb);
        }
    }

    /// <summary>The scalar path of <see cref="ToRgb"/>: the reference.</summary>
    /// <param name="gray">One byte per sample.</param>
    /// <param name="rgb">Three bytes per sample.</param>
    public static void ToRgbScalar(ReadOnlySpan<byte> gray, Span<byte> rgb)
    {
        int count = rgb.Length / 3;
        for (int i = 0; i < count; i++)
        {
            byte g = gray[i];
            rgb[3 * i] = g;
            rgb[(3 * i) + 1] = g;
            rgb[(3 * i) + 2] = g;
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="ToRgb"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="gray">One byte per sample.</param>
    /// <param name="rgb">Three bytes per sample.</param>
    public static void ToRgbVector(ReadOnlySpan<byte> gray, Span<byte> rgb)
    {
        int count = rgb.Length / 3;
        gray = gray[..count];
        ref byte grayRef = ref MemoryMarshal.GetReference(gray);
        ref byte rgbRef = ref MemoryMarshal.GetReference(rgb);
        int i = 0;

        // Every access is inside the row: samples i .. i + 15 and output bytes 3i .. 3i + 47, i + 16 ≤ count.
        for (; i <= count - 16; i += 16)
        {
            Vector128<byte> g = Vector128.LoadUnsafe(ref grayRef, (nuint)i);
            nuint o = (nuint)(3 * i);
            Vector128.Shuffle(g, Vector128.Create((byte)0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5)).StoreUnsafe(ref rgbRef, o);
            Vector128.Shuffle(g, Vector128.Create((byte)5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 8, 9, 9, 9, 10, 10)).StoreUnsafe(ref rgbRef, o + 16);
            Vector128.Shuffle(g, Vector128.Create((byte)10, 11, 11, 11, 12, 12, 12, 13, 13, 13, 14, 14, 14, 15, 15, 15)).StoreUnsafe(ref rgbRef, o + 32);
        }

        ToRgbScalar(gray[i..], rgb[(3 * i)..]);
    }
}
