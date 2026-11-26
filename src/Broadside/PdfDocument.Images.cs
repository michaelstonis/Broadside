using Broadside.Images;
using Broadside.Objects;

namespace Broadside;

/// <summary>Image XObjects (issue #60).</summary>
public sealed partial class PdfDocument
{
    /// <summary>Returns the image view over an image XObject of this document.</summary>
    /// <param name="image">An image XObject stream, or a reference to one, such as a value of a resource dictionary's <c>XObject</c> subdictionary.</param>
    /// <returns>
    /// The view; <see langword="null"/> when <paramref name="image"/> is not a stream, or is a stream whose <c>Subtype</c> names another
    /// kind of XObject. A stream without <c>Subtype</c> is read as an image (a thumbnail image has none, §12.3.4).
    /// </returns>
    /// <remarks>ISO 32000-2 §8.9.5, Table 87.</remarks>
    public PdfImage? GetImage(CosObject? image) => PdfImage.Create(this, image);

    /// <summary>
    /// Plans the save-time image pass (issue #625) without changing anything: for each image XObject the document paints, its largest
    /// placement, effective resolution, and whether a save with <paramref name="options"/> would resample or re-encode it.
    /// </summary>
    /// <param name="options">The pass's options, as for <see cref="PdfSaveOptions.ImageOptimization"/>.</param>
    /// <returns>
    /// One result per image XObject painted, in the order the walk first finds them (pages in order, each page's content, then its
    /// annotations' appearances), then one per inline image painted (<see cref="PdfImageOptimizationReason.InlineImage"/>, which the
    /// pass does not change). Images painted nowhere are not listed: the pass leaves them alone.
    /// </returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.9.4 (the unit square in user space), §8.9.5, §8.9.5.3 (reduction), §8.9.6, §7.4.4 and §7.4.8. The walk includes
    /// content hidden by optional content, image masks, artifacts, forms, tiling pattern cells and every appearance state of every
    /// annotation. Nothing is decoded or encoded, so whether the new encoding would be smaller (<see cref="PdfImageOptimizationReason.WouldGrow"/>)
    /// is known only once a save runs the pass. A read: safe alongside other reads.
    /// </remarks>
    public IReadOnlyList<PdfImageOptimizationResult> PlanImageOptimization(PdfImageOptimization options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ImageOptimizer.Plan(this, options);
    }

    /// <summary>
    /// Gets or sets the save-time image pass, set by the first <see cref="PdfImageOptimization"/> created: a save reaches the pass
    /// only through it, so a trimmed application that never asks for image optimization does not carry it.
    /// </summary>
    internal static Func<PdfDocument, PdfImageOptimization, System.IO.Compression.CompressionLevel, bool>? ImagePass { get; set; }

    /// <summary>Runs the save-time image pass when the options ask for it (issue #625).</summary>
    /// <returns>Whether an image changed.</returns>
    private bool OptimizeImagesForSave(PdfSaveOptions options) =>
        options.ImageOptimization is { } optimization && ImagePass is { } pass && pass(this, optimization, options.CompressionLevel);

    /// <summary>Refuses image optimization in an incremental update: the images of earlier, possibly signed, revisions stay as written (§7.5.6).</summary>
    private static void CheckImageOptimization(PdfSaveOptions options)
    {
        if (options.ImageOptimization is not null && options.IncrementalUpdate)
        {
            throw new InvalidOperationException(
                "Image optimization rewrites image streams and runs only on a full save; an incremental update keeps earlier revisions, which may be signed, as written (ISO 32000-2 §7.5.6). Save without IncrementalUpdate, or without ImageOptimization.");
        }
    }
}
