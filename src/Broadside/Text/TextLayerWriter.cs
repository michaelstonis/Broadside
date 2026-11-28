using Broadside.Graphics;
using Broadside.Structure;

namespace Broadside.Text;

/// <summary>Writes an OCR text layer onto a page (issue #649): invisible text over each word's box, one text object per line.</summary>
/// <remarks>ISO 32000-2 §9.3.6, §9.3.4, §9.4.2, §14.8.2.4 and §14.7.5; see <see cref="PdfPage.AddTextLayer(OcrPage, TextLayerOptions?)"/>.</remarks>
internal static class TextLayerWriter
{
    /// <summary>The share of the box height below the baseline: the glyphless font's descent over its ascent minus descent.</summary>
    private const double DescentShare = 0.2;

    /// <summary>The narrowest a space between two words that touch or overlap is written, as a share of the font size.</summary>
    private const double MinimumSpace = 0.05;

    public static void Write(PdfDocument document, PdfPage page, OcrPage ocr, TextLayerOptions options)
    {
        List<(OcrLine Line, List<Placed> Words)> lines = Place(page, ocr, options);
        if (lines.Count == 0)
        {
            return;
        }

        GlyphlessFont font = GlyphlessFont.For(document);
        PdfStructureElement? parent = options.Tag ? StructureParent(document) : null;
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.SaveState();
        foreach ((OcrLine line, List<Placed> words) in lines)
        {
            bool tagged = parent is not null;
            if (parent is not null)
            {
                PdfStructureElement paragraph = parent.AddElement("P");
                if (!string.IsNullOrWhiteSpace(line.Language))
                {
                    paragraph.Language = line.Language;
                }

                canvas.BeginTaggedContent(paragraph);
            }

            canvas.BeginText();
            canvas.SetTextRenderingMode(TextRenderingMode.Invisible);
            for (int index = 0; index < words.Count; index++)
            {
                Placed word = words[index];
                if (canvas.Font is null || canvas.FontSize != word.Size)
                {
                    canvas.SetFont(font, word.Size);
                }

                canvas.SetTextMatrix(word.Matrix);
                canvas.SetHorizontalScaling(Scaling(word.Length, font.MeasureWidth(word.Text, word.Size)));
                canvas.ShowText(word.Text);
                if (index + 1 < words.Count)
                {
                    // A real space, so extraction splits the words: stretched over the gap to the next word along the line.
                    double gap = Math.Max(words[index + 1].Start - word.End, MinimumSpace * word.Size);
                    canvas.SetHorizontalScaling(Scaling(gap, font.MeasureWidth(" ", word.Size)));
                    canvas.ShowText(" ");
                }
            }

            canvas.EndText();
            if (tagged)
            {
                canvas.EndMarkedContent();
            }
        }

        canvas.RestoreState();
    }

    /// <summary>The horizontal scaling, in percent, that makes text of natural width <paramref name="natural"/> span <paramref name="length"/>.</summary>
    private static double Scaling(double length, double natural) => Math.Round(100 * length / natural, 3);

    /// <summary>The element new lines are added under: the tree's one <c>Document</c> element, else the root; a new tree gets a <c>Document</c>.</summary>
    private static PdfStructureElement StructureParent(PdfDocument document)
    {
        if (document.StructureTree is not { } tree)
        {
            return document.CreateStructureTree().AddElement("Document");
        }

        foreach (PdfStructureElement child in tree.Children)
        {
            if (child.StructureType?.Value == "Document")
            {
                return child;
            }
        }

        return tree.AddElement("Document");
    }

