using System.Globalization;
using Broadside.Fonts;
using Broadside.Structure;

namespace Broadside.Graphics;

/// <summary>
/// Draws <see cref="PdfStamp"/>s and <see cref="PdfHeaderFooter"/>s on existing pages (issue #627): everything that can fail is
/// checked and laid out for every page first, so a failure changes nothing; then each page gets one canvas, foreground or background.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.2.2 (Table 363), §8.11.3, §12.4.2, §14.11.2, §8.10.1.</remarks>
internal static class StampWriter
{
    private const string PageNumberToken = "{page}";
    private const string PageCountToken = "{pages}";
    private const string PageLabelToken = "{label}";
    private static readonly PdfVersion Pdf20 = new(2, 0);

    public static void Stamp(PdfDocument document, PdfStamp stamp, Range? pages)
    {
        (int first, int count) = Pages(document, pages);
        if (count == 0)
        {
            return;
        }

        CheckOwner(document, stamp.OptionalContentGroup?.Document, nameof(stamp), "optional content group");
        TextBlock[]? texts = null;
        switch (stamp.Kind)
        {
            case PdfStampKind.Text:
                CheckOwner(document, stamp.Font!.Document, nameof(stamp), "font");
                CheckColor(document, stamp.TextColor, nameof(stamp));
                texts = new TextBlock[count];
                for (int index = 0; index < count; index++)
                {
                    string text = Expand(document, stamp.Text!, first + index, stamp.FirstPageNumber + index, count);
                    texts[index] = TextBlock.Lay(stamp.Font!, stamp.FontSize, text, stamp.HorizontalAlignment, nameof(stamp));
                }

                break;
            case PdfStampKind.Image:
                CheckOwner(document, stamp.Image!.Document, nameof(stamp), "image");
                if (stamp.Image.IsInline || stamp.Image.Reference is null)
                {
                    throw new ArgumentException("An image stamp shall be an image XObject, an indirect stream (ISO 32000-2 §8.9.5).", nameof(stamp));
                }

                break;
            case PdfStampKind.Form:
                CheckOwner(document, stamp.Form!.Document, nameof(stamp), "form");
                if (stamp.Form.Reference is null)
                {
                    throw new ArgumentException("A form stamp shall be an indirect form XObject (ISO 32000-2 §8.10.1).", nameof(stamp));
                }

                break;
        }

        // A page stamp's form is made before any page changes: from a page of this document it is the page as it was.
        PdfFormXObject? form = stamp.Kind switch
        {
            PdfStampKind.Form => stamp.Form,
            PdfStampKind.Page => document.CreateFormXObject(stamp.SourcePage!),
            _ => null,
        };

        for (int index = 0; index < count; index++)
        {
            Draw(document, document.Pages[first + index], stamp, texts?[index], form);
        }
    }

    /// <summary>
    /// Draws the text stamp <paramref name="stamp"/> on <paramref name="texts"/>.Length pages from <paramref name="first"/>, page
    /// <c>first + i</c> showing <paramref name="texts"/>[i] as given (no tokens), attached to <paramref name="edges"/>. Every text is
    /// laid out before any page changes, so a text the font cannot show changes nothing (issue #646).
    /// </summary>
    public static void StampTexts(PdfDocument document, PdfStamp stamp, int first, string[] texts, PdfArtifactEdges edges, string parameter)
    {
        CheckOwner(document, stamp.Font!.Document, parameter, "font");
        CheckColor(document, stamp.TextColor, parameter);
        var blocks = new TextBlock[texts.Length];
        for (int index = 0; index < texts.Length; index++)
        {
            blocks[index] = TextBlock.Lay(stamp.Font!, stamp.FontSize, texts[index], stamp.HorizontalAlignment, parameter);
        }

        for (int index = 0; index < texts.Length; index++)
        {
            Draw(document, document.Pages[first + index], stamp, blocks[index], form: null, edges);
        }
    }

