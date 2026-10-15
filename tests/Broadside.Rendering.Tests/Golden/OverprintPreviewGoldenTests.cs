using Broadside.GoldenImages;
using Broadside.Graphics;
using StbImageSharp;

namespace Broadside.Rendering.Tests.Golden;

/// <summary>
/// Overprint preview (issue #227; ISO 32000-2 §8.6.7, §11.7.4) as golden images: <c>overprint.pdf</c> rendered with
/// <see cref="RenderOptions.OverprintPreview"/> on is reviewed in <c>tests/Golden/OverprintPreview/</c> and compared with
/// Ghostscript's overprint simulation of the same page. With the option off the page is an ordinary golden image (knockout
/// painting), checked by <see cref="GoldenImageTests"/>.
/// </summary>
public sealed class OverprintPreviewGoldenTests
{
    private const string FileName = "overprint.pdf";
    private const string Variant = "OverprintPreview";

    /// <summary>
    /// Ghostscript 10.08.0's overprint simulation of the page as device colourants:
    /// <c>gs -sDEVICE=pamcmyk32 -r72 -dOverprint=/simulate</c>, its C, M, Y and K stored as the red, green, blue and alpha of a PNG
    /// (converted by a stdlib Python script; the PAM header is dropped). Its spot colourants are already folded into CMYK as
    /// tint × their equivalent, clipped. Ghostscript's RGB output would put its own CMYK profile between the two renders; comparing
    /// colourants converted by the same DeviceCMYK conversion tests the overprinting itself. The page paints no RGB colour, whose
    /// conversion to CMYK differs between the two (Ghostscript uses its ICC profiles; with <c>-dUseFastColor</c> it drops overprint
    /// mode 1 altogether).
    /// </summary>
    private static readonly string GhostscriptReference =
        Path.Combine(GoldenCorpus.VariantDirectory(Variant), "overprint.page0.ghostscript-cmyk.png");

    [Fact]
    public async Task Overprint_pdf_with_overprint_preview_matches_its_golden_image()
    {
        await GoldenCorpus.VerifyAsync(FileName, 0, Render(preview: true), Variant);
    }

    [Fact]
    public void Overprint_pdf_with_overprint_preview_matches_Ghostscript_overprint_simulation_within_the_harness_threshold()
    {
        GoldenImage render = Render(preview: true);
        GoldenImage reference = LoadGhostscriptReference();
        GoldenComparison comparison = GoldenComparison.Compare(reference, render, GoldenThreshold.Default);
        if (!comparison.Matches)
        {
            DirectoryInfo output = Directory.CreateTempSubdirectory("broadside-overprint-");
            reference.SavePng(Path.Combine(output.FullName, "overprint.page0.ghostscript.png"));
            render.SavePng(Path.Combine(output.FullName, "overprint.page0.received.png"));
            comparison.CreateDiffImage().SavePng(Path.Combine(output.FullName, "overprint.page0.diff.png"));
            Assert.Fail($"The overprint preview of {FileName} does not match Ghostscript's. {comparison.Describe()} See {output.FullName}.");
        }
    }

    [Fact]
    public void Without_overprint_preview_overprint_pdf_paints_by_knockout()
    {
        // The option off: the page's ordinary golden image, where every overlay hides the backdrop below it.
        GoldenImage render = Render(preview: false);

        Assert.True(GoldenComparison.Compare(GoldenCorpus.LoadReference(FileName, 0), render, GoldenThreshold.Exact).Matches);
        Assert.False(GoldenComparison.Compare(render, Render(preview: true), GoldenThreshold.Default).Matches);
    }

    private static GoldenImage Render(bool preview)
    {
        using PdfDocument document = GoldenCorpus.Open(FileName);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions().WithOverprintPreview(preview));
        return GoldenImage.FromBitmap(bitmap);
    }

    /// <summary>Ghostscript's colourants converted for display like Broadside's: DeviceCMYK through the document, no ink white.</summary>
    private static GoldenImage LoadGhostscriptReference()
    {
        ImageResult cmyk = ImageResult.FromMemory(File.ReadAllBytes(GhostscriptReference), ColorComponents.RedGreenBlueAlpha);
        using PdfDocument document = GoldenCorpus.Open(FileName);
        PdfColorConverter converter = document.GetColorConverter(PdfDeviceCmykColorSpace.Instance);
        byte[] rgb = new byte[cmyk.Width * cmyk.Height * 3];
        converter.Convert(cmyk.Data, rgb, cmyk.Width * cmyk.Height);
        byte[] rgba = new byte[cmyk.Width * cmyk.Height * 4];
        for (int i = 0; i < cmyk.Width * cmyk.Height; i++)
        {
            bool paper = cmyk.Data[4 * i] == 0 && cmyk.Data[(4 * i) + 1] == 0 && cmyk.Data[(4 * i) + 2] == 0 && cmyk.Data[(4 * i) + 3] == 0;
            rgba[4 * i] = paper ? (byte)255 : rgb[3 * i];
            rgba[(4 * i) + 1] = paper ? (byte)255 : rgb[(3 * i) + 1];
            rgba[(4 * i) + 2] = paper ? (byte)255 : rgb[(3 * i) + 2];
            rgba[(4 * i) + 3] = 255;
        }

        return GoldenImage.FromPixels(rgba, cmyk.Width, cmyk.Height, cmyk.Width * 4, GoldenPixelFormat.Rgba8);
    }
}
