using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.Standard14;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Layout.Flow;

namespace Broadside.Viewer.Blazor.Sample;

/// <summary>What the Create panel asks for.</summary>
public sealed class CreateRequest
{
    public string Title { get; set; } = "Quarterly report";

    public string Author { get; set; } = "Broadside demo";

    public string Body { get; set; } =
        "Broadside laid out this document in your browser with its flow layout package: paragraphs are broken into lines with the " +
        "Unicode line breaking algorithm, shaped with the font's OpenType tables, and flowed across pages with headers and footers.\n" +
        "Every block is tagged as it is placed, so the file carries a structure tree in reading order, alternate text for the figure " +
        "and header cells for the table. Choose a PDF/A level and PDF/UA below and the save checks the rules and identifies the file.";

    public bool IncludeTable { get; set; } = true;

    public bool IncludeFigure { get; set; } = true;

    public PdfAConformance Archival { get; set; } = PdfAConformance.PdfA2u;

    public PdfUAConformance Accessible { get; set; } = PdfUAConformance.PdfUA1;
}

/// <summary>The created file and what saving it reported.</summary>
/// <param name="Bytes">The saved file.</param>
/// <param name="Pages">Its page count.</param>
/// <param name="Diagnostics">What the layout and the save reported (conformance problems among them).</param>
public sealed record CreateResult(byte[] Bytes, int Pages, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Builds a document with the flow layout package (#171–#179) and saves it with the chosen PDF/A (ISO 19005) and PDF/UA (ISO 14289)
/// conformance (#201–#206): embedded Liberation fonts (the Standard 14 metric-compatible programs bundled with the app), tagging by
/// construction, a header and footer, a list, a table with a header row, a figure with alternate text and a link.
/// </summary>
public static class DocumentCreator
{
    public static CreateResult Create(CreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);

        // PDF/UA-1 and PDF/A-2/3 are PDF 1.7 formats: their structure uses the PDF 1.7 namespace. PDF/UA-2 and PDF/A-4 are PDF 2.0.
        bool pdf20 = request.Accessible == PdfUAConformance.PdfUA2 || request.Archival is PdfAConformance.PdfA4 or PdfAConformance.PdfA4f;
        PdfCanvasFont font = document.EmbedTrueTypeFont(Standard14FontResolver.GetProgram(Standard14Font.Helvetica));
        PdfCanvasFont bold = document.EmbedTrueTypeFont(Standard14FontResolver.GetProgram(Standard14Font.HelveticaBold));
        var body = new ParagraphStyle(font, 11).WithLineHeight(1.35).WithSpaceAfter(8);
        var heading = new ParagraphStyle(bold, 20).WithSpaceAfter(10);
        var subheading = new ParagraphStyle(bold, 14).WithSpaceBefore(8).WithSpaceAfter(6);
        var small = new TextStyle(font, 8).WithColor(Gray(0.35f));
        string title = string.IsNullOrWhiteSpace(request.Title) ? "Untitled" : request.Title.Trim();

        var template = new PageTemplate(PageSetup.A4)
        {
            Header = new HeaderFooter(small) { Left = title, Right = request.Author },
            Footer = new HeaderFooter(small) { Center = "Page {page}" },
        };
        var flow = new FlowDocument(template)
        {
            Tagging = FlowTagging.Default.WithLanguage("en-US").WithTitle(title)
                .WithNamespace(pdf20 ? FlowStructureNamespace.Pdf20 : FlowStructureNamespace.Pdf17),
        };

        flow.Add(new Paragraph(title, heading) { StructureType = "H1" });
        foreach (string paragraph in request.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            flow.Add(new Paragraph(paragraph, body));
        }

        flow.Add(new Paragraph("Highlights", subheading) { StructureType = "H2" });
        flow.Add(new ListBlock(new ListStyle(new TextStyle(font, 11)))
            .Add(new ListItem("Fully managed: no native code, so it runs in WebAssembly as it runs on a server.", body))
            .Add(new ListItem("Reads, renders, edits and writes PDF 2.0.", body))
            .Add(new ListItem("Accessible and archival output by construction.", body)));

        if (request.IncludeTable)
        {
            flow.Add(new Paragraph("Results by region", subheading) { StructureType = "H2" });
            var table = new Table(ColumnWidth.Proportional(2), ColumnWidth.Proportional(1), ColumnWidth.Proportional(1))
            {
                HeaderRows = 1,
                Style = TableStyle.Default.WithBorder(new TableBorder(0.5, Gray(0.6f))).WithHeaderBackground(new PdfColor(PdfDeviceRgbColorSpace.Instance, [0.88f, 0.92f, 0.97f]))
                    .WithPadding(new CellPadding(4)).WithRepeatHeaderRows(true),
            };
            var cell = new ParagraphStyle(font, 10);
            var head = new ParagraphStyle(bold, 10);
            table.AddRow(new TableCell("Region", head), new TableCell("Q3", head), new TableCell("Q4", head));
            foreach ((string region, int q3, int q4) in new[] { ("North", 120, 135), ("South", 98, 104), ("East", 143, 151), ("West", 87, 112) })
            {
                table.AddRow(new TableCell(region, cell), new TableCell(q3.ToString(System.Globalization.CultureInfo.InvariantCulture), cell),
                    new TableCell(q4.ToString(System.Globalization.CultureInfo.InvariantCulture), cell));
            }

            flow.Add(table);
        }

        if (request.IncludeFigure)
        {
            flow.Add(new Paragraph("Figure", subheading) { StructureType = "H2" });
            flow.Add(new ImageBlock(Gradient(document), 240, 120) { AlternateText = "A colour gradient from blue to orange." });
            flow.Add(new Paragraph("Figure 1. A gradient drawn as an image.", new ParagraphStyle(font, 9)) { StructureType = "Caption" });
        }

        flow.Add(new Paragraph(
            [new TextRun("Learn more at "), new TextRun("github.com/michaelstonis/Broadside") { Link = new Uri("https://github.com/michaelstonis/Broadside") }, new TextRun(".")],
            body));

        FlowLayoutResult layout = flow.Render(document);
        int before = document.Diagnostics.Count;
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithPdfAConformance(request.Archival).WithPdfUAConformance(request.Accessible));
        var reported = new List<Diagnostic>(layout.Diagnostics);
        reported.AddRange(document.Diagnostics.Skip(before));
        return new CreateResult(stream.ToArray(), document.Pages.Count, reported);
    }

    private static PdfColor Gray(float level) => new(PdfDeviceRgbColorSpace.Instance, [level, level, level]);

    private static PdfImage Gradient(PdfDocument document)
    {
        const int width = 96;
        const int height = 48;
        byte[] samples = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double t = x / (double)(width - 1);
                int at = ((y * width) + x) * 3;
                samples[at] = (byte)(40 + (215 * t));
                samples[at + 1] = (byte)(90 + (60 * t) - (30 * y / (double)height));
                samples[at + 2] = (byte)(200 - (170 * t));
            }
        }

        return document.CreateImage(samples, new PdfImageLayout(width, height, PdfDeviceRgbColorSpace.Instance), PdfImageCompression.Flate);
    }
}
