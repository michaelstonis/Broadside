using Broadside.Annotations;
using Broadside.Content;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>
/// The in-place page operations of issue #647: resizing a page, scaling its content and adding margins. Each is one matrix applied to
/// the page's content (a wrapping <c>q … cm</c> and <c>Q</c>), its boxes and every annotation's geometry.
/// </summary>
/// <remarks>ISO 32000-2 §7.8.2 (content streams in an array), §8.3.4, §14.11.2, §12.5.2 and §12.5.3.</remarks>
internal static class PageReshaping
{
    /// <summary>Resizes the page to <paramref name="width"/> by <paramref name="height"/> as displayed, fitting the crop box in as <paramref name="fit"/> says.</summary>
    public static void Resize(PdfPage page, double width, double height, PdfContentFit fit)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "A page's width is a finite, positive number of points.");
        }

        if (!double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "A page's height is a finite, positive number of points.");
        }

        if (!Enum.IsDefined(fit))
        {
            throw new ArgumentOutOfRangeException(nameof(fit), fit, "Not a content fit.");
        }

        if (page.Rotation is 90 or 270)
        {
            (width, height) = (height, width);
        }

        PdfRectangle crop = page.CropBox;
        double sx = width / crop.Width;
        double sy = height / crop.Height;
        (sx, sy) = fit switch
        {
            PdfContentFit.Fit => (Math.Min(sx, sy), Math.Min(sx, sy)),
            PdfContentFit.Fill => (Math.Max(sx, sy), Math.Max(sx, sy)),
            PdfContentFit.Stretch => (sx, sy),
            _ => (1d, 1d),
        };
        var matrix = new Matrix(sx, 0, 0, sy, ((width - (sx * crop.Width)) / 2) - (sx * crop.Left), ((height - (sy * crop.Height)) / 2) - (sy * crop.Bottom));
        Apply(page, matrix, new PdfRectangle(0, 0, width, height));
    }

    /// <summary>Scales the page's content and annotations by <paramref name="factor"/> about the center of its crop box; the boxes stay.</summary>
    public static void ScaleContent(PdfPage page, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "A scale factor is a finite, positive number.");
        }

        PdfRectangle crop = page.CropBox;
        double cx = (crop.Left + crop.Right) / 2;
        double cy = (crop.Bottom + crop.Top) / 2;
        Apply(page, new Matrix(factor, 0, 0, factor, cx * (1 - factor), cy * (1 - factor)), newBox: null);
    }

    /// <summary>Grows the page's crop box by the margins, given as the page is displayed, and moves the content so the new box starts at the origin.</summary>
    public static void AddMargins(PdfPage page, double left, double bottom, double right, double top)
    {
        Margin(left, nameof(left));
        Margin(bottom, nameof(bottom));
        Margin(right, nameof(right));
        Margin(top, nameof(top));

        // The margins as displayed, turned back into default user space (Rotate turns the page clockwise, Table 31).
        (left, bottom, right, top) = page.Rotation switch
        {
            90 => (top, left, bottom, right),
            180 => (right, top, left, bottom),
            270 => (bottom, right, top, left),
            _ => (left, bottom, right, top),
        };

        PdfRectangle crop = page.CropBox;
        Apply(page, Matrix.CreateTranslation(left - crop.Left, bottom - crop.Bottom), new PdfRectangle(0, 0, crop.Width + left + right, crop.Height + bottom + top));
    }

    private static void Margin(double value, string parameter)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameter, value, "A margin is a finite number of points, 0 or more.");
        }
    }

    /// <summary>
    /// Applies <paramref name="matrix"/> to the content, the bleed, trim and art boxes the page has and every annotation; with
    /// <paramref name="newBox"/>, sets the media box and crop box to it. One change: undone as a whole when it fails part way.
    /// </summary>
    private static void Apply(PdfPage page, Matrix matrix, PdfRectangle? newBox)
    {
        PdfDocument document = page.Document;
        IReadOnlyList<PdfAnnotation> annotations = page.Annotations;
        AnnotationEditing.Atomically(() =>
        {
            if (!matrix.IsIdentity)
            {
                WrapContent(document, page.Dictionary, matrix);
            }

            foreach (CosName key in (CosName[])[KnownNames.BleedBox, KnownNames.TrimBox, KnownNames.ArtBox])
            {
                if (page.Dictionary.TryGetValue(key, out CosObject? value) && AnnotationValues.ReadRectangle(document, value) is { } box)
                {
                    page.Dictionary[key] = DrawingCache.Rectangle(AnnotationGeometry.Bounds(matrix, box));
                }
            }

            if (newBox is { } box2)
            {
                page.MediaBox = box2;
                page.CropBox = box2;
            }

            if (!matrix.IsIdentity)
            {
                foreach (PdfAnnotation annotation in annotations)
                {
                    AnnotationGeometry.Map(document, annotation.Dictionary, matrix, honorNoZoom: true);
                }
            }

            return true;
        });
    }

    /// <summary>Puts a stream with <c>q a b c d e f cm</c> before the page's content and one with <c>Q</c> after it (§7.8.2: the streams of the array are one stream).</summary>
    private static void WrapContent(PdfDocument document, CosDictionary dictionary, Matrix matrix)
    {
        CosObject? entry = dictionary.TryGetValue(KnownNames.Contents, out CosObject? value) ? value : null;
        CosObject? existing = document.Resolve(entry);
        if (existing is not (CosStream or CosArray { Count: > 0 }))
        {
            return;
        }

        var writer = new ContentWriter();
        writer.Write("q "u8);
        writer.Number(matrix.A).Number(matrix.B).Number(matrix.C).Number(matrix.D).Number(matrix.E).Number(matrix.F).Operator("cm");
        var parts = new CosArray { document.AddObject(new CosStream([], writer.ToArray())) };
        if (existing is CosArray array)
        {
            foreach (CosObject part in array)
            {
                parts.Add(part);
            }
        }
        else
        {
            parts.Add(entry!);
        }

        parts.Add(document.AddObject(new CosStream([], "\nQ\n"u8.ToArray())));
        dictionary[KnownNames.Contents] = parts;
    }
}
