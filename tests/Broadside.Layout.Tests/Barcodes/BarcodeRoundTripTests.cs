// Module matrices are square grids; a rectangular array reads as the symbol does.
#pragma warning disable CA1814

using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Layout.Tests.Builder;
using Broadside.Rendering;

namespace Broadside.Layout.Tests.Barcodes;

/// <summary>
/// Each symbology drawn by <see cref="BarcodeCanvasExtensions.DrawBarcode"/> into a saved PDF, rendered by <see cref="SoftwareRasterizer"/>,
/// sampled at the module centers and read back by <see cref="ReferenceDecoders"/> (issue #631): the painted rectangles reproduce the
/// module matrix exactly, quiet zone included, and the symbol decodes to its data.
/// </summary>
public sealed class BarcodeRoundTripTests
{
    /// <summary>Points per module on the page; rendered at 144 dpi that is four pixels.</summary>
    private const double Module = 2;
    private const double PixelsPerPoint = 2;

    public static readonly string[] KindNames = ["Code128", "Gs1128", "Code39", "Ean13", "UpcA", "QrCode", "DataMatrix"];

    public static TheoryData<string> Kinds => [.. KindNames];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_drawn_symbol_renders_to_its_module_matrix_and_decodes_to_its_data(string kind)
    {
        Barcode barcode = Sample(kind);
        bool[,] sampled = RenderAndSample(barcode, out int quiet);

        for (int row = 0; row < barcode.Rows; row++)
        {
            for (int column = 0; column < barcode.Columns; column++)
            {
                Assert.True(barcode.IsDark(column, row) == sampled[row + (barcode.IsLinear ? 0 : quiet), column + quiet], $"{kind} module ({column}, {row})");
            }
        }

        // The quiet zone is light all round.
        for (int column = 0; column < quiet; column++)
        {
            Assert.False(sampled[0, column]);
            Assert.False(sampled[0, sampled.GetLength(1) - 1 - column]);
        }

        bool[] row0 = [.. Enumerable.Range(0, barcode.Columns).Select(column => sampled[barcode.IsLinear ? 0 : quiet, column + quiet])];
        string decoded = kind switch
        {
            "Code128" or "Gs1128" => ReferenceDecoders.Code128(row0),
            "Code39" => ReferenceDecoders.Code39(row0, checkDigit: true),
            "Ean13" or "UpcA" => ReferenceDecoders.Ean13(row0),
            "QrCode" => ReferenceDecoders.QrCode(Inner(sampled, quiet)).Text,
            _ => DataMatrix(Inner(sampled, quiet)),
        };
        Assert.Equal(Expected(kind), decoded);
    }

    internal static Barcode Sample(string kind) => kind switch
    {
        "Code128" => Barcode.Code128("Broadside-631"),
        "Gs1128" => Barcode.Gs1128("(01)09501101530003(17)250101(10)ABC123"),
        "Code39" => Barcode.Code39("BROADSIDE 631", checkDigit: true),
        "Ean13" => Barcode.Ean13("590123412345"),
        "UpcA" => Barcode.UpcA("03600029145"),
        "QrCode" => Barcode.QrCode("https://github.com/michaelstonis/Broadside/issues/631", QrErrorCorrection.Quartile),
        _ => Barcode.DataMatrix("Broadside 631: vector Data Matrix"),
    };

    private static string Expected(string kind) => kind switch
    {
        "Code128" => "Broadside-631",
        "Gs1128" => "\u001D01095011015300031725010110ABC123",
        "Code39" => Barcode.Code39("BROADSIDE 631", checkDigit: true).HumanReadableText!,
        "Ean13" => "5901234123457",
        "UpcA" => "0036000291452",
        "QrCode" => "https://github.com/michaelstonis/Broadside/issues/631",
        _ => "Broadside 631: vector Data Matrix",
    };

    private static string DataMatrix(bool[,] matrix)
    {
        (_, int region, int regions, int data, int ecc, int blocks) = BarcodeDataMatrix.Symbols.Single(symbol => symbol.Size == matrix.GetLength(0));
        return ReferenceDecoders.DataMatrix(matrix, region, regions, data, ecc, blocks);
    }

    /// <summary>Draws the symbol on a page with its text and quiet zone, renders it at 144 dpi, and reads each module's center pixel.</summary>
    private static bool[,] RenderAndSample(Barcode barcode, out int quiet)
    {
        quiet = barcode.QuietZone;
        int columns = barcode.Columns + (2 * quiet);
        int rows = barcode.IsLinear ? 1 : barcode.Rows + (2 * quiet);
        double width = columns * Module;
        double height = barcode.IsLinear ? 60 : rows * Module;
        using PdfDocument document = PdfDocument.Create(BuilderTestSupport.Options());
        PdfCanvasFont font = BuilderTestSupport.Embed(document, Standard14Font.Helvetica);
        var box = new PdfRectangle(20, 20, 20 + width, 20 + height);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawBarcode(barcode, box, new BarcodeOptions().WithFont(font, 8).WithBackground(PdfColor.FromGray(1)));
        }

        using PdfDocument reopened = PdfDocument.Open(BuilderTestSupport.Save(document));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(reopened.Pages[0], new RenderOptions().WithScale(PixelsPerPoint));
        double pageTop = reopened.Pages[0].MediaBox.Top;

        bool[,] sampled = new bool[rows, columns];
        for (int row = 0; row < rows; row++)
        {
            // A linear symbol is read across the middle of its bars, above the text.
            double y = barcode.IsLinear ? box.Top - 10 : box.Top - ((row + 0.5) * Module);
            for (int column = 0; column < columns; column++)
            {
                double x = box.Left + ((column + 0.5) * Module);
                int px = (int)(x * PixelsPerPoint);
                int py = (int)((pageTop - y) * PixelsPerPoint);
                Span<byte> pixel = bitmap.GetRow(py).Slice(px * 4, 4);
                sampled[row, column] = pixel[3] > 128 && pixel[1] < 128;
            }
        }

        return sampled;
    }

    private static bool[,] Inner(bool[,] sampled, int quiet)
    {
        int size = sampled.GetLength(0) - (2 * quiet);
        bool[,] inner = new bool[size, size];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                inner[row, column] = sampled[row + quiet, column + quiet];
            }
        }

        return inner;
    }
}
