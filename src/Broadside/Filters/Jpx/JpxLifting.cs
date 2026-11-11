using System.Runtime.Intrinsics;

namespace Broadside.Filters.Jpx;

/// <summary>
/// The element-wise steps of the JPEG 2000 inverse wavelet and component transforms (issue #477): one lifting step of the 5/3 or 9/7
/// filter over a run of samples, the 9/7 scaling, and the inverse RCT and ICT. Each has a scalar method (the reference), a
/// <see cref="Vector128{T}"/> method that matches it bit for bit, and a dispatcher that takes the vector method where
/// <see cref="Vector128.IsHardwareAccelerated"/>.
/// </summary>
/// <remarks>
/// <para>
/// ITU-T T.800 F.3.8.1 (equations F-5 and F-6, the 5/3 lifting), F.3.8.2 (equation F-7 and Table F.4, the 9/7 lifting), G.2.2
/// (equations G-6 to G-8, the RCT) and G.3.2 (equations G-12 to G-14, the ICT). A lifting step updates each sample of one parity
/// from its two neighbours of the other: <see cref="JpxWavelet"/> keeps a signal's low-pass and high-pass samples apart, so a step is
/// a run of targets, a run of left neighbours and a run of right neighbours of the same length (whole rows, for the vertical pass).
/// </para>
/// <para>
/// The 9/7 steps are single precision in the order the scalar methods write them, a multiply then a subtract (never fused): every
/// lane gives the scalar bits. The source is BCL-only, so the Blazor sample's kernel benchmark links it.
/// </para>
/// </remarks>
internal static partial class JpxLifting
{
    /// <summary>The ICT's coefficients (G-12 to G-14).</summary>
    private const float CrToRed = 1.402f;
    private const float CbToGreen = 0.34413f;
    private const float CrToGreen = 0.71414f;
    private const float CbToBlue = 1.772f;

