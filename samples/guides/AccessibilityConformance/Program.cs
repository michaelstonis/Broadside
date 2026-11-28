using Broadside;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout;
using Broadside.Layout.Flow;
using Broadside.Samples;

Sample.Start("Accessibility and conformance");

WriteAccessibleReport();
TagAnExistingFile();
ValidateAnExistingFile();
WriteArchivalFile();
AttachAnInvoice();
ConvertToPdfA();
Sample.Finish();

static void WriteAccessibleReport()
{
    #region pdfua-flow
    using PdfDocument document = PdfDocument.Create();
    document.Pages.RemoveAt(0);
    PdfCanvasFont regular = document.EmbedFont(File.ReadAllBytes("LiberationSans-Regular.ttf"));
    PdfCanvasFont bold = document.EmbedFont(File.ReadAllBytes("LiberationSans-Bold.ttf"));
    var body = new ParagraphStyle(regular, 11);

    // Flow layout tags what it lays out: headings, paragraphs, lists, tables and figures, with the page furniture as artifacts.
    // PDF/UA-1 is a PDF 1.7 profile, so the structure uses the PDF 1.7 namespace; PDF/UA-2 uses the default, PDF 2.0.
    FlowTagging tagging = FlowTagging.Default
        .WithLanguage("en-US")
        .WithTitle("Annual results")
        .WithNamespace(FlowStructureNamespace.Pdf17);
    var flow = new FlowDocument(PageSetup.A4) { Tagging = tagging };
    flow.Add(new Paragraph("Annual results", new ParagraphStyle(bold, 18)) { StructureType = "H1" });
    flow.Add(new Paragraph("Revenue grew in every region.", body));
    flow.Add(new ImageBlock(Chart(document), 120, 60) { AlternateText = "Revenue by region, rising in all four." });
    flow.Render(document);

    document.Save("annual-results-pdfua1.pdf", new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));
    #endregion

    Sample.Expect(!Problems(document).Any(), $"PDF/UA-1 by the library's check: {string.Join("; ", Problems(document))}");
}

static void TagAnExistingFile()
{
    #region auto-tag
    // An untagged file: the layout tree finds its columns, paragraphs, headings, lists, tables and figures, and tags them.
    PdfOptions options = new PdfOptions().UseStandard14Fonts(); // metrics and programs for fonts the file does not embed
    using PdfDocument document = PdfDocument.Open("layout-two-column-untagged.pdf", options);
    AutoTaggingResult result = document.AutoTag(new AutoTaggingOptions().WithLanguage("en-US"));
    document.Properties.Title = "Two columns";
    Console.WriteLine($"Tagged: {result.IsTagged}, figures needing alternate text: {result.Figures.Count}");

    // PDF/UA needs every font embedded, which the PDF/A conversion does; the file then claims both.
    PdfAConversion conversion = document.ConvertToPdfA(PdfAConformance.PdfA2u);
    document.Save("two-columns-pdfua1-pdfa2u.pdf", conversion.CreateSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));
    #endregion

    Sample.Expect(result.IsTagged && conversion.IsConformant, "tagged and converted");
    Sample.Expect(!Problems(document).Any(), $"PDF/UA-1 and PDF/A-2u by the library's check: {string.Join("; ", Problems(document))}");
}

static void ValidateAnExistingFile()
{
    #region pdfua-validate
    // Checks a file as it is, without saving it: the level comes from its pdfuaid claim, or pass one.
    using PdfDocument document = PdfDocument.Open("annual-results-pdfua1.pdf");
    PdfUAValidation validation = document.ValidatePdfUA();
    Console.WriteLine($"{validation.Conformance}: claimed {validation.IsClaimed}, {validation.Violations.Count} violations");
    foreach (PdfUARuleViolation violation in validation.Violations)
    {
        Console.WriteLine($"  rule {violation.Rule} (Matterhorn {violation.Checkpoint}): {violation.Message}");
    }

    // Machine checks are necessary, not sufficient: these need a person, and these are not checked yet.
    Console.WriteLine($"For manual review: {string.Join(", ", validation.MachineUncheckable)}");
    Console.WriteLine($"Not checked yet: {string.Join(", ", validation.CheckpointsNotChecked)}");
    #endregion

    Sample.Expect(validation.IsClaimed && validation.IsConformant, $"PDF/UA-1 by validation: {string.Join("; ", validation.Violations.Select(v => v.Message))}");
}

