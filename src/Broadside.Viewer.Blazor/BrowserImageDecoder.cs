using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Broadside.Images;

namespace Broadside.Viewer.Blazor;

/// <summary>
/// The browser's own JPEG decoder behind the platform image decoder seam: WebCodecs <c>ImageDecoder</c> where the browser has it
/// (Chrome and Edge 94+, Firefox 133+, on the page and in workers), <c>createImageBitmap</c> with a read back in a worker that does
/// not (Safari's render worker). The viewer registers it by default; turn it off with
/// <c>PdfOptions.WithPlatformImageDecoder(BrowserImageDecoder.DecoderName, false)</c>, or all platform decoding with
/// <c>PdfOptions.WithPlatformImageDecoding(false)</c>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.8 (DCTDecode) and §8.9.3. At full size <c>ImageDecoder</c> hands back the YCbCr planes, which are converted to RGB
/// with libjpeg's integer transform and chroma replication, the managed decoder's own, so the samples are bit-identical to it. For a
/// reduced decode it scales in the DCT (libjpeg's <c>desiredWidth</c>), within the measured tolerance (about 62 dB against the managed
/// reduced decode). The decode runs off the page's thread; only the copy into .NET memory and the plane conversion (in slices of about
/// 6 ms) run on it.
/// </para>
/// <para>
/// What the browser can do is probed at run time (<see cref="InitializeAsync"/>), never read from the user agent. A page without
/// <c>ImageDecoder</c> (Safari) decodes with the managed codec and its scaled decode: WebKit's <c>createImageBitmap</c> read back costs
/// 60 to 141 ms on the calling thread and converts embedded colour profiles regardless of <c>colorSpaceConversion: 'none'</c>. In the
/// render worker that cost leaves the page's thread, so the worker uses <c>createImageBitmap</c> there (with the Exif and ICC payloads
/// zero-filled by the core first), for images whose chroma is not subsampled: WebKit interpolates subsampled chroma, measured at max 27
/// levels and 1.03% of pixels over 2 levels on a 4:2:0 photograph, outside the tolerance, so those decode managed. Twelve-bit, arithmetic-coded and JPEG 2000 data always decode managed; so does anything a browser
/// rejects, with an <c>ImageDecodedManaged</c> diagnostic. No .NET WebAssembly threads are used.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
public sealed class BrowserImageDecoder : IPlatformImageDecoder
{
    /// <summary>The <see cref="Name"/> of every browser image decoder: <c>browser</c>.</summary>
    public const string DecoderName = "browser";

    private const int WebCodecs = 1;
    private const int ImageBitmap = 2;
    private const int Worker = 4;

    private static Task<int>? _probe;
    private static int _tiers = -1;

    private readonly BrowserImageDecoderOptions _options;

    /// <summary>Initializes a new instance of the <see cref="BrowserImageDecoder"/> class.</summary>
    /// <param name="options">Which browser decoders to use; the defaults when <see langword="null"/>.</param>
    public BrowserImageDecoder(BrowserImageDecoderOptions? options = null) => _options = options ?? new BrowserImageDecoderOptions();

    /// <summary>Gets the decoder the viewer registers by default.</summary>
    public static BrowserImageDecoder Shared { get; } = new();

    /// <summary>Gets a value indicating whether this context has a browser decoder the options allow; false until <see cref="InitializeAsync"/> completes.</summary>
    public bool IsAvailable => Tier != 0;

    /// <summary>Gets the browser decoder this context uses: <c>ImageDecoder</c>, <c>createImageBitmap</c>, or <c>none</c> (managed); <c>none</c> before <see cref="InitializeAsync"/>.</summary>
    public string Description => Tier switch
    {
        WebCodecs => "ImageDecoder",
        ImageBitmap => "createImageBitmap",
        _ => "none",
    };

    /// <summary>Gets a value indicating whether this context has <c>createImageBitmap</c> and no WebCodecs <c>ImageDecoder</c> for JPEG (Safari), after <see cref="InitializeAsync"/>.</summary>
    internal static bool ImageBitmapOnly => Volatile.Read(ref _tiers) is int tiers && tiers > 0 && (tiers & WebCodecs) == 0 && (tiers & ImageBitmap) != 0;

    /// <inheritdoc/>
    public string Name => DecoderName;

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §7.4.8. <see cref="PlatformImageFidelity.Tolerance"/>, the worst of its paths: the YCbCr planes at full size are
    /// exact; reduced decodes and the read back paths are within the tolerance.
    /// </remarks>
    public PlatformImageFidelity Fidelity => PlatformImageFidelity.Tolerance;

