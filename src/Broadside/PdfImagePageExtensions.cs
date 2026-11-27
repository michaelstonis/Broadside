using System.Diagnostics.CodeAnalysis;
using Broadside.Graphics;
using Broadside.Images;

namespace Broadside;

/// <summary>Turning image files into pages (issue #642): scan-to-PDF in one call.</summary>
/// <remarks>ISO 32000-2 §7.7.3.3 (page objects) and §8.9.5 (image XObjects).</remarks>
[SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "C# 14 extension blocks, which the analyzer sees as nested types.")]
public static class PdfImagePageExtensions
{
    /// <summary>The resolution assumed when a file does not state one: one pixel to the point.</summary>
    private const double DefaultDpi = 72;

    /// <summary>The scan-to-PDF members on a document.</summary>
    extension(PdfDocument document)
    {
        /// <summary>Appends one page per image of an encoded file, each the size of its image at the file's resolution.</summary>
        /// <param name="encoded">A JPEG, PNG, TIFF, GIF or BMP file; every page of a TIFF file becomes a page.</param>
        /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
        /// <exception cref="ArgumentException">As for <see cref="PdfDocument.CreateImages"/>.</exception>
        /// <exception cref="NotSupportedException">As for <see cref="PdfDocument.CreateImages"/>.</exception>
        /// <remarks>
        /// <para>
        /// ISO 32000-2 §7.7.3.3 (page objects), §8.9.4 and §8.9.5. Each page's media box is the image's width and height in points:
        /// pixels x 72 / pixels per inch, from TIFF's <c>XResolution</c>, <c>YResolution</c> and <c>ResolutionUnit</c> or BMP's pixels
        /// per metre; 72 pixels per inch when the file states none (GIF, JPEG and PNG are always placed so). The image fills the page.
        /// </para>
        /// <para>
        /// A TIFF <c>Orientation</c> other than 1 is applied by the image's transformation matrix on the page (§8.3.4), not by
        /// resampling: values 5 to 8 swap the page's width and height. A mutation.
        /// </para>
        /// </remarks>
        public void AppendImagePages(ReadOnlySpan<byte> encoded)
        {
            ArgumentNullException.ThrowIfNull(document);
            foreach ((PdfImage image, ImportedPlacement placement) in document.CreatePlacedImages(encoded))
            {
                double dpiX = placement.DpiX > 0 ? placement.DpiX : DefaultDpi;
                double dpiY = placement.DpiY > 0 ? placement.DpiY : DefaultDpi;
                double width = image.Width * 72 / dpiX;
                double height = image.Height * 72 / dpiY;
                int orientation = placement.Orientation is >= 1 and <= 8 ? placement.Orientation : 1;
                bool swapped = orientation >= 5;
                double pageWidth = swapped ? height : width;
                double pageHeight = swapped ? width : height;
                PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, pageWidth, pageHeight));
                using PdfCanvas canvas = page.CreateCanvas();
                canvas.Transform(Placement(orientation, pageWidth, pageHeight));
                canvas.DrawImage(image);
            }
        }
    }

    /// <summary>
    /// The matrix that maps the image's unit square onto a <paramref name="width"/> x <paramref name="height"/> page so that the image
    /// shows as TIFF 6.0 §8 <c>Orientation</c> says its rows and columns are meant to be seen.
    /// </summary>
    internal static Matrix Placement(int orientation, double width, double height) => orientation switch
    {
        2 => new Matrix(-width, 0, 0, height, width, 0),
        3 => new Matrix(-width, 0, 0, -height, width, height),
        4 => new Matrix(width, 0, 0, -height, 0, height),
        5 => new Matrix(0, -height, -width, 0, width, height),
        6 => new Matrix(0, -height, width, 0, 0, height),
        7 => new Matrix(0, height, width, 0, 0, 0),
        8 => new Matrix(0, height, -width, 0, width, 0),
        _ => new Matrix(width, 0, 0, height, 0, 0),
    };
}
