using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests.Icc;

/// <summary>
/// Matrix/TRC profiles converted by the ICC engine through the public document API (ISO 32000-2 §8.6.5.5; ICC.1:2022 §10.6,
/// §10.18, §10.31, Annex F.2 and F.3), driven by <c>icc-rgb-matrix.pdf</c>.
/// </summary>
public sealed class IccMatrixTests
{
    /// <summary>
    /// Media-relative colorimetric sRGB values, 0 to 255, of every colour the corpus file paints: computed from the profile maths
    /// and cross-checked against Little CMS 2.19 (largest difference 0.12) by <c>tools/IccReference/reference.py</c>.
    /// </summary>
    public static TheoryData<string, float[], float[]> References => new()
    {
        { "CS0", [0f, 0f, 0f], [0.006f, 0.000f, 51.644f] },
        { "CS0", [1f, 1f, 1f], [254.995f, 255.000f, 246.955f] },
        { "CS0", [1f, 0f, 0f], [255.000f, 0.024f, 51.678f] },
        { "CS0", [0f, 1f, 0f], [0.000f, 255.000f, 0.000f] },
        { "CS0", [0f, 0f, 1f], [0.168f, 0.000f, 251.904f] },
        { "CS0", [0.5f, 0.5f, 0.5f], [154.207f, 121.940f, 145.131f] },
        { "CS0", [0.25f, 0.6f, 0.9f], [0.000f, 149.252f, 229.801f] },
        { "CS0", [0.03f, 0.04f, 0.02f], [8.365f, 0.000f, 53.399f] },
        { "CS1", [0f, 0f, 0f], [0.000f, 0.000f, 0.000f] },
        { "CS1", [1f, 1f, 1f], [255.000f, 254.990f, 255.000f] },
        { "CS1", [1f, 0f, 0f], [255.000f, 0.000f, 0.000f] },
        { "CS1", [0f, 1f, 0f], [0.115f, 255.000f, 0.119f] },
        { "CS1", [0f, 0f, 1f], [0.368f, 0.000f, 255.000f] },
        { "CS1", [0.5f, 0.5f, 0.5f], [128.507f, 145.893f, 187.541f] },
        { "CS1", [0.25f, 0.6f, 0.9f], [61.544f, 169.335f, 243.478f] },
        { "CS1", [0.03f, 0.04f, 0.02f], [1.552f, 10.005f, 38.691f] },
        { "CS2", [0f], [0.000f, 0.000f, 0.000f] },
        { "CS2", [0.02f], [5.100f, 5.100f, 5.100f] },
        { "CS2", [0.03f], [7.649f, 7.649f, 7.649f] },
        { "CS2", [0.25f], [63.752f, 63.752f, 63.752f] },
        { "CS2", [0.5f], [127.501f, 127.501f, 127.501f] },
        { "CS2", [0.75f], [191.251f, 191.251f, 191.251f] },
        { "CS2", [1f], [255.000f, 255.000f, 255.000f] },
        { "CS3", [0f], [3.318f, 3.318f, 3.318f] },
        { "CS3", [0.02f], [8.589f, 8.589f, 8.589f] },
        { "CS3", [0.03f], [11.188f, 11.188f, 11.188f] },
        { "CS3", [0.25f], [58.663f, 58.663f, 58.663f] },
        { "CS3", [0.5f], [119.950f, 119.950f, 119.950f] },
        { "CS3", [0.75f], [185.245f, 185.245f, 185.245f] },
        { "CS3", [1f], [252.657f, 252.657f, 252.657f] },
    };

    [Theory]
    [MemberData(nameof(References))]
    public void Matrix_TRC_profiles_convert_within_half_a_level_of_the_profile_maths(string resource, float[] components, float[] expected)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("icc-rgb-matrix.pdf"), new PdfOptions().UseIccColorManagement());
        PdfColorSpace space = IccTesting.Space(document, resource);
        float[] rgb = new float[3];

        document.GetColorConverter(space, IccTesting.WithoutCompensation).Convert(components, rgb);

        for (int i = 0; i < 3; i++)
        {
            Assert.InRange(rgb[i] * 255, expected[i] - 0.5f, expected[i] + 0.5f);
        }

        Assert.Empty(document.Diagnostics);
    }
}
