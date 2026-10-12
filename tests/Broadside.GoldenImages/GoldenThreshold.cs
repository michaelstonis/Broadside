namespace Broadside.GoldenImages;

/// <summary>
/// How far a render may be from its golden image and still match: a per-pixel perceptual tolerance at edges, a tighter one in flat
/// areas, and a number of pixels allowed beyond them.
/// </summary>
/// <remarks>
/// <para>
/// A pixel's difference is the YIQ distance of Kotsarenko and Ramos ("Measuring perceived color difference using YIQ NTSC transmission
/// color space in mobile applications", 2010), scaled so that white against black is 1: two greys differ by their share of the way
/// from white to black, and a change of hue weighs less than the same change of brightness.
/// </para>
/// <para>
/// A pixel is on an edge when, in the golden image, one of its eight neighbours differs from it by more than
/// <see cref="FlatTolerance"/>; otherwise it is in a flat area. An edge pixel is tolerated up to <see cref="EdgeTolerance"/>
/// (renderers disagree there: anti-aliasing, sample positions, rounding), a flat pixel only up to <see cref="FlatTolerance"/> (a wrong
/// colour over an area is a bug, however slight it looks at one pixel). A render matches when at most
/// <see cref="MaxDifferingPixels"/> pixels differ beyond their tolerance.
/// </para>
/// <para>
/// The default (edges 0.3, flat areas 0.05, no differing pixel) lets two renderers' anti-aliasing disagree at every edge, as a 4 × 4
/// sample grid does against exact area coverage (at most a quarter), yet catches a glyph moved by one pixel at any sub-pixel phase:
/// moving an edge of a stem at least a pixel wide by a whole pixel changes a pixel on each edge row by at least half the ink's
/// contrast. Looser thresholds for single pages live in <see cref="GoldenThresholds"/>.
/// </para>
/// <para>Spec #33 story 13: output across backends matches within a perceptual threshold.</para>
/// </remarks>
public readonly record struct GoldenThreshold
{
    /// <summary>Initializes a new instance of the <see cref="GoldenThreshold"/> struct.</summary>
    /// <param name="edgeTolerance">The largest difference of an edge pixel that still counts as the same, from 0 (identical) to 1 (white against black).</param>
    /// <param name="flatTolerance">The largest difference of a pixel in a flat area that still counts as the same; also the contrast that makes a pixel an edge.</param>
    /// <param name="maxDifferingPixels">How many pixels may differ by more than their tolerance.</param>
    /// <exception cref="ArgumentOutOfRangeException">A tolerance is not in [0, 1], or the pixel count is negative.</exception>
    public GoldenThreshold(double edgeTolerance, double flatTolerance, int maxDifferingPixels)
    {
        EdgeTolerance = CheckTolerance(edgeTolerance, nameof(edgeTolerance));
        FlatTolerance = CheckTolerance(flatTolerance, nameof(flatTolerance));
        ArgumentOutOfRangeException.ThrowIfNegative(maxDifferingPixels);
        MaxDifferingPixels = maxDifferingPixels;
    }

    /// <summary>Gets the threshold every backend meets unless <see cref="GoldenThresholds"/> says otherwise: edges 0.3, flat areas 0.05, no differing pixel.</summary>
    public static GoldenThreshold Default { get; } = new(0.3, 0.05, 0);

    /// <summary>Gets the threshold that accepts only identical pixels.</summary>
    public static GoldenThreshold Exact { get; } = new(0, 0, 0);

    /// <summary>Gets the largest difference of an edge pixel that still counts as the same, from 0 to 1 (white against black).</summary>
    public double EdgeTolerance { get; init; }

    /// <summary>Gets the largest difference of a pixel in a flat area that still counts as the same, and the contrast with a neighbour that makes a pixel an edge.</summary>
    public double FlatTolerance { get; init; }

    /// <summary>Gets how many pixels may differ by more than their tolerance.</summary>
    public int MaxDifferingPixels { get; init; }

    /// <inheritdoc/>
    public override string ToString() =>
        FormattableString.Invariant($"edges {EdgeTolerance:0.###}, flat areas {FlatTolerance:0.###}, at most {MaxDifferingPixels} differing pixels");

    private static double CheckTolerance(double tolerance, string name) =>
        tolerance is >= 0 and <= 1 ? tolerance : throw new ArgumentOutOfRangeException(name, tolerance, "A tolerance must be between 0 and 1.");
}
