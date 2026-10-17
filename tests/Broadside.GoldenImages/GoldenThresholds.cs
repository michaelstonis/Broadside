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
        new(
            "android",
            "paths-strokes.pdf",
            0,
            new GoldenThreshold(GoldenThreshold.Default.EdgeTolerance, GoldenThreshold.Default.FlatTolerance, 8),
            "Android's stroker (Skia) leaves an anti-aliasing residue one pixel wide at the inner corner of each sharp black chevron, " +
            "lopsided between the two rows the apex spans (0.08 to 0.2 on pixels the rasterizer leaves black), and the one-pixel pens " +
            "of the zero-width circle and diagonal overlap differently on one pixel where they cross (issue #133: 3 pixels on the " +
            "Android 15 emulator). Every other pixel of the page is within the default."),
        new(
            "skia",
            "paths-strokes.pdf",
            0,
            new GoldenThreshold(0.3, 0.05, 2),
            "Skia's analytic anti-aliasing leaves the one pixel at the inner corner of each 30 degree chevron of band B (8 wide, mitered) " +
            "at about 92 % instead of fully covered (0.08 against the 0.05 flat tolerance), where its stroker's inner join folds the " +
            "outline back through the vertex; the two pixels are inside the stroke and everything else matches within the defaults (#121)."),
        new(
            "coregraphics",
            "shading-type3-radial.pdf",
            0,
            new GoldenThreshold(GoldenThreshold.Default.EdgeTolerance, GoldenThreshold.Default.FlatTolerance, 2),
            "CoreGraphics' radial shading (#126) does not anti-alias the rim of a circle it does not extend past, and on one pixel of " +
            "the rim, whose centre lies within a fraction of a pixel of the circle, it decides inside where the rasterizer decides " +
            "outside (0.89 on that pixel); every other pixel is within the default."),
        new(
            "direct2d",
            "shading-radial-cases.pdf",
            0,
            new GoldenThreshold(GoldenThreshold.Default.EdgeTolerance, GoldenThreshold.Default.FlatTolerance, 64),
            "Direct2D clips the ends of a radial it does not extend (#130) with an aliased layer mask, which it rasterizes on its own " +
            "fixed sub-pixel grid: the rim pixels of each 70-pixel circle whose centres lie about 0.01 pixel inside it (lattice points " +
            "nearly on the circle, 18 per circle) fall outside, where the rasterizer's per-pixel evaluation keeps them (56 pixels over " +
            "four cells); every other pixel, the rest of the rims included, is within the default."),
        new(
            "coregraphics-text",
            null,
            null,
            new GoldenThreshold(0.6, 0.05, 0),
            "Text pages with glyphs drawn by CoreGraphics' own font engine (TrueType and OpenType programs, #127): its glyph rasterizer " +
            "leaves single edge pixels of small text up to about half a level from the rasterizer's exact area coverage (0.52 at most " +
            "on the corpus, 12 pt Liberation Sans in text-lines-rotated and text-tagged-replacements) with the same total ink; with " +
            "outlines forced the same pages match within the default."),
        new(
            "coregraphics-text",
            "text-opentype-cff-embedded.pdf",
            0,
            new GoldenThreshold(0.6, 0.05, 4),
            "Glyph I of the corpus OpenType CFF font (generate.py minimal_cff) has a serif contour that runs in the opposite direction " +
            "to its stem's and overlaps it. The non-zero rule (ISO 32000-2 §9.2.4, §8.5.3.3.2) correctly leaves that overlap empty " +
            "(windings +1 and -1 cancel), a hole two pixel rows high in the golden image; CoreGraphics' CFF scaler fills it. Four pixels " +
            "of the hole differ by up to 1; everything else is within the coregraphics-text row."),
        new(
            "direct2d-outlines",
            "text-lines-rotated.pdf",
            0,
            new GoldenThreshold(0.05, 0.05, 0),
            "Forced outlines on Direct2D (#131) against the same outlines written as path objects, otherwise held to GoldenThreshold.Tight: " +
            "the glyphs arrive pre-mapped to user space while the path page reaches them through its cm, so Direct2D flattens the " +
            "curves of the rotated lines under different transforms; four edge pixels differ by up to 0.047 on WARP, the same ink."),
        new(
            "webgl",
            null,
            null,
            new GoldenThreshold(0.6, GoldenThreshold.Default.FlatTolerance, 0),
            "The WebGL backend (#353) anti-aliases paths, glyph outlines, strokes and clips by 4x multisampling (MAX_SAMPLES on ANGLE " +
            "Metal and SwiftShader), so an edge pixel's coverage is one of five levels where the rasterizer computes the exact area: " +
            "single edge pixels of small text and thin strokes differ by up to 0.55 (text-cff-embedded, paths-strokes) with the same " +
            "ink; flat areas match within the default."),
        new(
            "webgl",
            "image-stencil-mask.pdf",
            0,
            new GoldenThreshold(0.6, GoldenThreshold.Default.FlatTolerance, 100),
            "The magnified 8x8 stencil mask is sampled nearest-neighbour by the GPU, whose texel boundaries fall exactly on pixel " +
            "centres here: with float texture coordinates the GPU picks the next texel on one row and one column of each step of " +
            "the shape (100 pixels on the shape's edges, #353); the shape and every other pixel match."),
        new(
            "webgl",
            "text-type3.pdf",
            0,
            new GoldenThreshold(0.6, GoldenThreshold.Default.FlatTolerance, 36),
            "Glyph c of both Type 3 fonts is an 8x1 image mask magnified by the glyph matrix; as for image-stencil-mask, a texel " +
            "boundary on a pixel centre goes to the next texel on the GPU, moving the edge of one bar by a pixel (18 pixels a glyph, " +
            "#353)."),
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
