using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Signatures;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// A grounds maintenance agreement between two invented organizations, signed twice in three revisions after the original:
/// the provider certifies it (a certification signature that allows form filling and annotations), the client fills in its
/// name and date and signs its own field (an approval signature), and the client's records office then adds a note (an
/// allowed change after signing). The certificates are the throwaway test PKI in contract/test-pki/.
/// </summary>
internal static class Contract
{
    private static readonly string[] Clauses =
    [
        "1. Services. The Provider will maintain the grounds of the Client's properties listed in Schedule 1: mowing, edging, " +
        "hedge cutting, leaf clearance, the care of shrub beds and the removal of litter and fly-tipped waste found on the grounds. " +
        "The visit frequency for each task is in Schedule 2.",
        "2. Term. The agreement runs for three years from 1 April 2027. Either party may end it at the end of the second year by " +
        "giving the other three months' notice in writing.",
        "3. Price. The Client will pay the annual price in Schedule 3 in twelve equal monthly instalments, invoiced in arrears. " +
        "The price is fixed for the first year and then changes each April by the published index named in Schedule 3.",
        "4. Standards. The Provider will carry out the services in a good and workmanlike manner, using trained staff and " +
        "well-maintained equipment, and will meet the standards in Schedule 2. The Client may inspect the work at any time.",
        "5. Defects. If work does not meet the standards, the Client may give the Provider a defect notice. The Provider will put " +
        "the defect right within five working days of the notice at no extra cost.",
        "6. Health and safety. The Provider is responsible for the safety of its staff and of anyone affected by its work, will " +
        "keep risk assessments for every task and will give copies to the Client on request.",
        "7. Residents. The Provider's staff will be courteous to residents, wear identification and avoid noisy work before " +
        "8 a.m. and after 6 p.m. Complaints from residents will be passed to the Client within one working day.",
        "8. Insurance. The Provider will hold public liability insurance of at least $5,000,000 for any one event and employer's " +
        "liability insurance as the law requires, and will show the Client the certificates each year.",
        "9. Subcontracting. The Provider will not subcontract any part of the services without the Client's written consent, " +
        "which the Client will not withhold unreasonably.",
        "10. Termination. Either party may end the agreement at once by notice if the other commits a material breach that it " +
        "does not remedy within thirty days of being asked to in writing.",
        "11. Notices. Notices are given in writing to the addresses on the last page, or to any other address a party gives by notice.",
        "12. Entire agreement. This agreement and its schedules are the whole agreement between the parties about the services. " +
        "Changes are valid only when made in writing and signed by both parties.",
    ];

    private static readonly (string Task, string Frequency)[] Schedule =
    [
        ("Mowing of lawns and verges", "Every 14 days, March to October"),
        ("Edging of lawns against paths", "Monthly, March to October"),
        ("Hedge cutting", "Twice a year, June and October"),
        ("Leaf clearance", "Weekly, October to December"),
        ("Shrub bed weeding and mulching", "Monthly, all year"),
        ("Litter picking", "Weekly, all year"),
        ("Removal of fly-tipped waste", "Within two working days of a report"),
        ("Winter gritting of paths", "When frost is forecast, November to March"),
    ];

