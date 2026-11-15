namespace Broadside.Viewer;

/// <summary>Options for <see cref="PageThumbnails"/>; set before use, then treated as fixed.</summary>
/// <remarks>Spec #33 viewer capability 10.</remarks>
public sealed class ThumbnailOptions
{
    /// <summary>The default <see cref="Size"/>: 160 pixels.</summary>
    public const int DefaultSize = 160;

    /// <summary>The default <see cref="MaxDeferral"/>: a quarter of a second.</summary>
    public static readonly TimeSpan DefaultMaxDeferral = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets the length in pixels of a thumbnail's longer side (<see cref="DefaultSize"/> by default).</summary>
    public int Size { get; private set; } = DefaultSize;

    /// <summary>
    /// Gets how long a thumbnail waits for the session's other work to finish before it is drawn anyway (<see cref="DefaultMaxDeferral"/> by
    /// default): the most a busy viewer holds thumbnails back, per thumbnail.
    /// </summary>
    public TimeSpan MaxDeferral { get; private set; } = DefaultMaxDeferral;

    /// <summary>Sets <see cref="Size"/>.</summary>
    /// <param name="pixels">The longer side in pixels, from 8 to 2,048.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pixels"/> is outside the range.</exception>
    public ThumbnailOptions WithSize(int pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixels, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pixels, 2048);
        Size = pixels;
        return this;
    }

    /// <summary>Sets <see cref="MaxDeferral"/>.</summary>
    /// <param name="deferral">The longest wait; <see cref="TimeSpan.Zero"/> draws thumbnails without waiting.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deferral"/> is negative.</exception>
    public ThumbnailOptions WithMaxDeferral(TimeSpan deferral)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(deferral, TimeSpan.Zero);
        MaxDeferral = deferral;
        return this;
    }
}
