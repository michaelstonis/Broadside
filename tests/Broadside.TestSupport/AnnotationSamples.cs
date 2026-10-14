using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>Synthetic annotation-heavy pages for allocation tests and benchmarks of the annotation layers (ISO 32000-2 §12.5).</summary>
public static class AnnotationSamples
{
    /// <summary>
    /// A 612 x 792 page with a light content backdrop and a grid of <paramref name="columns"/> × <paramref name="rows"/> Square
    /// annotations sharing one appearance (a filled square under a stroked circle): every second one half transparent (<c>CA 0.5</c>,
    /// drawn through a layer), every third <c>NoZoom</c>, every fifth <c>NoRotate</c>, and every seventh a widget (form-field layer).
    /// </summary>
    /// <param name="columns">Annotations per row.</param>
    /// <param name="rows">Rows of annotations.</param>
    /// <returns>The file.</returns>
    public static byte[] Grid(int columns = 8, int rows = 10)
    {
        const string appearance =
            "1 0.5 0 rg 0 0 40 40 re f 0 0 1 RG 2 w 20 4 m 29 4 36 11 36 20 c 36 29 29 36 20 36 c 11 36 4 29 4 20 c 4 11 11 4 20 4 c S";
        double width = 612.0 / columns;
        double height = 792.0 / rows;
        var annotations = new StringBuilder();
        var objects = new List<string>();
        int first = 6;
        for (int index = 0; index < columns * rows; index++)
        {
            double x = (index % columns * width) + 10;
            double y = (index / columns * height) + 10;
            int flags = 4 | (index % 3 == 0 ? 8 : 0) | (index % 5 == 0 ? 16 : 0);
            string subtype = index % 7 == 0 ? "Widget" : "Square";
            string opacity = index % 2 == 0 ? " /CA 0.5" : string.Empty;
            objects.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /Annot /Subtype /{subtype} /Rect [{x} {y} {x + 40} {y + 40}] /F {flags}{opacity} /AP << /N 5 0 R >> >>"));
            annotations.Append(CultureInfo.InvariantCulture, $"{first + index} 0 R ");
        }

        return ContentSamples.File(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 4 0 R /Annots [{annotations}] >>",
            ContentSamples.Stream("0.9 g 0 0 612 792 re f 0 0 0.5 rg 0 0 306 396 re f"),
            ContentSamples.Stream(appearance, "/Type /XObject /Subtype /Form /BBox [0 0 40 40] /Resources << >>"),
            .. objects,
        ]);
    }
}
