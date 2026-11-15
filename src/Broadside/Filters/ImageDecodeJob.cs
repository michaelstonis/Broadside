using Broadside.Images;

namespace Broadside.Filters;

/// <summary>
/// An image decode run in slices on the caller's thread (issue #365): <see cref="Continue"/> decodes until the image is done or its
/// deadline has passed at one of the codec's resume points, and the next call resumes there. The samples equal one whole decode's.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4 and §8.9.3. A single-threaded viewer (the browser) cannot block its thread for the whole decode of a large
/// photograph; a codec that implements <see cref="ISlicedImageFilter"/> pauses between units of work (a DCT MCU row, a JPEG 2000
/// code-block). Any other codec decodes in one step.
/// </para>
/// <para>
/// Not thread-safe: one thread at a time continues a job. A job that throws (cancellation included) is abandoned: dispose it; nothing
/// it decoded is kept.
/// </para>
/// </remarks>
internal abstract class ImageDecodeJob : IDisposable
{
    /// <summary>Gets the samples once <see cref="Continue"/> returned <see langword="true"/>; <see langword="null"/> when nothing could be decoded.</summary>
    /// <remarks>The caller owns them from then on (the job no longer disposes them).</remarks>
    public DecodedImage? Result { get; protected set; }

    /// <summary>Decodes until the image is done or, after at least one unit of work, <see cref="System.Diagnostics.Stopwatch"/> reaches <paramref name="deadline"/>.</summary>
    /// <param name="deadline">The timestamp at which to pause; <see cref="long.MaxValue"/> decodes to the end.</param>
    /// <param name="cancellationToken">Stops this slice.</param>
    /// <returns><see langword="true"/> when the decode is done (or failed); <see langword="false"/> when it paused.</returns>
    public abstract bool Continue(long deadline, CancellationToken cancellationToken);

    /// <summary>Releases what an unfinished decode holds.</summary>
    public abstract void Dispose();

    /// <summary>A codec without resume points: the whole decode in one step.</summary>
    internal sealed class OneStep(Func<DecodedImage?> decode) : ImageDecodeJob
    {
        private bool _done;

        public override bool Continue(long deadline, CancellationToken cancellationToken)
        {
            if (!_done)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Result = decode();
                _done = true;
            }

            return true;
        }

        public override void Dispose()
        {
        }
    }
}

/// <summary>An image codec that decodes in slices (issue #365): implemented by the managed DCT and JPEG 2000 codecs.</summary>
/// <remarks>ISO 32000-2 §7.4.8 and §7.4.9. Internal: an optional capability of the built-in codecs, not a new extension point.</remarks>
internal interface ISlicedImageFilter
{
    /// <summary>Starts a decode of <paramref name="encoded"/> that <see cref="ImageDecodeJob.Continue"/> runs in slices.</summary>
    /// <param name="encoded">The data, as for <see cref="IImageFilter.DecodeImage"/>; it must not change until the job is done.</param>
    /// <param name="context">The image's values and limits, as for <see cref="IImageFilter.DecodeImage"/>.</param>
    /// <returns>The job; nothing is decoded until it is continued.</returns>
    ImageDecodeJob BeginDecodeImage(ReadOnlyMemory<byte> encoded, ImageFilterContext context);
}
