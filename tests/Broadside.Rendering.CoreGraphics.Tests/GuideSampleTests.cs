using Broadside.GoldenImages;
using Broadside.Samples;
using Broadside.TestSupport;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// The rendering guide's CoreGraphics sample (samples/guides/RenderingBackends/CoreGraphicsSample.cs, issue #268) runs here, on the
/// macOS leg that runs this backend's tests, and its render matches the page's golden image within the coregraphics threshold.
/// </summary>
public sealed class GuideSampleTests
{
    [Fact]
    public void The_rendering_guide_sample_matches_the_golden_image()
    {
        (byte[] pixels, int width, int height, int stride) = CoreGraphicsSample.Render(Corpus.Path("paths-fill-rules.pdf"));

        GoldenCorpus.AssertMatchesReference(
            "coregraphics",
            "paths-fill-rules.pdf",
            0,
            GoldenImage.FromPixels(pixels, width, height, stride, GoldenPixelFormat.Bgra8Premultiplied));
    }
}
