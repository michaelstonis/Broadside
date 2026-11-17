using System.Globalization;
using Broadside.Fonts;
using Broadside.Fonts.Standard14;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Layout.Flow.Units;

namespace Broadside.BuilderSamples;

/// <summary>
/// The samples of the document builder guide (issue #397, spec section 3), each a method that builds one document. Every one takes the
/// document's <see cref="PdfOptions"/>, so a test can fix the clock and get the same bytes each time.
/// </summary>
/// <remarks>
/// Amounts and dates are formatted with fixed patterns and the invariant culture, never the machine's, so the output does not depend on
/// where it runs; a real template would pass the reader's <see cref="CultureInfo"/> and format with it.
/// </remarks>
public static class Samples
{
    /// <summary>Gets the names of the samples, in the guide's order, with the method that builds each.</summary>
    public static IReadOnlyList<(string Name, Func<PdfOptions, ImageSource, PdfDocument> Build)> All { get; } =
    [
        ("hello", (options, _) => Hello(options)),
        ("field-report", (options, _) => CreateFieldReport(options, SampleData.Report)),
        ("results-table", (options, _) => ResultsTable(options, SampleData.Results)),
        ("images-and-drawing", (options, logo) => ImagesAndDrawing(options, logo, SampleData.Results)),
        ("guide", (options, _) => Guide(options)),
        ("form", (options, _) => LoanRequestForm(options)),
        ("invoice", (options, logo) => CreateInvoice(options, SampleData.Invoice, AcmeTheme.WithLogo(logo))),
        ("escape-hatches", (options, _) => EscapeHatches(options)),
    ];

    /// <summary>A one-page document: tagged, titled, in its language, with its font embedded (section 3.2).</summary>
    /// <param name="options">The document's options.</param>
    /// <returns>The document.</returns>
    public static PdfDocument Hello(PdfOptions options) =>
        PdfDocument.Create(options, doc => doc
            .WithTitle("Hello")
            .WithLanguage("en-US")
            .Content(c =>
            {
                c.Heading(1, "Hello, Broadside");
                c.Paragraph("This page is tagged, embeds its font and opens in any reader.");
            }));

    /// <summary>A multi-page report with a header, a footer with page numbers and data-driven content (section 3.3), declared PDF/UA-1.</summary>
    /// <param name="options">The document's options.</param>
    /// <param name="report">The report's data.</param>
    /// <returns>The document.</returns>
    public static PdfDocument CreateFieldReport(PdfOptions options, FieldReport report) =>
        PdfDocument.Create(options, doc => doc
            .WithTitle($"Field report, {report.Quarter}")
            .WithAuthor(report.Team)
            .WithLanguage("en-GB")
            .WithTheme(Theme.Default.WithBaseSize(10.5))
            .WithPdfUAConformance(PdfUAConformance.PdfUA1)
            .Section(s => s
                .Size(PageSize.A4)
                .Margins(top: 25.Mm, right: 20.Mm, bottom: 25.Mm, left: 20.Mm)
                .FirstPageHeader(h => { })                      // an empty header draws nothing
                .Header(h => h
                    .Left($"Field report, {report.Quarter}")
                    .Right(report.Team))
                .Footer(f => f
                    .Center($"Page {PageField.Number} of {PageField.Count}"))
                .Content(c =>
                {
                    c.Heading(1, "Field report");
                    c.Paragraph(report.Summary).Justify();

                    foreach (Region region in report.Regions)
                    {
                        c.Heading(2, region.Name).KeepWithNext();
                        foreach (string note in region.Notes)
                        {
                            c.Paragraph(note);
                        }

                        if (region.Incidents.Count == 0)
                        {
                            c.Paragraph("No incidents were reported.").Italic().Color(Theme.Default.Muted);
                        }
                        else
                        {
                            c.List(l =>
                            {
                                foreach (string incident in region.Incidents)
                                {
                                    l.Item(incident);
                                }
                            });
                        }
                    }
                })));

