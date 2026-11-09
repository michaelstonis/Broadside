using System.Globalization;
using System.Text;
using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Text;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// The scientific paper as a referee returns it: Chrome's file with an incremental update that adds a review. Every markup
/// annotation type (highlight, underline, squiggly, strike-out, caret, text notes with a popup and threaded replies with a review
/// state, free text, line with an arrowhead, square, circle, polygon, polyline, ink, a stamp), a file attachment, an unapplied
/// redaction, and a referee's report page whose links use every action a link carries: URI, GoTo a page, a named destination,
/// GoToR into another file, Launch and JavaScript (the last two are kept, never run). Text positions come from Broadside's own
/// text extraction, so the markup sits on the words it names.
/// </summary>
internal static class PaperReviewed
{
    private const string Reviewer = "Reviewer 2";
    private const string Author = "A. Okafor";
    private static readonly DateTimeOffset Reviewed = new(2027, 2, 14, 16, 30, 0, TimeSpan.Zero);

    public static void Write(GeneratorContext context)
    {
        using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(context.OutputPath("paper.pdf")));
        PdfPage one = document.Pages[0];
        PdfPage two = document.Pages[1];
        TextPage text1 = one.ExtractText();
        TextPage text2 = two.ExtractText();
        var yellow = new PdfDeviceColor(1, 0.86, 0.2);
        var blue = new PdfDeviceColor(0.2, 0.45, 0.9);
        var red = new PdfDeviceColor(0.85, 0.15, 0.15);
        var green = new PdfDeviceColor(0.15, 0.6, 0.3);
        static PdfAnnotationOptions Markup(PdfDeviceColor color, string contents) =>
            new PdfAnnotationOptions().WithColor(color).WithTitle(Reviewer).WithContents(contents);

        // Text markup on page 1.
        PdfAnnotation highlight = one.AddHighlightAnnotation(Find(text1, "underestimates the early cooling of the open cup"),
            Markup(yellow, "Is the 4 °C gap outside the thermometer's uncertainty? Please give the uncertainty here."));
        Popup(document, one, highlight, new PdfRectangle(400, 690, 590, 780));
        PdfAnnotation reply = Reply(document, one, highlight, Author, "Yes: the probe reads to ±0.2 °C. Added to Section 2.", null);
        Reply(document, one, reply, Reviewer, "Accepted.", "Accepted");
        one.AddUnderlineAnnotation(Find(text1, "a lid is worth"), Markup(blue, "Nice rule of thumb. Keep it in the conclusion too."));
        one.AddSquigglyAnnotation(Find(text1, "vapour pressure"), Markup(green, "House style is American spelling: vapor pressure."));
        IReadOnlyList<PdfQuadrilateral> strike = Find(text1, "with kitchen equipment,");
        one.AddStrikeOutAnnotation(strike, Markup(red, "Delete: Section 2 already says this."));
        PdfRectangle after = Bounds(Find(text1, "from the truth"));
        Caret(document, one, new PdfRectangle(after.Right - 1, after.Bottom - 4, after.Right + 9, after.Bottom + 6), "Insert: \"at the 1 °C level\"");