    public static void AddHeaderFooter(PdfDocument document, PdfHeaderFooter headerFooter, Range? pages)
    {
        (int first, int count) = Pages(document, pages);
        if (count == 0)
        {
            return;
        }

        CheckOwner(document, headerFooter.Font.Document, nameof(headerFooter), "font");
        (string? Text, bool Header, PdfStampAlignment Alignment)[] slots =
        [
            (headerFooter.HeaderLeft, true, PdfStampAlignment.Start),
            (headerFooter.HeaderCenter, true, PdfStampAlignment.Center),
            (headerFooter.HeaderRight, true, PdfStampAlignment.End),
            (headerFooter.FooterLeft, false, PdfStampAlignment.Start),
            (headerFooter.FooterCenter, false, PdfStampAlignment.Center),
            (headerFooter.FooterRight, false, PdfStampAlignment.End),
        ];
        var blocks = new TextBlock?[count][];
        for (int index = 0; index < count; index++)
        {
            blocks[index] = new TextBlock?[slots.Length];
            for (int slot = 0; slot < slots.Length; slot++)
            {
                if (!string.IsNullOrEmpty(slots[slot].Text))
                {
                    string text = Expand(document, slots[slot].Text!, first + index, headerFooter.FirstPageNumber + index, count);
                    blocks[index][slot] = TextBlock.Lay(headerFooter.Font, headerFooter.FontSize, text, slots[slot].Alignment, nameof(headerFooter));
                }
            }
        }

        bool pageNum = document.Version >= Pdf20;
        for (int index = 0; index < count; index++)
        {
            PdfPage page = document.Pages[first + index];
            Frame frame = Frame.Of(page, PdfPageBoundary.CropBox, followRotation: true);
            using PdfCanvas canvas = page.CreateCanvas();
            for (int slot = 0; slot < slots.Length; slot++)
            {
                if (blocks[index][slot] is not { } block)
                {
                    continue;
                }

                (string? text, bool header, PdfStampAlignment alignment) = slots[slot];
                double margin = headerFooter.Margin;
                double x = alignment switch
                {
                    PdfStampAlignment.Start => margin,
                    PdfStampAlignment.End => frame.Width - margin - block.Width,
                    _ => (frame.Width - block.Width) / 2,
                };
                double y = header ? frame.Height - margin - block.Height : margin;
                Matrix placement = Matrix.CreateTranslation(x, y) * frame.ToUserSpace;
                bool number = text!.Contains(PageNumberToken, StringComparison.Ordinal) || text.Contains(PageLabelToken, StringComparison.Ordinal);
                string subtype = number && pageNum ? "PageNum" : header ? "Header" : "Footer";
                canvas.BeginArtifact(PdfArtifactType.Pagination, subtype, header ? PdfArtifactEdges.Top : PdfArtifactEdges.Bottom, Bounds(placement, block.Width, block.Height));
                canvas.SaveState();
                canvas.Transform(placement);
                block.Show(canvas, PdfColor.FromGray(0));
                canvas.RestoreState();
                canvas.EndMarkedContent();
            }
        }
    }

    private static void Draw(PdfDocument document, PdfPage page, PdfStamp stamp, TextBlock? text, PdfFormXObject? form, PdfArtifactEdges edges = PdfArtifactEdges.None)
    {
        Frame frame = Frame.Of(page, stamp.Boundary, stamp.FollowPageRotation);
        (double width, double height, Matrix content) = stamp.Kind switch
        {
            PdfStampKind.Text => (text!.Width, text.Height, Matrix.Identity),
            PdfStampKind.Image => (stamp.Image!.Width, stamp.Image.Height, Matrix.CreateScale(stamp.Image.Width, stamp.Image.Height)),
            _ => FormBox(form!),
        };

        double radians = stamp.Rotation * Math.PI / 180;
        var rotate = new Matrix(Math.Cos(radians), Math.Sin(radians), -Math.Sin(radians), Math.Cos(radians), 0, 0);
        double scale = stamp.Scale;
        if (stamp.FitToPage)
        {
            PdfRectangle unit = Bounds(rotate, width, height);
            if (!(frame.Width > 0 && frame.Height > 0 && unit.Width > 0 && unit.Height > 0))
            {
                return;
            }

            scale = Math.Min(frame.Width / unit.Width, frame.Height / unit.Height);
        }

        Matrix turned = Matrix.CreateScale(scale, scale) * rotate;
        PdfRectangle bounds = Bounds(turned, width, height);
        double x = Align(stamp.HorizontalAlignment, frame.Width, bounds.Width, startIsLow: true) - bounds.Left + stamp.OffsetX;
        double y = Align(stamp.VerticalAlignment, frame.Height, bounds.Height, startIsLow: false) - bounds.Bottom + stamp.OffsetY;
        Matrix placement = turned * Matrix.CreateTranslation(x, y) * frame.ToUserSpace;

        using PdfCanvas canvas = stamp.Layer == PdfStampLayer.Background ? new PdfCanvas(document, page, before: true) : page.CreateCanvas();
        if (stamp.OptionalContentGroup is { } group)
        {
            canvas.BeginOptionalContent(group);
        }

        canvas.BeginArtifact(stamp.ArtifactType, stamp.ArtifactSubtype, edges, Bounds(placement, width, height));
        canvas.SaveState();
        if (stamp.Opacity < 1)
        {
            canvas.SetFillAlpha(stamp.Opacity);
            canvas.SetStrokeAlpha(stamp.Opacity);
        }

        canvas.Transform(content * placement);
        switch (stamp.Kind)
        {
            case PdfStampKind.Text:
                text!.Show(canvas, stamp.TextColor);
                break;
            case PdfStampKind.Image:
                canvas.DrawImage(stamp.Image!);
                break;
            default:
                canvas.DrawForm(form!);
                break;
        }

        canvas.RestoreState();
        canvas.EndMarkedContent();
        if (stamp.OptionalContentGroup is not null)
        {
            canvas.EndMarkedContent();
        }
    }

