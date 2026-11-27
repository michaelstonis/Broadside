using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// The save-time image pass (issue #625): effective resolution from the largest placement, box resampling, re-encoding, "never
/// grow", masks, and the refusals. ISO 32000-2 §8.9.4, §8.9.5.3, §8.9.6, §7.4.4 and §7.4.8.
/// </summary>
public sealed class ImageOptimizationTests
{
    [Fact]
    public void A_600_dpi_image_placed_at_one_inch_shrinks_to_150_dpi()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImage(Gradient(600, 600, 3), new PdfImageLayout(600, 600, PdfDeviceRgbColorSpace.Instance), PdfImageCompression.Flate);
        Draw(document, image, 72, 72);
        var options = new PdfImageOptimization().WithMaxResolution(150);

        PdfImageOptimizationResult planned = Assert.Single(document.PlanImageOptimization(options));
        Assert.Equal(600, planned.EffectiveResolution!.Value, 6);
        Assert.Equal(PdfImageOptimizationActions.Resample | PdfImageOptimizationActions.Recompress, planned.Actions);
        Assert.Equal((150, 150), (planned.TargetWidth, planned.TargetHeight));
        Assert.Equal(FilterNames.FlateDecode, planned.TargetFilter);
        Assert.Equal(600, image.Width);