        // Shapes and drawing on the figure.
        PdfRectangle legend = Bounds(Find(text1, "Open mug"));
        one.AddInkAnnotation([Loop(legend, 9)], new PdfAnnotationOptions().WithColor(red).WithBorderWidth(1.5).WithTitle(Reviewer)
            .WithContents("The legend hides the first minutes of the open mug's curve."));
        PdfRectangle panel = Bounds(Find(text1, "Logarithm of the excess temperature"));
        one.AddSquareAnnotation(new PdfRectangle(panel.Left - 30, panel.Bottom - 4, panel.Right + 8, panel.Top + 4),
            new PdfAnnotationOptions().WithColor(blue).WithBorderWidth(1).WithTitle(Reviewer).WithContents("Panel (b) needs a y-axis label with units."));
        PdfRectangle drinking = Bounds(Find(text1, "drinking range"));
        one.AddCircleAnnotation(new PdfRectangle(drinking.Left - 8, drinking.Bottom - 6, drinking.Right + 8, drinking.Top + 6),
            new PdfAnnotationOptions().WithColor(green).WithBorderWidth(1.2).WithTitle(Reviewer).WithContents("Which range? Give it in °C in the caption."));
        var note = new PdfRectangle(178, 566, 292, 598);
        one.AddFreeTextAnnotation(note, "Figure 1: label both y-axes and say how many runs each fit uses.",
            new PdfAnnotationOptions().WithColor(red).WithTextColor(new PdfDeviceColor(0.6, 0.05, 0.05)).WithFontSize(8).WithTitle(Reviewer));
        PdfLineAnnotation arrow = one.AddLineAnnotation(new PathPoint(292, 590), new PathPoint(legend.Left - 12, legend.Bottom),
            new PdfAnnotationOptions().WithColor(red).WithBorderWidth(1).WithTitle(Reviewer));
        arrow.Dictionary[new CosName("LE")] = new CosArray([new CosName("None"), new CosName("OpenArrow")]);

        // Page 2: a stamp, a polygon and polyline, a file attachment and an unapplied redaction.
        PdfRectangle page2 = two.MediaBox;
        one.AddStampAnnotation(new PdfRectangle(page2.Right - 190, page2.Top - 80, page2.Right - 40, page2.Top - 40), "ForComment",
            new PdfAnnotationOptions().WithColor(blue).WithTitle(Reviewer).WithContents("Returned to the authors for minor revisions."));
        PdfRectangle references = Bounds(Find(text2, "References"));
        two.AddPolygonAnnotation(
            [new PathPoint(references.Left - 6, references.Top + 6), new PathPoint(references.Right + 60, references.Top + 10),
             new PathPoint(references.Right + 70, references.Bottom - 40), new PathPoint(references.Left - 10, references.Bottom - 48)],
            new PdfAnnotationOptions().WithColor(green).WithBorderWidth(1).WithTitle(Reviewer).WithContents("Add a reference for the evaporation term."));
        PdfRectangle conclusion = Bounds(Find(text2, "Conclusion"));
        two.AddPolyLineAnnotation(
            [new PathPoint(conclusion.Left, conclusion.Bottom - 3), new PathPoint(conclusion.Left + 20, conclusion.Bottom - 6),
             new PathPoint(conclusion.Left + 40, conclusion.Bottom - 2), new PathPoint(conclusion.Left + 60, conclusion.Bottom - 6)],
            new PdfAnnotationOptions().WithColor(blue).WithBorderWidth(1).WithTitle(Reviewer).WithContents("A wavy line under the heading: shorten this section."));
        byte[] csv = Encoding.UTF8.GetBytes(Reanalysis());
        PdfFileSpecification attachment = document.CreateEmbeddedFile("tea-reanalysis.csv", csv, new PdfEmbeddedFileOptions()
            .WithMediaType("text/csv").WithDescription("The referee's refit of the open mug").WithCreationDate(Reviewed).WithModificationDate(Reviewed));
        FileAttachment(document, two, new PdfRectangle(conclusion.Right + 8, conclusion.Bottom - 2, conclusion.Right + 22, conclusion.Bottom + 16), attachment);
        // The authors' own earlier paper, withheld for double-blind review.
        PdfRectangle self = Bounds(Find(text2, "Okafor, A. and Brandt, L."));
        Redact(document, two, new PdfRectangle(self.Left - 2, self.Bottom - 2, self.Right + 2, self.Top + 2));

        // The referee's report: a new last page with links.
        ReportPage(document, one, two);