    /// <summary>
    /// Every line with at least one word to write, each word placed in default user space: the OCR box mapped onto the target as the
    /// page is displayed, then into user space through the page's rotation.
    /// </summary>
    private static List<(OcrLine Line, List<Placed> Words)> Place(PdfPage page, OcrPage ocr, TextLayerOptions options)
    {
        PdfRectangle target = options.Target ?? page.CropBox;
        int rotation = page.Rotation;
        bool turned = rotation is 90 or 270;
        double width = turned ? target.Height : target.Width;
        double height = turned ? target.Width : target.Height;
        double sx = width / ocr.Width;
        double sy = height / ocr.Height;

        // Displayed space (origin at the displayed page's bottom-left, y up) to default user space (§7.7.3.3, Table 31: Rotate turns the page clockwise for display).
        Matrix toUser = rotation switch
        {
            90 => new Matrix(0, 1, -1, 0, target.Left + target.Width, target.Bottom),
            180 => new Matrix(-1, 0, 0, -1, target.Right, target.Top),
            270 => new Matrix(0, -1, 1, 0, target.Left, target.Top),
            _ => new Matrix(1, 0, 0, 1, target.Left, target.Bottom),
        };

        var lines = new List<(OcrLine, List<Placed>)>();
        foreach (OcrLine? line in ocr.Lines)
        {
            if (line?.Words is null)
            {
                continue;
            }

            var words = new List<Placed>();
            foreach (OcrWord? word in line.Words)
            {
                if (word is null || string.IsNullOrWhiteSpace(word.Text) || word.Confidence < options.MinConfidence)
                {
                    continue;
                }

                PdfRectangle box = word.Bounds;
                double left = box.Left * sx;
                double right = box.Right * sx;
                double top = height - (box.Bottom * sy);
                double bottom = height - (box.Top * sy);
                double angle = line.BaselineAngle + word.Angle;
                if (!(right - left > 0) || !(top - bottom > 0) || !double.IsFinite(right - left) || !double.IsFinite(top - bottom) || !double.IsFinite(angle))
                {
                    continue;
                }

                words.Add(Placed.Create(word.Text.Trim(), left, bottom, right - left, top - bottom, angle, toUser));
            }

            if (words.Count > 0)
            {
                lines.Add((line, words));
            }
        }

        return lines;
    }

    /// <summary>A word placed on the page.</summary>
    /// <param name="Text">The text shown.</param>
    /// <param name="Size">The font size: the box's extent across the line, in points.</param>
    /// <param name="Length">The box's extent along the line, in points.</param>
    /// <param name="Matrix">The text matrix: the baseline start, rotated with the line, in default user space.</param>
    /// <param name="Start">Where the word starts along the line's direction (displayed space), to measure gaps between words.</param>
    private readonly record struct Placed(string Text, double Size, double Length, Matrix Matrix, double Start)
    {
        /// <summary>Gets where the word ends along the line's direction.</summary>
        public double End => Start + Length;

        /// <summary>
        /// Places a word whose axis-aligned box in displayed space is (<paramref name="left"/>, <paramref name="bottom"/>,
        /// <paramref name="width"/>, <paramref name="height"/>) and which runs at <paramref name="degrees"/> counterclockwise.
        /// </summary>
        public static Placed Create(string text, double left, double bottom, double width, double height, double degrees, Matrix toUser)
        {
            double radians = degrees * Math.PI / 180;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            if (Math.Abs(cos) < 1e-12)
            {
                cos = 0;
            }

            if (Math.Abs(sin) < 1e-12)
            {
                sin = 0;
            }

            // The rotated word's length along the line and thickness across it, from the box around it:
            // width = L|cos| + H|sin|, height = L|sin| + H|cos|. Near 45 degrees the system is singular; the longer side is the length.
            double c = Math.Abs(cos);
            double s = Math.Abs(sin);
            double determinant = (c * c) - (s * s);
            double length;
            double thickness;
            if (Math.Abs(determinant) > 0.2
                && ((width * c) - (height * s)) / determinant is > 0 and var solvedLength
                && ((height * c) - (width * s)) / determinant is > 0 and var solvedThickness)
            {
                (length, thickness) = (solvedLength, solvedThickness);
            }
            else
            {
                (length, thickness) = c >= s ? (width, height) : (height, width);
            }

            // The baseline start: from the box centre, back half the length along the line and down to the baseline across it.
            double centerX = left + (width / 2);
            double centerY = bottom + (height / 2);
            double across = -(thickness / 2) + (DescentShare * thickness);
            double originX = centerX - (cos * length / 2) - (sin * across);
            double originY = centerY - (sin * length / 2) + (cos * across);
            var displayed = new Matrix(cos, sin, -sin, cos, originX, originY);
            double start = (originX * cos) + (originY * sin);
            return new Placed(text, Math.Round(thickness, 4), length, displayed * toUser, start);
        }
    }
}
