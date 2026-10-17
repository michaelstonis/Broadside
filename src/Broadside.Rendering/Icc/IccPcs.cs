namespace Broadside.Rendering.Icc;

/// <summary>The profile connection space encodings a lookup table's PCS side uses, and the PCSLAB to PCSXYZ conversion.</summary>
/// <remarks>
/// ICC.1:2022 §6.3.4.2: PCSXYZ as u1Fixed15Number (Table 11, 8000h is 1.0), PCSLAB in the 8-bit and 16-bit encodings (Tables 12
/// and 13: FFh or FFFFh is L* 100, 80h or 8080h is a* 0); §10.10: lut16Type's legacy 16-bit PCSLAB (Tables 42 and 43: FF00h is
/// L* 100, 8000h is a* 0); §10.11: lut8Type's PCSLAB is the 8-bit encoding, and lut8Type PCSXYZ, which the specification leaves to
/// the implementation, is read as the 16-bit encoding scaled. CIE L*a*b* is relative to the D50 PCS white (§6.3.4.3, Annex A).
/// </remarks>
internal static class IccPcs
{
    /// <summary>u1Fixed15Number: a normalized 16-bit value of 1 is 65535 / 32768.</summary>
    private const float XyzScale = 65535f / 32768f;

    /// <summary>lut16Type legacy PCSLAB: a normalized value of 1 is FFFFh, which is FF00h × 65535 / 65280 (§10.10).</summary>
    private const float LegacyScale = 65535f / 65280f;

    private const float White0 = 0.9642f;
    private const float White2 = 0.8249f;

    /// <summary>Converts a lookup table's normalized PCS-side values to PCS values: L*, a*, b* or X, Y, Z.</summary>
    public static void Decode(IccLutKind kind, bool lab, ReadOnlySpan<float> normalized, Span<float> pcs)
    {
        if (!lab)
        {
            pcs[0] = normalized[0] * XyzScale;
            pcs[1] = normalized[1] * XyzScale;
            pcs[2] = normalized[2] * XyzScale;
            return;
        }

        float scale = kind == IccLutKind.Lut16 ? LegacyScale : 1;
        pcs[0] = normalized[0] * scale * 100;
        pcs[1] = (normalized[1] * scale * 255) - 128;
        pcs[2] = (normalized[2] * scale * 255) - 128;
    }

    /// <summary>Converts PCS values to a lookup table's normalized PCS-side values (clipped later, by the table).</summary>
    public static void Encode(IccLutKind kind, bool lab, ReadOnlySpan<float> pcs, Span<float> normalized)
    {
        if (!lab)
        {
            normalized[0] = pcs[0] / XyzScale;
            normalized[1] = pcs[1] / XyzScale;
            normalized[2] = pcs[2] / XyzScale;
            return;
        }

        float scale = kind == IccLutKind.Lut16 ? LegacyScale : 1;
        normalized[0] = pcs[0] / 100 / scale;
        normalized[1] = (pcs[1] + 128) / 255 / scale;
        normalized[2] = (pcs[2] + 128) / 255 / scale;
    }

    /// <summary>CIE L*a*b* relative to the D50 PCS white, to XYZ (CIE 15:2004 §8.2.1, inverted).</summary>
    public static void LabToXyz(float l, float a, float b, out float x, out float y, out float z)
    {
        float fy = (l + 16) / 116;
        x = White0 * Inverse(fy + (a / 500));
        y = Inverse(fy);
        z = White2 * Inverse(fy - (b / 200));
    }

    /// <summary>XYZ to CIE L*a*b* relative to the D50 PCS white (CIE 15:2004 §8.2.1).</summary>
    public static void XyzToLab(float x, float y, float z, out float l, out float a, out float b)
    {
        float fx = Forward(x / White0);
        float fy = Forward(y);
        float fz = Forward(z / White2);
        l = (116 * fy) - 16;
        a = 500 * (fx - fy);
        b = 200 * (fy - fz);
    }

    private static float Inverse(float t) => t > 6f / 29 ? t * t * t : 3 * (36f / 841) * (t - (4f / 29));

    private static float Forward(float t) => t > 216f / 24389 ? MathF.Cbrt(t) : ((24389f / 27 * t) + 16) / 116;
}
