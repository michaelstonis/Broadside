using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests.Icc;

/// <summary>
/// Rendering intents (ISO 32000-2 §8.6.5.8, Table 69) and UseBlackPtComp (§8.6.5.9) reach the ICC engine from every place content
/// sets them: <c>ri</c> (§8.4.4), the ExtGState <c>RI</c> and <c>UseBlackPtComp</c> entries (§8.4.5, Table 57) and an image's
/// <c>Intent</c> (§8.9.5, Table 87), rendered from <c>rendering-intents.pdf</c>.
/// </summary>
public sealed class RenderingIntentRenderingTests
{
    private static readonly float[] Bright = [0.2f, 0.6f, 0.8f, 0];
    private static readonly float[] Dark = [0.6f, 0.4f, 0.4f, 1];

    /// <summary>The squares of the file, in order (generate.py RENDERING_INTENT_SQUARES): colour, intent, black point compensation.</summary>
    private static readonly (float[] Color, RenderingIntent Intent, BlackPointCompensation Compensation)[] Squares =
    [
        (Bright, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Default),
        (Bright, RenderingIntent.Perceptual, BlackPointCompensation.Default),
        (Bright, RenderingIntent.Saturation, BlackPointCompensation.Default),
        (Bright, RenderingIntent.AbsoluteColorimetric, BlackPointCompensation.Default),
        (Bright, RenderingIntent.Saturation, BlackPointCompensation.Default),
        (Bright, RenderingIntent.Perceptual, BlackPointCompensation.Default),
        (Dark, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off),
        (Dark, RenderingIntent.RelativeColorimetric, BlackPointCompensation.On),
        (Dark, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Default),
    ];

    [Fact]
    public void Each_square_renders_with_the_intent_and_black_point_compensation_its_content_sets()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("rendering-intents.pdf"), new PdfOptions().UseIccColorManagement());
        PdfColorSpace space = IccTesting.Space(document, "CS0");
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        for (int i = 0; i < Squares.Length; i++)
        {
            (float[] color, RenderingIntent intent, BlackPointCompensation compensation) = Squares[i];
            float[] rgb = new float[3];
            document.GetColorConverter(space, new ColorConversion { Intent = intent, BlackPointCompensation = compensation }).Convert(color, rgb);
            Rgba pixel = Square(bitmap, i);
            Assert.True(
                Math.Abs(pixel.R - Level(rgb[0])) <= 1 && Math.Abs(pixel.G - Level(rgb[1])) <= 1 && Math.Abs(pixel.B - Level(rgb[2])) <= 1,
                $"square {i}: {pixel} against {Level(rgb[0])} {Level(rgb[1])} {Level(rgb[2])} ({intent}, {compensation})");
        }

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_profile_s_four_intents_and_the_compensation_settings_give_different_colours()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("rendering-intents.pdf"), new PdfOptions().UseIccColorManagement());
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        // The four intents (squares 0 to 3) differ pairwise, so a square showing the right one cannot pass by coincidence; the image
        // (5) shows its own Intent, not the ExtGState's Saturation that the fill before it (4) shows.
        for (int a = 0; a < 4; a++)
        {
            for (int b = a + 1; b < 4; b++)
            {
                Assert.True(Difference(Square(bitmap, a), Square(bitmap, b)) > 2, $"squares {a} and {b} look alike");
            }
        }

        Assert.Equal(Square(bitmap, 2), Square(bitmap, 4));
        Assert.True(Difference(Square(bitmap, 1), Square(bitmap, 5)) < 1, "the image does not show its Intent");
        Assert.True(Difference(Square(bitmap, 4), Square(bitmap, 5)) > 2, "the image shows the graphics state's intent");

        // UseBlackPtComp OFF keeps the press's dark gray; ON and Default (the engine compensates) reach the display's black.
        Assert.True(Difference(Square(bitmap, 6), Square(bitmap, 7)) > 10, "OFF and ON render alike");
        Assert.Equal(Square(bitmap, 7), Square(bitmap, 8));

        using PdfDocument managed = PdfDocument.Open(Corpus.Path("rendering-intents.pdf"));
        using Bitmap alternate = rasterizer.Render(managed.Pages[0]);
        Assert.Equal(Square(alternate, 0), Square(alternate, 1));
    }

    /// <summary>The pixel at the centre of square <paramref name="index"/> at 72 DPI: six 70 × 70 squares a row, 85 apart from x 40, rows from y 650 down by 100.</summary>
    private static Rgba Square(Bitmap bitmap, int index) => bitmap.At(40 + (85 * (index % 6)) + 35, 792 - (650 - (100 * (index / 6))) - 35);

    private static int Level(float value) => (int)((value * 255) + 0.5f);

    private static double Difference(Rgba a, Rgba b) => ColorDifference.DeltaE2000(
        ColorDifference.LabOfSrgb([a.R, a.G, a.B]),
        ColorDifference.LabOfSrgb([b.R, b.G, b.B]));
}
