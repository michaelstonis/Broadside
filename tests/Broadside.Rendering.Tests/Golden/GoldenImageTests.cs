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
        string stem = Path.Combine(GoldenCorpus.Directory, GoldenCorpus.SnapshotName(fileName, 0) + "." + backend);

        // A backend that hands over premultiplied RGBA with a transparent page: the same image once composited over white.
        byte[] rgba = new byte[reference.Width * reference.Height * 4];
        for (int y = 0; y < reference.Height; y++)
        {
            for (int x = 0; x < reference.Width; x++)
            {
                (byte r, byte g, byte b) = reference[x, y];
                byte alpha = (byte)(255 - Math.Min(r, Math.Min(g, b)));
                int offset = ((y * reference.Width) + x) * 4;
                rgba[offset] = (byte)(r - (255 - alpha));
                rgba[offset + 1] = (byte)(g - (255 - alpha));
                rgba[offset + 2] = (byte)(b - (255 - alpha));
                rgba[offset + 3] = alpha;
            }
        }

        GoldenImage same = GoldenImage.FromPixels(rgba, reference.Width, reference.Height, reference.Width * 4, GoldenPixelFormat.Rgba8Premultiplied);
        GoldenCorpus.AssertMatchesReference(backend, fileName, 0, same);
        Assert.False(File.Exists(stem + ".received.png"));

        // Shifted one pixel to the right.
        byte[] shifted = new byte[rgba.Length];
        for (int y = 0; y < reference.Height; y++)
        {
            rgba.AsSpan(y * reference.Width * 4, (reference.Width - 1) * 4).CopyTo(shifted.AsSpan((y * reference.Width * 4) + 4));
        }

        GoldenImage moved = GoldenImage.FromPixels(shifted, reference.Width, reference.Height, reference.Width * 4, GoldenPixelFormat.Rgba8Premultiplied);
        var failure = Assert.Throws<GoldenImageMismatchException>(() => GoldenCorpus.AssertMatchesReference(backend, fileName, 0, moved));
        try
        {
            Assert.Contains("does not match", failure.Message, StringComparison.Ordinal);
            Assert.True(GoldenComparison.Compare(moved, GoldenImage.LoadPng(stem + ".received.png"), GoldenThreshold.Exact).Matches);
            GoldenImage diff = GoldenImage.LoadPng(stem + ".diff.png");
            Assert.Equal((reference.Width, reference.Height), (diff.Width, diff.Height));
        }
        finally
        {
            // A later match removes them.
            GoldenCorpus.AssertMatchesReference(backend, fileName, 0, same);
        }

        Assert.False(File.Exists(stem + ".received.png"));
        Assert.False(File.Exists(stem + ".diff.png"));
    }

    [Fact]
    public void Every_page_of_every_corpus_file_has_a_golden_image_and_no_golden_image_is_orphaned()
    {
        var expected = GoldenCorpus.Pages.Select(row => GoldenCorpus.SnapshotName(row.Data.Item1, row.Data.Item2) + ".verified.png")
            .ToHashSet(StringComparer.Ordinal);
        var present = Directory.GetFiles(GoldenCorpus.Directory, "*.verified.png").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(present.Except(expected));
    }
}
