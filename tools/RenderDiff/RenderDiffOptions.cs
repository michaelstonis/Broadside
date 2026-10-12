using Broadside.GoldenImages;

namespace Broadside.Tools.RenderDiff;

/// <summary>What a RenderDiff run compares and where it writes the report.</summary>
public sealed record RenderDiffOptions
{
    /// <summary>Gets the PDF files and directories (searched recursively for <c>*.pdf</c>) to render.</summary>
    public required IReadOnlyList<string> Inputs { get; init; }

    /// <summary>Gets the directory the report, the reference renders and the report's images are written to.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>Gets the reference renderers to compare with; the unavailable ones are skipped with a message.</summary>
    public IReadOnlyList<ReferenceEngine> Engines { get; init; } = [];

    /// <summary>Gets the resolution every engine renders at, in dots per inch.</summary>
    public double Dpi { get; init; } = 72;

    /// <summary>Gets how many pages of each file are rendered, from the first.</summary>
    public int MaxPagesPerFile { get; init; } = 10;

    /// <summary>
    /// Gets the largest offset, in pixels along each axis, tried when aligning a reference render with Broadside's. The offset with
    /// the fewest differing pixels wins, the smallest on a tie; 0 compares the images as they are.
    /// </summary>
    public int MaxShift { get; init; } = 1;

    /// <summary>Gets the golden-image threshold a pixel's difference is judged by.</summary>
    public GoldenThreshold Threshold { get; init; } = GoldenThreshold.Default;

    /// <summary>Gets how many of the worst pages the HTML report shows with side-by-side and diff images.</summary>
    public int ReportedPages { get; init; } = 50;

    /// <summary>Gets how many of the worst pages <c>triage.md</c> lists for a verdict.</summary>
    public int TriagePages { get; init; } = 20;

    /// <summary>Gets how long one file may take to render, per engine (Broadside counts as one).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>Gets how many files are processed at once.</summary>
    public int Jobs { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>Gets a value indicating whether reference renders already in the output directory are reused instead of rendered again.</summary>
    public bool ReuseReferences { get; init; }
}
