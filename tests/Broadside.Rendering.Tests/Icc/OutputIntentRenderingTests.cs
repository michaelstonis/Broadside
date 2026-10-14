using Broadside.Graphics;
using Broadside.Rendering.Icc;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests.Icc;

/// <summary>
/// Output intents when rendering (ISO 32000-2 §14.11.5) with the ICC engine: DeviceCMYK fills of <c>output-intent-cmyk.pdf</c>
/// render through the output intent's profile, a page's own output intent takes precedence, other device spaces and documents
/// without one keep the managed default (open decision 6: the characterised CMYK polynomial), and a CMYK target is the profile's
/// own device.
/// </summary>
public sealed class OutputIntentRenderingTests
{
    /// <summary>The DeviceCMYK colours each page paints, left to right in 50 × 50 squares from x 40, y 680 (generate.py OUTPUT_INTENT_SAMPLES).</summary>
    private static readonly float[][] Samples = [[0, 0, 0, 0], [1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1], [0.2f, 0.4f, 0.6f, 0.1f], [0.6f, 0.4f, 0.4f, 1]];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DeviceCMYK_fills_render_through_the_output_intent_profile_in_effect(int page)
    {
        using PdfDocument document = Open(new IccColorManagement());
        PdfIccBasedColorSpace profile = (page == 0 ? document.OutputIntents : document.Pages[page].OutputIntents)[0].DestinationProfile!;
        PdfColorConverter converter = document.GetColorConverter(profile, new ColorConversion());
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[page]);

        for (int i = 0; i < Samples.Length; i++)
        {
            float[] rgb = new float[3];
            converter.Convert(Samples[i], rgb);
            Rgba pixel = Square(bitmap, i);
            Assert.InRange(pixel.R, Level(rgb[0]) - 1, Level(rgb[0]) + 1);
            Assert.InRange(pixel.G, Level(rgb[1]) - 1, Level(rgb[1]) + 1);
            Assert.InRange(pixel.B, Level(rgb[2]) - 1, Level(rgb[2]) + 1);
        }

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_output_intent_differs_measurably_from_the_polynomial_default_and_a_page_s_own_from_the_document_s()
    {
        using PdfDocument icc = Open(new IccColorManagement());
        using PdfDocument managed = Open(ManagedColorManagement.Default);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap withIntent = rasterizer.Render(icc.Pages[0]);
        using Bitmap pageIntent = rasterizer.Render(icc.Pages[1]);
        using Bitmap polynomial = rasterizer.Render(managed.Pages[0]);

        // Solid cyan, magenta, black and the rich black: each more than 5 ΔE2000 from the default's rendering (the made-up press's
        // yellow happens to be close to SWOP's). The page's own output intent is newsprint: its paper is darker, and its black ink
        // alone stays a mid gray where the press's reaches the display black (black point compensation maps the rich blacks of
        // both to black).
        foreach (int i in new[] { 1, 2, 4, 6 })
        {
            Assert.True(Difference(Square(withIntent, i), Square(polynomial, i)) > 5, $"square {i} matches the polynomial default");
        }

        Assert.True(Difference(Square(withIntent, 4), Square(pageIntent, 4)) > 20, "the black ink renders alike through both output intents");
        Assert.True(Square(withIntent, 0).R > Square(pageIntent, 0).R, "the newsprint's paper renders as light as the press's");
    }

    [Fact]
    public void Other_device_spaces_and_documents_without_an_output_intent_keep_the_managed_default()
    {
        using var rasterizer = new SoftwareRasterizer();
        using PdfDocument icc = Open(new IccColorManagement());
        using PdfDocument managed = Open(ManagedColorManagement.Default);
        using PdfDocument disregarded = Open(new IccColorManagement().WithOutputIntents(false));
        using Bitmap withIntent = rasterizer.Render(icc.Pages[0]);
        using Bitmap polynomial = rasterizer.Render(managed.Pages[0]);
        using Bitmap withoutIntent = rasterizer.Render(disregarded.Pages[0]);

        // The DeviceRGB and DeviceGray squares below the row: a CMYK output intent does not describe them.
        Assert.Equal(polynomial.At(65, 167), withIntent.At(65, 167));
        Assert.Equal(polynomial.At(130, 167), withIntent.At(130, 167));
        Assert.Equal(polynomial.Pixels.ToArray(), withoutIntent.Pixels.ToArray());

        using PdfDocument plain = PdfDocument.Open(Corpus.Path("paths-fill-rules.pdf"), new PdfOptions().UseIccColorManagement());
        using PdfDocument plainManaged = PdfDocument.Open(Corpus.Path("paths-fill-rules.pdf"));
        using Bitmap plainIcc = rasterizer.Render(plain.Pages[0]);
        using Bitmap plainDefault = rasterizer.Render(plainManaged.Pages[0]);
        Assert.Equal(plainDefault.Pixels.ToArray(), plainIcc.Pixels.ToArray());
    }

    [Fact]
    public void Device_colours_of_the_output_intent_s_own_device_pass_through_unchanged()
    {
        using PdfDocument document = Open(new IccColorManagement());
        PdfOutputIntent intent = document.OutputIntents[0];
        var conversion = new ColorConversion { Target = DeviceColorModel.Cmyk, OutputIntent = intent };
        float[] cmyk = new float[4];

        document.GetColorConverter(PdfDeviceCmykColorSpace.Instance, conversion).Convert(Samples[5], cmyk);
        Assert.Equal(Samples[5], cmyk);

        // An ICCBased space holding the output intent's own profile describes the device's colours too.
        PdfColorSpace same = document.GetColorSpace(IccProfiles.Space(intent.DestinationProfile!.GetProfileData().ToArray(), 4));
        document.GetColorConverter(same, conversion).Convert(Samples[6], cmyk);
        Assert.Equal(Samples[6], cmyk);
    }

    private static PdfDocument Open(IColorManagement management) =>
        PdfDocument.Open(Corpus.Path("output-intent-cmyk.pdf"), new PdfOptions().UseColorManagement(management));

    /// <summary>The pixel at the centre of square <paramref name="index"/> at 72 DPI (y down from the top of the 792 pt page).</summary>
    private static Rgba Square(Bitmap bitmap, int index) => bitmap.At(40 + (65 * index) + 25, 792 - 680 - 25);

    private static int Level(float value) => (int)((value * 255) + 0.5f);

    private static double Difference(Rgba a, Rgba b) => ColorDifference.DeltaE2000(
        ColorDifference.LabOfSrgb([a.R, a.G, a.B]),
        ColorDifference.LabOfSrgb([b.R, b.G, b.B]));
}