    /// <summary>A styled table with a repeating header row, row headers, a caption and striping (section 3.4), declared PDF/UA-2.</summary>
    /// <param name="options">The document's options.</param>
    /// <param name="results">The rows.</param>
    /// <returns>The document.</returns>
    public static PdfDocument ResultsTable(PdfOptions options, IReadOnlyList<RegionResult> results) =>
        PdfDocument.Create(options, doc => doc
            .WithTitle("Results by region")
            .WithLanguage("en-GB")
            .WithPdfUAConformance(PdfUAConformance.PdfUA2)
            .Content(c =>
            {
                Theme theme = c.Theme;
                c.Heading(1, "Results by region");
                c.Table(t =>
                {
                    t.Columns(ColumnWidth.Proportional(3), ColumnWidth.Proportional(1), ColumnWidth.Proportional(1), 28.Mm);
                    t.Header(h =>
                    {
                        h.Cell("Region");
                        h.Cell("Q3").AlignRight();
                        h.Cell("Q4").AlignRight();
                        h.Cell("Change").AlignRight();
                    });

                    foreach (RegionResult r in results)
                    {
                        t.Row(row =>
                        {
                            row.HeaderCell(r.Name);                                     // TH with Row scope
                            row.Cell(r.Q3.ToString("N0", CultureInfo.InvariantCulture)).AlignRight();
                            row.Cell(r.Q4.ToString("N0", CultureInfo.InvariantCulture)).AlignRight();
                            row.Cell(r.Change.ToString("P1", CultureInfo.InvariantCulture)).AlignRight()
                               .Color(r.Change < 0 ? theme.Negative : theme.Text);
                        });
                    }
                })
                .Caption("Table 1. Units shipped by region")
                .Striped()                                   // theme.Subtle on alternate body rows
                .Border(0.5, theme.Border);
            }));

    /// <summary>Images, a decorative image and a vector chart drawn on the page's canvas (section 3.5), declared PDF/A-2u.</summary>
    /// <param name="options">The document's options.</param>
    /// <param name="logo">The logo; embedded once however often it is drawn.</param>
    /// <param name="results">The chart's data.</param>
    /// <returns>The document.</returns>
    public static PdfDocument ImagesAndDrawing(PdfOptions options, ImageSource logo, IReadOnlyList<RegionResult> results) =>
        PdfDocument.Create(options, doc => doc
            .WithTitle("Images and drawings")
            .WithLanguage("en-GB")
            .WithPdfAConformance(PdfAConformance.PdfA2u)
            .Content(c =>
            {
                Theme theme = c.Theme;
                c.Image(logo, "Acme Instruments logo").Width(40.Mm);
                c.Heading(1, "Images and drawings");
                c.DecorativeImage(logo).Width(20.Mm).AlignCenter();   // tagged as an artifact

                c.Drawing(width: 120.Mm, height: 50.Mm,
                          "Bar chart of units shipped in Q4: North 135, South 104, East 151, West 112.",
                          (canvas, box) =>
                {
                    double slot = box.Width / results.Count;
                    double max = results.Max(r => r.Q4);
                    canvas.SetFillColor(theme.Accent);
                    for (int i = 0; i < results.Count; i++)
                    {
                        canvas.Rectangle((i * slot) + (slot * 0.2), 0, slot * 0.6, box.Height * results[i].Q4 / max);
                    }

                    canvas.Fill();
                })
                .Caption("Figure 1. Units shipped in Q4");
            }));

    /// <summary>
    /// Lists, links to a heading and to the web, a page reference, a table of contents and the outline built from the headings
    /// (section 3.6), declared PDF/UA-1.
    /// </summary>
    /// <param name="options">The document's options.</param>
    /// <returns>The document.</returns>
    public static PdfDocument Guide(PdfOptions options) =>
        PdfDocument.Create(options, doc => doc
            .WithTitle("Broadside user guide")
            .WithLanguage("en-US")
            .WithOutlineDepth(3)                              // the default is 3; 0 turns the outline off
            .WithPdfUAConformance(PdfUAConformance.PdfUA1)
            .Section(s => s
                .Footer(f => f.Center($"Page {PageField.Number} of {PageField.Count}"))
                .Content(c =>
                {
                    c.Heading(1, "Broadside user guide");
                    c.TableOfContents().Title("Contents").Depth(2);
                    c.PageBreak();

                    c.Heading(2, "Installing").Id("install");
                    c.List(l =>
                    {
                        l.Item("Add the Broadside package.");
                        l.Item(i =>
                        {
                            i.Paragraph("Add the optional packages you need:");
                            i.List(sub =>
                            {
                                sub.Item("Broadside.Rendering, for PNG output");
                                sub.Item("Broadside.Fonts.Standard14, for non-embedded fonts");
                            }).Bullet("–");
                        });
                    }).Numbered();

                    c.Heading(2, "Reading files");
                    c.Paragraph(t =>
                    {
                        t.Text("Install the packages first (see ");
                        t.Link("Installing", to: "install");
                        t.Text(" on page ");
                        t.PageOf("install");
                        t.Text("), or read the ");
                        t.Link("online guide", new Uri("https://example.org/guide"));
                        t.Text(".");
                    });

                    c.Heading(3, "Repairs and diagnostics");
                    c.Paragraph("Reading is lenient: a damaged file is repaired, and every repair is recorded as a diagnostic on the document.");
                    c.Heading(2, "Writing files");
                    c.Paragraph("Save with Save or SaveAsync; a document built for a PDF/A or PDF/UA profile saves with it by default.");
                })));

