using System.Text;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Layout.Flow.Units;
using Broadside.Structure;
using Broadside.Tests.Writing;
using static Broadside.Layout.Tests.Builder.BuilderTestSupport;

namespace Broadside.Layout.Tests.Barcodes;

/// <summary>
/// Barcodes in the document builder and on a canvas (issue #631): <see cref="ContainerBuilder.Barcode"/> is a <c>Figure</c> with alternate
/// text (ISO 32000-2 §14.8.4.8.5, §14.9.3) that saves clean as PDF/UA-2; <see cref="BarcodeCanvasExtensions.DrawBarcode"/> writes one
/// <c>re</c> per run of dark modules and one <c>f</c>, inside <c>q</c> ... <c>Q</c>; and one page per symbology is snapshotted.
/// </summary>
public sealed class BarcodeFlowTests
{
    [Fact]
    public void A_barcode_in_the_flow_is_a_Figure_whose_alternate_text_defaults_to_the_kind_and_data()
    {
        using PdfDocument pdf = PdfDocument.Create(Options(), doc => doc.WithTitle("Ticket").WithLanguage("en").Content(c =>
        {
            c.Barcode(Barcode.QrCode("https://example.com/t/42"), 30.Mm, 30.Mm);
            c.Barcode(Barcode.Ean13("590123412345"), 40.Mm, 20.Mm, "Product number 5901234123457");
        }));

        using PdfDocument reopened = PdfDocument.Open(Save(pdf));
        PdfStructureElement[] figures = [.. reopened.StructureTree!.Elements.Where(static element => element.StructureType?.Value == "Figure")];
        Assert.Equal(2, figures.Length);
        Assert.Equal("QR code: https://example.com/t/42", figures[0].AlternateDescription);
        Assert.Equal("Product number 5901234123457", figures[1].AlternateDescription);

        // The EAN-13's digits are set in the style's font under the bars.
        Assert.Contains("5901234123457", reopened.Pages[0].ExtractText().PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_barcode_saves_clean_as_PDF_UA_2()
    {
        using PdfDocument pdf = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Shipping label")
            .WithLanguage("en")
            .WithPdfUAConformance(PdfUAConformance.PdfUA2)
            .Content(c =>
            {
                c.Heading(1, "Shipping label");
                c.Barcode(Barcode.Gs1128("(00)106141411234567897"), 80.Mm, 25.Mm);
                c.Barcode(Barcode.DataMatrix("PO 4500012345"), 20.Mm, 20.Mm).AlignRight();
            }));

        byte[] saved = Save(pdf);
        Assert.DoesNotContain(pdf.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        FlowBuildingBlockTests.AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(2, reopened.Metadata!.Packet!.PdfUAPart);
        Assert.Equal(
            ["GS1-128 barcode: (00)106141411234567897", "Data Matrix code: PO 4500012345"],
            reopened.StructureTree!.Elements.Where(static element => element.StructureType?.Value == "Figure").Select(static element => element.AlternateDescription));
    }

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void A_page_of_barcodes_passes_verapdf_as_PDF_UA_2()
    {
        using PdfDocument pdf = AllKinds(PdfUAConformance.PdfUA2);

        Assert.Empty(VeraPdf.FailedRules(Save(pdf), "ua2"));
    }

    [Fact]
    public void A_symbol_is_one_rectangle_per_dark_run_and_one_fill_inside_a_saved_state()
    {
        Barcode barcode = Barcode.Code39("A");
        using PdfDocument document = PdfDocument.Create(Options());
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawBarcode(barcode, new PdfRectangle(0, 0, 200, 50), new BarcodeOptions().WithQuietZone(false));
        }

        string content = Content(document);
        int runs = ReferenceDecoders.Runs(Code128EncoderTests.Row(barcode)).Where(static (_, index) => index % 2 == 0).Count();
        Assert.Equal(runs, Count(content, " re"));
        Assert.Equal(1, Count(content, "\nf\n"));
        Assert.StartsWith("q\n", content, StringComparison.Ordinal);
        Assert.EndsWith("\nQ\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Rows_that_repeat_the_row_above_extend_its_rectangles()
    {
        // A Data Matrix has no two equal rows in a row, so each row's runs are drawn; two equal rows in a row share their rectangles.
        Barcode barcode = Barcode.DataMatrix("123456");
        int expected = 0;
        for (int row = 0; row < barcode.Rows; row++)
        {
            for (int column = 0; column < barcode.Columns; column++)
            {
                if (barcode.IsDark(column, row) && (column == 0 || !barcode.IsDark(column - 1, row)))
                {
                    expected++;
                }
            }
        }

        using PdfDocument document = PdfDocument.Create(Options());
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawBarcode(barcode, new PdfRectangle(0, 0, 100, 100));
            canvas.DrawBarcode(Barcode.Code39("A"), new PdfRectangle(0, 200, 100, 300), new BarcodeOptions().WithText(false));
        }

        string content = Content(document);
        int code39Bars = ReferenceDecoders.Runs(Code128EncoderTests.Row(Barcode.Code39("A"))).Count / 2 + 1;
        Assert.Equal(expected + code39Bars, Count(content, " re\n"));
    }

    [Fact]
    public void A_two_dimensional_symbol_keeps_square_modules_centered_in_its_box_and_paints_its_background()
    {
        Barcode barcode = Barcode.QrCode("1");
        using PdfDocument document = PdfDocument.Create(Options());
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            // 21 modules and 4 on each side: 29 modules of 2 points in a 58 point square, centered in 100 by 58.
            canvas.DrawBarcode(barcode, new PdfRectangle(0, 0, 100, 58), new BarcodeOptions().WithBackground(PdfColor.FromGray(1)));
        }

        string content = Content(document);
        Assert.Contains("21 0 58 58 re", content, StringComparison.Ordinal);

        // The top-left finder's outer ring starts 4 modules in from the area's top-left corner.
        Assert.Contains("29 48 14 2 re", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_box_without_area_throws_and_a_tiny_one_is_drawn()
    {
        using PdfDocument document = PdfDocument.Create(Options());
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();

        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.DrawBarcode(Barcode.QrCode("x"), new PdfRectangle(0, 0, 0, 10)));
        canvas.DrawBarcode(Barcode.QrCode("x"), new PdfRectangle(0, 0, 5, 5));
        Assert.Throws<ArgumentNullException>(() => canvas.DrawBarcode(null!, new PdfRectangle(0, 0, 5, 5)));
    }

    [Theory]
    [MemberData(nameof(BarcodeRoundTripTests.Kinds), MemberType = typeof(BarcodeRoundTripTests))]
    public Task One_page_per_kind_matches_its_snapshot(string kind) => Verify(Snapshot(kind)).UseParameters(kind);

    private static string Snapshot(string kind)
    {
        Barcode barcode = BarcodeRoundTripTests.Sample(kind);
        using PdfDocument pdf = PdfDocument.Create(Options(), doc => doc.WithTitle(kind).WithLanguage("en").Content(c =>
        {
            c.Heading(1, kind);
            c.Barcode(barcode, barcode.IsLinear ? 70.Mm : 40.Mm, barcode.IsLinear ? 25.Mm : 40.Mm);
        }));
        using PdfDocument reopened = PdfDocument.Open(Save(pdf));

        var matrix = new StringBuilder();
        for (int row = 0; row < barcode.Rows; row++)
        {
            for (int column = 0; column < barcode.Columns; column++)
            {
                matrix.Append(barcode.IsDark(column, row) ? '#' : '.');
            }

            matrix.AppendLine();
        }

        return $"""
            == structure ==
            {StructureText(reopened)}
            == text ==
            {PagesText(reopened)}
            == modules ({barcode.Columns} by {barcode.Rows}, quiet zone {barcode.QuietZone}) ==
            {matrix}
            """;
    }

    private static PdfDocument AllKinds(PdfUAConformance ua) => PdfDocument.Create(Options(), doc => doc
        .WithTitle("Barcodes")
        .WithLanguage("en")
        .WithPdfUAConformance(ua)
        .Content(c =>
        {
            c.Heading(1, "Barcodes");
            foreach (string kind in BarcodeRoundTripTests.KindNames)
            {
                Barcode barcode = BarcodeRoundTripTests.Sample(kind);
                c.Barcode(barcode, barcode.IsLinear ? 60.Mm : 25.Mm, barcode.IsLinear ? 18.Mm : 25.Mm);
            }
        }));

    /// <summary>The page's operators as written, operands first, one per line.</summary>
    private static string Content(PdfDocument document)
    {
        using PdfDocument reopened = PdfDocument.Open(Save(document));
        var recorder = new OperatorRecorder();
        reopened.Pages[0].ProcessContent(recorder);
        return recorder.Text.ToString();
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private sealed class OperatorRecorder : ContentProcessor
    {
        public StringBuilder Text { get; } = new();

        public override ContentEvents Events => ContentEvents.Operators;

        public override void VisitOperator(in ContentOperator op, ContentContext context)
        {
            foreach (ContentOperand operand in op.Operands)
            {
                Text.Append(operand.IsNumber ? operand.Number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : Encoding.Latin1.GetString(operand.Bytes)).Append(' ');
            }

            Text.Append(Encoding.Latin1.GetString(op.Keyword)).Append('\n');
        }
    }
}
