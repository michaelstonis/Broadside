using Broadside.Graphics;
using Broadside.Rendering.Icc;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests.Icc;

/// <summary>
/// Lookup-table profiles (ICC.1:2022 §8.10.2, §10.10 lut16Type, §10.11 lut8Type, §10.12 lutAToBType, §10.13 lutBToAType, §10.22
/// s15Fixed16ArrayType), driven by <c>icc-lut.pdf</c>: converted by the ICC engine through the public document API (ISO 32000-2
/// §8.6.5.5), and to and from the profile connection space through <see cref="IccProfile"/>, against Little CMS
/// (<see cref="IccLutReferences"/>).
/// </summary>
public sealed class IccLutTests
{
    [Theory]
    [MemberData(nameof(IccLutReferences.Srgb), MemberType = typeof(IccLutReferences))]
    public void Lookup_table_profiles_convert_to_sRGB_within_one_delta_E_2000_of_Little_CMS(string resource, float[] components, float[] expected)
    {
        using PdfDocument document = Open();
        PdfColorSpace space = IccTesting.Space(document, resource);
        float[] rgb = new float[3];

        document.GetColorConverter(space).Convert(components, rgb);

        float[] rgb255 = [rgb[0] * 255, rgb[1] * 255, rgb[2] * 255];
        double difference = ColorDifference.DeltaE2000(ColorDifference.LabOfSrgb(rgb255), ColorDifference.LabOfSrgb(expected));
        Assert.True(difference <= 1.0, $"ΔE2000 {difference:F3}: ({string.Join(", ", rgb255)}) against ({string.Join(", ", expected)})");
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(IccLutReferences.ConnectionSpace), MemberType = typeof(IccLutReferences))]
    public void Lookup_tables_convert_to_the_connection_space_by_intent_within_one_delta_E_2000_of_Little_CMS(
        string resource, RenderingIntent intent, float[] components, float[] expected)
    {
        IccProfile profile = Profile(resource);
        float[] pcs = new float[3];

        Assert.True(profile.TryConvertToConnectionSpace(components, pcs, intent));

        double difference = ColorDifference.DeltaE2000(Lab(profile, pcs), Lab(profile, expected));
        Assert.True(difference <= 1.0, $"ΔE2000 {difference:F3}: ({string.Join(", ", pcs)}) against ({string.Join(", ", expected)})");
    }

    [Theory]
    [MemberData(nameof(IccLutReferences.FromConnectionSpace), MemberType = typeof(IccLutReferences))]
    public void BToA_tables_convert_from_the_connection_space_as_Little_CMS_does(string resource, float[] connection, float[] expected)
    {
        IccProfile profile = Profile(resource);
        float[] device = new float[expected.Length];

        Assert.True(profile.TryConvertFromConnectionSpace(connection, device));

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(device[i], expected[i] - 0.005f, expected[i] + 0.005f);
        }
    }

    [Fact]
    public void The_intent_chooses_the_lookup_table_and_a_missing_one_falls_back_to_AToB0()
    {
        IccProfile profile = Profile("CS1");
        float[] perceptual = new float[3];
        float[] relative = new float[3];
        float[] saturation = new float[3];
        float[] black = [0, 0, 0, 1];

        // §8.10.2 and Table 25: AToB0 (a lighter press) is perceptual, AToB1 colorimetric; no AToB2, so saturation uses AToB0.
        Assert.True(profile.TryConvertToConnectionSpace(black, perceptual, RenderingIntent.Perceptual));
        Assert.True(profile.TryConvertToConnectionSpace(black, relative, RenderingIntent.RelativeColorimetric));
        Assert.True(profile.TryConvertToConnectionSpace(black, saturation, RenderingIntent.Saturation));
        Assert.InRange(perceptual[0], 61f, 62f);
        Assert.InRange(relative[0], 37f, 38f);
        Assert.Equal(perceptual, saturation);
    }

    [Fact]
    public void Profiles_report_their_lookup_tables_and_chromatic_adaptation()
    {
        IccProfile cs3 = Profile("CS3");

        Assert.All(new[] { "CS0", "CS1", "CS2", "CS3", "CS4" }, resource => Assert.Equal(IccProfileModel.LookupTable, Profile(resource).Model));
        double[] bradford = [1.0478, 0.0229, -0.0501, 0.0295, 0.9905, -0.0171, -0.0092, 0.0151, 0.7519];
        Assert.Equal(9, cs3.ChromaticAdaptation!.Count);
        Assert.All(Enumerable.Range(0, 9), i => Assert.Equal(bradford[i], cs3.ChromaticAdaptation[i], 4));
        Assert.Null(Profile("CS1").ChromaticAdaptation);
        Assert.False(Profile("CS2").TryConvertFromConnectionSpace([50, 0, 0], new float[3]));
    }

    [Fact]
    public void The_colour_difference_reproduces_Sharma_s_test_data()
    {
        // Sharma, Wu and Dalal (2005), Table 1, pairs 1, 17 and 25.
        Assert.Equal(2.0425, ColorDifference.DeltaE2000([50, 2.6772, -79.7751], [50, 0, -82.7485]), 4);
        Assert.Equal(27.1492, ColorDifference.DeltaE2000([50, 2.5, 0], [73, 25, -18]), 4);
        Assert.Equal(2.0373, ColorDifference.DeltaE2000([22.7233, 20.0904, -46.6940], [23.0331, 14.9730, -42.5619]), 4);
    }

    private static PdfDocument Open() => PdfDocument.Open(Corpus.Path("icc-lut.pdf"), new PdfOptions().UseIccColorManagement());

    private static IccProfile Profile(string resource)
    {
        using PdfDocument document = Open();
        var space = (PdfIccBasedColorSpace)IccTesting.Space(document, resource);
        Assert.True(IccProfile.TryParse(space.GetProfileData().Span, out IccProfile? profile, out string? problem), problem);
        return profile;
    }

    /// <summary>PCS values as L*a*b*: as they are for a PCSLAB profile, converted from XYZ for a PCSXYZ one.</summary>
    private static double[] Lab(IccProfile profile, float[] pcs) =>
        profile.Header.ConnectionSpace == "Lab " ? [pcs[0], pcs[1], pcs[2]] : ColorDifference.LabOfPcsXyz([pcs[0], pcs[1], pcs[2]]);
}
