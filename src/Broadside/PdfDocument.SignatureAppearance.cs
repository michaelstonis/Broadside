using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Signatures;

namespace Broadside;

/// <content>Visible signature appearances (issue #200): ISO 32000-2 §12.7.5.5 and §12.5.5.</content>
public sealed partial class PdfDocument
{
    /// <summary>The margin between the widget's rectangle and what the appearance draws, and between the image and the text.</summary>
    private const double AppearancePadding = 2;

    /// <summary>Checks a visible signature's page and rectangle before anything changes.</summary>
    private void CheckAppearance(PdfSignatureAppearance appearance)
    {
        if (appearance.PageIndex >= Pages.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(appearance), appearance.PageIndex, $"The signature's page index is beyond the document's {Pages.Count} pages.");
        }

        PdfRectangle rect = appearance.Rectangle;
        if (!double.IsFinite(rect.Left) || !double.IsFinite(rect.Bottom) || !double.IsFinite(rect.Right) || !double.IsFinite(rect.Top)
            || !(rect.Width > 0) || !(rect.Height > 0))
        {
            throw new ArgumentException(
                "A visible signature's rectangle has finite coordinates, a width and a height (ISO 32000-2 §12.7.5.5).", nameof(appearance));
        }
    }

    /// <summary>
    /// Draws the signature's appearance into a form XObject the size of the widget's rectangle and makes it the widget's normal
    /// appearance (<c>AP</c> <c>N</c>, §12.5.5). Every change is recorded in <paramref name="changes"/>, so a failed signature leaves
    /// the document as it was (#306).
    /// </summary>
    private void DrawSignatureAppearance(
        CosDictionary field, PdfSignatureAppearance appearance, PdfSignatureOptions options, X509Certificate2 certificate, DateTimeOffset time, CosChangeLog changes)
    {
        CosDictionary widget = SignatureWidget(field, options.FieldName);
        using (changes.Record())
        {
            PdfRectangle rect = WidgetRectangle(widget);
            if (!(rect.Width > 0) || !(rect.Height > 0))
            {
                throw new InvalidOperationException(
                    $"The signature field '{options.FieldName}' is invisible (its widget's rectangle is empty), so it has no room for an appearance (ISO 32000-2 §12.7.5.5).");
            }

            string text = appearance.Text ?? DefaultAppearanceText(options, certificate, time);
            string[] lines = text.Length == 0 ? [] : text.ReplaceLineEndings("\n").Split('\n');
            PdfFormXObject form = CreateFormXObject(new PdfRectangle(0, 0, rect.Width, rect.Height));
            using (PdfCanvas canvas = form.CreateCanvas())
            {
                LayOutSignatureAppearance(canvas, appearance, lines, rect.Width, rect.Height);
            }

            widget[AnnotationNames.AP] = new CosDictionary { [AnnotationNames.N] = form.Reference! };
            widget.Remove(AnnotationNames.AS);
        }
    }

    /// <summary>The widget of a signature field: the field itself when merged, else its first kid (§12.7.4.1, §12.5.6.19).</summary>
    private CosDictionary SignatureWidget(CosDictionary field, string? fieldName)
    {
        if (!field.ContainsKey(AnnotationNames.Rect)
            && Resolve(field.TryGetValue(FormNames.Kids, out CosObject? kids) ? kids : null) is CosArray array
            && array.Count > 0
            && Resolve(array[0]) is CosDictionary kid)
        {
            return kid;
        }

        return field.ContainsKey(AnnotationNames.Rect)
            ? field
            : throw new InvalidOperationException($"The signature field '{fieldName}' has no widget to show an appearance in.");
    }

    private PdfRectangle WidgetRectangle(CosDictionary widget)
    {
        if (Resolve(widget.TryGetValue(AnnotationNames.Rect, out CosObject? value) ? value : null) is CosArray { Count: 4 } array)
        {
            double[] numbers = new double[4];
            for (int index = 0; index < 4; index++)
            {
                if (Resolve(array[index]) is not CosNumber number)
                {
                    return default;
                }

                numbers[index] = number.ToDouble();
            }

            return new PdfRectangle(numbers[0], numbers[1], numbers[2], numbers[3]);
        }

        return default;
    }

    /// <summary>The default lines: who signed, when, and the reason and location when given (Table 255).</summary>
    private static string DefaultAppearanceText(PdfSignatureOptions options, X509Certificate2 certificate, DateTimeOffset time)
    {
        string signer = options.SignerName ?? certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        var lines = new List<string>
        {
            $"Digitally signed by {signer}",
            $"Date: {time.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}",
        };
        if (options.Reason is { Length: > 0 } reason)
        {
            lines.Add($"Reason: {reason}");
        }

        if (options.Location is { Length: > 0 } location)
        {
            lines.Add($"Location: {location}");
        }

        return string.Join('\n', lines);
    }

    /// <summary>Splits the appearance between the image and the text (see <see cref="PdfSignatureAppearance"/>) and draws both.</summary>
    private void LayOutSignatureAppearance(PdfCanvas canvas, PdfSignatureAppearance appearance, string[] lines, double width, double height)
    {
        double padding = Math.Min(AppearancePadding, Math.Min(width, height) / 4);
        var inner = new PdfRectangle(padding, padding, width - padding, height - padding);
        PdfRectangle textArea = inner;
        if (appearance.Image is { } image)
        {
            PdfRectangle imageArea = inner;
            if (lines.Length > 0)
            {
                if (inner.Width >= inner.Height)
                {
                    double split = inner.Left + (inner.Width * 0.4);
                    imageArea = new PdfRectangle(inner.Left, inner.Bottom, split, inner.Top);
                    textArea = new PdfRectangle(Math.Min(split + padding, inner.Right), inner.Bottom, inner.Right, inner.Top);
                }
                else
                {
                    double split = inner.Bottom + (inner.Height * 0.5);
                    imageArea = new PdfRectangle(inner.Left, split, inner.Right, inner.Top);
                    textArea = new PdfRectangle(inner.Left, inner.Bottom, inner.Right, Math.Max(split - padding, inner.Bottom));
                }
            }

            double scale = Math.Min(imageArea.Width / image.Width, imageArea.Height / image.Height);
            double imageWidth = image.Width * scale, imageHeight = image.Height * scale;
            canvas.DrawImage(
                image,
                imageArea.Left + ((imageArea.Width - imageWidth) / 2),
                imageArea.Bottom + ((imageArea.Height - imageHeight) / 2),
                imageWidth,
                imageHeight);
        }

        if (lines.Length > 0 && textArea.Width > 0 && textArea.Height > 0)
        {
            DrawSignatureText(canvas, appearance.Font ?? GetStandard14Font(Standard14Font.Helvetica), appearance.FontSize, lines, textArea);
        }
    }

    /// <summary>
    /// Shows <paramref name="lines"/> left-aligned and centred vertically in <paramref name="area"/>, at <paramref name="fontSize"/> or
    /// the largest size at which each line's width and the lines' height (ascent to descent, 1.15 em apart) fit, clipped to the area.
    /// </summary>
    private static void DrawSignatureText(PdfCanvas canvas, PdfCanvasFont font, double? fontSize, string[] lines, PdfRectangle area)
    {
        double ascent = font.Ascent / 1000, descent = Math.Min(0, font.Descent / 1000);
        if (!(ascent - descent > 0))
        {
            (ascent, descent) = (0.8, -0.2);
        }

        double leading = Math.Max(1.15, ascent - descent);
        double blockHeight = ((lines.Length - 1) * leading) + (ascent - descent);
        double size = fontSize ?? area.Height / blockHeight;
        if (fontSize is null)
        {
            foreach (string line in lines)
            {
                double unit = font.MeasureWidth(line, 1);
                if (unit > 0)
                {
                    size = Math.Min(size, area.Width / unit);
                }
            }
        }

        canvas.SaveState();
        canvas.Rectangle(area.Left, area.Bottom, area.Width, area.Height);
        canvas.Clip();
        canvas.EndPath();
        canvas.SetFillColor(PdfColor.FromGray(0));
        canvas.BeginText();
        canvas.SetFont(font, size);
        double baseline = area.Top - ((area.Height - (blockHeight * size)) / 2) - (ascent * size);
        double previousX = 0, previousY = 0;
        foreach (string line in lines)
        {
            canvas.MoveText(area.Left - previousX, baseline - previousY);
            canvas.ShowText(line);
            (previousX, previousY) = (area.Left, baseline);
            baseline -= leading * size;
        }

        canvas.EndText();
        canvas.RestoreState();
    }
}
