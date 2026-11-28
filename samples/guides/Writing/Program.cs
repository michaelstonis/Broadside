using Broadside;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Layout.Flow;
using Broadside.Samples;

Sample.Start("Writing");

DrawPaths();
DrawText();
EmbedAFont();
DrawImages();
ScanToPdf();
DrawTransparency();
FlowAReport();
Sample.Finish();

static void DrawPaths()
{
    #region paths
    using PdfDocument document = PdfDocument.Create(); // one empty US Letter page, 612 x 792 points, origin at the bottom left
    using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
    {
        canvas.SetStrokeColor(PdfColor.FromRgb(0.1f, 0.3f, 0.7f));
        canvas.SetLineWidth(4);
        canvas.SetLineJoin(LineJoin.Round);
        canvas.MoveTo(72, 600);
        canvas.LineTo(172, 700);
        canvas.LineTo(272, 600);
        canvas.CurveTo(300, 560, 340, 560, 372, 600);
        canvas.Stroke();

        canvas.SaveState();
        canvas.Translate(400, 600);
        canvas.Rotate(15);
        canvas.SetFillColor(PdfColor.FromHex("#E8A33D"));
        canvas.Rectangle(0, 0, 120, 80);
        canvas.Fill();
        canvas.RestoreState();
    }

    document.Save("paths.pdf");
    #endregion
}

static void DrawText()
{
    #region text
    using PdfDocument document = PdfDocument.Create();
    PdfCanvasFont helvetica = document.GetStandard14Font(Standard14Font.Helvetica);
    PdfCanvasFont bold = document.GetStandard14Font(Standard14Font.HelveticaBold);
    using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
    {
        canvas.BeginText();
        canvas.SetFont(bold, 20);
        canvas.MoveText(72, 720);
        canvas.ShowText("Quarterly report");
        canvas.SetFont(helvetica, 11);
        canvas.SetTextLeading(14);
        canvas.MoveText(0, -28);
        canvas.ShowText("A line of text in a Standard 14 font, which every reader has.");
        canvas.ShowTextOnNextLine("The next line starts one leading below.");
        canvas.EndText();
    }

    document.Save("text.pdf");
    #endregion

    using PdfDocument saved = PdfDocument.Open("text.pdf");
    Sample.Expect(saved.Pages[0].ExtractText().Text.Contains("Quarterly report", StringComparison.Ordinal), "the text reads back");
}

static void EmbedAFont()
{
    #region fonts
    using PdfDocument document = PdfDocument.Create();
    byte[] program = File.ReadAllBytes("LiberationSans-Regular.ttf");
    PdfCanvasFont sans = document.EmbedFont(program); // TrueType, OpenType or Type 1; a save keeps only the glyphs shown
    using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
    {
        canvas.BeginText();
        canvas.SetFont(sans, 14);
        canvas.MoveText(72, 720);
        canvas.ShowText("Embedded and subset: déjà vu, Ωμέγα, Ж", kerning: true);
        canvas.EndText();
    }

    document.Save("fonts.pdf");
    #endregion

    long whole = new FileInfo("LiberationSans-Regular.ttf").Length;
    long saved = new FileInfo("fonts.pdf").Length;
    Console.WriteLine($"The font program is {whole} bytes; the subset document is {saved} bytes");
    Sample.Expect(saved < whole / 4, "the font is subset");
    using PdfDocument reopened = PdfDocument.Open("fonts.pdf");
    Sample.Expect(reopened.Pages[0].ExtractText().Text.Contains("Ωμέγα", StringComparison.Ordinal), "the embedded text reads back");
}

static void DrawImages()
{
    #region images
    using PdfDocument document = PdfDocument.Create();
    PdfImage logo = document.CreateImageFromPng(File.ReadAllBytes("logo.png")); // CreateImageFromJpeg keeps JPEG data as it is

    // Raw samples: a 2 x 2 grey checkerboard, Flate-compressed.
    PdfImage checker = document.CreateImage(
        [0, 255, 255, 0],
        new PdfImageLayout(2, 2, PdfDeviceGrayColorSpace.Instance),
        PdfImageCompression.Flate);

    using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
    {
        canvas.DrawImage(logo, 72, 620, 144, 144 * logo.Height / logo.Width); // x, y, width, height in points
        canvas.DrawImage(checker, 300, 620, 100, 100);
    }

    document.Save("images.pdf");
    #endregion

    using PdfDocument saved = PdfDocument.Open("images.pdf");
    Sample.Expect(saved.Pages[0].ExtractImages().Count == 2, "two images on the page");
}