    /// <summary>
    /// F-5, the 5/3 update of the low-pass samples: <c>target[i] -= (left[i] + right[i] + 2) &gt;&gt; 2</c>; the vector path where
    /// <see cref="Vector128"/> is accelerated.
    /// </summary>
    /// <param name="target">The samples updated; its length is the run's.</param>
    /// <param name="left">Each sample's left neighbour, at least as long as <paramref name="target"/>.</param>
    /// <param name="right">Each sample's right neighbour, at least as long as <paramref name="target"/>.</param>
    public static void Lift53Low(Span<int> target, ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            Lift53LowVector(target, left, right);
        }
        else
        {
            Lift53LowScalar(target, left, right);
        }
    }

    /// <summary>The scalar <see cref="Lift53Low"/>: the reference.</summary>
    /// <param name="target">The samples updated.</param>
    /// <param name="left">Each sample's left neighbour.</param>
    /// <param name="right">Each sample's right neighbour.</param>
    public static void Lift53LowScalar(Span<int> target, ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        left = left[..target.Length];
        right = right[..target.Length];
        for (int i = 0; i < target.Length; i++)
        {
            target[i] -= (left[i] + right[i] + 2) >> 2;
        }
    }

    /// <summary>
    /// F-6, the 5/3 update of the high-pass samples: <c>target[i] += (left[i] + right[i]) &gt;&gt; 1</c>; the vector path where
    /// <see cref="Vector128"/> is accelerated.
    /// </summary>
    /// <param name="target">The samples updated; its length is the run's.</param>
    /// <param name="left">Each sample's left neighbour, at least as long as <paramref name="target"/>.</param>
    /// <param name="right">Each sample's right neighbour, at least as long as <paramref name="target"/>.</param>
    public static void Lift53High(Span<int> target, ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            Lift53HighVector(target, left, right);
        }
        else
        {
            Lift53HighScalar(target, left, right);
        }
    }

    /// <summary>The scalar <see cref="Lift53High"/>: the reference.</summary>
    /// <param name="target">The samples updated.</param>
    /// <param name="left">Each sample's left neighbour.</param>
    /// <param name="right">Each sample's right neighbour.</param>
    public static void Lift53HighScalar(Span<int> target, ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        left = left[..target.Length];
        right = right[..target.Length];
        for (int i = 0; i < target.Length; i++)
        {
            target[i] += (left[i] + right[i]) >> 1;
        }
    }

    /// <summary>
    /// One 9/7 lifting step of F-7: <c>target[i] -= factor * (left[i] + right[i])</c>; the vector path where <see cref="Vector128"/>
    /// is accelerated.
    /// </summary>
    /// <param name="target">The samples updated; its length is the run's.</param>
    /// <param name="left">Each sample's left neighbour, at least as long as <paramref name="target"/>.</param>
    /// <param name="right">Each sample's right neighbour, at least as long as <paramref name="target"/>.</param>
    /// <param name="factor">The step's coefficient (Table F.4: alpha, beta, gamma or delta).</param>
    public static void Lift97(Span<float> target, ReadOnlySpan<float> left, ReadOnlySpan<float> right, float factor)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            Lift97Vector(target, left, right, factor);
        }
        else
        {
            Lift97Scalar(target, left, right, factor);
        }
    }

    /// <summary>The scalar <see cref="Lift97"/>: the reference.</summary>
    /// <param name="target">The samples updated.</param>
    /// <param name="left">Each sample's left neighbour.</param>
    /// <param name="right">Each sample's right neighbour.</param>
    /// <param name="factor">The step's coefficient.</param>
    public static void Lift97Scalar(Span<float> target, ReadOnlySpan<float> left, ReadOnlySpan<float> right, float factor)
    {
        left = left[..target.Length];
        right = right[..target.Length];
        for (int i = 0; i < target.Length; i++)
        {
            target[i] -= factor * (left[i] + right[i]);
        }
    }

    /// <summary>The 9/7 scaling of F-7 (K or 1/K): <c>target[i] *= factor</c>; the vector path where <see cref="Vector128"/> is accelerated.</summary>
    /// <param name="target">The samples scaled.</param>
    /// <param name="factor">The factor.</param>
    public static void Scale97(Span<float> target, float factor)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            Scale97Vector(target, factor);
        }
        else
        {
            Scale97Scalar(target, factor);
        }
    }

    /// <summary>The scalar <see cref="Scale97"/>: the reference.</summary>
    /// <param name="target">The samples scaled.</param>
    /// <param name="factor">The factor.</param>
    public static void Scale97Scalar(Span<float> target, float factor)
    {
        for (int i = 0; i < target.Length; i++)
        {
            target[i] *= factor;
        }
    }

    /// <summary>
    /// F.3.3 in one dimension: <c>target[2i] = even[i]</c>, <c>target[2i + 1] = odd[i]</c>, and the last even sample where
    /// <paramref name="even"/> is one longer; the vector path where <see cref="Vector128"/> is accelerated. 32-bit samples of either
    /// kind (the 9/7 floats as their bits).
    /// </summary>
    /// <param name="even">The samples at even positions, as many as <paramref name="odd"/> or one more.</param>
    /// <param name="odd">The samples at odd positions.</param>
    /// <param name="target">The interleaved signal, at least <c>even.Length + odd.Length</c> long.</param>
    public static void Interleave(ReadOnlySpan<int> even, ReadOnlySpan<int> odd, Span<int> target)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            InterleaveVector(even, odd, target);
        }
        else
        {
            InterleaveScalar(even, odd, target);
        }
    }

    /// <summary>The scalar <see cref="Interleave"/>: the reference.</summary>
    /// <param name="even">The samples at even positions.</param>
    /// <param name="odd">The samples at odd positions.</param>
    /// <param name="target">The interleaved signal.</param>
    public static void InterleaveScalar(ReadOnlySpan<int> even, ReadOnlySpan<int> odd, Span<int> target)
    {
        target = target[..(even.Length + odd.Length)];
        for (int i = 0; i < odd.Length; i++)
        {
            target[2 * i] = even[i];
            target[(2 * i) + 1] = odd[i];
        }

        if (even.Length > odd.Length)
        {
            target[^1] = even[odd.Length];
        }
    }

    /// <summary>
    /// The inverse RCT (G-6 to G-8) in place: components 0, 1, 2 hold Y0, Y1, Y2 and come back as I0, I1, I2; the vector path where
    /// <see cref="Vector128"/> is accelerated.
    /// </summary>
    /// <param name="c0">Y0, then I0; its length is the run's.</param>
    /// <param name="c1">Y1, then I1, at least as long as <paramref name="c0"/>.</param>
    /// <param name="c2">Y2, then I2, at least as long as <paramref name="c0"/>.</param>
    public static void InverseRct(Span<int> c0, Span<int> c1, Span<int> c2)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            InverseRctVector(c0, c1, c2);
        }
        else
        {
            InverseRctScalar(c0, c1, c2);
        }
    }

    /// <summary>The scalar <see cref="InverseRct"/>: the reference.</summary>
    /// <param name="c0">Y0, then I0.</param>
    /// <param name="c1">Y1, then I1.</param>
    /// <param name="c2">Y2, then I2.</param>
    public static void InverseRctScalar(Span<int> c0, Span<int> c1, Span<int> c2)
    {
        c1 = c1[..c0.Length];
        c2 = c2[..c0.Length];
        for (int i = 0; i < c0.Length; i++)
        {
            int g = c0[i] - ((c1[i] + c2[i]) >> 2);
            c0[i] = c2[i] + g;
            c2[i] = c1[i] + g;
            c1[i] = g;
        }
    }

    /// <summary>
    /// The inverse ICT (G-12 to G-14) in place: components 0, 1, 2 hold Y, Cb, Cr and come back as I0, I1, I2; the vector path where
    /// <see cref="Vector128"/> is accelerated.
    /// </summary>
    /// <param name="c0">Y, then I0; its length is the run's.</param>
    /// <param name="c1">Cb, then I1, at least as long as <paramref name="c0"/>.</param>
    /// <param name="c2">Cr, then I2, at least as long as <paramref name="c0"/>.</param>
    public static void InverseIct(Span<float> c0, Span<float> c1, Span<float> c2)
    {
        if (Vector128.IsHardwareAccelerated)
        {
            InverseIctVector(c0, c1, c2);
        }
        else
        {
            InverseIctScalar(c0, c1, c2);
        }
    }

    /// <summary>The scalar <see cref="InverseIct"/>: the reference.</summary>
    /// <param name="c0">Y, then I0.</param>
    /// <param name="c1">Cb, then I1.</param>
    /// <param name="c2">Cr, then I2.</param>
    public static void InverseIctScalar(Span<float> c0, Span<float> c1, Span<float> c2)
    {
        c1 = c1[..c0.Length];
        c2 = c2[..c0.Length];
        for (int i = 0; i < c0.Length; i++)
        {
            float y = c0[i];
            float cb = c1[i];
            float cr = c2[i];
            c0[i] = y + (CrToRed * cr);
            c1[i] = y - (CbToGreen * cb) - (CrToGreen * cr);
            c2[i] = y + (CbToBlue * cb);
        }
    }
}