static void WriteArchivalFile()
{
    #region pdfa-save
    using PdfDocument document = PdfDocument.Create();
    PdfCanvasFont sans = document.EmbedFont(File.ReadAllBytes("LiberationSans-Regular.ttf")); // PDF/A embeds every font
    using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
    {
        canvas.BeginText();
        canvas.SetFont(sans, 14);
        canvas.MoveText(72, 720);
        canvas.ShowText("Kept for the long term.");
        canvas.EndText();
    }

    document.Properties.Title = "Archive copy";
    document.Save("archive-pdfa2b.pdf", new PdfSaveOptions().WithPdfAConformance(PdfAConformance.PdfA2b));

    foreach (Diagnostic diagnostic in document.Diagnostics.Where(d => d.Code == "PdfANonConformant"))
    {
        Console.WriteLine($"Not PDF/A: {diagnostic.Message}"); // the save checks the rules it can and says what fails
    }
    #endregion

    Sample.Expect(!Problems(document).Any(), $"PDF/A-2b by the library's check: {string.Join("; ", Problems(document))}");
}

static void AttachAnInvoice()
{
    File.WriteAllText("factur-x.xml", """
        <?xml version="1.0" encoding="UTF-8"?>
        <rsm:CrossIndustryInvoice xmlns:rsm="urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100"
            xmlns:ram="urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100">
          <rsm:ExchangedDocument><ram:ID>INV-42</ram:ID><ram:TypeCode>380</ram:TypeCode></rsm:ExchangedDocument>
        </rsm:CrossIndustryInvoice>
        """);

    #region einvoice
    using PdfDocument document = PdfDocument.Create();
    PdfCanvasFont sans = document.EmbedFont(File.ReadAllBytes("LiberationSans-Regular.ttf"));
    using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
    {
        canvas.BeginText();
        canvas.SetFont(sans, 14);
        canvas.MoveText(72, 720);
        canvas.ShowText("Invoice INV-42"); // the human-readable invoice
        canvas.EndText();
    }

    // The CII XML your invoicing system produced: embedded, associated with the document and described in the XMP packet.
    byte[] xml = File.ReadAllBytes("factur-x.xml");
    document.AttachInvoice(xml, new PdfInvoiceOptions().WithProfile(PdfInvoiceProfile.EN16931));
    document.Properties.Title = "Invoice INV-42";
    document.Save("invoice-pdfa3b.pdf", new PdfSaveOptions().WithPdfAConformance(PdfAConformance.PdfA3b));

    using PdfDocument received = PdfDocument.Open("invoice-pdfa3b.pdf");
    PdfInvoiceAttachment? invoice = received.ExtractInvoice();
    Console.WriteLine($"{invoice?.File.Name}: profile {invoice?.Profile}, {invoice?.GetXml().Length} bytes");
    #endregion

    Sample.Expect(!Problems(document).Any() && invoice?.Profile == PdfInvoiceProfile.EN16931, $"a PDF/A-3b e-invoice: {string.Join("; ", Problems(document))}");
}

static void ConvertToPdfA()
{
    #region pdfa-convert
    using PdfDocument document = PdfDocument.Open("text-standard14.pdf", new PdfOptions().UseStandard14Fonts());
    PdfAConversion conversion = document.ConvertToPdfA(PdfAConformance.PdfA2b);
    foreach (string change in conversion.Changes)
    {
        Console.WriteLine($"Changed: {change}");
    }

    foreach (string problem in conversion.NotConverted)
    {
        Console.WriteLine($"Could not convert: {problem}");
    }

    if (conversion.IsConformant)
    {
        document.Save("text-standard14-pdfa2b.pdf", conversion.CreateSaveOptions());
    }
    #endregion

    Sample.Expect(conversion.IsConformant, "the conversion succeeds");
    Sample.Expect(!Problems(document).Any(), $"PDF/A-2b by the library's check: {string.Join("; ", Problems(document))}");
}

// What a PDF/A or PDF/UA save reports when the file does not conform.
static IEnumerable<string> Problems(PdfDocument document) =>
    document.Diagnostics.Where(d => d.Code is "PdfANonConformant" or "PdfUANonConformant" or "PdfUANotIdentified" or "InvoiceNotPdfA3").Select(d => d.Message);

// A small chart image for the report.
static Broadside.Images.PdfImage Chart(PdfDocument document) => document.CreateImage(
    [40, 80, 120, 160, 200, 240, 200, 160],
    new Broadside.Images.PdfImageLayout(4, 2, PdfDeviceGrayColorSpace.Instance),
    Broadside.Images.PdfImageCompression.Flate);
