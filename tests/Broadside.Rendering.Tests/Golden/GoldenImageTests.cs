using Broadside.GoldenImages;

namespace Broadside.Rendering.Tests.Golden;

/// <summary>
/// The golden images (issue #120): every page of every hand-written corpus file rendered by the software rasterizer at 72 DPI and
/// reviewed as a snapshot in <c>tests/Golden/</c>. Adding a corpus file (to <c>Corpus.cs</c>) adds its pages here; review a new or
/// changed image as carefully as code, since every other backend is compared with it.
/// </summary>
public sealed class GoldenImageTests
{
    [Theory]
    [MemberData(nameof(GoldenCorpus.Pages), MemberType = typeof(GoldenCorpus))]
    public async Task Every_page_of_every_corpus_file_matches_its_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        await GoldenCorpus.VerifyAsync(fileName, page, GoldenImage.FromBitmap(bitmap));
    }

    [Fact]
    public void A_backend_render_that_matches_passes_and_one_that_does_not_leaves_a_received_and_a_diff_image()
    {
        const string backend = "harness-self-test";
        const string fileName = "paths-fill-rules.pdf";
        GoldenImage reference = GoldenCorpus.LoadReference(fileName, 0);
        GoldenImage same = AsTransparentPremultipliedRgba(reference, shift: 0);
        GoldenImage moved = AsTransparentPremultipliedRgba(reference, shift: 1);

        // A directory of this run's own (issue #293): runs sharing a checkout never meet over the same received and diff images.
        DirectoryInfo output = Directory.CreateTempSubdirectory("broadside-golden-");
        try
        {
            string stem = Path.Combine(output.FullName, GoldenCorpus.SnapshotName(fileName, 0) + "." + backend);

            GoldenCorpus.AssertMatchesReference(backend, fileName, 0, same, output.FullName);
            Assert.False(File.Exists(stem + ".received.png"));

            var failure = Assert.Throws<GoldenImageMismatchException>(
                () => GoldenCorpus.AssertMatchesReference(backend, fileName, 0, moved, output.FullName));
            Assert.Contains("does not match", failure.Message, StringComparison.Ordinal);
            Assert.Contains(stem + ".received.png", failure.Message, StringComparison.Ordinal);
            Assert.True(GoldenComparison.Compare(moved, GoldenImage.LoadPng(stem + ".received.png"), GoldenThreshold.Exact).Matches);
            GoldenImage diff = GoldenImage.LoadPng(stem + ".diff.png");
            Assert.Equal((reference.Width, reference.Height), (diff.Width, diff.Height));

            // A later match removes them, and nothing was written beside the golden images.
            GoldenCorpus.AssertMatchesReference(backend, fileName, 0, same, output.FullName);
            Assert.False(File.Exists(stem + ".received.png"));
            Assert.False(File.Exists(stem + ".diff.png"));
            Assert.Empty(Directory.GetFiles(GoldenCorpus.Directory, "*." + backend + ".*"));
        }
        finally
        {
            output.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_backend_failure_leaves_its_received_and_diff_images_beside_the_golden_image_by_default()
    {
        // A backend name of this run's own, so concurrent runs in one checkout never share these files.
        string backend = "harness-self-test-" + Guid.NewGuid().ToString("N");
        const string fileName = "paths-fill-rules.pdf";
        GoldenImage reference = GoldenCorpus.LoadReference(fileName, 0);
        string stem = Path.Combine(GoldenCorpus.Directory, GoldenCorpus.SnapshotName(fileName, 0) + "." + backend);
        try
        {
            Assert.Throws<GoldenImageMismatchException>(
                () => GoldenCorpus.AssertMatchesReference(backend, fileName, 0, AsTransparentPremultipliedRgba(reference, shift: 1)));
            Assert.True(File.Exists(stem + ".received.png"));
            Assert.True(File.Exists(stem + ".diff.png"));

            GoldenCorpus.AssertMatchesReference(backend, fileName, 0, reference);
            Assert.False(File.Exists(stem + ".received.png"));
            Assert.False(File.Exists(stem + ".diff.png"));
        }
        finally
        {
            File.Delete(stem + ".received.png");
            File.Delete(stem + ".diff.png");
        }
    }

    [Fact]
    public void Every_page_of_every_corpus_file_has_a_golden_image_and_no_golden_image_is_orphaned()
    {
        var expected = GoldenCorpus.Pages.Select(row => GoldenCorpus.SnapshotName(row.Data.Item1, row.Data.Item2) + ".verified.png")
            .ToHashSet(StringComparer.Ordinal);
        var present = Directory.GetFiles(GoldenCorpus.Directory, "*.verified.png").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(present.Except(expected));
    }

    // A backend that hands over premultiplied RGBA with a transparent page: the golden image once composited over white, optionally
    // shifted right by some pixels (the uncovered columns are transparent, so white).
    private static GoldenImage AsTransparentPremultipliedRgba(GoldenImage reference, int shift)
    {
        byte[] rgba = new byte[reference.Width * reference.Height * 4];
        for (int y = 0; y < reference.Height; y++)
        {
            for (int x = shift; x < reference.Width; x++)
            {
                (byte r, byte g, byte b) = reference[x - shift, y];
                byte alpha = (byte)(255 - Math.Min(r, Math.Min(g, b)));
                int offset = ((y * reference.Width) + x) * 4;
                rgba[offset] = (byte)(r - (255 - alpha));
                rgba[offset + 1] = (byte)(g - (255 - alpha));
                rgba[offset + 2] = (byte)(b - (255 - alpha));
                rgba[offset + 3] = alpha;
            }
        }

        return GoldenImage.FromPixels(rgba, reference.Width, reference.Height, reference.Width * 4, GoldenPixelFormat.Rgba8Premultiplied);
    }
}
