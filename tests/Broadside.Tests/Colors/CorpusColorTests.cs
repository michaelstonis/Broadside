using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;
using static Broadside.Tests.Colors.ColorTesting;

namespace Broadside.Tests.Colors;

/// <summary>
/// The colour corpus files (tests/Corpus/README.md) read through the public seams. Expected device colours are poppler's
/// (<c>pdftoppm -r 72</c>, rectangle centres) where both apply the same model, and otherwise the managed default's documented
/// values: poppler converts CMYK with its own formula and uses the ICC profiles, which Phase 3 brings.
/// </summary>
public sealed class CorpusColorTests
{
    [Fact]
    public void Every_family_of_colorspace_families_pdf_is_typed_and_painted()
    {
        (List<Paint> paints, string[] codes) = Run(File.ReadAllBytes(Corpus.Path("colorspace-families.pdf")));

        Assert.Equal(
            ["DeviceGray 0.5", "DeviceRgb 1 0 0", "DeviceCmyk 0 1 0 0", "CalGray 0.5", "CalRgb 0 0 1", "Lab 50 60 40", "IccBased 0 1 0",
             "IccBased 0.25", "Indexed 2", "Separation 1", "DeviceN 1 0.4", "Pattern 0 0.5 0 /P0"],
            paints.Select(p => p.FillText));
        // CMYK colours (rectangles 3, 10, 11) are the CGATS TR 001 measurements of those patches in sRGB (tools/CmykFit/fit.py's
        // pipeline), which the default characterisation approximates; poppler shows (236, 0, 140), (248, 156, 14) and
        // (18, 123, 202) with its own formula. ICCBased gray converts through DeviceGray until the ICC engine (poppler: 62).
        int[][] expected =
        [
            [128, 128, 128], [255, 0, 0], [236, 41, 144], [128, 128, 128], [0, 0, 255], [214, 60, 55], [0, 255, 0], [64, 64, 64],
            [0, 0, 255], [250, 168, 54], [0, 131, 200], [0, 128, 0],
        ];
        for (int i = 0; i < expected.Length; i++)
        {
            Near([.. expected[i].Select(v => v / 255f)], paints[i].FillRgb, 12 / 255.0);
        }

        Assert.Empty(codes);
    }

    [Fact]
    public void The_twelve_operators_of_color_operators_pdf_set_fill_and_stroke_colours()
    {
        (List<Paint> paints, string[] codes) = Run(File.ReadAllBytes(Corpus.Path("color-operators.pdf")));

        Assert.Equal(
            ["DeviceGray 0.75", "DeviceRgb 0 0 1", "DeviceCmyk 0 1 0 0", "CalGray 0.8", "Separation 1", "Separation 0.25", "Pattern 1 0 0 /P0", "DeviceRgb 0 0 0"],
            paints.Select(p => p.FillText));
        Assert.Equal(
            ["DeviceGray 0.25", "DeviceRgb 1 0 0", "DeviceCmyk 0 0 0 1", "CalGray 0.2", "Separation 1", "Separation 0.5", "Pattern 0 0 1 /P0", "DeviceCmyk 0 0 0 1"],
            paints.Select(p => p.StrokeText));
        Assert.Empty(codes);
    }

    [Fact]
    public void Inline_image_colour_spaces_resolve_abbreviations_and_the_abbreviated_Indexed_space()
    {
        using PdfDocument operators = PdfDocument.Open(Corpus.Path("color-operators.pdf"));
        using PdfDocument gray = PdfDocument.Open(Corpus.Path("inline-image.pdf"));
        var indexed = new InlineImageSpaces();
        var device = new InlineImageSpaces();

        operators.Pages[0].ProcessContent(indexed);
        gray.Pages[0].ProcessContent(device);

        PdfIndexedColorSpace space = Assert.IsType<PdfIndexedColorSpace>(Assert.Single(indexed.Spaces));
        Assert.Same(PdfDeviceRgbColorSpace.Instance, space.Base);
        byte[] rgb = new byte[6];
        operators.GetColorConverter(space).Convert(new byte[] { 0, 1 }, rgb, 2);
        Assert.Equal(new byte[] { 255, 0, 0, 0, 0, 255 }, rgb);
        Assert.Same(PdfDeviceGrayColorSpace.Instance, Assert.Single(device.Spaces));
        Assert.Empty(operators.Diagnostics);
    }

