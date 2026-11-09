using System.Globalization;
using Broadside.Fonts;
using Broadside.Graphics;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// The Complete Works of William Shakespeare (public domain) in more than a thousand pages: Project Gutenberg's eBook #100 set in
/// two columns of 7.6 pt EB Garamond, line for line as the transcription has it (verse keeps its lines, long prose lines wrap),
/// each work from a new page, with running heads, folios and an outline of the 44 works. The large-document sample: scrolling,
/// thumbnails, search and memory over many pages. build.sh downloads the text to the output folder's .pg100.txt for this step and
/// deletes it afterwards; the text is not stored in the repository.
/// </summary>
internal static class Shakespeare
{
    private const double Width = 612;
    private const double Height = 792;
    private const double Size = 7.6;
    private const double Leading = 9.1;
    private const double Margin = 40;
    private const double Gutter = 18;
    private const double Top = Height - 58;
    private const double Bottom = 46;
    private const double ColumnWidth = (Width - (2 * Margin) - Gutter) / 2;

    public static void Write(GeneratorContext context)
    {
        string[] all = File.ReadAllLines(context.OutputPath(".pg100.txt"));
        int start = Array.FindIndex(all, static l => l.StartsWith("*** START OF THE PROJECT GUTENBERG EBOOK", StringComparison.Ordinal)) + 1;
        int end = Array.FindIndex(all, static l => l.StartsWith("*** END OF THE PROJECT GUTENBERG EBOOK", StringComparison.Ordinal));
        string[] lines = all[start..end];
        int contents = Array.FindIndex(lines, static l => l.Trim() == "Contents");
        var titles = new List<string>();
        int i = contents + 1;
        while (i < lines.Length && lines[i].Trim().Length == 0)
        {
            i++;
        }

        while (i < lines.Length && lines[i].Trim().Length > 0)
        {
            titles.Add(lines[i].Trim());
            i++;
        }

        // Each work starts where its title stands alone on a line after the contents.
        var starts = new List<int>();
        foreach (string title in titles)
        {
            int at = Array.FindIndex(lines, starts.Count == 0 ? i : starts[^1] + 1, l => l.Trim() == title);
            starts.Add(at >= 0 ? at : throw new InvalidOperationException($"'{title}' not found in the text."));
        }

        using PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        SetProperties(document, "The Complete Works of William Shakespeare", "The complete works in two columns, a public-domain text from Project Gutenberg (eBook #100).");
        document.Properties.Author = "William Shakespeare";
        PdfCanvasFont regular = context.Font(document, "ebgaramond/EBGaramond-Regular.otf");
        PdfCanvasFont bold = context.Font(document, "ebgaramond/EBGaramond-Bold.otf");
        PdfCanvasFont italic = context.Font(document, "ebgaramond/EBGaramond-Italic.otf");

        TitlePage(document, regular, bold, italic);
        PdfOutline outline = document.CreateOutline();
        for (int w = 0; w < titles.Count; w++)
        {
            int from = starts[w] + 1;
            int to = w + 1 < starts.Count ? starts[w + 1] : lines.Length;
            int first = document.Pages.Count;
            SetWork(document, regular, bold, italic, Title(titles[w]), lines[from..to]);
            outline.Add(Title(titles[w])).Destination = document.CreateDestination(document.Pages[first], PdfDestinationView.Fit);
        }

        PdfPageLabels labels = document.CreatePageLabels();
        labels.SetRange(0, PdfPageLabelStyle.LowercaseRoman);
        labels.SetRange(1, PdfPageLabelStyle.Arabic);
        document.Save(context.OutputPath("shakespeare.pdf"));
    }

    private static string Title(string title) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(title.ToLowerInvariant()).Replace("’S", "’s", StringComparison.Ordinal);

