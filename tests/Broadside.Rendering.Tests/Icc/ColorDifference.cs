namespace Broadside.Rendering.Tests.Icc;

/// <summary>Colour differences for comparing the engine with reference values: CIEDE2000 and the conversions it needs.</summary>
/// <remarks>
/// CIEDE2000 as in CIE 15:2004 §8.3.1 (Sharma, Wu and Dalal's formulation); sRGB to Lab through IEC 61966-2-1 and the D65 white, and
/// PCSXYZ to Lab against the D50 PCS white (ICC.1:2022 Annex A). Independent of the engine's own code.
/// </remarks>
internal static class ColorDifference
{
    private static readonly double[] D50 = [0.9642, 1.0, 0.8249];
    private static readonly double[] D65 = [0.95047, 1.0, 1.08883];

    /// <summary>The CIEDE2000 difference of two L*a*b* colours.</summary>
    public static double DeltaE2000(ReadOnlySpan<double> lab1, ReadOnlySpan<double> lab2)
    {
        double c1 = Math.Sqrt((lab1[1] * lab1[1]) + (lab1[2] * lab1[2]));
        double c2 = Math.Sqrt((lab2[1] * lab2[1]) + (lab2[2] * lab2[2]));
        double meanC7 = Math.Pow((c1 + c2) / 2, 7);
        double g = 0.5 * (1 - Math.Sqrt(meanC7 / (meanC7 + Math.Pow(25, 7))));
        double a1 = (1 + g) * lab1[1];
        double a2 = (1 + g) * lab2[1];
        double cp1 = Math.Sqrt((a1 * a1) + (lab1[2] * lab1[2]));
        double cp2 = Math.Sqrt((a2 * a2) + (lab2[2] * lab2[2]));
        double h1 = Hue(lab1[2], a1);
        double h2 = Hue(lab2[2], a2);

        double dL = lab2[0] - lab1[0];
        double dC = cp2 - cp1;
        double dh = cp1 * cp2 == 0 ? 0 : h2 - h1;
        if (dh > 180)
        {
            dh -= 360;
        }
        else if (dh < -180)
        {
            dh += 360;
        }

        double dH = 2 * Math.Sqrt(cp1 * cp2) * Math.Sin(Radians(dh / 2));
        double meanL = (lab1[0] + lab2[0]) / 2;
        double meanC = (cp1 + cp2) / 2;
        double meanH = h1 + h2;
        if (cp1 * cp2 != 0)
        {
            meanH = Math.Abs(h1 - h2) <= 180 ? meanH / 2 : (meanH < 360 ? (meanH + 360) / 2 : (meanH - 360) / 2);
        }

        double t = 1 - (0.17 * Math.Cos(Radians(meanH - 30))) + (0.24 * Math.Cos(Radians(2 * meanH)))
            + (0.32 * Math.Cos(Radians((3 * meanH) + 6))) - (0.20 * Math.Cos(Radians((4 * meanH) - 63)));
        double dTheta = 30 * Math.Exp(-Math.Pow((meanH - 275) / 25, 2));
        double meanC7p = Math.Pow(meanC, 7);
        double rc = 2 * Math.Sqrt(meanC7p / (meanC7p + Math.Pow(25, 7)));
        double sl = 1 + (0.015 * Math.Pow(meanL - 50, 2) / Math.Sqrt(20 + Math.Pow(meanL - 50, 2)));
        double sc = 1 + (0.045 * meanC);
        double sh = 1 + (0.015 * meanC * t);
        double rt = -Math.Sin(Radians(2 * dTheta)) * rc;
        return Math.Sqrt(Math.Pow(dL / sl, 2) + Math.Pow(dC / sc, 2) + Math.Pow(dH / sh, 2) + (rt * (dC / sc) * (dH / sh)));
    }

    /// <summary>The L*a*b* (D65 white) of an encoded sRGB colour, components 0 to 255.</summary>
    public static double[] LabOfSrgb(ReadOnlySpan<float> rgb255)
    {
        double r = Linear(rgb255[0] / 255.0);
        double g = Linear(rgb255[1] / 255.0);
        double b = Linear(rgb255[2] / 255.0);
        double x = (0.4124564 * r) + (0.3575761 * g) + (0.1804375 * b);
        double y = (0.2126729 * r) + (0.7151522 * g) + (0.0721750 * b);
        double z = (0.0193339 * r) + (0.1191920 * g) + (0.9503041 * b);
        return Lab(x, y, z, D65);
    }

    /// <summary>The L*a*b* of a PCSXYZ colour against the D50 PCS white.</summary>
    public static double[] LabOfPcsXyz(ReadOnlySpan<double> xyz) => Lab(xyz[0], xyz[1], xyz[2], D50);

    private static double[] Lab(double x, double y, double z, double[] white)
    {
        double fx = F(x / white[0]);
        double fy = F(y / white[1]);
        double fz = F(z / white[2]);
        return [(116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz)];
    }

    private static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : ((24389.0 / 27 * t) + 16) / 116;

    private static double Linear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    private static double Hue(double b, double a)
    {
        if (a == 0 && b == 0)
        {
            return 0;
        }

        double h = Math.Atan2(b, a) * 180 / Math.PI;
        return h < 0 ? h + 360 : h;
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}
