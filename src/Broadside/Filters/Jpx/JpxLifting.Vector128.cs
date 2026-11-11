using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Broadside.Filters.Jpx;

/// <summary>
/// The <see cref="Vector128{T}"/> paths of <see cref="JpxLifting"/> (issue #477): four samples per vector, bit-exact with the scalar
/// methods, which finish each run's last zero to three samples.
/// </summary>
/// <remarks>
/// Each lane runs the scalar arithmetic in the scalar order: the 5/3 steps' 32-bit sums and arithmetic shifts (which wrap the same way
/// on corrupt input), the RCT's, and the 9/7 and ICT single-precision products and sums, a multiply then an add or subtract, never
/// fused. Only operations that are fast in every build Broadside runs in (adding-a-benchmark skill, "SIMD kernels"):
/// <c>LoadUnsafe</c>/<c>StoreUnsafe</c> bounded by the slices at the top of each method and a loop condition that keeps every access
/// inside them, <c>+</c>, <c>-</c>, <c>*</c>, the <c>&gt;&gt;</c> operator, constant <c>Shuffle</c>s and <c>ConditionalSelect</c>.
/// </remarks>
internal static partial class JpxLifting
{
    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="Lift53Low"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="target">The samples updated.</param>
    /// <param name="left">Each sample's left neighbour.</param>
    /// <param name="right">Each sample's right neighbour.</param>
    public static void Lift53LowVector(Span<int> target, ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        int n = target.Length;
        left = left[..n];
        right = right[..n];
        ref int t = ref MemoryMarshal.GetReference(target);
        ref int l = ref MemoryMarshal.GetReference(left);
        ref int r = ref MemoryMarshal.GetReference(right);
        Vector128<int> two = Vector128.Create(2);
        int i = 0;
        for (; i <= n - 4; i += 4)
        {
            Vector128<int> sum = Vector128.LoadUnsafe(ref l, (nuint)i) + Vector128.LoadUnsafe(ref r, (nuint)i) + two;
            (Vector128.LoadUnsafe(ref t, (nuint)i) - (sum >> 2)).StoreUnsafe(ref t, (nuint)i);
        }

        if (i < n)
        {
            Lift53LowScalar(target[i..], left[i..], right[i..]);
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="Lift53High"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="target">The samples updated.</param>
    /// <param name="left">Each sample's left neighbour.</param>
    /// <param name="right">Each sample's right neighbour.</param>
    public static void Lift53HighVector(Span<int> target, ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        int n = target.Length;
        left = left[..n];
        right = right[..n];
        ref int t = ref MemoryMarshal.GetReference(target);
        ref int l = ref MemoryMarshal.GetReference(left);
        ref int r = ref MemoryMarshal.GetReference(right);
        int i = 0;
        for (; i <= n - 4; i += 4)
        {
            Vector128<int> sum = Vector128.LoadUnsafe(ref l, (nuint)i) + Vector128.LoadUnsafe(ref r, (nuint)i);
            (Vector128.LoadUnsafe(ref t, (nuint)i) + (sum >> 1)).StoreUnsafe(ref t, (nuint)i);
        }

        if (i < n)
        {
            Lift53HighScalar(target[i..], left[i..], right[i..]);
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="Lift97"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="target">The samples updated.</param>
    /// <param name="left">Each sample's left neighbour.</param>
    /// <param name="right">Each sample's right neighbour.</param>
    /// <param name="factor">The step's coefficient.</param>
    public static void Lift97Vector(Span<float> target, ReadOnlySpan<float> left, ReadOnlySpan<float> right, float factor)
    {
        int n = target.Length;
        left = left[..n];
        right = right[..n];
        ref float t = ref MemoryMarshal.GetReference(target);
        ref float l = ref MemoryMarshal.GetReference(left);
        ref float r = ref MemoryMarshal.GetReference(right);
        Vector128<float> f = Vector128.Create(factor);
        int i = 0;
        for (; i <= n - 4; i += 4)
        {
            Vector128<float> sum = Vector128.LoadUnsafe(ref l, (nuint)i) + Vector128.LoadUnsafe(ref r, (nuint)i);
            (Vector128.LoadUnsafe(ref t, (nuint)i) - (f * sum)).StoreUnsafe(ref t, (nuint)i);
        }

        if (i < n)
        {
            Lift97Scalar(target[i..], left[i..], right[i..], factor);
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="Scale97"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="target">The samples scaled.</param>
    /// <param name="factor">The factor.</param>
    public static void Scale97Vector(Span<float> target, float factor)
    {
        int n = target.Length;
        ref float t = ref MemoryMarshal.GetReference(target);
        Vector128<float> f = Vector128.Create(factor);
        int i = 0;
        for (; i <= n - 4; i += 4)
        {
            (Vector128.LoadUnsafe(ref t, (nuint)i) * f).StoreUnsafe(ref t, (nuint)i);
        }

        if (i < n)
        {
            Scale97Scalar(target[i..], factor);
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="Interleave"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="even">The samples at even positions.</param>
    /// <param name="odd">The samples at odd positions.</param>
    /// <param name="target">The interleaved signal.</param>
    /// <remarks>
    /// Four pairs per iteration: each half of the even and odd vectors is spread over a vector by a constant shuffle (lanes 0, 0, 1, 1
    /// or 2, 2, 3, 3) and the odd lanes are taken from the odd samples' by a select.
    /// </remarks>
    public static void InterleaveVector(ReadOnlySpan<int> even, ReadOnlySpan<int> odd, Span<int> target)
    {
        // The loop reads even[i..i + 4) and odd[i..i + 4) and writes target[2i..2i + 8) with i + 4 <= odd.Length <= even.Length.
        int n = odd.Length;
        even = even[..Math.Max(n, even.Length)];
        target = target[..(even.Length + n)];
        ref int e = ref MemoryMarshal.GetReference(even);
        ref int o = ref MemoryMarshal.GetReference(odd);
        ref int t = ref MemoryMarshal.GetReference(target);
        int i = 0;
        for (; i <= n - 4; i += 4)
        {
            Vector128<int> evens = Vector128.LoadUnsafe(ref e, (nuint)i);
            Vector128<int> odds = Vector128.LoadUnsafe(ref o, (nuint)i);
            Vector128.ConditionalSelect(
                Vector128.Create(0, -1, 0, -1),
                Vector128.Shuffle(odds, Vector128.Create(0, 0, 1, 1)),
                Vector128.Shuffle(evens, Vector128.Create(0, 0, 1, 1))).StoreUnsafe(ref t, (nuint)(2 * i));
            Vector128.ConditionalSelect(
                Vector128.Create(0, -1, 0, -1),
                Vector128.Shuffle(odds, Vector128.Create(2, 2, 3, 3)),
                Vector128.Shuffle(evens, Vector128.Create(2, 2, 3, 3))).StoreUnsafe(ref t, (nuint)((2 * i) + 4));
        }

        InterleaveScalar(even[i..], odd[i..], target[(2 * i)..]);
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="InverseRct"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="c0">Y0, then I0.</param>
    /// <param name="c1">Y1, then I1.</param>
    /// <param name="c2">Y2, then I2.</param>
    public static void InverseRctVector(Span<int> c0, Span<int> c1, Span<int> c2)
    {
        int n = c0.Length;
        c1 = c1[..n];
        c2 = c2[..n];
        ref int p0 = ref MemoryMarshal.GetReference(c0);
        ref int p1 = ref MemoryMarshal.GetReference(c1);
        ref int p2 = ref MemoryMarshal.GetReference(c2);
        int i = 0;
        for (; i <= n - 4; i += 4)
        {
            Vector128<int> y1 = Vector128.LoadUnsafe(ref p1, (nuint)i);
            Vector128<int> y2 = Vector128.LoadUnsafe(ref p2, (nuint)i);
            Vector128<int> g = Vector128.LoadUnsafe(ref p0, (nuint)i) - ((y1 + y2) >> 2);
            (y2 + g).StoreUnsafe(ref p0, (nuint)i);
            (y1 + g).StoreUnsafe(ref p2, (nuint)i);
            g.StoreUnsafe(ref p1, (nuint)i);
        }

        if (i < n)
        {
            InverseRctScalar(c0[i..], c1[i..], c2[i..]);
        }
    }

    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="InverseIct"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="c0">Y, then I0.</param>
    /// <param name="c1">Cb, then I1.</param>
    /// <param name="c2">Cr, then I2.</param>
    public static void InverseIctVector(Span<float> c0, Span<float> c1, Span<float> c2)
    {
        int n = c0.Length;
        c1 = c1[..n];
        c2 = c2[..n];
        ref float p0 = ref MemoryMarshal.GetReference(c0);
        ref float p1 = ref MemoryMarshal.GetReference(c1);
        ref float p2 = ref MemoryMarshal.GetReference(c2);
        Vector128<float> crToRed = Vector128.Create(CrToRed);
        Vector128<float> cbToGreen = Vector128.Create(CbToGreen);
        Vector128<float> crToGreen = Vector128.Create(CrToGreen);
        Vector128<float> cbToBlue = Vector128.Create(CbToBlue);
        int i = 0;
        for (; i <= n - 4; i += 4)
        {
            Vector128<float> y = Vector128.LoadUnsafe(ref p0, (nuint)i);
            Vector128<float> cb = Vector128.LoadUnsafe(ref p1, (nuint)i);
            Vector128<float> cr = Vector128.LoadUnsafe(ref p2, (nuint)i);
            (y + (crToRed * cr)).StoreUnsafe(ref p0, (nuint)i);
            (y - (cbToGreen * cb) - (crToGreen * cr)).StoreUnsafe(ref p1, (nuint)i);
            (y + (cbToBlue * cb)).StoreUnsafe(ref p2, (nuint)i);
        }

        if (i < n)
        {
            InverseIctScalar(c0[i..], c1[i..], c2[i..]);
        }
    }
}