    /// <summary>A form with labeled fields of every kind (section 3.7), declared PDF/UA-1.</summary>
    /// <param name="options">The document's options.</param>
    /// <returns>The document.</returns>
    public static PdfDocument LoanRequestForm(PdfOptions options) =>
        PdfDocument.Create(options, doc => doc
            .WithTitle("Equipment loan request")
            .WithLanguage("en-GB")
            .WithPdfUAConformance(PdfUAConformance.PdfUA1)
            .Content(c =>
            {
                c.Heading(1, "Equipment loan request");
                c.Paragraph("Complete every field, sign, and return the form to the stores office.");

                c.TextField("name", "Full name").Width(90.Mm).Required();
                c.TextField("email", "Email address").Width(90.Mm).Required();
                c.ComboBox("site", "Site", ["Leeds", "Manchester", "York"]).Value("Leeds");
                c.TextField("items", "Items requested").Multiline(lines: 4).FillWidth();
                c.RadioGroup("collection", "Collection", ["Pick up", "Deliver to my desk"]).Value("Pick up");
                c.CheckBox("terms", "I will return the equipment in working order").Required();
                c.SignatureField("signature", "Signature").Size(70.Mm, 20.Mm);
            }));

    /// <summary>
    /// An invoice driven by data (section 3.8): columns, a reusable address component, a table of lines, a narrow totals table at the
    /// right, a page footer, and the invoice's XML attached as its machine-readable alternative; declared PDF/A-3u.
    /// </summary>
    /// <param name="options">The document's options.</param>
    /// <param name="invoice">The invoice's data.</param>
    /// <param name="theme">The theme, shared by every invoice.</param>
    /// <returns>The document.</returns>
    public static PdfDocument CreateInvoice(PdfOptions options, Invoice invoice, Theme theme) =>
        PdfDocument.Create(options, doc => doc
            .WithTitle($"Invoice {invoice.Number}")
            .WithAuthor(invoice.Seller.Name)
            .WithLanguage("en-GB")
            .WithTheme(theme)
            .WithPdfAConformance(PdfAConformance.PdfA3u)
            .AttachFile("invoice.xml", invoice.ToXml(), PdfFileRelationship.Alternative)
            .Section(s => s
                .Size(PageSize.A4)
                .Footer(f => f
                    .Left(invoice.Seller.Name)
                    .Right($"Page {PageField.Number} of {PageField.Count}"))
                .Content(c =>
                {
                    c.Columns(cols =>
                    {
                        cols.Column(col =>
                        {
                            if (theme.Logo is { } logo)
                            {
                                col.Image(logo, $"{invoice.Seller.Name} logo").Width(35.Mm);
                            }

                            col.AddressBlock(invoice.Seller);                   // a component, see InvoiceComponents
                        });
                        cols.Column(col =>
                        {
                            col.Heading(1, "Invoice").AlignRight();
                            col.Paragraph($"No. {invoice.Number}").AlignRight();
                            col.Paragraph($"Issued {Date(invoice.IssuedOn)}").AlignRight();
                            col.Paragraph($"Due {Date(invoice.DueOn)}").AlignRight().Bold();
                        });
                    });

                    c.Heading(2, "Bill to").SpaceBefore(8.Mm);
                    c.AddressBlock(invoice.Buyer);

                    c.Table(t =>
                    {
                        t.Columns(ColumnWidth.Proportional(1), 18.Mm, 28.Mm, 28.Mm);
                        t.Header(h =>
                        {
                            h.Cell("Description");
                            h.Cell("Qty").AlignRight();
                            h.Cell("Unit price").AlignRight();
                            h.Cell("Amount").AlignRight();
                        });

                        foreach (InvoiceLine line in invoice.Lines)
                        {
                            t.Row(r =>
                            {
                                r.Cell(line.Description);
                                r.Cell(line.Quantity.ToString(CultureInfo.InvariantCulture)).AlignRight();
                                r.Cell(Money(line.UnitPrice)).AlignRight();
                                r.Cell(Money(line.Amount)).AlignRight();
                            });

                            if (line.Note is { Length: > 0 } note)
                            {
                                t.Row(r => r.Cell(note).ColumnSpan(4).Italic().Color(theme.Muted));
                            }
                        }
                    }).Striped();

                    c.Table(t =>
                    {
                        t.Columns(ColumnWidth.Proportional(1), 30.Mm);
                        t.Row(r => { r.HeaderCell("Subtotal"); r.Cell(Money(invoice.Subtotal)).AlignRight(); });
                        if (invoice.Discount > 0)
                        {
                            t.Row(r => { r.HeaderCell("Discount"); r.Cell(Money(-invoice.Discount)).AlignRight(); });
                        }

                        t.Row(r => { r.HeaderCell($"VAT {invoice.VatRate.ToString("P0", CultureInfo.InvariantCulture)}"); r.Cell(Money(invoice.Vat)).AlignRight(); });
                        t.Row(r => { r.HeaderCell("Total due").Bold(); r.Cell(Money(invoice.Total)).AlignRight().Bold(); });
                    })
                    .Width(80.Mm).AlignRight().KeepTogether().Border(TableBorder.None);

                    if (invoice.IsOverdue)
                    {
                        c.Paragraph("This invoice is overdue. Please pay within 7 days.").Bold().Color(theme.Negative);
                    }

                    c.Paragraph(t =>
                    {
                        t.Text("Pay by bank transfer to ");
                        t.Text(invoice.Seller.Iban).Bold();
                        t.Text($", quoting reference {invoice.Number}.");
                    }).SpaceBefore(6.Mm);
                })));

