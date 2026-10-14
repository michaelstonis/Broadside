using Broadside.Graphics;

namespace Broadside.Rendering.Icc;

/// <summary>
/// The black points black point compensation maps onto each other: a source profile's, and a destination profile's as Adobe's
/// algorithm finds it, both media-relative PCSXYZ (D50, white Y = 1).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.5.9 (with <c>UseBlackPtComp</c> ON, conversion "according to the provisions in ISO 18619"), PDF 2.0 Application
/// Note 001 (black point compensation aligns the darkest colour the source can describe with the darkest the destination can
/// produce). ISO 18619 is Adobe's published method; it is followed here as Little CMS implements it (<c>cmsDetectBlackPoint</c> and
/// <c>cmsDetectDestinationBlackPoint</c>):
/// </para>
/// <list type="bullet">
/// <item>Device link, abstract and named colour profiles, and the ICC-absolute intent, have no black point (zero).</item>
/// <item>
/// Version 4 profiles under the perceptual and saturation intents: the perceptual reference medium black (ICC.1:2022 §6.3.4.3,
/// Table 16), except matrix/TRC profiles, whose transform serves every intent: their darkest colorant, media-relative.
/// </item>
/// <item>
/// A source output profile of CMYK, media-relative: black ink limits are discounted by the round trip L*a*b* (0, 0, 0) → BToA0 →
/// device → AToB1 → L*a*b*.
/// </item>
/// <item>
/// Otherwise the darkest colorant (gray 0, RGB 0 0 0, CMYK 1 1 1 1) through the intent's transform. The result is made neutral
/// (a* = b* = 0), with L* limited to 50 (and an L* above 95, a negative profile, read as 0).
/// </item>
/// <item>
/// A destination gray, RGB or CMYK profile with the intent's BToA tag: the L* round trip through BToA (the intent) and AToB1 is
/// sampled at 256 levels and made monotonic; under the media-relative intent a ramp that is straight above its shadows keeps the
/// source-style black point, otherwise the shadow section is fitted with a quadratic by least squares and its vertex is the black
/// L*. A destination without that tag is treated as a source.
/// </item>
/// </list>
/// <para>
/// Little CMS 2.19.1 omits the a* = b* = 0 step for the darkest-colorant case (restored after that release); it is applied here, as
/// the method requires a neutral black point.
/// </para>
/// </remarks>
internal static class IccBlackPoint
{
    /// <summary>The perceptual reference medium black point in PCSXYZ (ICC.1:2022 Table 16).</summary>
    public static readonly CieXyz PerceptualBlack = new(0.003357, 0.003479, 0.002869);

    /// <summary>Returns the black point of a profile used as the source of a conversion.</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="intent">The rendering intent.</param>
    /// <returns>The black point; zero when it has none.</returns>
    public static CieXyz OfSource(IccProfile profile, RenderingIntent intent)
    {
        if (!HasBlackPoint(profile, intent))
        {
            return default;
        }

        if (profile.IsVersion4 && intent is RenderingIntent.Perceptual or RenderingIntent.Saturation)
        {
            return profile.IsMatrixShaper ? DarkestColorant(profile, RenderingIntent.RelativeColorimetric) : PerceptualBlack;
        }

        if (intent == RenderingIntent.RelativeColorimetric && profile.Header is { DeviceClass: "prtr", DataColorSpace: "CMYK" })
        {
            return PerceptualRoundTripBlack(profile);
        }

        return DarkestColorant(profile, intent);
    }

    /// <summary>Returns the black point of a profile used as the destination of a conversion (Adobe's algorithm).</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="intent">The rendering intent.</param>
    /// <returns>The black point; zero when it has none or cannot be found.</returns>
    public static CieXyz OfDestination(IccProfile profile, RenderingIntent intent)
    {
        if (!HasBlackPoint(profile, intent))
        {
            return default;
        }

        if (profile.IsVersion4 && intent is RenderingIntent.Perceptual or RenderingIntent.Saturation)
        {
            return profile.IsMatrixShaper ? DarkestColorant(profile, RenderingIntent.RelativeColorimetric) : PerceptualBlack;
        }

        if (!profile.HasBToATag(intent) || profile.Header.DataColorSpace is not ("GRAY" or "RGB " or "CMYK"))
        {
            return OfSource(profile, intent);
        }

        double initialL = 0;
        if (intent == RenderingIntent.RelativeColorimetric)
        {
            initialL = Lab(OfSource(profile, intent)).L;
        }

        Span<double> inRamp = stackalloc double[256];
        Span<double> outRamp = stackalloc double[256];
        for (int l = 0; l < 256; l++)
        {
            inRamp[l] = l * 100.0 / 255;
            outRamp[l] = RoundTrip(profile, intent, (float)inRamp[l]) ?? double.NaN;
            if (double.IsNaN(outRamp[l]))
            {
                return default;
            }
        }

        for (int l = 254; l > 0; l--)
        {
            outRamp[l] = Math.Min(outRamp[l], outRamp[l + 1]);
        }

        double minL = outRamp[0];
        double maxL = outRamp[255];
        if (!(minL < maxL))
        {
            return default;
        }

        if (intent == RenderingIntent.RelativeColorimetric)
        {
            bool straight = true;
            for (int l = 0; l < 256; l++)
            {
                if (!(inRamp[l] <= minL + (0.2 * (maxL - minL)) || Math.Abs(inRamp[l] - outRamp[l]) < 4))
                {
                    straight = false;
                }
            }

            if (straight)
            {
                return Xyz(initialL);
            }
        }

        // The shadow section of the normalized ramp, fitted with a quadratic whose vertex is the black L*.
        (double lo, double hi) = intent == RenderingIntent.RelativeColorimetric ? (0.1, 0.5) : (0.03, 0.25);
        Span<double> x = stackalloc double[256];
        Span<double> y = stackalloc double[256];
        int n = 0;
        for (int l = 0; l < 256; l++)
        {
            double normalized = (outRamp[l] - minL) / (maxL - minL);
            if (normalized >= lo && normalized < hi)
            {
                x[n] = inRamp[l];
                y[n] = normalized;
                n++;
            }
        }

        return n < 3 ? default : Xyz(Math.Max(0, QuadraticRoot(x[..n], y[..n])));
    }

