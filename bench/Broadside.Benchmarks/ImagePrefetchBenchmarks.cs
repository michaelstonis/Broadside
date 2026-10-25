using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The platform image decoder seam's asynchronous boundary (issue #372), which a viewer awaits before every tile: with a platform
/// decoder registered, on a text-only page (the gate case: nothing to offer, under 1 µs) and on an image page whose image the
/// platform already decoded (the warm case: found in the cache). Both read <c>-</c> in <c>Allocated</c>;
/// <c>Broadside.Tests.Images.PlatformImageAllocationTests</c> fails the build if they regress.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class ImagePrefetchBenchmarks
{
    private static readonly Matrix Device = new(1, 0, 0, 1, 0, 0);

    private PdfDocument? _text;
    private PdfDocument? _image;
    private DisplayList? _textList;
    private DisplayList? _imageList;

    [GlobalSetup]
    public void Setup()
    {
        static PdfOptions Options() => new PdfOptions().UsePlatformImageDecoder(new SynchronousDecoder());
        _text = PdfDocument.Open(File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, "text-standard14.pdf")), Options());
        _image = PdfDocument.Open(File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, "dct-baseline.pdf")), Options());
        _textList = _text.Pages[0].CreateDisplayList();
        _imageList = _image.Pages[0].CreateDisplayList();
        _imageList.PrefetchImagesAsync(Device, null, 2, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _text?.Dispose();
        _image?.Dispose();
    }

    [Benchmark]
    public bool TextPage() => Done(_textList!.PrefetchImagesAsync(Device, null, 2, CancellationToken.None));

    [Benchmark]
    public bool ImagePageWarm() => Done(_imageList!.PrefetchImagesAsync(Device, null, 2, CancellationToken.None));

    private static bool Done(ValueTask prefetch) => prefetch.IsCompletedSuccessfully;

    /// <summary>A platform decoder that completes at once with gray samples.</summary>
    private sealed class SynchronousDecoder : IPlatformImageDecoder
    {
        public string Name => "synchronous";

        public PlatformImageFidelity Fidelity => PlatformImageFidelity.Tolerance;

        public PlatformImageFeatures Features => PlatformImageFeatures.None;

        public bool CanDecode(in PlatformImageRequest request) => true;

        public ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
        {
            using DecodedImageBuilder builder = request.CreateImage(request.Reduction);
            builder.Samples.Fill(128);
            return ValueTask.FromResult<DecodedImage?>(builder.Build());
        }
    }
}
