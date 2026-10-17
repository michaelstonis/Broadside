using System.Text;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Transfer functions, special separations and the overprint and halftone posture through the software rasterizer (ISO 32000-2
/// §10.5, §8.6.6.4, §8.6.7, §10.6), observed as pixels. Pages are 612 x 792 at 72 DPI, so pixel (x, y) has its centre at user
/// (x + 0.5, 791.5 − y). Expected values are the corpus file's functions evaluated by hand on the colours it paints, times 255.
/// </summary>
public sealed class TransferFunctionRenderingTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);

    [Fact]
    public void Transfer_functions_apply_to_fills_images_strokes_and_shadings_of_transfer_functions_pdf()
    {
        using Bitmap bitmap = RenderCorpus("transfer-functions.pdf");

        // Row 1 (y 640..740, pixel row 101), TR Square on every component: 0.25² , 0.75², (0.2², 0.5², 0.8²); Im0 64 and 192.
        AssertNear(Gray(0.0625), bitmap.At(90, 101));
        AssertNear(Gray(0.5625), bitmap.At(230, 101));
        AssertNear(Rgb(0.04, 0.25, 0.64), bitmap.At(370, 101));
        AssertNear(Gray(Math.Pow(64 / 255.0, 2)), bitmap.At(485, 101));
        AssertNear(Gray(Math.Pow(192 / 255.0, 2)), bitmap.At(535, 101));

        // Row 2 (pixel row 241), TR [Invert Square Root Invert]: red 1 − r, green g², blue √b; gray 0.25 becomes coloured.
        AssertNear(Rgb(0.75, 0.0625, 0.5), bitmap.At(90, 241));
        AssertNear(Rgb(0.8, 0.25, Math.Sqrt(0.8)), bitmap.At(230, 241));
        AssertNear(Rgb(0, 1, 1), bitmap.At(370, 241));
        double high = 192 / 255.0;
        AssertNear(Rgb(1 - high, high * high, Math.Sqrt(high)), bitmap.At(535, 241));

        // Row 3 (pixel row 381), TR2 PsInvert, then TR2 /Default and TR /Identity restore the colour as specified.
        AssertNear(Gray(0.75), bitmap.At(90, 381));
        AssertNear(Rgb(0.8, 0.5, 0.2), bitmap.At(230, 381));
        AssertNear(Gray(0.25), bitmap.At(370, 381));
        AssertNear(Gray(0.75), bitmap.At(510, 381));

        // Row 4 (pixel row 521), TR2 [Sampled Square Root Invert] (TR beside it is ignored). Sampled at 0.6 lies 0.4 of the way
        // from sample 60 to sample 230: 128 / 255. Separation All at 0.5 gives 0.5 on every component before the functions;
        // Sampled at 0.5 is the sample 60. Separation None leaves the page white.
        AssertNear(Rgb(128 / 255.0, 0.36, Math.Sqrt(0.6)), bitmap.At(90, 521));
        AssertNear(Rgb(60 / 255.0, 0.25, Math.Sqrt(0.5)), bitmap.At(230, 521));
        Assert.Equal(White, bitmap.At(370, 521));

        // The shading at pixel 509 (s = 0.495) goes through the same functions: Sampled 0.98 of the way from 200 to 60, 0.495²,
        // √0.495; its colours are sampled into a table, so they may move by a few levels.
        AssertNear(Rgb((200 + (0.98 * (60 - 200))) / 255, 0.495 * 0.495, Math.Sqrt(0.495)), bitmap.At(509, 521), tolerance: 3);

        // Row 5: the stroke, 0.5 under Square.
        AssertNear(Gray(0.25), bitmap.At(300, 661));
    }

    [Fact]
    public void Separation_All_paints_every_colourant_and_None_paints_nothing_in_separation_special_pdf()
    {
        // Over a 0.8 gray band (y 600..760), three rectangles at tint 0.5 from x = 40 every 140, y 640..740: All gives 1 − 0.5 on
        // every RGB component (its tint transform, which would paint white, is not used); None and an all-None DeviceN paint nothing.
        using Bitmap bitmap = RenderCorpus("separation-special.pdf");

        AssertNear(Gray(0.8), bitmap.At(590, 101));
        AssertNear(Gray(0.5), bitmap.At(90, 101));
        AssertNear(Gray(0.8), bitmap.At(230, 101));
        AssertNear(Gray(0.8), bitmap.At(370, 101));
    }

    [Fact]
    public void Overprint_and_halftones_change_nothing_the_rasterizer_paints()
    {
        // §8.6.7: a device that does not overprint ignores the overprint parameters; §10.6.1: a continuous-tone device needs no
        // halftone. A CMYK fill with zero components under OP/OPM 1 still knocks out what is below it.
        const string paint = "0 0 1 rg 100 100 300 300 re f 0 1 0 0 k 200 200 300 300 re f 0.5 g 30 w 50 600 m 550 600 l S";
        const string resources =
            "/ExtGState << /Over << /OP true /op true /OPM 1 /HT << /Type /Halftone /HalftoneType 1 /Frequency 10 /Angle 0 "
            + "/SpotFunction /Round >> /HTO [3 4] >> >>";

        using Bitmap plain = RenderContent(paint, resources);
        using Bitmap overprinted = RenderContent("/Over gs " + paint, resources);

        Assert.True(plain.Pixels.SequenceEqual(overprinted.Pixels));
    }

    private static Rgba Gray(double value) => Rgb(value, value, value);

    private static Rgba Rgb(double r, double g, double b) => new(Channel(r), Channel(g), Channel(b), 255);

    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

    private static void AssertNear(Rgba expected, Rgba actual, int tolerance = 1)
    {
        bool near = Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance && expected.A == actual.A;
        Assert.True(near, $"Expected {expected} within {tolerance}, got {actual}.");
    }

    private static Bitmap RenderCorpus(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }

    private static Bitmap RenderContent(string content, string resources)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), resources));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }
}