    private static void TitlePage(PdfDocument document, PdfCanvasFont regular, PdfCanvasFont bold, PdfCanvasFont italic)
    {
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, Width, Height));
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.SetFillColor(Gray(0.1));
        Centered(canvas, italic, 16, 560, "The Complete Works of");
        Centered(canvas, bold, 36, 515, "William Shakespeare");
        Centered(canvas, regular, 10, 120, "The text of Project Gutenberg’s eBook #100, in the public domain in the United States.");
        Centered(canvas, regular, 10, 106, "Set in two columns by Broadside for the Broadside sample documents.");
    }

    private static void SetWork(PdfDocument document, PdfCanvasFont regular, PdfCanvasFont bold, PdfCanvasFont italic, string title, string[] lines)
    {
        PdfCanvas? canvas = null;
        int column = 0;
        double y = 0;
        double columnTop = Top;
        bool firstPage = true;

        void NewPage()
        {
            canvas?.Dispose();
            PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, Width, Height));
            canvas = page.CreateCanvas();
            canvas.SetFillColor(Gray(0.35));
            int folio = document.Pages.Count - 1;
            if (!firstPage)
            {
                Line(canvas, italic, 8, Margin, Height - 34, "The Complete Works");
                Line(canvas, italic, 8, Width - Margin - italic.MeasureWidth(title, 8), Height - 34, title);
            }

            string number = folio.ToString(CultureInfo.InvariantCulture);
            Line(canvas, regular, 8, (Width - regular.MeasureWidth(number, 8)) / 2, 26, number);
            canvas.SetStrokeColor(Gray(0.6));
            canvas.SetLineWidth(0.3);
            canvas.MoveTo(Margin + ColumnWidth + (Gutter / 2), Bottom);
            canvas.LineTo(Margin + ColumnWidth + (Gutter / 2), firstPage ? Top - 60 : Top + 6);
            canvas.Stroke();
            canvas.SetFillColor(Gray(0.05));
            column = 0;
            columnTop = firstPage ? Top - 60 : Top;
            y = columnTop;
            if (firstPage)
            {
                canvas.SetFillColor(Gray(0.1));
                Line(canvas, bold, 16, (Width - bold.MeasureWidth(title, 16)) / 2, Top - 18, title);
                canvas.SetFillColor(Gray(0.05));
            }

            firstPage = false;
        }

        void Next()
        {
            y -= Leading;
            if (y < Bottom)
            {
                if (column == 0)
                {
                    column = 1;
                    y = columnTop;
                }
                else
                {
                    NewPage();
                }
            }
        }

        NewPage();
        bool blank = true;
        foreach (string raw in lines)
        {
            string line = raw.Replace("\t", "    ", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).TrimEnd();
            if (line.Length == 0)
            {
                if (!blank)
                {
                    y -= Leading / 2;
                }

                blank = true;
                continue;
            }

            blank = false;
            int indent = line.Length - line.TrimStart().Length;
            string text = line.TrimStart();
            double x = Margin + (column * (ColumnWidth + Gutter)) + Math.Min(indent, 24) * 1.6;
            double room = Margin + (column * (ColumnWidth + Gutter)) + ColumnWidth - x;
            PdfCanvasFont font = text.Length > 1 && !text.Any(char.IsLower) && text.Any(char.IsLetter) ? bold : regular;
            foreach (string piece in Wrap(font, text, room))
            {
                x = Margin + (column * (ColumnWidth + Gutter)) + Math.Min(indent, 24) * 1.6;
                Line(canvas!, font, Size, x, y, piece);
                Next();
                indent = Math.Max(indent, 4);
            }
        }

        canvas?.Dispose();
    }

    private static IEnumerable<string> Wrap(PdfCanvasFont font, string text, double room)
    {
        if (font.MeasureWidth(text, Size) <= room)
        {
            yield return text;
            yield break;
        }

        string line = string.Empty;
        foreach (string word in text.Split(' '))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (font.MeasureWidth(candidate, Size) > room && line.Length > 0)
            {
                yield return line;
                room -= 6.4;
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0)
        {
            yield return line;
        }
    }

    private static void Centered(PdfCanvas canvas, PdfCanvasFont font, double size, double y, string text) =>
        Line(canvas, font, size, (Width - font.MeasureWidth(text, size)) / 2, y, text);

    private static void Line(PdfCanvas canvas, PdfCanvasFont font, double size, double x, double y, string text)
    {
        canvas.BeginText();
        canvas.SetFont(font, size);
        canvas.MoveText(x, y);
        canvas.ShowText(text);
        canvas.EndText();
    }
}
