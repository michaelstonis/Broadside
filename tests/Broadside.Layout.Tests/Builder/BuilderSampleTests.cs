using System.Globalization;
using System.Security.Cryptography;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Layout.Flow.Units;
using static Broadside.Layout.Tests.Builder.BuilderTestSupport;

namespace Broadside.Layout.Tests.Builder;

/// <summary>
/// The spec's samples built with the document builder (issue #397, test plan "Snapshot tests"): each snapshot holds the structure tree as
/// text, the extracted text per page and a SHA-256 of the bytes with a fixed clock. The invoice also proves that one static theme builds
/// identical documents on 16 threads at once.
/// </summary>
public sealed class BuilderSampleTests
{
    private static readonly Theme Acme = Theme.Default with
    {
        BaseSize = 10,
        Accent = PdfColor.FromRgb(0x0B / 255f, 0x5F / 255f, 0xFF / 255f),
        Headings = Theme.Default.Headings with { H1 = new Style { FontSize = 22, SpaceAfter = 4.Mm } },
    };

    [Fact]
    public Task The_field_report_sample() => VerifyDocument(FieldReport());

    [Fact]
    public Task The_invoice_sample() => VerifyDocument(Invoice());

    [Fact]
    public void Sixteen_invoices_built_at_once_from_one_theme_are_identical()
    {
        byte[] expected;
        using (PdfDocument first = Invoice())
        {
            expected = Save(first);
        }

        byte[][] results = new byte[16][];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 16 }, index =>
        {
            using PdfDocument invoice = Invoice();
            results[index] = Save(invoice);
        });

        Assert.All(results, bytes => Assert.Equal(expected, bytes));
    }

    private static async Task VerifyDocument(PdfDocument document) => await VerifyXunit.Verifier.Verify(Summary(document));

    private static string Summary(PdfDocument document)
    {
        using (document)
        {
            byte[] bytes = Save(document);
            using PdfDocument reopened = PdfDocument.Open(bytes);
            return $"""
                == structure ==
                {StructureText(reopened)}
                == text ==
                {PagesText(reopened)}
                == sha256 ==
                {Convert.ToHexString(SHA256.HashData(bytes))}
                """;
        }
    }

    private static PdfDocument FieldReport() =>
        PdfDocument.Create(Options(), doc => doc
            .WithTitle("Field report, Q4")
            .WithAuthor("Operations")
            .WithLanguage("en-GB")
            .WithTheme(Theme.Default.WithBaseSize(10.5))
            .Section(s => s
                .Size(PageSize.A4)
                .Margins(top: 25.Mm, right: 20.Mm, bottom: 25.Mm, left: 20.Mm)
                .FirstPageHeader(h => { })
                .Header(h => h.Left("Field report, Q4").Right("Operations"))
                .Footer(f => f.Center($"Page {PageField.Number} of {PageField.Count}"))
                .Content(c =>
                {
                    c.Heading(1, "Field report");
                    c.Paragraph("Flow was steady across every region this quarter, with two short outages in the east.").Justify();
                    foreach (string region in new[] { "North", "South", "East", "West" })
                    {
                        c.Heading(2, region);
                        for (int note = 1; note <= 6; note++)
                        {
                            c.Paragraph($"{region} note {note}: the meters were read on schedule and the readings agree with the telemetry.");
                        }

                        if (region == "West")
                        {
                            c.Paragraph("No incidents were reported.").Italic().Color(Theme.Default.Muted);
                        }
                    }
                })));

    private static PdfDocument Invoice()
    {
        CultureInfo culture = CultureInfo.InvariantCulture;
        (string Description, int Quantity, decimal Price)[] lines =
        [
            ("Flow meter, 50 mm", 2, 420m),
            ("Calibration service", 1, 180m),
            ("Mounting kit", 4, 35.5m),
        ];
        decimal subtotal = lines.Sum(line => line.Quantity * line.Price);
        return PdfDocument.Create(Options(), doc => doc
            .WithTitle("Invoice 2026-0042")
            .WithAuthor("Acme Instruments")
            .WithLanguage("en-GB")
            .WithTheme(Acme)
            .Section(s => s
                .Footer(f => f.Left("Acme Instruments").Right($"Page {PageField.Number} of {PageField.Count}"))
                .Content(c =>
                {
                    c.Heading(1, "Invoice");
                    c.Paragraph("No. 2026-0042").AlignRight();
                    c.Paragraph("Due 14/12/2026").AlignRight().Bold();
                    c.Heading(2, "Bill to").SpaceBefore(8.Mm);
                    c.Group(g =>
                    {
                        g.Paragraph("Northern Water Ltd").Bold();
                        g.Paragraph("1 Reservoir Road");
                        g.Paragraph("Leeds LS1 1AA");
                    }).Style(new Style { LineHeight = 1.25 });

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
                        foreach ((string description, int quantity, decimal price) in lines)
                        {
                            t.Row(r =>
                            {
                                r.Cell(description);
                                r.Cell(quantity.ToString(culture)).AlignRight();
                                r.Cell(price.ToString("0.00", culture)).AlignRight();
                                r.Cell((quantity * price).ToString("0.00", culture)).AlignRight();
                            });
                        }

                        t.Row(r => r.Cell("Prices exclude delivery.").ColumnSpan(4).Italic().Color(Acme.Muted));
                    }).Striped();

                    c.Table(t =>
                    {
                        t.Columns(ColumnWidth.Proportional(1), 30.Mm);
                        t.Row(r =>
                        {
                            r.HeaderCell("Subtotal");
                            r.Cell(subtotal.ToString("0.00", culture)).AlignRight();
                        });
                        t.Row(r =>
                        {
                            r.HeaderCell("Total due").Bold();
                            r.Cell((subtotal * 1.2m).ToString("0.00", culture)).AlignRight().Bold();
                        });
                    }).Border(TableBorder.None);

                    c.Paragraph(t =>
                    {
                        t.Text("Pay by bank transfer to ");
                        t.Text("GB33 BUKB 2020 1555 5555 55").Bold();
                        t.Text(", quoting reference 2026-0042.");
                    }).SpaceBefore(6.Mm);
                })));
    }
}