        using FileStream output = File.Create(context.OutputPath("paper-reviewed.pdf"));
        document.Save(output, new PdfSaveOptions().WithIncrementalUpdate(true).WithUpdateModificationDate(false));
    }

    private static void ReportPage(PdfDocument document, PdfPage one, PdfPage two)
    {
        PdfRectangle box = one.MediaBox;
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, box.Width, box.Height));
        PdfCanvasFont regular = document.GetStandard14Font(Standard14Font.Helvetica);
        PdfCanvasFont bold = document.GetStandard14Font(Standard14Font.HelveticaBold);
        double left = 56;
        double y = box.Height - 72;
        var links = new List<(string Text, CosDictionary Action)>
        {
            ("Reviewer guidelines (journal website)", Uri("https://journal.example.org/reviewers")),
            ("Figure 1, on page 1", GoTo(one)),
            ("The results section (named destination \"results\")", new CosDictionary { [new CosName("S")] = new CosName("GoTo"), [new CosName("D")] = Ascii("results") }),
            ("The Ghostscript copy of this paper, page 1 (paper-ghostscript.pdf)", GoToR("paper-ghostscript.pdf")),
            ("Open the attached refit (tea-reanalysis.csv)", Launch("tea-reanalysis.csv")),
            ("Print this report", JavaScript("this.print({bUI: true, nStart: 2, nEnd: 2});")),
        };

        // A named destination at the top of page 2, in the catalog's Dests dictionary (PDF 1.1 style).
        var dests = new CosDictionary { [new CosName("results")] = new CosArray([two.Reference!, new CosName("XYZ"), new CosInteger(0), new CosReal(two.MediaBox.Top), CosNull.Instance]) };
        document.Catalog[new CosName("Dests")] = document.AddObject(dests);

        using (PdfCanvas canvas = page.CreateCanvas())
        {
            canvas.SetFillColor(Gray(0.1));
            Line(canvas, bold, 16, left, y, "Referee's report");
            y -= 20;
            Line(canvas, regular, 10, left, y, "Manuscript: \"How fast does tea cool? A two-term model of a cup on a desk\"");
            y -= 14;
            Line(canvas, regular, 10, left, y, "Recommendation: minor revision. " + Reviewed.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) + ".");
            y -= 30;
            string[] body =
            [
                "The two-term model is a sensible, economical extension of Newton's law, and the measurements are",
                "careful. My comments are on the page as annotations; the main ones are:",
                "",
                "1. State the thermometer's uncertainty where the 4 °C discrepancy is first reported.",
                "2. Label both y-axes in Figure 1, with units, and move the legend off the early data.",
                "3. Give the drinking range in degrees, and add a reference for the evaporation term.",
                "4. The attached file has my refit of the open mug, for the authors to check.",
            ];
            foreach (string line in body)
            {
                Line(canvas, regular, 10, left, y, line);
                y -= 14;
            }

            y -= 16;
            Line(canvas, bold, 11, left, y, "Links");
            y -= 18;
            canvas.SetFillColor(new PdfColor(PdfDeviceRgbColorSpace.Instance, [0.1f, 0.3f, 0.75f]));
            foreach ((string text, _) in links)
            {
                Line(canvas, regular, 10, left, y, text);
                y -= 16;
            }
        }

        y = box.Height - 72 - 20 - 14 - 30 - (7 * 14) - 16 - 18;
        foreach ((string text, CosDictionary action) in links)
        {
            double width = regular.MeasureWidth(text, 10);
            var link = new CosDictionary
            {
                [new CosName("Type")] = new CosName("Annot"),
                [new CosName("Subtype")] = new CosName("Link"),
                [new CosName("Rect")] = Rect(new PdfRectangle(left, y - 3, left + width, y + 10)),
                [new CosName("Border")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(0)]),
                [new CosName("A")] = action,
                [new CosName("Contents")] = Ascii(text),
            };
            AddAnnotation(document, page, link);
            y -= 16;
        }
    }

    // A text annotation replying to another (IRT), optionally setting its review state (a state reply, StateModel Review).
    private static PdfTextAnnotation Reply(PdfDocument document, PdfPage page, PdfAnnotation parent, string author, string contents, string? state)
    {
        PdfRectangle at = Bounds(parent.Dictionary);
        PdfTextAnnotation reply = page.AddTextAnnotation(new PdfRectangle(at.Left, at.Top, at.Left + 20, at.Top + 20), contents,
            new PdfAnnotationOptions().WithTitle(author).WithColor(new PdfDeviceColor(0.5, 0.7, 1)));
        reply.Dictionary[new CosName("IRT")] = parent.Reference!;
        reply.Dictionary[new CosName("RT")] = new CosName("R");
        reply.Dictionary[new CosName("M")] = Ascii(Date(Reviewed.AddDays(state is null ? 3 : 5)));
        if (state is not null)
        {
            reply.Dictionary[new CosName("StateModel")] = Ascii("Review");
            reply.Dictionary[new CosName("State")] = Ascii(state);
            reply.Flags = PdfAnnotationFlags.Hidden | PdfAnnotationFlags.NoView;
        }

        _ = document;
        return reply;
    }

    private static void Popup(PdfDocument document, PdfPage page, PdfAnnotation parent, PdfRectangle rect)
    {
        var popup = new CosDictionary
        {
            [new CosName("Type")] = new CosName("Annot"),
            [new CosName("Subtype")] = new CosName("Popup"),
            [new CosName("Rect")] = Rect(rect),
            [new CosName("Parent")] = parent.Reference!,
            [new CosName("Open")] = CosBoolean.False,
        };
        parent.Dictionary[new CosName("Popup")] = AddAnnotation(document, page, popup);
    }

    private static void Caret(PdfDocument document, PdfPage page, PdfRectangle rect, string contents)
    {
        string ap = string.Create(CultureInfo.InvariantCulture, $"0.85 0.15 0.15 rg 0 0 m {rect.Width / 2:0.##} {rect.Height:0.##} l {rect.Width:0.##} 0 l h f");
        var caret = Markup("Caret", rect, contents, [0.85, 0.15, 0.15]);
        caret[new CosName("Sy")] = new CosName("P");
        caret[new CosName("AP")] = Appearance(document, rect, ap, null);
        AddAnnotation(document, page, caret);
    }

    private static void FileAttachment(PdfDocument document, PdfPage page, PdfRectangle rect, PdfFileSpecification file)
    {
        // A paperclip: a long rounded loop drawn with two strokes.
        string ap = "0.3 0.3 0.35 RG 1.2 w 1 J 4 2 m 4 14 l 4 17 10 17 10 14 c 10 5 l 10 3 7 3 7 5 l 7 13 l S";
        var annotation = Markup("FileAttachment", rect, "Refit of the open mug (CSV)", [0.3, 0.3, 0.35]);
        annotation[new CosName("FS")] = (CosObject?)file.Reference ?? file.Dictionary!;
        annotation[new CosName("Name")] = new CosName("Paperclip");
        annotation[new CosName("AP")] = Appearance(document, rect, ap, null);
        AddAnnotation(document, page, annotation);
    }

    private static void Redact(PdfDocument document, PdfPage page, PdfRectangle rect)
    {
        string ap = string.Create(CultureInfo.InvariantCulture, $"0.85 0.15 0.15 RG 1 w 0.5 0.5 {rect.Width - 1:0.##} {rect.Height - 1:0.##} re S");
        var redact = Markup("Redact", rect, "Withheld for double-blind review", [0.85, 0.15, 0.15]);
        redact[new CosName("QuadPoints")] = new CosArray([.. new[] { rect.Left, rect.Top, rect.Right, rect.Top, rect.Left, rect.Bottom, rect.Right, rect.Bottom }.Select(static v => new CosReal(v))]);
        redact[new CosName("IC")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(0)]);
        redact[new CosName("OverlayText")] = Ascii("WITHHELD");
        redact[new CosName("AP")] = Appearance(document, rect, ap, null);
        AddAnnotation(document, page, redact);
    }

    private static CosDictionary Markup(string subtype, PdfRectangle rect, string contents, double[] color) => new()
    {
        [new CosName("Type")] = new CosName("Annot"),
        [new CosName("Subtype")] = new CosName(subtype),
        [new CosName("Rect")] = Rect(rect),
        [new CosName("Contents")] = Utf16(contents),
        [new CosName("T")] = Ascii(Reviewer),
        [new CosName("M")] = Ascii(Date(Reviewed)),
        [new CosName("CreationDate")] = Ascii(Date(Reviewed)),
        [new CosName("C")] = new CosArray([.. color.Select(static c => new CosReal(c))]),
        [new CosName("F")] = new CosInteger(4),
    };

    private static CosDictionary Appearance(PdfDocument document, PdfRectangle rect, string content, CosDictionary? resources)
    {
        var form = new CosDictionary
        {
            [new CosName("Type")] = new CosName("XObject"),
            [new CosName("Subtype")] = new CosName("Form"),
            [new CosName("BBox")] = Rect(new PdfRectangle(0, 0, rect.Width, rect.Height)),
        };
        if (resources is not null)
        {
            form[new CosName("Resources")] = resources;
        }

        return new CosDictionary { [new CosName("N")] = document.AddObject(new CosStream(form, Encoding.ASCII.GetBytes(content))) };
    }

    private static CosReference AddAnnotation(PdfDocument document, PdfPage page, CosDictionary annotation)
    {
        annotation[new CosName("P")] = page.Reference!;
        CosReference reference = document.AddObject(annotation);
        if (page.Dictionary.TryGetValue(new CosName("Annots"), out CosObject? existing) && document.Resolve(existing) is CosArray annots)
        {
            annots.Add(reference);
        }
        else
        {
            page.Dictionary[new CosName("Annots")] = new CosArray([reference]);
        }

        return reference;
    }

    private static CosDictionary Uri(string uri) => new() { [new CosName("S")] = new CosName("URI"), [new CosName("URI")] = Ascii(uri) };

    private static CosDictionary GoTo(PdfPage page) => new()
    {
        [new CosName("S")] = new CosName("GoTo"),
        [new CosName("D")] = new CosArray([page.Reference!, new CosName("Fit")]),
    };

    private static CosDictionary GoToR(string file) => new()
    {
        [new CosName("S")] = new CosName("GoToR"),
        [new CosName("F")] = new CosDictionary { [new CosName("Type")] = new CosName("Filespec"), [new CosName("F")] = Ascii(file), [new CosName("UF")] = Utf16(file) },
        [new CosName("D")] = new CosArray([new CosInteger(0), new CosName("Fit")]),
        [new CosName("NewWindow")] = CosBoolean.True,
    };

    private static CosDictionary Launch(string file) => new()
    {
        [new CosName("S")] = new CosName("Launch"),
        [new CosName("F")] = new CosDictionary { [new CosName("Type")] = new CosName("Filespec"), [new CosName("F")] = Ascii(file), [new CosName("UF")] = Utf16(file) },
    };

    private static CosDictionary JavaScript(string script) => new() { [new CosName("S")] = new CosName("JavaScript"), [new CosName("JS")] = Ascii(script) };

    private static CosArray Rect(PdfRectangle r) => new([new CosReal(r.Left), new CosReal(r.Bottom), new CosReal(r.Right), new CosReal(r.Top)]);

    private static CosString Ascii(string text) => new(Encoding.ASCII.GetBytes(text));

    private static CosString Utf16(string text) => new([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)]);

    private static string Date(DateTimeOffset date) => "D:" + date.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";

    private static PdfRectangle Bounds(CosDictionary annotation)
    {
        var rect = (CosArray)annotation[new CosName("Rect")];
        double Value(int i) => rect[i] switch { CosInteger n => n.Value, CosReal r => r.Value, _ => 0 };
        return new PdfRectangle(Value(0), Value(1), Value(2), Value(3));
    }

    private static PdfRectangle Bounds(IReadOnlyList<PdfQuadrilateral> quads)
    {
        double left = double.MaxValue, bottom = double.MaxValue, right = double.MinValue, top = double.MinValue;
        foreach (PdfQuadrilateral quad in quads)
        {
            foreach (PathPoint point in new[] { quad.Point1, quad.Point2, quad.Point3, quad.Point4 })
            {
                left = Math.Min(left, point.X);
                right = Math.Max(right, point.X);
                bottom = Math.Min(bottom, point.Y);
                top = Math.Max(top, point.Y);
            }
        }

        return new PdfRectangle(left, bottom, right, top);
    }

    /// <summary>The quadrilaterals of the first occurrence of a phrase (the first of several candidates found), one per line.</summary>
    private static List<PdfQuadrilateral> Find(TextPage page, params string[] phrases)
    {
        string text = page.Text;
        foreach (string phrase in phrases)
        {
            int start = text.IndexOf(phrase, StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            int end = start + phrase.Length;
            var lines = new List<PdfRectangle>();
            foreach (TextWord word in page.Words)
            {
                if (word.TextStart + word.TextLength <= start || word.TextStart >= end)
                {
                    continue;
                }

                PdfRectangle b = word.Quad.Bounds;
                int same = lines.FindIndex(l => Math.Abs(l.Bottom - b.Bottom) < 2);
                lines.Add(b);
                if (same >= 0)
                {
                    PdfRectangle l = lines[same];
                    lines[same] = new PdfRectangle(Math.Min(l.Left, b.Left), Math.Min(l.Bottom, b.Bottom), Math.Max(l.Right, b.Right), Math.Max(l.Top, b.Top));
                    lines.RemoveAt(lines.Count - 1);
                }
            }

            return [.. lines.Select(static l => new PdfQuadrilateral(new PathPoint(l.Left, l.Top), new PathPoint(l.Right, l.Top), new PathPoint(l.Left, l.Bottom), new PathPoint(l.Right, l.Bottom)))];
        }

        throw new InvalidOperationException($"'{phrases[0]}' is not in the paper's text; update PaperReviewed.cs.");
    }

    // A hand-drawn loop around a rectangle: an ellipse with a little wobble and an overlapping tail, as a pen stroke.
    private static List<PathPoint> Loop(PdfRectangle around, double margin)
    {
        var points = new List<PathPoint>();
        double cx = (around.Left + around.Right) / 2;
        double cy = (around.Bottom + around.Top) / 2;
        double rx = (around.Width / 2) + margin;
        double ry = (around.Height / 2) + margin;
        for (int i = 0; i <= 44; i++)
        {
            double t = (i / 40.0 * 2 * Math.PI) + 0.4;
            double wobble = 1 + (0.04 * Math.Sin(i * 1.7));
            points.Add(new PathPoint(cx + (rx * wobble * Math.Cos(t)), cy + (ry * wobble * Math.Sin(t))));
        }

        return points;
    }

    private static string Reanalysis()
    {
        var csv = new StringBuilder("minute,measured_c,newton_fit_c,two_term_fit_c\n");
        for (int minute = 0; minute <= 60; minute += 5)
        {
            double newton = 22 + (73 * Math.Exp(-0.031 * minute));
            double twoTerm = newton - (3.6 * Math.Exp(-0.12 * minute) * (1 - Math.Exp(-0.5 * minute)));
            double measured = twoTerm + (0.15 * Math.Sin(minute));
            csv.Append(CultureInfo.InvariantCulture, $"{minute},{measured:0.0},{newton:0.0},{twoTerm:0.0}\n");
        }

        return csv.ToString();
    }

    private static void Line(PdfCanvas canvas, PdfCanvasFont font, double size, double x, double y, string text)
    {
        canvas.BeginText();
        canvas.SetFont(font, size);
        canvas.MoveText(x, y);
        canvas.ShowText(text);
        canvas.EndText();
    }
}
