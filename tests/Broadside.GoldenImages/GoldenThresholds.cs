namespace Broadside.GoldenImages;

/// <summary>One row of <see cref="GoldenThresholds.Rules"/>.</summary>
/// <param name="Backend">The backend the row applies to (<see cref="GoldenThresholds.SoftwareRasterizer"/>, <c>skia</c>, <c>coregraphics</c>, ...).</param>
/// <param name="FileName">The corpus file, or <see langword="null"/> for every file.</param>
/// <param name="Page">The zero-based page, or <see langword="null"/> for every page of the file.</param>
/// <param name="Threshold">The threshold.</param>
/// <param name="Reason">Why this page needs it: what differs and why that is acceptable. Required.</param>
public sealed record GoldenThresholdRule(string Backend, string? FileName, int? Page, GoldenThreshold Threshold, string Reason);

/// <summary>
/// The one reviewed table of golden-image thresholds. A page compares under the most specific row that matches it (backend, file and
/// page; then backend and file; then backend), else under <see cref="GoldenThreshold.Default"/>.
/// </summary>
/// <remarks>
/// Loosening a threshold is a code change like any other: add or edit a row here, with its reason, and it shows in review.
/// <c>GoldenThresholdTableTests</c> checks that every row names a corpus file and page that exist, gives a reason and is unique.
/// </remarks>
public static class GoldenThresholds
{
    /// <summary>The backend name of the software rasterizer, whose renders are the golden images.</summary>
    public const string SoftwareRasterizer = "software";

    /// <summary>Gets the rows, in review order.</summary>
    public static IReadOnlyList<GoldenThresholdRule> Rules { get; } =
    [
        new(
            SoftwareRasterizer,
            null,
            null,
            new GoldenThreshold(0.01, 0.01, 0),
            "The rasterizer is compared with its own reviewed renders, so anything visible is a regression; 0.01 (under 3 levels of " +
            "grey) absorbs last-bit differences of the platform maths library between operating systems and nothing more."),
    ];

    /// <summary>Returns the threshold a backend's render of one corpus page must meet.</summary>
    /// <param name="backend">The backend.</param>
    /// <param name="fileName">The corpus file.</param>
    /// <param name="page">The zero-based page.</param>
    /// <returns>The threshold of the most specific matching row, else <see cref="GoldenThreshold.Default"/>.</returns>
    public static GoldenThreshold For(string backend, string fileName, int page)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(fileName);
        GoldenThresholdRule? best = null;
        int bestRank = -1;
        foreach (GoldenThresholdRule rule in Rules)
        {
            if (!string.Equals(rule.Backend, backend, StringComparison.Ordinal)
                || (rule.FileName is not null && !string.Equals(rule.FileName, fileName, StringComparison.Ordinal))
                || (rule.Page is int rulePage && rulePage != page))
            {
                continue;
            }

            int rank = (rule.FileName is null ? 0 : 1) + (rule.Page is null ? 0 : 1);
            if (rank > bestRank)
            {
                best = rule;
                bestRank = rank;
            }
        }

        return best?.Threshold ?? GoldenThreshold.Default;
    }
}
