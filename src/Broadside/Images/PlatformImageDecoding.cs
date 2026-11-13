using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Images;

/// <summary>
/// An engine's platform image decoders, in order, with the verification switch and the decodes in flight: the part of the platform
/// decoder seam that runs a decode, checks what comes back and falls back to the managed codec.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.4.8 and §8.9.3. Concurrent prefetches of one image at one reduction share one decode. A failure, an exception or
/// samples other than asked for leave the image to the managed codec and record <c>ImageDecodedManaged</c> (Information) for it,
/// never silently; that image is then not offered again.
/// </remarks>
internal sealed class PlatformImageDecoding
{
    private readonly IPlatformImageDecoder[] _decoders;
    private readonly Dictionary<(object Image, int Reduction), Task> _inFlight = [];
    private readonly Lock _gate = new();
    private readonly ConditionalWeakTable<EncodedImage, CosReference> _references = [];

    private PlatformImageDecoding(IPlatformImageDecoder[] decoders, bool verify)
    {
        _decoders = decoders;
        Verify = verify;
    }

    /// <summary>Gets a value indicating whether every platform decode is checked against the managed codec.</summary>
    public bool Verify { get; }

    /// <summary>Returns the decoding of <paramref name="options"/>, or <see langword="null"/> when no platform decoder is to be used.</summary>
    public static PlatformImageDecoding? Create(PdfOptions options)
    {
        if (!options.PlatformImageDecoding || !options.HasPlatformImageDecoders)
        {
            return null;
        }

        IPlatformImageDecoder[] decoders = [.. options.PlatformImageDecoders];
        return decoders.Length == 0 ? null : new PlatformImageDecoding(decoders, options.VerifyPlatformImageDecoder);
    }

    /// <summary>Remembers the reference of an image's stream, which makes each image's diagnostics its own.</summary>
    public void Identify(EncodedImage image, CosReference reference) => _references.AddOrUpdate(image, reference);

