using System.Runtime.InteropServices;
using Broadside.Diagnostics;
using Broadside.GoldenImages;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// Transparency groups and soft masks on the Skia backend (issue #124: ISO 32000-2 §11.4, §11.5, §11.6.5, §11.6.6, §11.7.4.4):
/// the transparency corpus pages against the software rasterizer's golden images under the <c>skia</c> rows of
/// <see cref="GoldenThresholds"/>, the groups handed to the software rasterizer reported once per kind per page, the Ghent
/// transparency patches against the rasterizer, and a render onto a GPU surface where one is available.
/// </summary>
public sealed class TransparencyTests
{
    private const string Knockout = "BackendKnockoutGroupRasterized";
    private const string ColorSpace = "BackendGroupColorSpaceRasterized";
    private const string NonIsolated = "BackendNonIsolatedGroupApproximated";
    private const string SoftMask = "BackendSoftMaskRasterized";

    [Theory]
    [MemberData(nameof(TransparencyPages))]
    public void A_transparency_page_matches_its_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var backend = new SkiaBackend();
        using SKBitmap bitmap = backend.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        Assert.DoesNotContain(backend.Diagnostics, diagnostic => diagnostic.Code is "BackendTransparencyGroupApproximated" or "BackendSoftMaskIgnored" or NonIsolated);
        GoldenCorpus.AssertMatchesReference("skia", fileName, page, PaintGoldenImageTests.Image(bitmap));
    }

    [Theory]
    [MemberData(nameof(ReportedPages))]
    public void Each_kind_of_group_the_software_rasterizer_draws_is_reported_once_per_page(string fileName, int page, string codes)
    {
        // transparency-groups page 1: the explicit knockout groups of rows 1 and 2 and the DeviceCMYK group of row 3 that multiplies;
        // the implicit knockout group of row 4's b, the isolated and the non-isolated groups are Skia layers or drawn directly.
        // Page 2: the DeviceCMYK page group, whose discs multiply. Soft-mask values always come from the software rasterizer.
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var backend = new SkiaBackend();
        using (backend.Render(document.Pages[page]))
        {
        }

        Assert.Equal(codes.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal), backend.Diagnostics.Select(diagnostic => diagnostic.Code).Order(StringComparer.Ordinal));
        Assert.All(backend.Diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void A_synthetic_transparency_page_renders_as_the_software_rasterizer_renders_it(string sample, double edges, double flat, int pixels)
    {
        using PdfDocument document = PdfDocument.Open(Sample(sample));
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(document.Pages[0], options);
        using var backend = new SkiaBackend();
        using SKBitmap actual = backend.Render(document.Pages[0], options);

        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(expected), PaintGoldenImageTests.Image(actual), new GoldenThreshold(edges, flat, pixels));
        Assert.True(comparison.Matches, comparison.Describe());
    }

    [Fact]
    public void A_repeated_render_of_a_masked_page_reuses_its_mask_values_and_draws_the_same_pixels()
    {
        using PdfDocument document = GoldenCorpus.Open("transparency-soft-masks.pdf");
        using var backend = new SkiaBackend();
        using SKBitmap first = backend.Render(document.Pages[0]);
        using SKBitmap second = backend.Render(document.Pages[0]);

        Assert.True(first.GetPixelSpan().SequenceEqual(second.GetPixelSpan()));
    }

    [Theory]
    [MemberData(nameof(GhentPatches))]
    [Trait("Category", "Corpus")]
    public void A_ghent_transparency_patch_renders_as_the_software_rasterizer_renders_it(string name, double edges, double flat, int pixels)
    {
        string? path = RealWorldCorpus.Files("gwg-output-suite").FirstOrDefault(file => Path.GetFileName(file).StartsWith(name, StringComparison.Ordinal));
        Assert.SkipWhen(path is null, "The gwg-output-suite corpus is not fetched (BROADSIDE_CORPUS_DIR).");
        using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(path!)));
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(document.Pages[0], options);
        using var backend = new SkiaBackend();
        using SKBitmap actual = backend.Render(document.Pages[0], options);

        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(expected), PaintGoldenImageTests.Image(actual), new GoldenThreshold(edges, flat, pixels));
        Assert.True(comparison.Matches, comparison.Describe());
    }

    [Fact]
    public void A_page_rendered_onto_a_gpu_surface_matches_the_raster_render()
    {
        using GpuContext? gpu = GpuContext.TryCreate();
        Assert.SkipWhen(gpu is null, "No GPU context is available (Metal on macOS only).");
        using PdfDocument document = GoldenCorpus.Open("transparency-groups.pdf");
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        PixelSize size = options.GetPixelSize(document.Pages[0]);
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var backend = new SkiaBackend();
        using SKBitmap raster = backend.Render(document.Pages[0], options);
        using SKSurface? surface = SKSurface.Create(gpu!.Context, budgeted: false, info);
        Assert.NotNull(surface);
        backend.Render(document.Pages[0], surface.Canvas, options);
        surface.Flush();
        using var read = new SKBitmap(info);
        Assert.True(surface.ReadPixels(info, read.GetPixels(), read.RowBytes, 0, 0));

        GoldenComparison comparison = GoldenComparison.Compare(PaintGoldenImageTests.Image(raster), PaintGoldenImageTests.Image(read), GoldenThreshold.Default);
        Assert.True(comparison.Matches, comparison.Describe());
    }

    public static TheoryData<string, int> TransparencyPages() =>
        GoldenCorpus.PagesOf("transparency-blend-modes.pdf", "transparency-groups.pdf", "transparency-soft-masks.pdf", "extgstate-params.pdf");

    /// <summary>The pages and the diagnostics (comma-separated codes) a render of each reports.</summary>
    public static TheoryData<string, int, string> ReportedPages() => new()
    {
        { "transparency-blend-modes.pdf", 0, string.Empty },
        { "transparency-groups.pdf", 0, Knockout + "," + ColorSpace },
        { "transparency-groups.pdf", 1, ColorSpace },
        { "transparency-soft-masks.pdf", 0, SoftMask },
        { "extgstate-params.pdf", 0, SoftMask },
    };

    /// <summary>
    /// The synthetic pages and the threshold each meets against the rasterizer. group-grid: two cells differ, 19 860 pixels at most
    /// 0.17 apart. The non-isolated group of Multiply paints itself painted in Multiply (§11.4.8) needs the backdrop removed from
    /// its result before it blends again; Skia has no layer for that, so the software rasterizer draws it against a transparent
    /// backdrop, which blends once where the rasterizer blends twice (reported as <c>BackendNonIsolatedGroupApproximated</c>). The
    /// tiling pattern whose cell blends is the non-isolated group of §11.6.7 NOTE 2 in the rasterizer and an isolated picture in
    /// Skia (#122). Every other cell, the knockout, gray, CMYK, nested and clipped groups, matches within the default.
    /// </summary>
    public static TheoryData<string, double, double, int> Samples() => new()
    {
        { "group-grid", 0.3, 0.05, 20000 },
        { "soft-mask-grid", 0.3, 0.05, 0 },
        { "non-isolated", 0.3, 0.05, 0 },
    };

    private static byte[] Sample(string name) => name switch
    {
        "group-grid" => GroupSamples.Grid(),
        "soft-mask-grid" => SoftMaskSamples.Grid(),
        "non-isolated" => NonIsolatedSample(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>
    /// A non-isolated group of Multiply paints at <c>/ca 0.5</c> (§11.4.8) over an opaque backdrop, and the same group inside an
    /// isolated group over a translucent backdrop of its own (a backdrop whose alpha is below 1).
    /// </summary>
    private static byte[] NonIsolatedSample() => ContentSamples.OnePage(
        "0 0.8 0.2 rg 20 20 572 300 re f q /H gs 1 0 0 1 40 40 cm /N Do Q q 1 0 0 1 320 40 cm /I Do Q"u8.ToArray(),
        "/ExtGState << /H << /ca 0.5 >> >> /XObject << /N 5 0 R /I 6 0 R >>",
        ContentSamples.Stream(
            "/M gs 1 0 0 rg 20 20 120 120 re f 0 0 1 rg 70 70 120 120 re f",
            "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Resources << /ExtGState << /M << /BM /Multiply >> >> >> /Group << /S /Transparency >>"),
        ContentSamples.Stream(
            "/H gs 1 1 0 rg 0 0 200 200 re f /N Do",
            "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Resources << /ExtGState << /H << /ca 0.5 >> >> /XObject << /N 5 0 R >> >> /Group << /S /Transparency /I true >>"));

    /// <summary>The Ghent Output Suite 5.0 transparency and soft-mask patches and the threshold each meets against the rasterizer.</summary>
    public static TheoryData<string, double, double, int> GhentPatches() => new()
    {
        { "GWG160_", 0.3, 0.05, 0 },
        { "GWG161_Transp_Basic_BM_DeviceCMYK", 0.3, 0.05, 0 },
        { "GWG161_Transp_Basic_BM_ICCBasedRGB", 0.3, 0.05, 0 },
        { "GWG162_", 0.3, 0.05, 0 },
        { "GWG164_", 0.3, 0.05, 0 },
        { "GWG1610_", 0.3, 0.05, 0 },
        { "GWG1611_", 0.3, 0.05, 0 },
    };

    /// <summary>A Metal device, command queue and Skia context, created only on macOS where a Metal device exists.</summary>
    private sealed class GpuContext : IDisposable
    {
        private readonly IntPtr _device;
        private readonly IntPtr _queue;

        private GpuContext(IntPtr device, IntPtr queue, GRContext context) => (_device, _queue, Context) = (device, queue, context);

        public GRContext Context { get; }

        public static GpuContext? TryCreate()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return null;
            }

            try
            {
                IntPtr device = MTLCreateSystemDefaultDevice();
                if (device == IntPtr.Zero)
                {
                    return null;
                }

                IntPtr queue = SendMessage(device, RegisterSelector("newCommandQueue"));
                using var backend = new GRMtlBackendContext { DeviceHandle = device, QueueHandle = queue };
#pragma warning disable CA2000 // The context is owned by the returned GpuContext.
                GRContext context = GRContext.CreateMetal(backend);
#pragma warning restore CA2000
                return new GpuContext(device, queue, context);
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            Context.Dispose();
            _ = SendMessage(_queue, RegisterSelector("release"));
            _ = SendMessage(_device, RegisterSelector("release"));
        }

        [DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
        private static extern IntPtr MTLCreateSystemDefaultDevice();

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
        private static extern IntPtr RegisterSelector(string name);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector);
    }
}
