namespace Broadside.Tools.RenderDiff;

/// <summary>How one reference renderer's image of a page compares with Broadside's.</summary>
public sealed record EngineDiff
{
    /// <summary>Gets the engine's name.</summary>
    public required string Engine { get; init; }

    /// <summary>Gets why there is no comparison (the engine failed or drew no image), or null when there is one.</summary>
    public string? Error { get; init; }

    /// <summary>Gets the reference image's width in pixels (0 without an image).</summary>
    public int ReferenceWidth { get; init; }

    /// <summary>Gets the reference image's height in pixels (0 without an image).</summary>
    public int ReferenceHeight { get; init; }

    /// <summary>Gets how far right the reference image was moved to line up with Broadside's.</summary>
    public int OffsetX { get; init; }

    /// <summary>Gets how far down the reference image was moved to line up with Broadside's.</summary>
    public int OffsetY { get; init; }

    /// <summary>Gets the pixels that differ beyond the golden-image tolerance once aligned.</summary>
    public int DifferingPixels { get; init; }

    /// <summary>Gets the pixels that differ within the tolerance.</summary>
    public int TolerantPixels { get; init; }

    /// <summary>Gets the largest per-pixel difference, from 0 to 1 (white against black).</summary>
    public double MaxDifference { get; init; }

    /// <summary>Gets <see cref="DifferingPixels"/> as a share of the compared area (the larger of the two images).</summary>
    public double DifferingFraction { get; init; }

    /// <summary>Gets a value indicating whether there is a comparison.</summary>
    public bool Compared => Error is null;

    /// <summary>Gets the reference image file, when there is one.</summary>
    internal string? ImagePath { get; init; }
}

/// <summary>One page of one file: Broadside's render compared with every reference renderer.</summary>
public sealed record PageDiff
{
    /// <summary>Gets the file's path relative to its input, with forward slashes.</summary>
    public required string File { get; init; }

    /// <summary>Gets the zero-based page.</summary>
    public required int Page { get; init; }

    /// <summary>Gets the PDF file the page came from.</summary>
    internal string SourcePath { get; init; } = string.Empty;

    /// <summary>Gets why Broadside drew no image (an exception, a timeout), or null.</summary>
    public string? BroadsideError { get; init; }

    /// <summary>Gets Broadside's image width in pixels (0 without an image).</summary>
    public int Width { get; init; }

    /// <summary>Gets Broadside's image height in pixels (0 without an image).</summary>
    public int Height { get; init; }

    /// <summary>Gets the comparison with each engine, in the order the engines were given.</summary>
    public IReadOnlyList<EngineDiff> Engines { get; init; } = [];

    /// <summary>
    /// Gets the share of pixels by which the first two references that drew the page differ from each other, or null with fewer than
    /// two. A page the references disagree on is a candidate for a reference-engine bug rather than a Broadside one.
    /// </summary>
    public double? ReferenceSpread { get; init; }

    /// <summary>
    /// Gets the page's rank score: the smallest <see cref="EngineDiff.DifferingFraction"/> over the engines that drew it, so a page
    /// ranks high only when Broadside differs from every reference. Null when nothing was compared.
    /// </summary>
    public double? Score => Engines.Where(static e => e.Compared).Select(static e => (double?)e.DifferingFraction).Min();
}

/// <summary>The outcome of a run: compared pages worst first, the pages that could not be compared, and the engines skipped.</summary>
public sealed record RenderDiffReport
{
    /// <summary>Gets the pages Broadside and at least one reference drew, worst first.</summary>
    public required IReadOnlyList<PageDiff> Pages { get; init; }

    /// <summary>Gets the pages Broadside, or every reference, failed to draw, and files that did not open.</summary>
    public required IReadOnlyList<PageDiff> Failures { get; init; }

    /// <summary>Gets the engines that took part.</summary>
    public required IReadOnlyList<string> Engines { get; init; }

    /// <summary>Gets one message per engine that was skipped, saying why and how to install it.</summary>
    public required IReadOnlyList<string> SkippedEngines { get; init; }

    /// <summary>Gets the resolution, in dots per inch.</summary>
    public required double Dpi { get; init; }

    /// <summary>Gets the threshold, as text.</summary>
    public required string Threshold { get; init; }
}
