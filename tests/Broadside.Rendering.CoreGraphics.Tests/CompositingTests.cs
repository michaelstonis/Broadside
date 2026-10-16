using Broadside.TestSupport;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// Constant alpha and blend modes on CoreGraphics (ISO 32000-2 §11.3.5, §11.6.3, §11.6.4.4): the native blend modes, and the three whose
/// CoreGraphics functions are not Table 134's, drawn natively and reported.
/// </summary>
public sealed class CompositingTests
{
    /// <summary>The blend modes CoreGraphics computes as §11.3.5 defines them (within the golden-image threshold of the rasterizer's).</summary>
    internal static readonly string[] ExactModes =
    [
        "Normal", "Multiply", "Screen", "Overlay", "Darken", "Lighten", "HardLight", "Difference", "Exclusion", "Hue", "Saturation",
        "Color", "Luminosity",
    ];

    [Fact]
    public void Blend_modes_coregraphics_computes_otherwise_are_reported_once_per_render()
    {
        using PdfDocument document = PdfDocument.Open(BlendSamples.SolidGrid("ColorDodge", "ColorBurn", "SoftLight"));
        using var renderer = new CoreGraphicsRenderer();

        Render(renderer, document.Pages[0]);

        Assert.Equal("BackendBlendModeApproximated", Assert.Single(renderer.Diagnostics).Code);
    }

    [Fact]
    public void The_modes_coregraphics_computes_as_written_report_nothing_and_each_render_starts_without_diagnostics()
    {
        using PdfDocument approximated = PdfDocument.Open(BlendSamples.SolidGrid("SoftLight"));
        using PdfDocument exact = PdfDocument.Open(BlendSamples.SolidGrid(ExactModes));
        using var renderer = new CoreGraphicsRenderer();
        Render(renderer, approximated.Pages[0]);

        Render(renderer, exact.Pages[0]);

        Assert.Empty(renderer.Diagnostics);
    }

    private static void Render(CoreGraphicsRenderer renderer, PdfPage page)
    {
        RenderOptions options = new();
        PixelSize size = options.GetPixelSize(page);
        using var context = BitmapRendering.CreateContext(size.Width, size.Height, out _, out _);
        renderer.Render(page, context, options);
    }
}