    /// <inheritdoc/>
    /// <remarks>
    /// Browsers apply the Exif orientation in <c>createImageBitmap</c>, and WebKit converts embedded profiles regardless of
    /// <c>colorSpaceConversion: 'none'</c>; the core zero-fills both payloads for every browser path, which costs one copy of the data.
    /// </remarks>
    public PlatformImageFeatures Features => PlatformImageFeatures.AppliesExifOrientation | PlatformImageFeatures.ConvertsEmbeddedProfiles;

    // The tier this context decodes with under the options: 1 ImageDecoder, 2 createImageBitmap, 0 none.
    private int Tier
    {
        get
        {
            int tiers = Volatile.Read(ref _tiers);
            if (tiers < 0)
            {
                return 0;
            }

            if ((tiers & WebCodecs) != 0 && _options.UseWebCodecs)
            {
                return WebCodecs;
            }

            bool worker = (tiers & Worker) != 0;
            return (tiers & ImageBitmap) != 0 && (worker ? _options.UseImageBitmapInWorker : _options.UseImageBitmapOnPage) ? ImageBitmap : 0;
        }
    }

    /// <summary>
    /// Imports the decoder's script and probes what this context can decode with; the viewer awaits it before it opens a document.
    /// Until it completes, no image is offered to the browser.
    /// </summary>
    /// <returns>A task that completes when the probe is done.</returns>
    public static Task InitializeAsync() => _probe ??= ProbeAsync();

    /// <inheritdoc/>
    public bool CanDecode(in PlatformImageRequest request) =>
        Tier is int tier && tier != 0 && request.Filter.Value == "DCTDecode" && !request.ArithmeticCoding && request.BitsPerComponent == 8
        && (tier == WebCodecs || !request.ChromaSubsampled);

    /// <inheritdoc/>
    public async ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
    {
        int tier = Tier;
        if (tier == 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> data = encoded.Span;
        int id = ImageDecoderInterop.Begin(MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(data), data.Length), tier, request.Reduction, request.Width, request.Height, request.Components);
        string status;
        try
        {
            status = await ImageDecoderInterop.Wait(id).WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            ImageDecoderInterop.Release(id);
            throw;
        }

        string[] parts = status.Split(' ', 6);
        if (parts[0] != "ok")
        {
            throw new InvalidOperationException(FormattableString.Invariant($"{(tier == WebCodecs ? "ImageDecoder" : "createImageBitmap")}: {status[Math.Min(status.Length, 6)..]}"));
        }

        int width = int.Parse(parts[1], CultureInfo.InvariantCulture);
        int height = int.Parse(parts[2], CultureInfo.InvariantCulture);
        int reduction = -1;
        for (int r = 0; r <= request.Reduction; r++)
        {
            if (ImageSize(request.Width, r) == width && ImageSize(request.Height, r) == height)
            {
                reduction = r;
                break;
            }
        }


        if (reduction < 0)
        {
            ImageDecoderInterop.Release(id);
            throw new InvalidOperationException(FormattableString.Invariant($"the browser delivered {width} x {height} for a {request.Width} x {request.Height} image at reduction {request.Reduction} ({parts[4]})"));
        }

        DecodedImageBuilder builder = request.CreateImage(reduction);
        try
        {
            if (!ImageDecoderInterop.Read(id, builder.Samples))
            {
                return null;
            }

            return builder.Build();
        }
        finally
        {
            builder.Dispose();
        }
    }

    private static int ImageSize(int size, int reduction) => (int)(((long)size + (1L << reduction) - 1) >> reduction);

    private static async Task<int> ProbeAsync()
    {
        try
        {
            await JSHost.ImportAsync(ImageDecoderInterop.Module, "../_content/Broadside.Viewer.Blazor/broadside-image-decoder.js").ConfigureAwait(true);
            int tiers = await ImageDecoderInterop.Probe().ConfigureAwait(true);
            Volatile.Write(ref _tiers, tiers);
            return tiers;
        }
#pragma warning disable CA1031 // a browser without the script or the APIs decodes managed
        catch (Exception)
#pragma warning restore CA1031
        {
            Volatile.Write(ref _tiers, 0);
            return 0;
        }
    }
}

/// <summary>The browser image decoder's script, <c>broadside-image-decoder.js</c>.</summary>
[SupportedOSPlatform("browser")]
internal static partial class ImageDecoderInterop
{
    public const string Module = "broadside-image-decoder";

    [JSImport("probe", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    public static partial Task<int> Probe();

    [JSImport("begin", Module)]
    public static partial int Begin([JSMarshalAs<JSType.MemoryView>] Span<byte> data, int tier, int reduction, int width, int height, int components);

    [JSImport("wait", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string> Wait(int id);

    [JSImport("read", Module)]
    public static partial bool Read(int id, [JSMarshalAs<JSType.MemoryView>] Span<byte> destination);

    [JSImport("release", Module)]
    public static partial void Release(int id);
}