    [Fact]
    public void Default_colour_spaces_of_default_colorspaces_pdf_apply_at_paint_time()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("default-colorspaces.pdf"));
        var recorder = new ColorRecorder();
        document.Pages[0].ProcessContent(recorder);

        // The third paint is inside the form XObject, whose own DefaultRGB is current there (§8.6.5.6, issue #56).
        Assert.Equal(["DeviceRgb 0.5 0.5 0.5", "DeviceGray 0.5", "DeviceRgb 0.5 0.5 0.5"], recorder.Paints.Select(p => p.FillText));
        Near([0.504f, 0.504f, 0.504f], recorder.Paints[2].FillRgb, 3e-3);
        Assert.NotSame(recorder.Paints[0].Defaults.Rgb, recorder.Paints[2].Defaults.Rgb);
        Assert.IsType<PdfCalRgbColorSpace>(recorder.Paints[0].Defaults.Rgb);

        // The page's CalRGB and CalGray defaults (gamma 1, D50) take 0.5 to linear 0.5, sRGB 188/255; poppler shows (188, 188, 188)
        // where the device colour would be 128.
        Near([0.7354f, 0.7354f, 0.7354f], recorder.Paints[0].FillRgb, 2e-3);
        Near([0.7354f, 0.7354f, 0.7354f], recorder.Paints[1].FillRgb, 2e-3);

        // The form has its own DefaultRGB (gamma 2.2, D65: 0.5 is sRGB 0.504; poppler 128), the image's Indexed base remaps to the
        // page's (poppler 188).
        var form = (CosStream)document.Resolve(new CosReference(5, 0));
        PdfDefaultColorSpaces formDefaults = document.GetDefaultColorSpaces((CosDictionary)document.Resolve(form.Dictionary[new CosName("Resources")]));
        Near([0.504f, 0.504f, 0.504f], document.GetColorConverter(PdfDeviceRgbColorSpace.Instance, formDefaults).ConvertOne(0.5f, 0.5f, 0.5f), 3e-3);
        var image = (CosStream)document.Resolve(new CosReference(6, 0));
        PdfColorSpace indexed = document.GetColorSpace(image.Dictionary[new CosName("ColorSpace")]);
        byte[] pixels = new byte[6];
        document.GetColorConverter(indexed, recorder.Paints[0].Defaults).Convert(new byte[] { 0, 1 }, pixels, 2);
        Assert.Equal(255, pixels[0]);
        Assert.InRange(pixels[3], 187, 189);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void All_paints_every_colourant_and_None_paints_nothing_in_separation_special_pdf()
    {
        (List<Paint> paints, string[] codes) = Run(File.ReadAllBytes(Corpus.Path("separation-special.pdf")));

        Assert.Equal(4, paints.Count);
        Near([0.5f, 0.5f, 0.5f], paints[1].FillRgb);
        Assert.False(paints[1].PaintsNothing);
        Assert.True(paints[2].PaintsNothing);
        Assert.True(paints[3].PaintsNothing);
        Assert.Empty(codes);
    }

    /// <summary>Resolves the colour space of every inline image through <see cref="ContentContext.GetInlineImageColorSpace"/>.</summary>
    private sealed class InlineImageSpaces : ContentProcessor
    {
        public List<PdfColorSpace> Spaces { get; } = [];

        public override ContentEvents Events => ContentEvents.Operators;

        public override void VisitOperator(in ContentOperator op, ContentContext context)
        {
            if (op.Code != ContentOperatorCode.BeginInlineImage)
            {
                return;
            }

            var dictionary = (CosDictionary)op.Operands[0].ToCosObject();
            if (dictionary.TryGetValue(new CosName("CS"), out CosObject? value) || dictionary.TryGetValue(new CosName("ColorSpace"), out value))
            {
                Spaces.Add(context.GetInlineImageColorSpace(value));
            }
        }
    }
}
