using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests.Icc;

/// <summary>
/// A CalGray <c>BlackPoint</c> (ISO 32000-2 §8.6.5.2, Table 62) keeps the tonal ramp below and above it, with the managed default
/// colour management and with the ICC engine alike (#279): the colours of a CalGray space are XW·A^G, YW·A^G, ZW·A^G whatever the
/// black point, and black point compensation (§8.6.5.9) maps the darkest colour the space produces, A = 0, to the display's black,
/// so it changes nothing. The references (PDFium, pdf.js, Ghostscript, Poppler) render pdf.js's <c>calgray.pdf</c> this way.
/// </summary>
public sealed class CalGrayBlackPointTests
{
    /// <summary>The sixteen A values of each page of calgray.pdf, in content order (rows of four from the bottom left).</summary>
    private static readonly float[] Ramp = [0f, 0.01f, 0.05f, 0.1f, 0.15f, 0.2f, 0.25f, 0.3f, 0.35f, 0.4f, 0.45f, 0.5f, 0.55f, 0.6f, 0.65f, 0.7f];

    /// <summary>The 8-bit sRGB levels of <see cref="Ramp"/> (IEC 61966-2-1 encoding of the luminance A), which Poppler renders.</summary>
    private static readonly int[] Levels = [0, 25, 63, 89, 108, 124, 137, 149, 160, 170, 179, 188, 196, 203, 211, 218];

    public static TheoryData<bool> Engines => new() { false, true };

    [Theory]
    [MemberData(nameof(Engines))]
    public void Values_around_a_CalGray_BlackPoint_convert_as_without_it(bool icc)
    {
        using PdfDocument document = PdfDocument.Create(Options(icc));
        PdfColorSpace withBlack = document.GetColorSpace(CosObject.Parse("[/CalGray << /WhitePoint [0.9505 1 1.089] /BlackPoint [0.6654 0.7 0.7623] /Gamma 2.2 >>]"u8));
        PdfColorSpace without = document.GetColorSpace(CosObject.Parse("[/CalGray << /WhitePoint [0.9505 1 1.089] /Gamma 2.2 >>]"u8));
        float[] actual = new float[3];
        float[] expected = new float[3];

        foreach (BlackPointCompensation compensation in Enum.GetValues<BlackPointCompensation>())
        {
            var conversion = new ColorConversion { BlackPointCompensation = compensation };
            foreach (float a in (float[])[0.05f, 0.8f, 0.85f, 0.8492f, 0.9f, 1f])
            {
                document.GetColorConverter(withBlack, conversion).Convert([a], actual);
                document.GetColorConverter(without, conversion).Convert([a], expected);
                Assert.Equal(expected, actual);
            }
        }

        // 0.85^2.2 = 0.6993: just below the black point's Y of 0.7, still well above black.
        document.GetColorConverter(withBlack).Convert([0.8492f], actual);
        Assert.InRange(actual[1], 0.84f, 0.86f);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Calgray_pdf_renders_the_ramp_below_its_black_point(bool icc)
    {
        if (RealWorldCorpus.Directory("pdfjs") is null)
        {
            Assert.Skip($"pdfjs not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        using PdfDocument document = PdfDocument.Open(RealWorldCorpus.PathOf("pdfjs/calgray.pdf"), Options(icc));
        using var rasterizer = new SoftwareRasterizer();

        // Page 3 has BlackPoint [0.7 0.7 0.7]; page 1 is the same ramp with the default [0 0 0].
        using Bitmap black = rasterizer.Render(document.Pages[2]);
        using Bitmap none = rasterizer.Render(document.Pages[0]);

        for (int i = 0; i < Ramp.Length; i++)
        {
            Rgba pixel = Box(black, i);
            Assert.True(Math.Abs(pixel.G - Levels[i]) <= 1, $"A = {Ramp[i]}: {pixel} against {Levels[i]}");
            Assert.Equal(Box(none, i), pixel);
        }
    }

    private static PdfOptions Options(bool icc) => icc ? new PdfOptions().UseIccColorManagement() : new PdfOptions();

    /// <summary>The pixel at the centre of box <paramref name="index"/> at 72 DPI: 200-point boxes from (25, 25), four a row, on an 850 × 1100 page.</summary>
    private static Rgba Box(Bitmap bitmap, int index) => bitmap.At(125 + (200 * (index % 4)), 1100 - (125 + (200 * (index / 4))));
}
