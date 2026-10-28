using System.Text.Json;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// The large-print edition of The Wind in the Willows (1908, public domain in the US), laid out by Broadside's flow layout from
/// novel/willows.json: Atkinson Hyperlegible at 16 pt on A4, tagged as it is placed (PDF/UA-1), a contents page with page numbers,
/// an outline, roman page labels for the front matter and each chapter on a new page. The contents needs the chapters' page
/// numbers, so the book is laid out twice: the first pass finds where each chapter starts by extracting the text of its pages.
/// </summary>
internal static class NovelLargePrint
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record Book(string Title, string Author, int Year, Chapter[] Chapters);

    private sealed record Chapter(string Number, string Title, string[] Paragraphs);

    public static void Write(GeneratorContext context)
    {
        Book book = JsonSerializer.Deserialize<Book>(File.ReadAllText(Path.Combine(context.Root, "novel", "willows.json")),
            JsonOptions)!;

        // Pass 1: lay the book out with placeholder page numbers and find the first page of each chapter.
        int[] starts;
        using (PdfDocument draft = Lay(context, book, null))
        {
            starts = FindChapters(draft, book);
        }

        using PdfDocument document = Lay(context, book, starts);
        int[] final = FindChapters(document, book);
        if (!final.SequenceEqual(starts))
        {
            throw new InvalidOperationException("The contents changed the pagination; lay the book out again.");
        }

        int front = final[0];
        PdfPageLabels labels = document.CreatePageLabels();
        labels.SetRange(0, PdfPageLabelStyle.LowercaseRoman);
        labels.SetRange(front, PdfPageLabelStyle.Arabic, firstNumber: front + 1); // the labels match the printed folios

        PdfOutline outline = document.CreateOutline();
        for (int i = 0; i < book.Chapters.Length; i++)
        {
            PdfOutlineItem item = outline.Add($"{book.Chapters[i].Number}. {book.Chapters[i].Title}");
            item.Destination = document.CreateDestination(document.Pages[final[i]], PdfDestinationView.Fit);
        }

        document.Save(context.OutputPath("novel-large-print.pdf"), new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));
    }

    private static PdfDocument Lay(GeneratorContext context, Book book, int[]? starts)
    {
        PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        SetProperties(document, $"{book.Title} (large print)", $"{book.Title} by {book.Author} ({book.Year}), a large-print edition laid out by Broadside.");
        document.Properties.Author = book.Author;

        PdfCanvasFont regular = context.Font(document, "atkinsonhyperlegible/AtkinsonHyperlegible-Regular.ttf");
        PdfCanvasFont bold = context.Font(document, "atkinsonhyperlegible/AtkinsonHyperlegible-Bold.ttf");
        PdfCanvasFont italic = context.Font(document, "atkinsonhyperlegible/AtkinsonHyperlegible-Italic.ttf");
        PdfColor ink = Rgb(0.08, 0.08, 0.1);
        var body = new ParagraphStyle(regular, 16).WithLineHeight(1.5).WithSpaceAfter(10);
        var running = new TextStyle(regular, 11).WithColor(Gray(0.35));

        var page = new PageSetup(Mm(210), Mm(297), new PageMargins { Left = Mm(25), Right = Mm(25), Top = Mm(28), Bottom = Mm(26) });
        var front = new PageTemplate(page);
        var chapters = new PageTemplate(page)
        {
            Header = new HeaderFooter(running) { Left = book.Title, Right = "Large print edition" },
            Footer = new HeaderFooter(running) { Center = "{page}" },
            FirstPageHeader = new HeaderFooter(running),
        };
        var flow = new FlowDocument(front)
        {
            Tagging = FlowTagging.Default.WithLanguage("en-GB").WithTitle(book.Title),
        };

        flow.Add(new Paragraph(book.Title, new ParagraphStyle(bold, 40).WithAlignment(TextAlignment.Center).WithSpaceBefore(180).WithSpaceAfter(24)) { StructureType = "H1" });
        flow.Add(new Paragraph(book.Author, new ParagraphStyle(regular, 24).WithAlignment(TextAlignment.Center).WithSpaceAfter(200)));
        flow.Add(new Paragraph($"First published in {book.Year}. This text is in the public domain in the United States. Large-print edition laid out by Broadside for the Broadside project's sample documents.",
            new ParagraphStyle(regular, 13).WithAlignment(TextAlignment.Center).WithLineHeight(1.4)));

        flow.StartSection(front);
        flow.Add(new Paragraph("Contents", new ParagraphStyle(bold, 28).WithSpaceAfter(24)) { StructureType = "H2" });
        var contents = new Table(ColumnWidth.Fixed(Mm(18)), ColumnWidth.Proportional(1), ColumnWidth.Fixed(Mm(18)))
        {
            Style = TableStyle.Default.WithBorder(new TableBorder(0, ink)).WithPadding(new CellPadding(5)),
        };
        var contentsStyle = new ParagraphStyle(regular, 16);
        for (int i = 0; i < book.Chapters.Length; i++)
        {
            // The first pass leaves room for three digits; the second fills in the real page numbers.
            string number = starts is null ? "000" : (starts[i] + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            contents.AddRow(new TableCell(book.Chapters[i].Number, contentsStyle), new TableCell(book.Chapters[i].Title, contentsStyle),
                new TableCell(number, contentsStyle.WithAlignment(TextAlignment.Right)));
        }

        flow.Add(contents);

        for (int i = 0; i < book.Chapters.Length; i++)
        {
            Chapter chapter = book.Chapters[i];
            flow.StartSection(chapters);
            flow.Add(new Paragraph($"Chapter {chapter.Number}", new ParagraphStyle(regular, 16).WithSpaceBefore(60)));
            flow.Add(new Paragraph(chapter.Title, new ParagraphStyle(bold, 30).WithSpaceAfter(30)) { StructureType = "H2" });
            foreach (string paragraph in chapter.Paragraphs)
            {
                flow.Add(new Paragraph(Runs(paragraph, italic), body));
            }
        }

        flow.Add(new Paragraph("The End", new ParagraphStyle(bold, 18).WithAlignment(TextAlignment.Center).WithSpaceBefore(30)));
        flow.Render(document);
        return document;
    }

    // The transcription marks italics with underscores.
    private static IEnumerable<InlineElement> Runs(string paragraph, PdfCanvasFont italic)
    {
        string[] parts = paragraph.Split('_');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
            {
                continue;
            }

            yield return i % 2 == 1 ? new TextRun(parts[i], new TextStyle(italic, 16)) : new TextRun(parts[i]);
        }
    }

    private static int[] FindChapters(PdfDocument document, Book book)
    {
        int[] starts = new int[book.Chapters.Length];
        int next = 0;
        for (int p = 0; p < document.Pages.Count && next < starts.Length; p++)
        {
            string text = document.Pages[p].ExtractText().Text;
            if (text.Contains($"Chapter {book.Chapters[next].Number}\n", StringComparison.Ordinal) || text.StartsWith($"Chapter {book.Chapters[next].Number}", StringComparison.Ordinal)
                || text.Contains($"\nChapter {book.Chapters[next].Number}", StringComparison.Ordinal))
            {
                starts[next++] = p;
            }
        }

        return next == starts.Length ? starts : throw new InvalidOperationException($"Found {next} of {starts.Length} chapter openings.");
    }
}