    public static void Write(GeneratorContext context)
    {
        TestPki pki = TestPki.Load(context);
        string original = context.OutputPath(".contract-unsigned.pdf");
        WriteUnsigned(context, original);

        // Revision 2: the provider certifies the agreement and signs its field.
        byte[] certified;
        using (PdfDocument document = PdfDocument.Open(File.ReadAllBytes(original)))
        using (var output = new MemoryStream())
        {
            document.Sign(output, new PdfCertificateSigner(pki.Provider, [pki.Root]), new PdfSignatureOptions()
                .WithFieldName("provider.signature")
                .WithCertification(PdfCertificationPermissions.FormFillingAndAnnotations)
                .WithSignerName("R. Okafor, Ashcombe Grounds Ltd")
                .WithReason("I am the author of this agreement")
                .WithLocation("Ashcombe")
                .WithSigningTime(new DateTimeOffset(2027, 3, 8, 10, 15, 0, TimeSpan.Zero))
                .WithAppearance(new PdfSignatureAppearance(2, ProviderBox)));
            certified = output.ToArray();
        }

        // Revision 3: the client fills in its name and the date, then signs its own field.
        byte[] countersigned;
        using (PdfDocument document = PdfDocument.Open(certified))
        using (var output = new MemoryStream())
        {
            ((PdfTextField)document.AcroForm!.FindField("client.name")!).SetValue("M. Delgado, Director of Housing");
            ((PdfTextField)document.AcroForm!.FindField("client.date")!).SetValue("03/12/2027");
            document.Sign(output, new PdfCertificateSigner(pki.Client, [pki.Root]), new PdfSignatureOptions()
                .WithFieldName("client.signature")
                .WithSignerName("M. Delgado, Linden Housing Association")
                .WithReason("I agree to the terms of this agreement")
                .WithLocation("Ashcombe")
                .WithSigningTime(new DateTimeOffset(2027, 3, 12, 14, 40, 0, TimeSpan.Zero))
                .WithAppearance(new PdfSignatureAppearance(2, ClientBox)));
            countersigned = output.ToArray();
        }

        // Revision 4: a note from the records office, a change the certification allows.
        using (PdfDocument document = PdfDocument.Open(countersigned))
        {
            PdfPage last = document.Pages[2];
            last.AddTextAnnotation(new PdfRectangle(Mm(186), Mm(250), Mm(194), Mm(258)),
                "Countersigned copy filed in the contracts register as LHA/C/2027/014. Next review: January 2029.",
                new PdfAnnotationOptions().WithTitle("Records office").WithColor(new PdfDeviceColor(1, 0.85, 0.2)));
            using FileStream output = File.Create(context.OutputPath("contract-signed.pdf"));
            document.Save(output, new PdfSaveOptions().WithIncrementalUpdate(true).WithUpdateModificationDate(false));
        }

        File.Delete(original);
    }

    private static PdfRectangle ProviderBox => new(Mm(22), Mm(150), Mm(100), Mm(178));

    private static PdfRectangle ClientBox => new(Mm(110), Mm(150), Mm(188), Mm(178));

    private static void WriteUnsigned(GeneratorContext context, string path)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        SetProperties(document, "Grounds maintenance agreement", "A services agreement certified by one party and signed by the other.");

        PdfCanvasFont serif = context.Font(document, "ptserif/PT_Serif-Web-Regular.ttf");
        PdfCanvasFont serifBold = context.Font(document, "ptserif/PT_Serif-Web-Bold.ttf");
        PdfCanvasFont sans = context.Font(document, "ptsans/PT_Sans-Web-Regular.ttf");
        PdfCanvasFont sansBold = context.Font(document, "ptsans/PT_Sans-Web-Bold.ttf");
        PdfColor ink = Rgb(0.1, 0.12, 0.16);
        PdfColor muted = Rgb(0.4, 0.43, 0.48);

        var body = new ParagraphStyle(serif, 10).WithLineHeight(1.45).WithSpaceAfter(6);
        var title = new ParagraphStyle(sansBold, 20).WithSpaceAfter(4);
        var subtitle = new ParagraphStyle(sans, 10.5).WithSpaceAfter(16);
        var heading = new ParagraphStyle(sansBold, 11.5).WithSpaceBefore(10).WithSpaceAfter(6);
        var cell = new ParagraphStyle(serif, 9).WithLineHeight(1.3);
        var head = new ParagraphStyle(sansBold, 9);
        var footer = new TextStyle(sans, 7.5).WithColor(muted);

        var template = new PageTemplate(new PageSetup(Mm(210), Mm(297), new PageMargins { Left = Mm(25), Right = Mm(25), Top = Mm(25), Bottom = Mm(25) }))
        {
            Footer = new HeaderFooter(footer) { Left = "Grounds maintenance agreement · invented parties, for a sample document", Right = "Page {page} of 3" },
        };
        var flow = new FlowDocument(template) { Tagging = FlowTagging.Default.WithLanguage("en-US").WithTitle("Grounds maintenance agreement") };
        flow.Add(new Paragraph("Grounds Maintenance Agreement", title) { StructureType = "H1" });
        flow.Add(new Paragraph([new TextRun("between Ashcombe Grounds Ltd (the Provider) and Linden Housing Association (the Client)", new TextStyle(sans, 10.5).WithColor(muted))], subtitle));
        flow.Add(new Paragraph("Terms", heading) { StructureType = "H2" });
        foreach (string clause in Clauses)
        {
            flow.Add(new Paragraph(clause, body));
        }

