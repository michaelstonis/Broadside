using System.Globalization;
using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// The conservative invoice: a surveyors' practice billing a housing association. Laid out by the flow layout package (letterhead
/// drawn with PdfCanvas, a long table that repeats its header row across pages, IBM Plex Serif and Mono embedded as subset TrueType),
/// followed by a remittance slip with AcroForm fields. A second file fills those fields in an incremental update.
/// </summary>
internal static class InvoiceConservative
{
    private const string Number = "HF/27/0318";

    // Every name, address, rate and number here is invented for the Broadside sample documents.
    private static readonly (string Date, string Description, double Hours, double Rate)[] Items =
    [
        ("06/01", "Initial meeting with the association's asset team; review of stock list", 2.5, 95),
        ("07/01", "Desk study: historic maps, planning history and building control records", 4.0, 95),
        ("09/01", "Site inspection, Linden Court blocks A and B (external envelope)", 6.5, 110),
        ("10/01", "Site inspection, Linden Court blocks C and D (external envelope)", 6.0, 110),
        ("13/01", "Roof access survey with rope access contractor (supervision)", 5.0, 110),
        ("14/01", "Moisture readings and thermal imaging, ground-floor flats", 4.5, 110),
        ("16/01", "Analysis of survey data; defect schedule, first draft", 7.0, 95),
        ("17/01", "Window condition survey, 112 openings", 6.0, 85),
        ("20/01", "Meeting with residents' committee", 2.0, 95),
        ("21/01", "Drainage CCTV survey review (contractor's footage)", 3.0, 95),
        ("23/01", "Structural observations: lintels and balcony slabs", 4.0, 120),
        ("24/01", "Photographic record, labelling and indexing (486 photographs)", 3.5, 65),
        ("27/01", "Condition report, sections 1 to 4", 7.5, 95),
        ("28/01", "Condition report, sections 5 to 8", 7.0, 95),
        ("30/01", "Budget estimates for the five-year maintenance plan", 6.0, 95),
        ("31/01", "Internal peer review of the report", 2.5, 120),
        ("03/02", "Revisions following peer review", 3.0, 95),
        ("04/02", "Presentation of findings to the association's board", 2.5, 120),
        ("06/02", "Specification of urgent works: balcony balustrades", 5.0, 95),
        ("07/02", "Specification of urgent works: roof outlets and gutters", 4.0, 95),
        ("10/02", "Tender list and invitation documents", 3.5, 85),
        ("12/02", "Responding to tenderers' queries", 2.0, 85),
        ("17/02", "Tender analysis and recommendation report", 5.5, 95),
        ("18/02", "Pre-contract meeting with the appointed contractor", 2.0, 95),
        ("20/02", "Party wall notices for the boundary with 14 Linden Road", 2.5, 95),
        ("24/02", "Site meeting, first week of works", 2.5, 110),
        ("27/02", "Monthly progress report", 2.0, 95),
    ];

    private static readonly (string Description, double Amount)[] Disbursements =
    [
        ("Rope access contractor (invoice RA-2207, at cost)", 1240.00),
        ("Drainage CCTV survey (invoice DS-0193, at cost)", 685.00),
        ("Printing and binding: board report, 12 copies", 96.40),
        ("Mileage: 412 miles at £0.45", 185.40),
    ];

    public static void Write(GeneratorContext context)
    {
        string blank = context.OutputPath("invoice-conservative.pdf");
        WriteInvoice(context, blank);
        Fill(blank, context.OutputPath("invoice-conservative-filled.pdf"));
    }

    private static void WriteInvoice(GeneratorContext context, string path)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        SetProperties(document, $"Invoice {Number}", "A conservative invoice laid out by Broadside's flow layout, with a fillable remittance slip.");

        PdfCanvasFont serif = context.Font(document, "ibmplexserif/IBMPlexSerif-Regular.ttf");
        PdfCanvasFont serifBold = context.Font(document, "ibmplexserif/IBMPlexSerif-Bold.ttf");
        PdfCanvasFont serifItalic = context.Font(document, "ibmplexserif/IBMPlexSerif-Italic.ttf");
        PdfCanvasFont mono = context.Font(document, "ibmplexmono/IBMPlexMono-Regular.ttf");
        PdfColor ink = Rgb(0.12, 0.16, 0.22);
        PdfColor muted = Rgb(0.42, 0.45, 0.5);
        PdfColor green = Rgb(0.1, 0.32, 0.25);