    private static bool HasBlackPoint(IccProfile profile, RenderingIntent intent) =>
        intent != RenderingIntent.AbsoluteColorimetric && profile.Header.DeviceClass is not ("link" or "abst" or "nmcl");

    /// <summary>Whether the profile converts to the PCS under an intent: by that intent's own AToB tag, or by matrix/TRC tags.</summary>
    private static bool SupportsAsInput(IccProfile profile, RenderingIntent intent) => profile.HasAToBTag(intent) || profile.IsMatrixShaper;

    /// <summary>The darkest colorant through the intent's transform, made neutral with L* at most 50.</summary>
    private static CieXyz DarkestColorant(IccProfile profile, RenderingIntent intent)
    {
        if (!SupportsAsInput(profile, intent))
        {
            return default;
        }

        Span<float> device = stackalloc float[4];
        device.Fill(profile.Header.DataColorSpace == "CMYK" ? 1 : 0);
        Span<float> pcs = stackalloc float[3];
        if (!profile.TryConvertToConnectionSpace(device, pcs, intent))
        {
            return default;
        }

        double l = LabOf(profile, pcs);
        return Xyz(l > 95 || !(l >= 0) ? 0 : Math.Min(l, 50));
    }

    /// <summary>L*a*b* (0, 0, 0) through the perceptual BToA tag and back through AToB1, made neutral with L* at most 50.</summary>
    private static CieXyz PerceptualRoundTripBlack(IccProfile profile)
    {
        if (!SupportsAsInput(profile, RenderingIntent.Perceptual))
        {
            return default;
        }

        return RoundTrip(profile, RenderingIntent.Perceptual, 0) is { } l ? Xyz(Math.Min(l, 50)) : default;
    }

    /// <summary>
    /// The L* a neutral L* comes back as after the intent's BToA tag (or BToA0) and the media-relative AToB tag; null when the
    /// profile has neither BToA tag.
    /// </summary>
    private static double? RoundTrip(IccProfile profile, RenderingIntent intent, float lightness)
    {
        if (!profile.HasBToATag(intent) && !profile.HasBToATag(RenderingIntent.Perceptual))
        {
            return null;
        }

        Span<float> pcs = stackalloc float[3];
        Span<float> device = stackalloc float[IccClut.MaxChannels];
        bool lab = profile.Header.ConnectionSpace == "Lab ";
        pcs[0] = lightness;
        pcs[1] = 0;
        pcs[2] = 0;
        if (!lab)
        {
            IccPcs.LabToXyz(pcs[0], pcs[1], pcs[2], out pcs[0], out pcs[1], out pcs[2]);
        }

        if (!profile.TryConvertFromConnectionSpace(pcs, device, intent)
            || !profile.TryConvertToConnectionSpace(device, pcs, RenderingIntent.RelativeColorimetric))
        {
            return null;
        }

        return LabOf(profile, pcs);
    }

    private static double LabOf(IccProfile profile, ReadOnlySpan<float> pcs)
    {
        if (profile.Header.ConnectionSpace == "Lab ")
        {
            return pcs[0];
        }

        IccPcs.XyzToLab(pcs[0], pcs[1], pcs[2], out float l, out _, out _);
        return l;
    }

    private static (double L, double A, double B) Lab(CieXyz xyz)
    {
        IccPcs.XyzToLab((float)xyz.X, (float)xyz.Y, (float)xyz.Z, out float l, out float a, out float b);
        return (l, a, b);
    }

    /// <summary>The neutral of lightness L* relative to the D50 PCS white, as XYZ.</summary>
    private static CieXyz Xyz(double lightness)
    {
        IccPcs.LabToXyz((float)lightness, 0, 0, out float x, out float y, out float z);
        return new CieXyz(x, y, z);
    }

    /// <summary>The larger root of the least-squares quadratic through the points, clipped to [0, 50]; 0 when there is none.</summary>
    private static double QuadraticRoot(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        if (x.Length < 4)
        {
            return 0;
        }

        double sx = 0, sx2 = 0, sx3 = 0, sx4 = 0, sy = 0, syx = 0, syx2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double xn = x[i];
            double x2 = xn * xn;
            sx += xn;
            sx2 += x2;
            sx3 += x2 * xn;
            sx4 += x2 * x2;
            sy += y[i];
            syx += y[i] * xn;
            syx2 += y[i] * x2;
        }

        if (Colorimetry.Invert([x.Length, sx, sx2, sx, sx2, sx3, sx2, sx3, sx4]) is not { } inverse)
        {
            return 0;
        }

        double c = (inverse[0] * sy) + (inverse[1] * syx) + (inverse[2] * syx2);
        double b = (inverse[3] * sy) + (inverse[4] * syx) + (inverse[5] * syx2);
        double a = (inverse[6] * sy) + (inverse[7] * syx) + (inverse[8] * syx2);
        if (Math.Abs(a) < 1e-10)
        {
            return Math.Abs(b) < 1e-10 ? 0 : Math.Max(0, Math.Min(50, -c / b));
        }

        double d = (b * b) - (4 * a * c);
        return d <= 0 ? 0 : Math.Max(0, Math.Min(50, (-b + Math.Sqrt(d)) / (2 * a)));
    }
}
