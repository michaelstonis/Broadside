using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Layout.Flow;

/// <summary>
/// Draws what a <see cref="PageTemplate"/> adds around the body, each as an artifact (ISO 32000-2 §14.8.2.2.2, Table 363): the
/// background and watermark when a page starts, under the body; headers and footers after the last page, when the page count
/// their fields need is known, in a second content stream of each page.
/// </summary>
internal static class PageDecorations
{
    private static readonly CosName TypeKey = new("Type");
    private static readonly CosName SubtypeKey = new("Subtype");
    private static readonly CosName AttachedKey = new("Attached");
    private static readonly CosName BBoxKey = new("BBox");

    /// <summary>Paints the template's background colour, background image and watermark on a page's fresh canvas.</summary>
    public static void DrawBackground(PdfCanvas canvas, PageTemplate template, ITextShaper shaper)
    {
        PageSetup setup = template.Setup;
        if (template.BackgroundColor is not null || template.BackgroundImage is not null)
        {
            var properties = new CosDictionary
            {
                [TypeKey] = new CosName("Background"),
                [BBoxKey] = Box(0, 0, setup.Width, setup.Height),
                [AttachedKey] = new CosArray { new CosName("Left"), new CosName("Bottom"), new CosName("Right"), new CosName("Top") },
            };
            canvas.BeginMarkedContent("Artifact", properties);
            if (template.BackgroundColor is { } color)
            {
                canvas.SaveState();
                canvas.SetFillColor(color);
                canvas.Rectangle(0, 0, setup.Width, setup.Height);
                canvas.Fill();
                canvas.RestoreState();
            }

            if (template.BackgroundImage is { } image)
            {
                canvas.DrawImage(image, 0, 0, setup.Width, setup.Height);
            }

            canvas.EndMarkedContent();
        }

        if (template.Watermark is { } watermark)
        {
            canvas.BeginMarkedContent("Artifact", Pagination("Watermark"));
            canvas.SaveState();
            if (watermark.Opacity < 1)
            {
                canvas.SetFillAlpha(watermark.Opacity);
            }

            double radians = watermark.Angle * Math.PI / 180;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            canvas.Transform(new Matrix(cos, sin, -sin, cos, setup.Width / 2, setup.Height / 2));
            TextStyle style = watermark.Style;
            double width = shaper.Measure(watermark.Text, style);
            ShowLine(canvas, shaper, watermark.Text, style, -width / 2, -(style.Ascent + style.Descent) / 2);
            canvas.RestoreState();
            canvas.EndMarkedContent();
        }
    }

    /// <summary>Draws the header and footer of every page, with the page number and count fields filled in.</summary>
    public static void DrawHeadersAndFooters(List<(PdfPage Page, PageTemplate Template, bool First)> pages, ITextShaper shaper)
    {
        for (int index = 0; index < pages.Count; index++)
        {
            (PdfPage page, PageTemplate template, bool first) = pages[index];
            HeaderFooter? header = template.HeaderFor(index + 1, first);
            HeaderFooter? footer = template.FooterFor(index + 1, first);
            bool hasHeader = header is { IsEmpty: false };
            bool hasFooter = footer is { IsEmpty: false };
            if (!hasHeader && !hasFooter)
            {
                continue;
            }

            using PdfCanvas canvas = page.CreateCanvas();
            if (hasHeader)
            {
                DrawRegion(canvas, shaper, template.Setup, header!, isHeader: true, index + 1, pages.Count);
            }

            if (hasFooter)
            {
                DrawRegion(canvas, shaper, template.Setup, footer!, isHeader: false, index + 1, pages.Count);
            }
        }
    }

    private static void DrawRegion(PdfCanvas canvas, ITextShaper shaper, PageSetup setup, HeaderFooter region, bool isHeader, int pageNumber, int pageCount)
    {
        TextStyle style = region.Style;
        double left = setup.Margins.Left;
        double right = setup.Width - setup.Margins.Right;
        double outer = isHeader ? setup.Height - region.Offset : region.Offset;
        double bottom = isHeader ? outer - region.Height : outer;
        double baseline = isHeader ? outer - style.Ascent : outer - style.Descent;
        CosDictionary properties = Pagination(isHeader ? "Header" : "Footer");
        properties[AttachedKey] = new CosArray { new CosName(isHeader ? "Top" : "Bottom") };
        properties[BBoxKey] = Box(left, bottom, right, bottom + region.Height);
        canvas.BeginMarkedContent("Artifact", properties);
        foreach ((string? slot, double alignment) in (ReadOnlySpan<(string?, double)>)[(region.Left, 0), (region.Center, 0.5), (region.Right, 1)])
        {
            if (string.IsNullOrEmpty(slot))
            {
                continue;
            }

            string text = HeaderFooter.Expand(slot, pageNumber, pageCount);
            double width = shaper.Measure(text, style);
            ShowLine(canvas, shaper, text, style, left + (((right - left) - width) * alignment), baseline);
        }

        canvas.EndMarkedContent();
    }

    private static void ShowLine(PdfCanvas canvas, ITextShaper shaper, string text, TextStyle style, double x, double baseline)
    {
        ShapedRun shaped = shaper.Shape(text, style);
        canvas.BeginText();
        canvas.SetFont(style.Font, style.FontSize);
        if (style.Color is { } color)
        {
            canvas.SetFillColor(color);
        }

        canvas.SetTextMatrix(new Matrix(1, 0, 0, 1, x, baseline));
        canvas.ShowText(shaped.Glyphs, shaped.Adjustments);
        canvas.EndText();
        if (style.Color is not null)
        {
            canvas.SetFillColor(PdfColor.FromGray(0));
        }
    }

    private static CosDictionary Pagination(string subtype) => new()
    {
        [TypeKey] = new CosName("Pagination"),
        [SubtypeKey] = new CosName(subtype),
    };

    private static CosArray Box(double left, double bottom, double right, double top) =>
        [new CosReal(Round(left)), new CosReal(Round(bottom)), new CosReal(Round(right)), new CosReal(Round(top))];

    private static double Round(double value) => Math.Round(value, 4);
}
