using Broadside.Graphics;

namespace Broadside;

/// <content>Resizing a page, scaling its content and adding margins, in place (issue #647).</content>
/// <remarks>
/// ISO 32000-2 §7.8.2, §8.3.4, §14.11.2, §12.5.2 and §12.5.3. Each operation is one matrix <c>[a 0 0 d e f]</c>: the page's content is
/// wrapped in a new first content stream <c>q a 0 0 d e f cm</c> and a new last one <c>Q</c>; the bleed, trim and art boxes the page
/// has are mapped through it; and every annotation's geometry is mapped through it (<c>Rect</c>, <c>QuadPoints</c>, <c>Vertices</c>,
/// <c>Path</c>, <c>InkList</c>, <c>L</c>, the callout line <c>CL</c>, and <c>RD</c> scaled), so each annotation still sits over the
/// content it marks; an annotation with the <c>NoZoom</c> flag, or a text annotation (§12.5.6.4), keeps its size and moves with its
/// upper-left corner. Sizes and margins are given as the page is displayed, with its <c>Rotate</c> applied. A mutation: no read may run
/// concurrently; a failure part way changes nothing.
/// </remarks>
public sealed partial class PdfPage
{
    /// <summary>Resizes the page to <paramref name="width"/> by <paramref name="height"/> points, fitting its content (its crop box) in as <paramref name="fit"/> says, centered.</summary>
    /// <param name="width">The new width as displayed, in points.</param>
    /// <param name="height">The new height as displayed, in points.</param>
    /// <param name="fit">How the content is scaled: to fit, to fill, stretched, or not at all (a smaller size crops).</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is not finite and positive, or <paramref name="fit"/> is not defined.</exception>
    /// <remarks>
    /// ISO 32000-2 §14.11.2 and §8.3.4. The media box and crop box become <c>[0 0 width height]</c> (swapped into default user space for
    /// a page rotated 90° or 270°); content outside the old crop box is not shown. See the class remarks for the content and annotations.
    /// </remarks>
    public void Resize(double width, double height, PdfContentFit fit = PdfContentFit.Fit) => PageReshaping.Resize(this, width, height, fit);

    /// <summary>Scales the page's content and annotations by <paramref name="factor"/> about the center of its crop box; the page keeps its size.</summary>
    /// <param name="factor">The scale; below 1 shrinks the content, above 1 enlarges it (what leaves the crop box is not shown).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="factor"/> is not finite and positive.</exception>
    /// <remarks>ISO 32000-2 §8.3.4. The page's boxes do not change. See the class remarks for the content and annotations.</remarks>
    public void ScaleContent(double factor) => PageReshaping.ScaleContent(this, factor);

    /// <summary>Adds blank margins around the page's content: the crop box grows by them and the content moves, unscaled.</summary>
    /// <param name="left">The margin added at the left, as displayed, in points.</param>
    /// <param name="bottom">The margin added at the bottom.</param>
    /// <param name="right">The margin added at the right.</param>
    /// <param name="top">The margin added at the top.</param>
    /// <exception cref="ArgumentOutOfRangeException">A margin is negative or not finite.</exception>
    /// <remarks>
    /// ISO 32000-2 §14.11.2. The media box and crop box become the old crop box grown by the margins, moved so its lower-left corner is
    /// the origin; the content, the other boxes and the annotations move with it. See the class remarks.
    /// </remarks>
    public void AddMargins(double left, double bottom, double right, double top) => PageReshaping.AddMargins(this, left, bottom, right, top);
}
