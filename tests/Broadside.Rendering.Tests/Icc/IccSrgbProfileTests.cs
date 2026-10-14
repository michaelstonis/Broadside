using Broadside.Graphics;
using Broadside.Rendering.Icc;
using static Broadside.Rendering.Tests.Icc.IccProfiles;

namespace Broadside.Rendering.Tests.Icc;

/// <summary>
/// The in-house sRGB profile (IEC 61966-2-1 as an ICC.1:2022 version 4 display profile), the ICC engine's default RGB target
/// (ISO 32000-2 §10.3): what it holds, and that colours described by it come back unchanged.
/// </summary>
public sealed class IccSrgbProfileTests
{
    [Fact]
    public void The_in_house_sRGB_profile_is_a_version_4_display_profile_with_the_D50_adapted_sRGB_primaries()
    {
        IccProfile srgb = IccProfile.Srgb;

        Assert.Equal(new Version(4, 4, 0), srgb.Header.Version);
        Assert.Equal("mntr", srgb.Header.DeviceClass);
        Assert.Equal("RGB ", srgb.Header.DataColorSpace);
        Assert.Equal("XYZ ", srgb.Header.ConnectionSpace);
        Assert.Equal(IccProfileModel.MatrixTrc, srgb.Model);
        Assert.Equal(0.9642, srgb.MediaWhitePoint.X, 0.0001);
        Assert.Equal(0.8249, srgb.MediaWhitePoint.Z, 0.0001);

        // The Bradford D65 to D50 matrix (ICC.1:2022 Annex E) and the colorants of the ICC's own sRGB v4 profile.
        double[] bradford = [1.0479, 0.0229, -0.0502, 0.0296, 0.9904, -0.0171, -0.0092, 0.0151, 0.7519];
        Assert.NotNull(srgb.ChromaticAdaptation);
        for (int i = 0; i < 9; i++)
        {
            Assert.Equal(bradford[i], srgb.ChromaticAdaptation[i], 0.0005);
        }

        AssertPcs(srgb, [1, 0, 0], [0.4361, 0.2225, 0.0139]);
        AssertPcs(srgb, [0, 1, 0], [0.3851, 0.7169, 0.0971]);
        AssertPcs(srgb, [0, 0, 1], [0.1431, 0.0606, 0.7141]);

        // IEC 61966-2-1: the encoded 0.5 is linear 0.21404, and 0.04 is on the straight segment, 0.04 / 12.92.
        AssertPcs(srgb, [0.5f, 0.5f, 0.5f], [0.9642 * 0.21404, 0.21404, 0.8249 * 0.21404]);
        AssertPcs(srgb, [0.04f, 0.04f, 0.04f], [0.9642 * 0.04 / 12.92, 0.04 / 12.92, 0.8249 * 0.04 / 12.92]);
    }

    [Fact]
    public void The_profile_data_reads_back_as_the_same_profile()
    {
        byte[] data = IccProfile.CreateSrgbProfileData();

        Assert.True(IccProfile.TryParse(data, out IccProfile? profile, out string? problem), problem);
        Assert.Equal(data.Length, (int)profile.Header.ProfileSize);
        Assert.Equal(0, data.Length % 4);
        Assert.Equal(IccProfile.Srgb.Header.Version, profile.Header.Version);
        Assert.NotSame(data, IccProfile.CreateSrgbProfileData());
    }

    [Fact]
    public void Colours_described_by_the_in_house_profile_convert_to_sRGB_unchanged()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseIccColorManagement());
        PdfColorSpace space = document.GetColorSpace(Space(IccProfile.CreateSrgbProfileData(), 3));

        foreach (float[] color in new[] { new[] { 0f, 0f, 0f }, [1f, 1f, 1f], [1f, 0f, 0f], [0.2f, 0.5f, 0.8f], [0.03f, 0.9f, 0.4f] })
        {
            float[] rgb = document.Rgb(space, color);
            for (int i = 0; i < 3; i++)
            {
                Assert.InRange(rgb[i], color[i] - (0.5f / 255), color[i] + (0.5f / 255));
            }
        }

        Assert.Empty(document.Diagnostics);
    }

    private static void AssertPcs(IccProfile profile, float[] device, double[] expected)
    {
        float[] pcs = new float[3];
        Assert.True(profile.TryConvertToConnectionSpace(device, pcs));
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(expected[i], pcs[i], 0.0003);
        }
    }
}