    /// <summary>
    /// The four levels below the builder (section 3.10): a per-page decoration, an engine block built by hand, a drawing on the page's
    /// canvas, and the plain <see cref="PdfDocument"/> the builder returns.
    /// </summary>
    /// <param name="options">The document's options.</param>
    /// <returns>The document.</returns>
    public static PdfDocument EscapeHatches(PdfOptions options)
    {
        PdfDocument pdf = PdfDocument.Create(options, doc => doc
            .WithTitle("Escape hatches")
            .WithLanguage("en")
            .Section(s => s
                // 1. Per-page drawing after layout, as an artifact: crop marks, letterhead art, a page border.
                .Decorate((canvas, page) =>
                {
                    canvas.SetStrokeColor(PdfColor.FromGray(0.8f));
                    canvas.Rectangle(page.Margins.Left, page.Margins.Bottom, page.ContentWidth, page.ContentHeight);
                    canvas.Stroke();
                })
                .Watermark("SAMPLE")
                .Content(c =>
                {
                    // 2. Any engine block, built by hand, with a font embedded in the document being built.
                    PdfCanvasFont times = c.Document.EmbedFont(Standard14FontResolver.GetProgram(Standard14Font.TimesRoman));
                    c.Add(new Paragraph("Built with the engine directly.", new ParagraphStyle(times, 11).WithSpaceAfter(6)));

                    // 3. PdfCanvas inside the flow.
                    c.Drawing(40.Mm, 40.Mm, "A red square.", (canvas, box) =>
                    {
                        canvas.SetFillColor(PdfColor.FromRgb(0.8f, 0.1f, 0.1f));
                        canvas.Rectangle(0, 0, box.Width, box.Height);
                        canvas.Fill();
                    });
                })));

        // 4. The result is a plain PdfDocument: every core API and the COS layer are available.
        pdf.Pages[0].AddTextAnnotation(new PdfRectangle(500, 760, 520, 780), "Reviewed");
        return pdf;
    }

    private static string Money(decimal amount) => (amount < 0 ? "-£" : "£") + Math.Abs(amount).ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
}

/// <summary>A reusable component: an extension method on the container builder, with no interface to implement and no lifecycle (section 3.9).</summary>
public static class InvoiceComponents
{
    private static readonly Style AddressStyle = new() { LineHeight = 1.25 };

    /// <summary>Adds an address: the name in bold, then its lines and country, kept on one page.</summary>
    /// <param name="c">The container to add to.</param>
    /// <param name="address">The address.</param>
    public static void AddressBlock(this ContainerBuilder c, Address address) =>
        c.Group(g =>
        {
            g.Paragraph(address.Name).Bold().SpaceAfter(0);
            foreach (string line in address.Lines)
            {
                g.Paragraph(line).SpaceAfter(0);
            }

            g.Paragraph(address.Country);
        })
        .Style(AddressStyle)
        .KeepTogether();
}

/// <summary>A theme shared by every invoice: an immutable record, safe to use from many threads at once (section 3.9).</summary>
public static class AcmeTheme
{
    /// <summary>Gets the theme.</summary>
    public static Theme Instance { get; } = Theme.Default with
    {
        BaseSize = 10,
        Accent = PdfColor.FromHex("#0B5FFF"),
        Muted = PdfColor.FromHex("#5B6770"),
        Negative = PdfColor.FromHex("#B42318"),
        Headings = Theme.Default.Headings with
        {
            H1 = new Style { FontSize = 22, Color = PdfColor.FromHex("#0B5FFF"), SpaceAfter = 4.Mm },
        },
    };

    /// <summary>A note's style: small, italic and muted.</summary>
    public static readonly Style Note = new() { FontSize = 8.5, Italic = true, Color = PdfColor.FromHex("#5B6770") };

    /// <summary>The theme with <paramref name="logo"/>.</summary>
    /// <param name="logo">The logo.</param>
    /// <returns>The theme.</returns>
    public static Theme WithLogo(ImageSource logo) => Instance with { Logo = logo };
}