        flow.Add(new Paragraph("Schedule 2: Tasks and frequencies", heading) { StructureType = "H2" });
        var table = new Table(ColumnWidth.Proportional(1.2), ColumnWidth.Proportional(1))
        {
            HeaderRows = 1,
            Style = TableStyle.Default.WithBorder(new TableBorder(0.4, Gray(0.7))).WithHeaderBackground(Rgb(0.9, 0.93, 0.9)).WithPadding(new CellPadding(4)),
        };
        table.AddRow(new TableCell("Task", head), new TableCell("Frequency", head));
        foreach ((string task, string frequency) in Schedule)
        {
            table.AddRow(new TableCell(task, cell), new TableCell(frequency, cell));
        }

        flow.Add(table);
        flow.Render(document);
        while (document.Pages.Count < 2)
        {
            document.Pages.Add(new PdfRectangle(0, 0, Mm(210), Mm(297)));
        }

        // The execution page: two signature blocks drawn here, with the form fields on top of them.
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, Mm(210), Mm(297)));
        using (PdfCanvas canvas = page.CreateCanvas())
        {
            canvas.SetFillColor(ink);
            Text(canvas, sansBold, 16, Mm(22), Mm(270), "Execution");
            canvas.SetFillColor(muted);
            Text(canvas, serif, 10, Mm(22), Mm(260), "Signed by the authorized representatives of the parties on the dates shown.");
            Text(canvas, sans, 7.5, Mm(22), Mm(18), "Grounds maintenance agreement · invented parties, for a sample document");
            Text(canvas, sans, 7.5, Mm(170), Mm(18), "Page 3 of 3");
            canvas.SetFillColor(ink);
            Text(canvas, sansBold, 10, Mm(22), Mm(232), "For the Provider");
            Text(canvas, serif, 9.5, Mm(22), Mm(225), "Ashcombe Grounds Ltd, 4 Mill Lane, Ashcombe");
            Text(canvas, serif, 9.5, Mm(22), Mm(218), "Name: R. Okafor, Managing Director");
            Text(canvas, serif, 9.5, Mm(22), Mm(211), "Date: 03/08/2027");
            Text(canvas, sansBold, 10, Mm(110), Mm(232), "For the Client");
            Text(canvas, serif, 9.5, Mm(110), Mm(225), "Linden Housing Association, 3 Station Approach");
            Text(canvas, serif, 9.5, Mm(110), Mm(218), "Name:");
            Text(canvas, serif, 9.5, Mm(110), Mm(208), "Date:");
            canvas.SetStrokeColor(Gray(0.6));
            canvas.SetLineWidth(0.5);
            foreach (PdfRectangle box in new[] { ProviderBox, ClientBox })
            {
                canvas.Rectangle(box.Left, box.Bottom, box.Width, box.Height);
                canvas.Stroke();
            }

            canvas.SetFillColor(muted);
            Text(canvas, serif, 8, Mm(22), Mm(144), "Signature of the Provider (certifies the document)");
            Text(canvas, serif, 8, Mm(110), Mm(144), "Signature of the Client");
        }

        PdfAcroForm form = document.CreateAcroForm();
        var options = new PdfFieldOptions().WithFontSize(9.5).WithBorderColor(new PdfDeviceColor(0.6, 0.6, 0.6)).WithBorderWidth(0.5);
        form.AddTextField("client.name", page, new PdfRectangle(Mm(122), Mm(216), Mm(188), Mm(222)), options.WithAlternateName("Name and title of the Client's signatory"));
        form.AddTextField("client.date", page, new PdfRectangle(Mm(122), Mm(206), Mm(160), Mm(212)), options.WithAlternateName("Date the Client signs (MM/DD/YYYY)").WithMaxLength(10));
        form.AddSignatureField("provider.signature", page, ProviderBox, new PdfFieldOptions().WithAlternateName("Provider's certification signature"));
        form.AddSignatureField("client.signature", page, ClientBox, new PdfFieldOptions().WithAlternateName("Client's signature"));
        document.Save(path);
    }

    private static void Text(PdfCanvas canvas, PdfCanvasFont font, double size, double x, double y, string text)
    {
        canvas.BeginText();
        canvas.SetFont(font, size);
        canvas.MoveText(x, y);
        canvas.ShowText(text);
        canvas.EndText();
    }
}