        using PdfDocument saved = SaveAndOpen(document, options);
        PdfImage written = Assert.Single(saved.Pages[0].ExtractImages()).Image;
        Assert.Equal((150, 150, 8), (written.Width, written.Height, written.BitsPerComponent));
        Assert.Equal(PdfColorSpaceFamily.DeviceRgb, written.ColorSpace!.Family);
        using DecodedImage decoded = written.Decode()!;
        Assert.Equal(150, decoded.Geometry.Width);
    }

    [Fact]
    public void An_image_placed_twice_uses_its_largest_placement()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImage(Gradient(600, 600, 1), new PdfImageLayout(600, 600, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(image, 0, 0, 72, 72);
            canvas.DrawImage(image, 100, 100, 144, 144);
        }

        PdfImageOptimizationResult planned = Assert.Single(document.PlanImageOptimization(new PdfImageOptimization().WithMaxResolution(150)));

        Assert.Equal(2, planned.Placements);
        Assert.Equal(300, planned.EffectiveResolution!.Value, 6);
        Assert.Equal((300, 300), (planned.TargetWidth, planned.TargetHeight));
    }

    [Fact]
    public void An_unplaced_image_is_unchanged()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImage(Gradient(600, 600, 1), new PdfImageLayout(600, 600, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);
        byte[] before = image.Stream!.EncodedData.ToArray();
        var options = new PdfImageOptimization().WithMaxResolution(72).WithJpegQuality(50).WithRecompressLossless();

        Assert.Empty(document.PlanImageOptimization(options));
        Assert.Empty(ImageOptimizer.Apply(document, options, System.IO.Compression.CompressionLevel.Optimal));

        Assert.Equal(before, image.Stream.EncodedData.ToArray());
        Assert.Equal(600, image.Width);
    }

    [Fact]
    public void A_re_encoded_image_that_would_grow_is_kept()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImage(Gradient(64, 64, 3), new PdfImageLayout(64, 64, PdfDeviceRgbColorSpace.Instance), PdfImageCompression.Flate);
        Draw(document, image, 72, 72);
        byte[] before = image.Stream!.EncodedData.ToArray();

        // A smooth gradient deflates to almost nothing; JPEG at quality 100 cannot beat it.
        var options = new PdfImageOptimization().WithJpegQuality(100).WithRecompressLossless();
        PdfImageOptimizationResult planned = Assert.Single(document.PlanImageOptimization(options));
        Assert.Equal(PdfImageOptimizationActions.Recompress, planned.Actions);

        PdfImageOptimizationResult applied = Assert.Single(ImageOptimizer.Apply(document, options, System.IO.Compression.CompressionLevel.Optimal));

        Assert.Equal(PdfImageOptimizationActions.None, applied.Actions);
        Assert.Equal(PdfImageOptimizationReason.WouldGrow, applied.Reason);
        Assert.Equal(before, image.Stream.EncodedData.ToArray());
        Assert.Equal(FilterNames.FlateDecode, image.Dictionary[FilterNames.Filter]);
    }

    [Fact]
    public void An_smask_is_resampled_with_its_image()
    {
        using PdfDocument document = PdfDocument.Create();
        byte[] alpha = Gradient(600, 600, 1);
        PdfImage image = document.CreateImage(Gradient(600, 600, 3), alpha, new PdfImageLayout(600, 600, PdfDeviceRgbColorSpace.Instance), PdfImageCompression.Flate);
        Draw(document, image, 72, 72);

        using PdfDocument saved = SaveAndOpen(document, new PdfImageOptimization().WithMaxResolution(150));

        PdfImage written = Assert.Single(saved.Pages[0].ExtractImages()).Image;
        Assert.Equal(PdfImageMaskKind.Soft, written.MaskKind);
        PdfImage mask = written.Mask!;
        Assert.Equal((150, 150), (written.Width, written.Height));
        Assert.Equal((150, 150), (mask.Width, mask.Height));
        using DecodedImage decodedMask = mask.Decode()!;

        // The alpha is a horizontal ramp (value = x × 255 / 599); its box average keeps the ramp.
        Assert.InRange(decodedMask.GetRow(75)[0], 0, 2);
        Assert.InRange(decodedMask.GetRow(75)[149], 252, 255);
    }

    [Fact]
    public void A_lossless_image_becomes_dct_only_when_allowed()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImage(Noise(256, 256, 3), new PdfImageLayout(256, 256, PdfDeviceRgbColorSpace.Instance), PdfImageCompression.Flate);
        Draw(document, image, 300, 300);
        int originalLength = image.Stream!.EncodedData.Length;

        PdfImageOptimizationResult kept = Assert.Single(document.PlanImageOptimization(new PdfImageOptimization().WithJpegQuality(60)));
        Assert.Equal(PdfImageOptimizationActions.None, kept.Actions);
        Assert.Equal(PdfImageOptimizationReason.WithinLimits, kept.Reason);

        using PdfDocument saved = SaveAndOpen(document, new PdfImageOptimization().WithJpegQuality(60).WithRecompressLossless());
        PdfImage written = Assert.Single(saved.Pages[0].ExtractImages()).Image;
        Assert.Equal(FilterNames.DctDecode, written.Dictionary[FilterNames.Filter]);
        Assert.False(written.Dictionary.ContainsKey(FilterNames.DecodeParms));
        Assert.Equal((256, 256), (written.Width, written.Height));
        Assert.True(written.Stream!.EncodedData.Length < originalLength);
    }

    [Fact]
    public void A_dct_image_is_resampled_as_dct_and_keeps_its_decode_array()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImage(Gradient(400, 400, 4), new PdfImageLayout(400, 400, PdfDeviceCmykColorSpace.Instance), PdfImageCompression.Dct);
        image.Dictionary[ImageNames.Decode] = new CosArray { new CosInteger(1), new CosInteger(0), new CosInteger(1), new CosInteger(0), new CosInteger(1), new CosInteger(0), new CosInteger(1), new CosInteger(0) };
        Draw(document, image, 72, 72);

        using PdfDocument saved = SaveAndOpen(document, new PdfImageOptimization().WithMaxResolution(100));

        PdfImage written = Assert.Single(saved.Pages[0].ExtractImages()).Image;
        Assert.Equal(FilterNames.DctDecode, written.Dictionary[FilterNames.Filter]);
        Assert.Equal((100, 100), (written.Width, written.Height));
        Assert.Equal([1, 0, 1, 0, 1, 0, 1, 0], written.DecodeArray);
        using DecodedImage decoded = written.Decode()!;
        Assert.Equal(4, decoded.Geometry.Components);
    }

    [Fact]
    public void An_image_mask_stays_one_bit_under_the_bitonal_cap()
    {
        using PdfDocument document = PdfDocument.Create();
        byte[] bits = new byte[(1200 / 8) * 1200];
        for (int y = 0; y < 1200; y++)
        {
            for (int x = 0; x < 1200 / 8; x++)
            {
                bits[(y * 150) + x] = x < 75 ? (byte)0xFF : (byte)0x00;
            }
        }

        PdfImage image = document.CreateImage(bits, new PdfImageLayout(1200, 1200, PdfDeviceGrayColorSpace.Instance, 1), PdfImageCompression.Flate);
        Draw(document, image, 72, 72);
        var options = new PdfImageOptimization().WithMaxResolution(72).WithMaxBitonalResolution(300);

        PdfImageOptimizationResult planned = Assert.Single(document.PlanImageOptimization(options));
        Assert.Equal((300, 300), (planned.TargetWidth, planned.TargetHeight));

        using PdfDocument saved = SaveAndOpen(document, options);
        PdfImage written = Assert.Single(saved.Pages[0].ExtractImages()).Image;
        Assert.Equal((300, 300, 1), (written.Width, written.Height, written.BitsPerComponent));
        using DecodedImage decoded = written.Decode()!;
        Assert.Equal(0xFF, decoded.GetRow(10)[0]);
        Assert.Equal(0x00, decoded.GetRow(10)[37]);
    }

    [Fact]
    public void Images_the_pass_cannot_change_faithfully_are_refused()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfIndexedColorSpace indexed = document.CreateIndexedColorSpace(PdfDeviceRgbColorSpace.Instance, [0, 0, 0, 255, 255, 255]);
        PdfImage palette = document.CreateImage(Gradient(600, 600, 1), new PdfImageLayout(600, 600, indexed), PdfImageCompression.Flate);
        PdfImage keyed = document.CreateImage(Gradient(601, 600, 1), new PdfImageLayout(601, 600, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);
        keyed.Dictionary[ImageNames.Mask] = new CosArray { new CosInteger(0), new CosInteger(10) };
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(palette, 0, 0, 72, 72);
            canvas.DrawImage(keyed, 100, 0, 72, 72);
        }

        IReadOnlyList<PdfImageOptimizationResult> plan = document.PlanImageOptimization(new PdfImageOptimization().WithMaxResolution(150));

        Assert.Equal([PdfImageOptimizationReason.Unsupported, PdfImageOptimizationReason.ColorKeyMask], plan.Select(result => result.Reason));
        Assert.All(plan, result => Assert.Equal(PdfImageOptimizationActions.None, result.Actions));
    }

    [Fact]
    public void Images_within_the_threshold_are_kept()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImage(Gradient(200, 200, 1), new PdfImageLayout(200, 200, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);
        Draw(document, image, 72, 72);

        PdfImageOptimizationResult planned = Assert.Single(document.PlanImageOptimization(new PdfImageOptimization().WithMaxResolution(150)));

        Assert.Equal(PdfImageOptimizationActions.None, planned.Actions);
        Assert.Equal(PdfImageOptimizationReason.WithinLimits, planned.Reason);
        Assert.Equal(PdfImageOptimizationActions.Resample | PdfImageOptimizationActions.Recompress,
            Assert.Single(document.PlanImageOptimization(new PdfImageOptimization().WithMaxResolution(150).WithResampleThreshold(1))).Actions);
    }

    [Fact]
    public void Images_in_forms_patterns_and_annotation_appearances_are_found_and_inline_images_reported()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-oversampled.pdf"));
        IReadOnlyList<PdfImageOptimizationResult> plan = document.PlanImageOptimization(new PdfImageOptimization().WithMaxResolution(150));

        PdfImageOptimizationResult result = Assert.Single(plan);
        Assert.Equal(1200, result.EffectiveResolution!.Value, 6);
        Assert.Equal((150, 150), (result.TargetWidth, result.TargetHeight));

        using PdfDocument inline = PdfDocument.Open(Corpus.Path("inline-image.pdf"));
        Assert.All(inline.PlanImageOptimization(new PdfImageOptimization().WithMaxResolution(1)), r => Assert.Equal(PdfImageOptimizationReason.InlineImage, r.Reason));
    }

    [Fact]
    public void A_pattern_cell_and_an_appearance_place_their_images()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage inPattern = document.CreateImage(Gradient(500, 500, 1), new PdfImageLayout(500, 500, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);
        PdfImage inAppearance = document.CreateImage(Gradient(400, 400, 1), new PdfImageLayout(400, 400, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);

        // A tiling pattern whose cell paints the image over 72 x 72 units, with a pattern matrix scaling by 2: 144 pt on the page.
        var patternResources = new CosDictionary { [new CosName("XObject")] = new CosDictionary { [new CosName("Im0")] = inPattern.Reference! } };
        var pattern = new CosStream(
            new CosDictionary
            {
                [new CosName("PatternType")] = new CosInteger(1),
                [new CosName("PaintType")] = new CosInteger(1),
                [new CosName("TilingType")] = new CosInteger(1),
                [new CosName("BBox")] = new CosArray { new CosInteger(0), new CosInteger(0), new CosInteger(72), new CosInteger(72) },
                [new CosName("XStep")] = new CosInteger(72),
                [new CosName("YStep")] = new CosInteger(72),
                [new CosName("Matrix")] = new CosArray { new CosInteger(2), new CosInteger(0), new CosInteger(0), new CosInteger(2), new CosInteger(0), new CosInteger(0) },
                [new CosName("Resources")] = patternResources,
            },
            "q 72 0 0 72 0 0 cm /Im0 Do Q"u8.ToArray());
        CosReference patternReference = document.AddObject(pattern);
        PdfPage page = document.Pages[0];
        page.Dictionary[new CosName("Resources")] = new CosDictionary { [new CosName("Pattern")] = new CosDictionary { [new CosName("P0")] = patternReference } };
        page.Dictionary[new CosName("Contents")] = document.AddObject(new CosStream(new CosDictionary(), "/Pattern cs /P0 scn 0 0 300 300 re f"u8.ToArray()));

        // An annotation whose appearance paints the other image over its whole 100 x 100 bounding box, on a 36 x 36 rectangle.
        var appearance = new CosStream(
            new CosDictionary
            {
                [new CosName("Type")] = new CosName("XObject"),
                [new CosName("Subtype")] = new CosName("Form"),
                [new CosName("BBox")] = new CosArray { new CosInteger(0), new CosInteger(0), new CosInteger(100), new CosInteger(100) },
                [new CosName("Resources")] = new CosDictionary { [new CosName("XObject")] = new CosDictionary { [new CosName("Im1")] = inAppearance.Reference! } },
            },
            "q 100 0 0 100 0 0 cm /Im1 Do Q"u8.ToArray());
        var annotation = new CosDictionary
        {
            [new CosName("Type")] = new CosName("Annot"),
            [new CosName("Subtype")] = new CosName("Stamp"),
            [new CosName("Rect")] = new CosArray { new CosInteger(400), new CosInteger(400), new CosInteger(436), new CosInteger(436) },
            [new CosName("AP")] = new CosDictionary { [new CosName("N")] = document.AddObject(appearance) },
        };
        page.Dictionary[new CosName("Annots")] = new CosArray { document.AddObject(annotation) };

        IReadOnlyList<PdfImageOptimizationResult> plan = document.PlanImageOptimization(new PdfImageOptimization().WithMaxResolution(100));

        Assert.Equal(2, plan.Count);
        Assert.Same(inPattern.Stream, plan[0].Image.Stream);
        Assert.Equal(250, plan[0].EffectiveResolution!.Value, 6);
        Assert.Same(inAppearance.Stream, plan[1].Image.Stream);
        Assert.Equal(800, plan[1].EffectiveResolution!.Value, 6);
    }

    [Fact]
    public void An_image_that_fails_to_decode_is_kept_with_an_information_diagnostic()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImage(Gradient(600, 600, 1), new PdfImageLayout(600, 600, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);
        image.Stream!.EncodedData = "not deflate data at all"u8.ToArray();
        Draw(document, image, 72, 72);

        PdfImageOptimizationResult result = Assert.Single(ImageOptimizer.Apply(document, new PdfImageOptimization().WithMaxResolution(150), System.IO.Compression.CompressionLevel.Optimal));

        Assert.Equal(PdfImageOptimizationActions.None, result.Actions);
        Assert.Contains(result.Reason, new[] { PdfImageOptimizationReason.DecodeFailed, PdfImageOptimizationReason.Unsupported });
        Assert.Equal("not deflate data at all"u8.ToArray(), image.Stream.EncodedData.ToArray());
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == ImageDiagnostics.ImageOptimizationSkipped.Name && diagnostic.Severity == DiagnosticSeverity.Information);
    }

    [Fact]
    public void The_options_validate_their_values()
    {
        var options = new PdfImageOptimization();
        Assert.Null(options.MaxResolution);
        Assert.Equal(1.5, options.ResampleThreshold);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithMaxResolution(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithMaxBitonalResolution(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithResampleThreshold(0.9));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithJpegQuality(101));
        Assert.Same(options, options.WithMaxResolution(150).WithJpegQuality(80).WithRecompressLossless().WithMaxBitonalResolution(300).WithResampleThreshold(2));
        Assert.Equal((150, 80, true, 300, 2), (options.MaxResolution, options.JpegQuality, options.RecompressLossless, options.MaxBitonalResolution, options.ResampleThreshold));
    }

    /// <summary>A horizontal ramp: each row runs from 0 at the left to 255 at the right, in every component.</summary>
    internal static byte[] Gradient(int width, int height, int components)
    {
        byte[] samples = new byte[width * height * components];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte value = (byte)(x * 255 / Math.Max(1, width - 1));
                for (int c = 0; c < components; c++)
                {
                    samples[(((y * width) + x) * components) + c] = value;
                }
            }
        }

        return samples;
    }

    /// <summary>Smooth noise a photograph might have: a ramp with deterministic pseudo-random grain.</summary>
    internal static byte[] Noise(int width, int height, int components)
    {
        byte[] samples = Gradient(width, height, components);
        uint state = 12345;
        for (int i = 0; i < samples.Length; i++)
        {
            state = (state * 1103515245) + 12345;
            samples[i] = (byte)Math.Clamp(samples[i] + (int)((state >> 16) % 32) - 16, 0, 255);
        }

        return samples;
    }

    private static void Draw(PdfDocument document, PdfImage image, double width, double height)
    {
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        canvas.DrawImage(image, 72, 72, width, height);
    }

    private static PdfDocument SaveAndOpen(PdfDocument document, PdfImageOptimization options)
    {
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithImageOptimization(options));
        return PdfDocument.Open(stream.ToArray());
    }
}