        var body = new ParagraphStyle(serif, 9.5).WithLineHeight(1.4).WithSpaceAfter(4);
        var small = new ParagraphStyle(serif, 8).WithLineHeight(1.35);
        var heading = new ParagraphStyle(serifBold, 12).WithSpaceBefore(10).WithSpaceAfter(6);
        var cell = new ParagraphStyle(serif, 8.5).WithLineHeight(1.3);
        var cellRight = new ParagraphStyle(mono, 8).WithAlignment(TextAlignment.Right);
        var head = new ParagraphStyle(serifBold, 8);
        var headRight = new ParagraphStyle(serifBold, 8).WithAlignment(TextAlignment.Right);
        var footer = new TextStyle(serif, 7).WithColor(muted);

        var page = new PageSetup(Mm(210), Mm(297), new PageMargins { Left = Mm(22), Right = Mm(22), Top = Mm(58), Bottom = Mm(24) });
        var template = new PageTemplate(page)
        {
            Header = new HeaderFooter(footer) { Left = "Harrow & Finch LLP", Right = $"Invoice {Number} (continued)" },
            FirstPageHeader = new HeaderFooter(footer),
            Footer = new HeaderFooter(footer)
            {
                Left = "Harrow & Finch LLP · Chartered Building Surveyors · an invented practice for a sample document",
                Right = "Page {page}",
            },
        };
        var flow = new FlowDocument(template)
        {
            Tagging = FlowTagging.Default.WithLanguage("en-GB").WithTitle($"Invoice {Number}"),
        };

        var addressTable = new Table(ColumnWidth.Proportional(1), ColumnWidth.Proportional(1))
        {
            Style = TableStyle.Default.WithBorder(new TableBorder(0, ink)).WithPadding(new CellPadding(0)),
            SpaceAfter = 14,
        };
        addressTable.AddRow(
            new TableCell("Invoice to\nThe Finance Manager\nLinden Housing Association\n3 Station Approach\nAshcombe AC4 9QT", body),
            new TableCell(string.Join('\n', new[] { ("Invoice number", Number), ("Invoice date", "28 February 2027"), ("Your reference", "LHA/PO/55120"), ("Our reference", "JF/LC/2027"), ("Payment terms", "30 days") }.Select(static r => r.Item1.PadRight(16) + r.Item2)), new ParagraphStyle(mono, 8.5).WithLineHeight(1.55)));
        flow.Add(addressTable);

        flow.Add(new Paragraph("Linden Court: condition survey and contract administration", heading) { StructureType = "H1" });
        flow.Add(new Paragraph(
            "Professional fees for the period 6 January to 27 February 2027, in accordance with our letter of appointment dated " +
            "2 December 2026. Time is charged in half-hour units at the rates agreed for each grade of staff. Disbursements are " +
            "charged at cost and listed separately.", body));

        var table = new Table(ColumnWidth.Fixed(Mm(15)), ColumnWidth.Proportional(1), ColumnWidth.Fixed(Mm(14)), ColumnWidth.Fixed(Mm(17)), ColumnWidth.Fixed(Mm(22)))
        {
            HeaderRows = 1,
            Style = TableStyle.Default.WithBorder(new TableBorder(0.4, Gray(0.75))).WithHeaderBackground(Rgb(0.92, 0.94, 0.93))
                .WithPadding(new CellPadding(3.5)).WithRepeatHeaderRows(true).WithKeepRowsTogether(true),
            SpaceBefore = 4,
            SpaceAfter = 10,
        };
        table.AddRow(new TableCell("Date", head), new TableCell("Description", head), new TableCell("Hours", headRight),
            new TableCell("Rate", headRight), new TableCell("Amount", headRight));
        double fees = 0;
        foreach ((string date, string description, double hours, double rate) in Items)
        {
            double amount = hours * rate;
            fees += amount;
            table.AddRow(new TableCell(date, new ParagraphStyle(mono, 8)), new TableCell(description, cell),
                new TableCell(hours.ToString("0.0", CultureInfo.InvariantCulture), cellRight),
                new TableCell(Pounds(rate, 0), cellRight), new TableCell(Pounds(amount), cellRight));
        }