    /// <summary>Returns whether a decoder named <paramref name="name"/> is in use.</summary>
    public bool Uses(string name)
    {
        foreach (IPlatformImageDecoder decoder in _decoders)
        {
            if (string.Equals(decoder.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the first decoder that takes <paramref name="request"/>, or <see langword="null"/>.</summary>
    public IPlatformImageDecoder? Choose(in PlatformImageRequest request)
    {
        foreach (IPlatformImageDecoder decoder in _decoders)
        {
            if (request.ArithmeticCoding && (decoder.Features & PlatformImageFeatures.ArithmeticCoding) == 0)
            {
                continue;
            }

            if (decoder.CanDecode(request))
            {
                return decoder;
            }
        }

        return null;
    }

    /// <summary>Returns the decode of (<paramref name="image"/>, <paramref name="reduction"/>) in flight, or starts one with <paramref name="start"/>.</summary>
    /// <remarks>
    /// The decode is registered before <paramref name="start"/> runs and outside the lock, so it is shared whether the platform
    /// decoder completes later or decodes on the calling thread and returns a completed task (issue #484).
    /// </remarks>
    public Task Join(object image, int reduction, Func<Task> start)
    {
        TaskCompletionSource placeholder;
        lock (_gate)
        {
            if (_inFlight.TryGetValue((image, reduction), out Task? running))
            {
                return running;
            }

            placeholder = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight[(image, reduction)] = placeholder.Task;
        }

        _ = Run(image, reduction, start, placeholder);
        return placeholder.Task;
    }

    // Never throws: the outcome of the decode, failed or cancelled included, goes to the placeholder every requester awaits.
    private async Task Run(object image, int reduction, Func<Task> start, TaskCompletionSource placeholder)
    {
        Task decode;
        try
        {
            decode = start();
        }
#pragma warning disable CA1031 // handed to every requester through the placeholder
        catch (Exception exception)
#pragma warning restore CA1031
        {
            decode = Task.FromException(exception);
        }

        await decode.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        lock (_gate)
        {
            _inFlight.Remove((image, reduction));
        }

        placeholder.SetFromTask(decode);
    }

    /// <summary>
    /// Decodes <paramref name="encoded"/> with <paramref name="decoder"/>: the data cleaned as its features ask, the result checked,
    /// verified against the managed codec when asked; <see langword="null"/> when it failed (recorded on the document).
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled; nothing is kept.</exception>
    public async Task<DecodedImage?> DecodeAsync(EncodedImage encoded, IPlatformImageDecoder decoder, PlatformImageRequest request, CancellationToken cancellationToken)
    {
        PlatformImageFeatures features = decoder.Features;
        bool exif = request.HasExif && (features & PlatformImageFeatures.AppliesExifOrientation) != 0;
        bool icc = request.HasIccProfile && (features & PlatformImageFeatures.ConvertsEmbeddedProfiles) != 0;
        ReadOnlyMemory<byte> data = encoded.Data;
        byte[]? rented = null;
        if (exif || icc)
        {
            rented = ArrayPool<byte>.Shared.Rent(data.Length);
            data.Span.CopyTo(rented);
            PlatformImageEligibility.Sanitize(rented.AsSpan(0, data.Length), exif, icc);
            data = rented.AsMemory(0, data.Length);
        }

        DecodedImage? decoded = null;
        string? failure = null;
        try
        {
            decoded = await decoder.DecodeAsync(data, request, cancellationToken).ConfigureAwait(false);
            if (decoded is null)
            {
                failure = "it could not decode the data";
            }
            else if (Mismatch(decoded, request) is { } wrong)
            {
                failure = wrong;
                decoded.Dispose();
                decoded = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a platform decoder's failure falls back to the managed codec, with a diagnostic
        catch (Exception exception)
#pragma warning restore CA1031
        {
            failure = exception.Message;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        if (decoded is null)
        {
            Report(encoded, ImageDiagnostics.ImageDecodedManaged, DiagnosticSeverity.Information, string.Create(
                CultureInfo.InvariantCulture,
                $"The platform image decoder '{decoder.Name}' did not decode the {request.Width} x {request.Height} {request.Filter.Value} image ({failure}); it is decoded with the managed codec."));
            return null;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            decoded.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (Verify)
        {
            Compare(encoded, decoder, request, decoded, cancellationToken);
        }

        return decoded;
    }

    /// <summary>Records a diagnostic of <paramref name="image"/>, under its stream's reference when known.</summary>
    private void Report(EncodedImage image, DiagnosticCode code, DiagnosticSeverity severity, string message) =>
        image.Report(code, severity, message, _references.TryGetValue(image, out CosReference? reference) ? reference : null);

    /// <summary>Returns why <paramref name="decoded"/> is not what <paramref name="request"/> asked for, or <see langword="null"/>.</summary>
    private static string? Mismatch(DecodedImage decoded, in PlatformImageRequest request)
    {
        int r = decoded.Reduction;
        if (r <= request.Reduction
            && decoded.Geometry.Width == ImageGeometry.Reduce(request.Width, r)
            && decoded.Geometry.Height == ImageGeometry.Reduce(request.Height, r)
            && decoded.Geometry.Components == request.Components
            && decoded.Geometry.BitsPerComponent == 8
            && decoded.StorageBits == 8
            && !decoded.SamplesInverted
            && decoded.Alpha is null)
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"it delivered {decoded.Geometry.Width} x {decoded.Geometry.Height} x {decoded.Geometry.Components} samples of {decoded.Geometry.BitsPerComponent} bits at reduction {r} for a request of reduction {request.Reduction}");
    }

    /// <summary>Decodes the image with the managed codec at the platform's reduction and records how far apart the two are.</summary>
    private void Compare(EncodedImage encoded, IPlatformImageDecoder decoder, in PlatformImageRequest request, DecodedImage platform, CancellationToken cancellationToken)
    {
        using DecodedImage? managed = encoded.Decode(platform.Reduction, cancellationToken);
        if (managed is null || managed.Geometry.Width != platform.Geometry.Width || managed.Geometry.Height != platform.Geometry.Height || managed.Geometry.Components != platform.Geometry.Components)
        {
            Report(encoded, ImageDiagnostics.PlatformImageMismatch, DiagnosticSeverity.Warning, string.Create(
                CultureInfo.InvariantCulture,
                $"The platform image decoder '{decoder.Name}' decoded the {request.Width} x {request.Height} image at reduction {platform.Reduction}, and the managed codec could not decode it the same way to compare."));
            return;
        }

        SampleDifference difference = SampleDifference.Measure(platform, managed);
        bool within = decoder.Fidelity == PlatformImageFidelity.Exact
            ? difference.Max == 0
            : platform.Reduction == 0
                ? difference.Max <= 24 && difference.Psnr >= 50 && difference.OverTwoLevels <= 0.01
                : difference.Psnr >= 55;
        Report(
            encoded,
            within ? ImageDiagnostics.PlatformImageVerified : ImageDiagnostics.PlatformImageMismatch,
            within ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The platform image decoder '{decoder.Name}' ({decoder.Fidelity}) decoded the {request.Width} x {request.Height} image at reduction {platform.Reduction}: max {difference.Max}, PSNR {(double.IsPositiveInfinity(difference.Psnr) ? "inf" : difference.Psnr.ToString("0.0", CultureInfo.InvariantCulture))} dB, {difference.OverTwoLevels * 100:0.###}% of pixels over 2 levels against the managed codec."));
    }
}
