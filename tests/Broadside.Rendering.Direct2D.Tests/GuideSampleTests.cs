using Broadside.GoldenImages;
using Broadside.Samples;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// The rendering guide's Direct2D sample (samples/guides/RenderingBackends/Direct2DSample.cs, issue #268) runs here, on the Windows leg
/// that runs this backend's tests, on a WARP device, and its render matches the page's golden image within the direct2d threshold.
/// </summary>
public sealed class GuideSampleTests
{
    [Fact]
    public void The_rendering_guide_sample_matches_the_golden_image()
    {
        using Bitmap bitmap = Direct2DSample.Render(Corpus.Path("paths-fill-rules.pdf"), Direct2DDeviceType.Warp);

        GoldenCorpus.AssertMatchesReference("direct2d", "paths-fill-rules.pdf", 0, GoldenImage.FromBitmap(bitmap));
    }
}