        table.AddRow(new TableCell("", cell), new TableCell("Total professional fees", head), new TableCell("", cell), new TableCell("", cell),
            new TableCell(Pounds(fees), headRight));
        flow.Add(table);

        flow.Add(new Paragraph("Disbursements", heading) { StructureType = "H2" });
        var costs = new Table(ColumnWidth.Proportional(1), ColumnWidth.Fixed(Mm(22)))
        {
            HeaderRows = 1,
            Style = TableStyle.Default.WithBorder(new TableBorder(0.4, Gray(0.75))).WithHeaderBackground(Rgb(0.92, 0.94, 0.93)).WithPadding(new CellPadding(3.5)),
            SpaceAfter = 10,
        };
        costs.AddRow(new TableCell("Item", head), new TableCell("Amount", headRight));
        double disbursements = 0;
        foreach ((string description, double amount) in Disbursements)
        {
            disbursements += amount;
            costs.AddRow(new TableCell(description, cell), new TableCell(Pounds(amount), cellRight));
        }

        flow.Add(costs);

        double net = fees + disbursements;
        double vat = Math.Round(net * 0.2, 2);
        var totals = new Table(ColumnWidth.Proportional(1), ColumnWidth.Fixed(Mm(45)), ColumnWidth.Fixed(Mm(28)))
        {
            Style = TableStyle.Default.WithBorder(new TableBorder(0, ink)).WithPadding(new CellPadding(2.5)),
            SpaceAfter = 14,
        };
        totals.AddRow(new TableCell("", cell), new TableCell("Net total", cell), new TableCell(Pounds(net), cellRight));
        totals.AddRow(new TableCell("", cell), new TableCell("VAT at 20%", cell), new TableCell(Pounds(vat), cellRight));
        totals.AddRow(
            new TableCell("", cell),
            new TableCell("Amount due", new ParagraphStyle(serifBold, 10)) { Background = Rgb(0.92, 0.94, 0.93) },
            new TableCell(Pounds(net + vat), new ParagraphStyle(serifBold, 10).WithAlignment(TextAlignment.Right)) { Background = Rgb(0.92, 0.94, 0.93) });
        flow.Add(totals);

        flow.Add(new Paragraph("Payment", heading) { StructureType = "H2" });
        flow.Add(new Paragraph(
            "Please pay within 30 days of the invoice date by bank transfer, quoting the invoice number. The bank details on this " +
            "sample document are placeholders and must not be used. If you have a query about this invoice, please contact the " +
            "partner responsible within 14 days so that we can resolve it before payment falls due.", body));
        flow.Add(new Paragraph(
            [new TextRun("Bank: "), new TextRun("Sample Bank plc · sort code 00-00-00 · account 00000000", new TextStyle(mono, 8.5)),
             new TextRun(". Complete the remittance slip on the last page and send it with your payment.")],
            body));
        flow.Add(new Paragraph("J. Finch MRICS, Partner", new ParagraphStyle(serifItalic, 9.5).WithSpaceBefore(8)));
        flow.Add(new Paragraph("Harrow & Finch LLP is a fictional practice. All figures in this document are invented.", small));

