using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Rendering.Skia;
using Broadside.TestSupport;

namespace Broadside.Viewer.Maui.Tests;

/// <summary>
/// Issue #374: a document <c>PdfView</c> opens from its source decodes JPEG images with SkiaSharp's decoder before a tile is drawn,
/// unless the options turn it off by name or give their own engine.
/// </summary>
/// <remarks>In the non-parallel collection: the document caches' epoch is process-wide (see <c>PlatformImageViewerTests</c>).</remarks>
[Collection(HeavyTestCollection.Name)]
public sealed class SkiaImageDecoderRegistrationTests
{
    [Fact]
    public async Task The_view_registers_the_Skia_decoder_with_the_engine_it_builds()
    {
        ViewerOptions options = SkiaViewerOptions.For(null);
        options.DocumentOptions!.WithPlatformImageVerification(true);

        Assert.Equal(["PlatformImageVerified"], await DecodesAsync(options));
    }

    [Fact]
    public async Task Options_can_turn_the_Skia_decoder_off_by_name()
    {
        var given = new ViewerOptions().WithDocumentOptions(
            new PdfOptions().WithPlatformImageVerification(true).WithPlatformImageDecoder(SkiaImageDecoder.DecoderName, false));

        Assert.Empty(await DecodesAsync(SkiaViewerOptions.For(given)));
    }

    [Fact]
    public void An_engine_the_app_gives_is_used_as_it_is()
    {
        var engine = new PdfEngine(new PdfOptions());
        var given = new ViewerOptions().UseEngine(engine);

        ViewerOptions options = SkiaViewerOptions.For(given);

        Assert.Same(engine, options.Engine);
        Assert.Null(options.DocumentOptions);
    }

    /// <summary>Opens a photograph's page with <paramref name="options"/>, prefetches a tile's images and returns the platform decode diagnostics.</summary>
    private static async Task<string[]> DecodesAsync(ViewerOptions options)
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(640, 800, DctChromaSubsampling.None), options.DocumentOptions!);
        DisplayList list = document.Pages[0].CreateDisplayList();
        using ITileBackend backend = options.BackendFactory!();

        await backend.PrefetchImagesAsync(list, Matrix.Identity, 256, 256, TestContext.Current.CancellationToken);

        return [.. document.Diagnostics.Select(d => d.Code).Where(c => c.StartsWith("PlatformImage", StringComparison.Ordinal))];
    }
}
