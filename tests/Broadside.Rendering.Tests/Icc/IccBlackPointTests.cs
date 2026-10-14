using Broadside.Graphics;
using Broadside.Rendering.Icc;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests.Icc;

/// <summary>
/// Black point compensation (ISO 32000-2 §8.6.5.9, PDF 2.0 Application Note 001, ISO 18619) in the ICC engine, through the public
/// document API: conversions to sRGB and to a CMYK output intent profile against Little CMS (<see cref="IccBlackPointReferences"/>),
/// and how UseBlackPtComp, the absolute intent and the engine's default decide whether it applies.
/// </summary>
public sealed class IccBlackPointTests
{
    [Theory]
    [MemberData(nameof(IccBlackPointReferences.Srgb), MemberType = typeof(IccBlackPointReferences))]
    public void Black_point_compensation_to_sRGB_is_within_one_delta_E_2000_of_Little_CMS(
        string file, string resource, RenderingIntent intent, float[] components, float[] expected)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(file), new PdfOptions().UseIccColorManagement());
        PdfColorSpace space = IccTesting.Space(document, resource);
        float[] rgb = new float[3];

        document.GetColorConverter(space, Compensated(intent)).Convert(components, rgb);

        float[] rgb255 = [rgb[0] * 255, rgb[1] * 255, rgb[2] * 255];
        double difference = ColorDifference.DeltaE2000(ColorDifference.LabOfSrgb(rgb255), ColorDifference.LabOfSrgb(expected));
        Assert.True(difference <= 1.0, $"ΔE2000 {difference:F3}: ({string.Join(", ", rgb255)}) against ({string.Join(", ", expected)})");
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(IccBlackPointReferences.OutputIntentCmyk), MemberType = typeof(IccBlackPointReferences))]
    public void Black_point_compensation_to_a_CMYK_output_intent_profile_is_within_one_percent_of_Little_CMS(
        string file, string resource, RenderingIntent intent, float[] components, float[] expected)
    {
        byte[] profile;
        using (PdfDocument source = PdfDocument.Open(Corpus.Path(file)))
        {
            profile = ((PdfIccBasedColorSpace)IccTesting.Space(source, resource)).GetProfileData().ToArray();
        }

        using PdfDocument document = PdfDocument.Open(Corpus.Path("output-intent-cmyk.pdf"), new PdfOptions().UseIccColorManagement());
        PdfColorSpace space = document.GetColorSpace(IccProfiles.Space(profile, components.Length));
        var conversion = Compensated(intent) with { Target = DeviceColorModel.Cmyk, OutputIntent = document.OutputIntents[0] };
        float[] cmyk = new float[4];

        document.GetColorConverter(space, conversion).Convert(components, cmyk);

        for (int i = 0; i < 4; i++)
        {
            Assert.InRange(cmyk[i], expected[i] - 0.01f, expected[i] + 0.01f);
        }
    }

    [Fact]
    public void UseBlackPtComp_OFF_leaves_the_source_black_and_ON_and_Default_map_it_to_the_display_black()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("rendering-intents.pdf"), new PdfOptions().UseIccColorManagement());
        PdfColorSpace space = IccTesting.Space(document, "CS0");
        float[] black = [1, 1, 1, 1];

        float[] off = Convert(document, space, black, new ColorConversion { BlackPointCompensation = BlackPointCompensation.Off });
        float[] on = Convert(document, space, black, new ColorConversion { BlackPointCompensation = BlackPointCompensation.On });
        float[] byDefault = Convert(document, space, black, new ColorConversion());

        // The press's darkest ink is far from L* 0: without compensation it shows as a dark gray; with it, as the display's black.
        Assert.All(off, value => Assert.InRange(value, 0.08f, 0.6f));
        Assert.All(on, value => Assert.InRange(value, 0f, 0.01f));
        Assert.Equal(on, byDefault);
    }

    [Fact]
    public void The_engine_decides_Default_and_never_compensates_under_the_absolute_intent()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("rendering-intents.pdf"), new PdfOptions().UseIccColorManagement());
        PdfColorSpace space = IccTesting.Space(document, "CS0");
        float[] dark = [0.6f, 0.4f, 0.4f, 1];
        var engine = new IccColorManagement().WithDefaultBlackPointCompensation(false);
        using PdfDocument withoutByDefault = PdfDocument.Open(Corpus.Path("rendering-intents.pdf"), new PdfOptions().UseColorManagement(engine));
        PdfColorSpace same = IccTesting.Space(withoutByDefault, "CS0");

        Assert.True(new IccColorManagement().CompensatesBlackPointByDefault);
        Assert.False(engine.CompensatesBlackPointByDefault);
        Assert.Equal(
            Convert(document, space, dark, new ColorConversion { BlackPointCompensation = BlackPointCompensation.Off }),
            Convert(withoutByDefault, same, dark, new ColorConversion()));
        Assert.NotEqual(
            Convert(document, space, dark, new ColorConversion { BlackPointCompensation = BlackPointCompensation.Off }),
            Convert(document, space, dark, new ColorConversion()));

        // §8.6.5.9: "If the current render intent of an object is AbsColorimetric then the value of UseBlackPtComp shall be treated as OFF".
        var absolute = new ColorConversion { Intent = RenderingIntent.AbsoluteColorimetric };
        Assert.Equal(
            Convert(document, space, dark, absolute with { BlackPointCompensation = BlackPointCompensation.Off }),
            Convert(document, space, dark, absolute with { BlackPointCompensation = BlackPointCompensation.On }));
    }

    private static ColorConversion Compensated(RenderingIntent intent) => new() { Intent = intent, BlackPointCompensation = BlackPointCompensation.On };

    private static float[] Convert(PdfDocument document, PdfColorSpace space, float[] components, ColorConversion conversion)
    {
        float[] rgb = new float[3];
        document.GetColorConverter(space, conversion).Convert(components, rgb);
        return rgb;
    }
}