        flow.Render(document);
        DrawLetterhead(document.Pages[0], serif, serifBold, ink, muted, green);
        AddRemittanceSlip(document, serif, serifBold, mono, ink, muted, green, net + vat);
        document.Save(path);
    }

    private static void DrawLetterhead(PdfPage page, PdfCanvasFont serif, PdfCanvasFont bold, PdfColor ink, PdfColor muted, PdfColor green)
    {
        double top = Mm(297) - Mm(18);
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.BeginArtifact();

        // The monogram: two interlocking rings and a keystone, drawn as paths.
        canvas.SaveState();
        canvas.Translate(Mm(22) + 20, top - 18);
        canvas.SetLineWidth(2.2);
        canvas.SetStrokeColor(green);
        Circle(canvas, -6, 0, 15);
        canvas.Stroke();
        canvas.SetStrokeColor(ink);
        Circle(canvas, 6, 0, 15);
        canvas.Stroke();
        canvas.SetFillColor(green);
        canvas.MoveTo(-4, 9);
        canvas.LineTo(4, 9);
        canvas.LineTo(2.5, -9);
        canvas.LineTo(-2.5, -9);
        canvas.ClosePath();
        canvas.Fill();
        canvas.RestoreState();

        canvas.BeginText();
        canvas.SetFillColor(ink);
        canvas.SetFont(bold, 17);
        canvas.MoveText(Mm(22) + 46, top - 14);
        canvas.ShowText("Harrow & Finch LLP");
        canvas.SetFont(serif, 8.5);
        canvas.SetFillColor(muted);
        canvas.MoveText(0, -13);
        canvas.ShowText("Chartered Building Surveyors · Project Managers");
        canvas.EndText();

        string[] address = ["17 Cornmarket Yard", "Ashcombe AC1 2BX", "accounts@harrowfinch.example", "VAT registration 000 0000 00"];
        canvas.SetFillColor(muted);
        double y = top - 4;
        foreach (string line in address)
        {
            double width = serif.MeasureWidth(line, 8);
            canvas.BeginText();
            canvas.SetFont(serif, 8);
            canvas.MoveText(Mm(210 - 22) - width, y);
            canvas.ShowText(line);
            canvas.EndText();
            y -= 10.5;
        }

        canvas.SetStrokeColor(green);
        canvas.SetLineWidth(0.8);
        canvas.MoveTo(Mm(22), top - 40);
        canvas.LineTo(Mm(210 - 22), top - 40);
        canvas.Stroke();

        canvas.BeginText();
        canvas.SetFont(bold, 22);
        canvas.SetFillColor(ink);
        canvas.MoveText(Mm(22), top - 66);
        canvas.ShowText("Invoice");
        canvas.EndText();
        canvas.EndMarkedContent();
    }

    private static void AddRemittanceSlip(PdfDocument document, PdfCanvasFont serif, PdfCanvasFont bold, PdfCanvasFont mono, PdfColor ink, PdfColor muted, PdfColor green, double due)
    {
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, Mm(210), Mm(297)));
        double left = Mm(22);
        double right = Mm(210 - 22);
        using (PdfCanvas canvas = page.CreateCanvas())
        {
            canvas.SetFillColor(ink);
            Text(canvas, bold, 16, left, Mm(297 - 30), "Remittance advice");
            canvas.SetFillColor(muted);
            Text(canvas, serif, 9, left, Mm(297 - 37), "Detach or print this page and send it with your payment, or fill it in on screen and email it to us.");

            // The dashed cut line with a pair of scissors drawn from two circles and two blades.
            canvas.SetStrokeColor(Gray(0.55));
            canvas.SetLineWidth(0.6);
            canvas.SetDashPattern([4, 3], 0);
            canvas.MoveTo(left + 16, Mm(297 - 46));
            canvas.LineTo(right, Mm(297 - 46));
            canvas.Stroke();
            canvas.SetDashPattern([], 0);
            Circle(canvas, left + 3, Mm(297 - 46) + 3, 2.4);
            Circle(canvas, left + 3, Mm(297 - 46) - 3, 2.4);
            canvas.MoveTo(left + 5, Mm(297 - 46) + 2);
            canvas.LineTo(left + 13, Mm(297 - 46) - 1.5);
            canvas.MoveTo(left + 5, Mm(297 - 46) - 2);
            canvas.LineTo(left + 13, Mm(297 - 46) + 1.5);
            canvas.Stroke();

            canvas.SetFillColor(ink);
            string[] labels = ["Invoice number", "Amount due", "Paid by (organisation)", "Amount paid", "Date paid", "Payment reference", "Method", "Notes"];
            double y = Mm(297 - 60);
            foreach (string label in labels)
            {
                Text(canvas, bold, 9, left, y, label);
                y -= label == "Method" ? Mm(14) : Mm(13);
            }

            canvas.SetFillColor(ink);
            Text(canvas, mono, 10, left + Mm(50), Mm(297 - 60), Number);
            Text(canvas, mono, 10, left + Mm(50), Mm(297 - 73), Pounds(due));
            Text(canvas, serif, 9, left + Mm(56), Mm(297 - 138) + 2, "Bank transfer");
            Text(canvas, serif, 9, left + Mm(96), Mm(297 - 138) + 2, "Cheque");

            canvas.SetStrokeColor(green);
            canvas.SetLineWidth(0.8);
            canvas.MoveTo(left, Mm(60));
            canvas.LineTo(right, Mm(60));
            canvas.Stroke();
            canvas.SetFillColor(muted);
            Text(canvas, serif, 8, left, Mm(54), "Harrow & Finch LLP · 17 Cornmarket Yard · Ashcombe AC1 2BX · an invented address for a sample document");
        }

        PdfAcroForm form = document.CreateAcroForm();
        var fieldBorder = new PdfDeviceColor(0.55, 0.6, 0.65);
        var fieldFill = new PdfDeviceColor(0.96, 0.97, 0.98);
        PdfFieldOptions Options() => new PdfFieldOptions().WithFontSize(10).WithBorderColor(fieldBorder).WithBackgroundColor(fieldFill).WithBorderWidth(0.8);
        double x = left + Mm(50);
        double width = right - x;
        PdfRectangle Box(double baselineMm, double heightMm = 8) => new(x, Mm(297 - baselineMm) - 6, x + width, Mm(297 - baselineMm) - 6 + Mm(heightMm));
        form.AddTextField("payer", page, Box(86), Options().WithAlternateName("Organisation making the payment"));
        form.AddTextField("amount", page, Box(99), Options().WithAlternateName("Amount paid in pounds").WithQuadding(PdfTextJustification.Right));
        form.AddTextField("date", page, Box(112), Options().WithAlternateName("Date of payment").WithMaxLength(10));
        form.AddTextField("reference", page, Box(125), Options().WithAlternateName("Payment reference"));
        form.AddCheckBox("method.transfer", page, new PdfRectangle(x, Mm(297 - 138) - 2, x + 12, Mm(297 - 138) + 10), Options().WithAlternateName("Paid by bank transfer"));
        form.AddCheckBox("method.cheque", page, new PdfRectangle(x + Mm(40), Mm(297 - 138) - 2, x + Mm(40) + 12, Mm(297 - 138) + 10), Options().WithAlternateName("Paid by cheque"));
        form.AddTextField("notes", page, new PdfRectangle(x, Mm(297 - 178), x + width, Mm(297 - 152) + 6),
            Options().WithAlternateName("Notes for the accounts team").WithFlags(PdfFieldFlags.Multiline));
    }

    private static void Fill(string blank, string filled)
    {
        using (PdfDocument document = PdfDocument.Open(File.ReadAllBytes(blank)))
        {
            PdfAcroForm form = document.AcroForm!;
            ((PdfTextField)form.FindField("payer")!).SetValue("Linden Housing Association");
            ((PdfTextField)form.FindField("amount")!).SetValue(Pounds(AmountDue()));
            ((PdfTextField)form.FindField("date")!).SetValue("24/03/2027");
            ((PdfTextField)form.FindField("reference")!).SetValue("LHA-BACS-004417");
            ((PdfCheckBoxField)form.FindField("method.transfer")!).SetChecked(true);
            ((PdfTextField)form.FindField("notes")!).SetValue("Paid in full. Please send the receipted invoice to the finance team, and copy the asset manager.");
            using FileStream output = File.Create(filled);
            document.Save(output, new PdfSaveOptions().WithIncrementalUpdate(true).WithUpdateModificationDate(false));
        }
    }

    private static double AmountDue()
    {
        double net = Items.Sum(static i => i.Hours * i.Rate) + Disbursements.Sum(static d => d.Amount);
        return net + Math.Round(net * 0.2, 2);
    }

    private static string Pounds(double amount, int decimals = 2) =>
        (amount < 0 ? "−£" : "£") + Math.Abs(amount).ToString(decimals == 0 ? "N0" : "N2", CultureInfo.InvariantCulture);

    private static void Text(PdfCanvas canvas, PdfCanvasFont font, double size, double x, double y, string text)
    {
        canvas.BeginText();
        canvas.SetFont(font, size);
        canvas.MoveText(x, y);
        canvas.ShowText(text);
        canvas.EndText();
    }

    private static void Circle(PdfCanvas canvas, double cx, double cy, double r)
    {
        const double k = 0.5523;
        canvas.MoveTo(cx + r, cy);
        canvas.CurveTo(cx + r, cy + (k * r), cx + (k * r), cy + r, cx, cy + r);
        canvas.CurveTo(cx - (k * r), cy + r, cx - r, cy + (k * r), cx - r, cy);
        canvas.CurveTo(cx - r, cy - (k * r), cx - (k * r), cy - r, cx, cy - r);
        canvas.CurveTo(cx + (k * r), cy - r, cx + r, cy - (k * r), cx + r, cy);
        canvas.ClosePath();
    }
}