    /// <summary>A form's bounding box through its matrix: its size, and the translation that puts the box's lower-left corner at the origin.</summary>
    private static (double Width, double Height, Matrix Content) FormBox(PdfFormXObject form)
    {
        PdfRectangle box = form.BoundingBox ?? default;
        Matrix matrix = form.Matrix;
        PdfRectangle shown = Bounds(matrix, box);
        return (shown.Width, shown.Height, Matrix.CreateTranslation(-shown.Left, -shown.Bottom));
    }

    /// <summary>Where an item of <paramref name="size"/> starts in a span of <paramref name="extent"/>: left or top is Start.</summary>
    private static double Align(PdfStampAlignment alignment, double extent, double size, bool startIsLow) => alignment switch
    {
        PdfStampAlignment.Start => startIsLow ? 0 : extent - size,
        PdfStampAlignment.End => startIsLow ? extent - size : 0,
        _ => (extent - size) / 2,
    };

    private static PdfRectangle Bounds(Matrix matrix, double width, double height) => Bounds(matrix, new PdfRectangle(0, 0, width, height));

    private static PdfRectangle Bounds(Matrix matrix, PdfRectangle box)
    {
        PathPoint a = matrix.Transform(box.Left, box.Bottom);
        PathPoint b = matrix.Transform(box.Right, box.Bottom);
        PathPoint c = matrix.Transform(box.Right, box.Top);
        PathPoint d = matrix.Transform(box.Left, box.Top);
        return new PdfRectangle(
            Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)),
            Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)),
            Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)),
            Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
    }

    private static (int First, int Count) Pages(PdfDocument document, Range? pages)
    {
        (int offset, int length) = (pages ?? Range.All).GetOffsetAndLength(document.Pages.Count);
        return (offset, length);
    }

    /// <summary>Replaces the tokens <c>{page}</c>, <c>{pages}</c> and <c>{label}</c>, spelled as the flow layout spells them (§12.4.2 for labels).</summary>
    private static string Expand(PdfDocument document, string text, int pageIndex, int number, int count)
    {
        if (!text.Contains('{', StringComparison.Ordinal))
        {
            return text;
        }

        string numberText = number.ToString(CultureInfo.InvariantCulture);
        string result = text
            .Replace(PageCountToken, count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(PageNumberToken, numberText, StringComparison.Ordinal);
        if (result.Contains(PageLabelToken, StringComparison.Ordinal))
        {
            result = result.Replace(PageLabelToken, document.PageLabels is { } labels ? labels.GetLabel(pageIndex) : numberText, StringComparison.Ordinal);
        }

        return result;
    }

    private static void CheckOwner(PdfDocument document, PdfDocument? owner, string parameter, string what)
    {
        if (owner is not null && !ReferenceEquals(owner, document))
        {
            throw new ArgumentException($"The stamp's {what} belongs to another document.", parameter);
        }
    }

    private static void CheckColor(PdfDocument document, PdfColor color, string parameter)
    {
        if (color.ColorSpace.Cache is { } cache && !ReferenceEquals(cache.Document, document))
        {
            throw new ArgumentException("The stamp's text colour space belongs to another document.", parameter);
        }

        if (color.ColorSpace.Family == PdfColorSpaceFamily.Pattern)
        {
            throw new ArgumentException("A stamp's text colour shall not be a pattern.", parameter);
        }
    }

    /// <summary>
    /// The page's box as viewed: its width and height after <c>Rotate</c> when the stamp follows it, and the matrix from that upright
    /// frame (origin at its lower-left corner) to default user space (§7.7.3.3, §14.11.2).
    /// </summary>
    private readonly record struct Frame(double Width, double Height, Matrix ToUserSpace)
    {
        public static Frame Of(PdfPage page, PdfPageBoundary boundary, bool followRotation)
        {
            PdfRectangle box = boundary switch
            {
                PdfPageBoundary.MediaBox => page.MediaBox,
                PdfPageBoundary.BleedBox => page.BleedBox,
                PdfPageBoundary.TrimBox => page.TrimBox,
                PdfPageBoundary.ArtBox => page.ArtBox,
                _ => page.CropBox,
            };
            int rotation = followRotation ? page.Rotation : 0;
            Matrix toFrame = PageFormImporter.FormMatrix(box, rotation, 1);
            _ = toFrame.TryInvert(out Matrix toUser);
            bool turned = rotation is 90 or 270;
            return new Frame(turned ? box.Height : box.Width, turned ? box.Width : box.Height, toUser);
        }
    }

    /// <summary>Lines of text laid out for one page: measured (shaped where needed) before anything is drawn.</summary>
    private sealed class TextBlock
    {
        private readonly PdfCanvasFont _font;
        private readonly double _size;
        private readonly List<(string Text, ShapedText? Shaped, double X, double Baseline)> _lines = [];

        private TextBlock(PdfCanvasFont font, double size)
        {
            _font = font;
            _size = size;
        }

        public double Width { get; private set; }

        public double Height { get; private set; }

        /// <summary>Measures every line; throws <see cref="ArgumentException"/> when the font has no glyph for a character (never <c>.notdef</c>).</summary>
        public static TextBlock Lay(PdfCanvasFont font, double size, string text, PdfStampAlignment alignment, string parameter)
        {
            var block = new TextBlock(font, size);
            string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            double ascent = font.Ascent > 0 ? font.Ascent * size / 1000 : size * 0.8;
            double descent = font.Descent < 0 ? font.Descent * size / 1000 : -size * 0.2;
            double leading = ascent - descent;
            var measured = new (string Text, ShapedText? Shaped, double Width)[lines.Length];
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                try
                {
                    if (NeedsShaping(line))
                    {
                        ShapedText shaped = font.Shape(line, TextShaping.Default);
                        measured[index] = (line, shaped, shaped.MeasureWidth(size));
                    }
                    else
                    {
                        if (!font.CanEncode(line))
                        {
                            throw new ArgumentException($"The stamp's font {font.Name} cannot show the text \"{line}\".", parameter);
                        }

                        measured[index] = (line, null, font.MeasureWidth(line, size));
                    }
                }
                catch (ArgumentException exception) when (exception.ParamName != parameter)
                {
                    throw new ArgumentException($"The stamp's font {font.Name} cannot show the text \"{line}\": {exception.Message}", parameter, exception);
                }
            }

            block.Width = measured.Max(static line => line.Width);
            block.Height = leading * lines.Length;
            for (int index = 0; index < measured.Length; index++)
            {
                (string line, ShapedText? shaped, double width) = measured[index];
                double x = alignment switch
                {
                    PdfStampAlignment.Start => 0,
                    PdfStampAlignment.End => block.Width - width,
                    _ => (block.Width - width) / 2,
                };
                block._lines.Add((line, shaped, x, block.Height - ascent - (index * leading)));
            }

            return block;
        }

        public void Show(PdfCanvas canvas, PdfColor color)
        {
            canvas.BeginText();
            canvas.SetFont(_font, _size);
            canvas.SetFillColor(color);
            foreach ((string text, ShapedText? shaped, double x, double baseline) in _lines)
            {
                if (text.Length == 0)
                {
                    continue;
                }

                canvas.SetTextMatrix(new Matrix(1, 0, 0, 1, x, baseline));
                if (shaped is not null)
                {
                    canvas.ShowText(shaped);
                }
                else
                {
                    canvas.ShowText(text);
                }
            }

            canvas.EndText();
        }

        /// <summary>Whether a line holds right-to-left or Arabic-script characters, which <see cref="PdfCanvasFont.Shape"/> orders and joins.</summary>
        private static bool NeedsShaping(string line)
        {
            foreach (char c in line)
            {
                if (c is (>= '֐' and <= 'ࣿ') or (>= 'יִ' and <= '﷿') or (>= 'ﹰ' and <= '﻿'))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
