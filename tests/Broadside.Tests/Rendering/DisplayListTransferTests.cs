using System.Text;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Transfer functions in the display list (ISO 32000-2 §10.5; ADR 0006): the transfer function a <c>gs</c> sets travels with every
/// colour in its <see cref="ColorConversion"/>, and a converter built from that conversion applies it to the device colour after
/// the colour-space conversion. Expected values are the functions evaluated by hand.
/// </summary>
public sealed class DisplayListTransferTests
{
    // Square: f(x) = x²; Invert: f(x) = 1 − x; Root: f(x) = x^0.5.
    private const string Square = "<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 2 >>";
    private const string Invert = "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>";
    private const string Root = "<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 0.5 >>";

    private const string Resources =
        "/ExtGState << /Single << /TR " + Square + " >> /Four << /TR [" + Invert + " " + Square + " " + Root + " " + Invert + "] >> "
        + "/Identity << /TR /Identity >> /Default << /TR2 /Default >> /Both << /TR " + Invert + " /TR2 " + Square + " >> >>";

    [Fact]
    public void A_single_transfer_function_applies_to_every_component_of_the_device_colour()
    {
        float[] rgb = ConvertFills("/Single gs 0.5 g 0 0 1 1 re f 0.2 0.4 0.6 rg 0 0 1 1 re f")[..2].SelectMany(c => c).ToArray();

        AssertNear([0.25f, 0.25f, 0.25f, 0.04f, 0.16f, 0.36f], rgb);
    }

    [Fact]
    public void Four_transfer_functions_apply_red_green_and_blue_on_an_rgb_device()
    {
        float[] rgb = ConvertFills("/Four gs 0.2 0.4 0.64 rg 0 0 1 1 re f")[0];

        AssertNear([0.8f, 0.16f, 0.8f], rgb);
    }

    [Fact]
    public void The_gray_function_alone_applies_on_a_gray_device()
    {
        using PdfDocument document = Open("/Four gs 0.25 g 0 0 1 1 re f");
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        var fill = recorder.Fills[0];

        var gray = new float[1];
        document.GetColorConverter(fill.ColorSpace, fill.Conversion with { Target = DeviceColorModel.Gray }, fill.Defaults).Convert(fill.Components, gray);

        AssertNear([0.75f], gray);
    }

    [Fact]
    public void Subtractive_components_go_through_the_function_in_additive_form()
    {
        // §10.5: a CMYK component c is passed as 1 − c and the result converted back: Square gives 1 − (1 − c)².
        using PdfDocument document = Open("/Single gs 0.1 0.3 0.5 0.7 k 0 0 1 1 re f");
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        var fill = recorder.Fills[0];
        ColorConversion toCmyk = fill.Conversion with { Target = DeviceColorModel.Cmyk };

        var plain = new float[4];
        document.GetColorConverter(fill.ColorSpace, toCmyk with { TransferFunction = null }, fill.Defaults).Convert(fill.Components, plain);
        var cmyk = new float[4];
        document.GetColorConverter(fill.ColorSpace, toCmyk, fill.Defaults).Convert(fill.Components, cmyk);

        AssertNear(plain.Select(c => 1 - ((1 - c) * (1 - c))).ToArray(), cmyk);
    }

    [Fact]
    public void A_DeviceGray_colour_on_a_CMYK_device_goes_through_the_gray_function_on_black_alone()
    {
        // §10.5, last paragraph: gray g converts to (0, 0, 0, 1 − g); only black goes through the gray function (Invert, the fourth),
        // in additive form: 1 − Invert(g) = g. Cyan, magenta and yellow stay 0 rather than becoming Invert(1) = 0 → ink 1.
        using PdfDocument document = Open("/Four gs 0.25 g 0 0 1 1 re f");
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        var fill = recorder.Fills[0];

        var cmyk = new float[4];
        document.GetColorConverter(fill.ColorSpace, fill.Conversion with { Target = DeviceColorModel.Cmyk }, fill.Defaults).Convert(fill.Components, cmyk);

        AssertNear([0f, 0f, 0f, 0.25f], cmyk);
    }

    [Fact]
    public void Identity_and_default_transfers_leave_colours_unchanged_and_a_restore_brings_back_the_saved_one()
    {
        float[][] fills = ConvertFills(
            "/Single gs q /Identity gs 0.5 g 0 0 1 1 re f Q 0.5 g 0 0 2 2 re f /Default gs 0.5 g 0 0 3 3 re f");

        AssertNear([0.5f, 0.5f, 0.5f, 0.25f, 0.25f, 0.25f, 0.5f, 0.5f, 0.5f], fills.SelectMany(c => c).ToArray());
    }

    [Fact]
    public void TR2_takes_precedence_over_TR()
    {
        float[] rgb = ConvertFills("/Both gs 0.5 g 0 0 1 1 re f")[0];

        AssertNear([0.25f, 0.25f, 0.25f], rgb);
    }

    [Fact]
    public void Strokes_and_images_carry_the_transfer_function_too()
    {
        using PdfDocument document = Open("/Single gs 0 0 1 1 re S q 10 0 0 10 0 0 cm BI /W 1 /H 1 /CS /G /BPC 8 ID \u0080 EI Q");
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.NotNull(recorder.Strokes[0].Conversion.TransferFunction);
        Assert.NotNull(recorder.Images[0].Conversion.TransferFunction);

        // An image's samples convert through the same function: 128/255 squared.
        var image = recorder.Images[0];
        var bytes = new byte[3];
        document.GetColorConverter(image.Image.ColorSpace, image.Conversion, image.Defaults).Convert([(byte)128], bytes, 1);
        Assert.Equal((byte)Math.Round(Math.Pow(128 / 255.0, 2) * 255), bytes[0]);
    }

    private static PdfDocument Open(string content) =>
        PdfDocument.Open(ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), Resources));

    private static float[][] ConvertFills(string content)
    {
        using PdfDocument document = Open(content);
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        return recorder.Fills.Select(fill =>
        {
            var rgb = new float[3];
            document.GetColorConverter(fill.ColorSpace, fill.Conversion, fill.Defaults).Convert(fill.Components, rgb);
            return rgb;
        }).ToArray();
    }

    private static void AssertNear(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(expected[i] - actual[i]) < 1e-4, $"Component {i}: expected {expected[i]}, got {actual[i]}.");
        }
    }
}
