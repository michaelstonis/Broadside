using System.Runtime.CompilerServices;

namespace Broadside.Rendering;

/// <summary>What a display list keeps in memory (issue #103), which the document's display-list cache charges against its budget.</summary>
public sealed partial class DisplayList
{
    private long _retainedBytes = -1;

    /// <summary>
    /// Gets the bytes the list keeps: its tables (operations, paths, colours, transforms, strokes, glyphs and the rest) at their
    /// element sizes, and the samples of the images and stencil masks it paints. Computed from sizes, so the same on every run.
    /// </summary>
    internal long RetainedBytes
    {
        get
        {
            long bytes = Volatile.Read(ref _retainedBytes);
            if (bytes < 0)
            {
                bytes = Measure();
                Volatile.Write(ref _retainedBytes, bytes);
            }

            return bytes;
        }
    }

    private static long SizeOf<T>(T[] table) => (long)table.Length * Unsafe.SizeOf<T>();

    private long Measure()
    {
        long bytes = SizeOf(_operations) + SizeOf(_verbs) + SizeOf(_points) + SizeOf(_paths) + SizeOf(_colors) + SizeOf(_components)
            + SizeOf(_transforms) + SizeOf(Strokes) + SizeOf(Dashes) + SizeOf(Images) + SizeOf(ImagePaints) + SizeOf(GlyphRuns)
            + SizeOf(Glyphs) + SizeOf(ShadingPaints) + SizeOf(ImageMasks) + SizeOf(Compositings) + SizeOf(DeviceParameters) + SizeOf(Groups) + SizeOf(Annotations)
            + SizeOf(TilingPaints) + SizeOf(TextGlyphs) + SizeOf(Text)

            // The region bounds (issue #96) are charged up front: a kept list is what tiles replay, so it gains them on the first one.
            + ((long)_operations.Length * Unsafe.SizeOf<OperationExtent>());

        // A tiling pattern's cell (issue #112) is a list of its own, shared by every paint of the same pattern: charged once.
        if (TilingPaints.Length > 0)
        {
            var cells = new HashSet<DisplayList>(ReferenceEqualityComparer.Instance);
            foreach (DisplayTilingPaint paint in TilingPaints)
            {
                if (cells.Add(paint.Cell))
                {
                    bytes += paint.Cell.RetainedBytes;
                }
            }
        }

        foreach (DisplayImage image in Images)
        {
            bytes += image.RetainedBytes;
        }

        foreach (DisplayImageMask mask in ImageMasks)
        {
            bytes += mask.RetainedBytes;
        }

        return bytes;
    }
}