static void ScanToPdf()
{
    #region scans
    using PdfDocument document = PdfDocument.Create();

    // One page per TIFF page, sized by its resolution; CCITT G4 and JPEG data are kept as they are.
    document.AppendImagePages(File.ReadAllBytes("scans.tif"));

    // Or make the images and place them yourself: CreateImages reads JPEG, PNG, TIFF, GIF and BMP.
    IReadOnlyList<PdfImage> pages = document.CreateImagesFromTiff(File.ReadAllBytes("scans.tif"));
    using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
    {
        canvas.DrawImage(pages[0], 72, 72, 200, 200 * pages[0].Height / pages[0].Width);
    }

    document.Save("scans.pdf");
    #endregion

    using PdfDocument saved = PdfDocument.Open("scans.pdf");
    Sample.Expect(saved.Pages.Count == 4, "a blank first page and three scanned pages");
}

static void DrawTransparency()
{
    #region transparency
    using PdfDocument document = PdfDocument.Create();
    using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
    {
        PdfShading gradient = document.CreateAxialShading(
            new PathPoint(72, 0),
            new PathPoint(540, 0),
            [new PdfGradientStop(0, PdfColor.FromRgb(0.1f, 0.3f, 0.7f)), new PdfGradientStop(1, PdfColor.FromRgb(0.9f, 0.6f, 0.2f))]);
        canvas.SaveState();
        canvas.Rectangle(72, 560, 468, 160);
        canvas.Clip();
        canvas.EndPath();
        canvas.PaintShading(gradient);
        canvas.RestoreState();

        canvas.SetFillAlpha(0.5);               // 50 % opaque
        canvas.SetBlendMode(BlendMode.Multiply);
        canvas.SetFillColor(PdfColor.FromRgb(0.8f, 0.1f, 0.1f));
        canvas.Rectangle(150, 600, 120, 160);
        canvas.Fill();
    }

    document.Save("transparency.pdf");
    #endregion

    using PdfDocument saved = PdfDocument.Open("transparency.pdf");
    Sample.Expect(saved.Version >= new PdfVersion(1, 4), "transparency raises the version to PDF 1.4");
}

static void FlowAReport()
{
    #region flow
    using PdfDocument document = PdfDocument.Create();
    document.Pages.RemoveAt(0); // flow layout adds the pages it needs

    PdfCanvasFont regular = document.EmbedFont(File.ReadAllBytes("LiberationSans-Regular.ttf"));
    PdfCanvasFont bold = document.EmbedFont(File.ReadAllBytes("LiberationSans-Bold.ttf"));
    var body = new ParagraphStyle(regular, 10.5).WithSpaceAfter(6);
    var heading = new ParagraphStyle(bold, 16).WithSpaceAfter(8);
    var small = new TextStyle(regular, 8);

    var template = new PageTemplate(PageSetup.A4)
    {
        Header = new HeaderFooter(small) { Left = "Field report", Right = "Acme Instruments" },
        Footer = new HeaderFooter(small) { Center = "Page {page} of {pages}" },
    };
    var flow = new FlowDocument(template) { Tagging = FlowTagging.Default.WithLanguage("en-US").WithTitle("Field report") };

    flow.Add(new Paragraph("Field report", heading) { StructureType = "H1" });
    flow.Add(new Paragraph(
        "Paragraphs wrap to the page width and break across pages. Text runs can change the style mid-paragraph.",
        body.WithAlignment(TextAlignment.Justify)));

    flow.Add(new ListBlock(new ListStyle(new TextStyle(regular, 10.5)).WithNumbering(ListNumbering.Decimal))
        .Add(new ListItem("Calibrate the sensors.", body))
        .Add(new ListItem("Record the readings.", body)));

    var table = new Table(ColumnWidth.Proportional(2), ColumnWidth.Fixed(80), ColumnWidth.Fixed(80)) { HeaderRows = 1 }; // header rows repeat
    table.AddRow(new TableCell("Region", body), new TableCell("Q3", body), new TableCell("Q4", body));
    table.AddRow(new TableCell("North", body), new TableCell("120", body), new TableCell("135", body));
    table.AddRow(new TableCell("South", body), new TableCell("98", body), new TableCell("104", body));
    flow.Add(table);

    FlowLayoutResult result = flow.Render(document);
    document.Save("report.pdf");
    Console.WriteLine($"Laid out {result.Pages.Count} page(s) with {result.Diagnostics.Count} diagnostic(s)");
    #endregion

    Sample.Expect(result.Pages.Count == 1, "one page");
    using PdfDocument saved = PdfDocument.Open("report.pdf");
    Sample.Expect(saved.StructureTree is not null, "the report is tagged");
    Sample.Expect(saved.Pages[0].ExtractText().Text.Contains("Calibrate the sensors", StringComparison.Ordinal), "the list reads back");
}
