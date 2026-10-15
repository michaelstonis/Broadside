using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Signatures;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Signatures;

/// <summary>Visible signatures (#200): the widget's appearance shows the signature's properties and an optional image (ISO 32000-2 §12.7.5.5).</summary>
public sealed class SignatureAppearanceTests : IDisposable
{
    private static readonly DateTimeOffset SigningTime = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
    private readonly X509Certificate2 _certificate = TestCertificates.SelfSigned("CN=Ada Lovelace");

    public void Dispose() => _certificate.Dispose();

    [Fact]
    public void A_visible_signature_shows_its_properties_on_the_chosen_page_and_rectangle_and_still_verifies()
    {
        var rect = new PdfRectangle(100, 600, 400, 680);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        byte[] signed = Sign(document, Options(rect));

        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
        OpenSsl.VerifyCms(SignedFile.Last(signed).Cms, SignedFile.Last(signed).CoveredBytes(), _certificate);
        Assert.InRange(Ink.Count(signed, rect), 200, int.MaxValue);
        Assert.Equal(0, Ink.Count(signed, new PdfRectangle(0, 0, 612, 792)) - Ink.Count(signed, rect));

        using PdfDocument reopened = PdfDocument.Open(signed);
        var field = (PdfSignatureField)Assert.Single(reopened.AcroForm!.Fields);
        Assert.True(field.IsSigned);
        CosDictionary widget = field.Dictionary;
        Assert.Equal(rect, Rect(reopened, widget));
        Assert.Equal(132, ((CosInteger)widget[new CosName("F")]).Value);
        Assert.Equal(reopened.Pages[0].Reference, widget[new CosName("P")]);
        string content = AppearanceContent(reopened, widget);
        Assert.Contains("(Digitally signed by Ada Lovelace)", content, StringComparison.Ordinal);
        Assert.Contains("(Date: 2026-03-04 05:06:07 +00:00)", content, StringComparison.Ordinal);
        Assert.Contains("(Reason: I approve this document)", content, StringComparison.Ordinal);
        Assert.Contains("(Location: London)", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(50, 700, 550, 730)]
    [InlineData(50, 100, 150, 500)]
    [InlineData(300, 300, 400, 360)]
    [InlineData(200, 400, 560, 600)]
    public void Text_is_sized_to_fill_the_rectangle_without_leaving_it(double left, double bottom, double right, double top)
    {
        // The text grows until the longest line meets the sides or the lines meet the top and bottom, whichever comes first, so
        // poppler draws it inside the rectangle and across most of one of its dimensions.
        var rect = new PdfRectangle(left, bottom, right, top);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        byte[] signed = Sign(document, Options(rect));

        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
        Assert.Equal(Ink.Count(signed, new PdfRectangle(0, 0, 612, 792)), Ink.Count(signed, rect));
        PdfRectangle ink = Ink.Bounds(signed, rect);
        Assert.True(
            ink.Width >= 0.85 * (rect.Width - 4) || ink.Height >= 0.75 * (rect.Height - 4),
            $"The text's ink {ink.Width}x{ink.Height} does not fill the {rect.Width}x{rect.Height} rectangle.");
    }

    [Fact]
    public void A_font_size_given_is_used_instead_of_the_automatic_one()
    {
        var rect = new PdfRectangle(100, 500, 500, 700);
        using PdfDocument automatic = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        using PdfDocument fixedSize = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        PdfRectangle large = Ink.Bounds(Sign(automatic, Options(rect)), rect);
        byte[] signed = Sign(fixedSize, Options(rect).WithAppearance(new PdfSignatureAppearance(0, rect).WithFontSize(6)));

        PdfRectangle small = Ink.Bounds(signed, rect);
        Assert.InRange(small.Height, 20, 30); // four lines 6.9 points apart, from a cap height to a descender
        Assert.True(small.Width < large.Width / 2, $"{small.Width} vs {large.Width}");
        using PdfDocument reopened = PdfDocument.Open(signed);
        Assert.Contains("/F1 6 Tf", AppearanceContent(reopened, SignatureWidget(reopened)), StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_takes_the_left_of_a_wide_rectangle_keeping_its_aspect_ratio_and_the_text_the_rest()
    {
        // A black 4 x 2 image in a 300 x 100 rectangle: the image's part is the left two fifths inside a 2-point margin (118.4 x 96),
        // so the image is 118.4 x 59.2, centred vertically; the text starts 2 points right of it.
        var rect = new PdfRectangle(100, 500, 400, 600);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        PdfImage image = document.CreateImage(new byte[8], new PdfImageLayout(4, 2, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);

        byte[] signed = Sign(document, Options(rect).WithAppearance(new PdfSignatureAppearance(0, rect).WithImage(image)));

        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
        PdfRectangle drawn = Ink.Bounds(signed, new PdfRectangle(100, 500, 222, 600));
        Assert.InRange(drawn.Left, 101, 103);
        Assert.InRange(drawn.Right, 219, 222);
        Assert.InRange(drawn.Height, 58, 61);
        Assert.InRange(drawn.Bottom, 519, 522);
        Assert.InRange(Ink.Count(signed, new PdfRectangle(224, 500, 400, 600)), 200, int.MaxValue);
    }

    [Fact]
    public void An_image_alone_fills_the_rectangle_and_an_empty_text_shows_no_text()
    {
        var rect = new PdfRectangle(100, 500, 200, 700);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        PdfImage image = document.CreateImage(new byte[4], new PdfImageLayout(2, 2, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);

        byte[] signed = Sign(document, Options(rect).WithAppearance(new PdfSignatureAppearance(0, rect).WithImage(image).WithText("")));

        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
        PdfRectangle drawn = Ink.Bounds(signed, rect);
        // 96 x 96 (the rectangle less a 2-point margin is 96 x 196), centred: 102..198 across, 552..648 up, give or take a pixel
        // of anti-aliasing.
        Assert.InRange(drawn.Left, 101, 103);
        Assert.InRange(drawn.Right, 197, 199);
        Assert.InRange(drawn.Bottom, 551, 553);
        Assert.InRange(drawn.Top, 647, 649);
        using PdfDocument reopened = PdfDocument.Open(signed);
        Assert.DoesNotContain("BT", AppearanceContent(reopened, SignatureWidget(reopened)), StringComparison.Ordinal);
    }

    [Fact]
    public void An_existing_visible_field_is_signed_with_the_appearance_in_its_own_rectangle()
    {
        var fieldRect = new PdfRectangle(72, 72, 272, 132);
        byte[] unsigned;
        using (PdfDocument author = PdfDocument.Open(Corpus.Bytes("empty-page.pdf")))
        {
            author.CreateAcroForm().AddSignatureField("approval", author.Pages[0], fieldRect);
            using var saved = new MemoryStream();
            author.Save(saved);
            unsigned = saved.ToArray();
        }

        using PdfDocument document = PdfDocument.Open(unsigned);
        byte[] signed = Sign(document, Options(new PdfRectangle(300, 600, 500, 700)).WithFieldName("approval"));

        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
        Assert.Equal(0, Ink.Count(signed, new PdfRectangle(300, 600, 500, 700)));
        Assert.InRange(Ink.Count(signed, fieldRect), 200, int.MaxValue);
        using PdfDocument reopened = PdfDocument.Open(signed);
        Assert.Equal(fieldRect, Rect(reopened, SignatureWidget(reopened)));
        Assert.Contains("(Digitally signed by Ada Lovelace)", AppearanceContent(reopened, SignatureWidget(reopened)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_visible_signature_after_a_certification_for_form_filling_is_a_permitted_change()
    {
        // Issue #199: the new widget, its appearance, font and image are what signing a new field adds; none of it may read as a
        // change the certification forbids.
        byte[] certified;
        using (PdfDocument author = PdfDocument.Open(Corpus.Bytes("acroform-fields.pdf")))
        {
            certified = Sign(author, new PdfSignatureOptions().WithFieldName("certification").WithCertification(PdfCertificationPermissions.FormFilling));
        }

        using PdfDocument document = PdfDocument.Open(certified);
        PdfImage image = document.CreateImage(new byte[4], new PdfImageLayout(2, 2, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);
        var rect = new PdfRectangle(300, 40, 560, 100);
        byte[] signed = Sign(document, Options(rect).WithFieldName("approval").WithAppearance(new PdfSignatureAppearance(0, rect).WithImage(image)));

        Assert.Equal(2, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
        using PdfDocument reopened = PdfDocument.Open(signed);
        IReadOnlyList<PdfSignatureValidation> validations = reopened.ValidateSignatures(SignatureValidationTests.Trusting(_certificate));
        PdfSignatureValidation certification = Assert.Single(validations, static validation => validation.FieldName == "certification");
        Assert.Equal(PdfSignatureStatus.Valid, certification.Status);
        Assert.Contains(certification.Modifications, static change => change is { Kind: PdfModificationKind.Signing, FieldName: "approval" });
        Assert.All(certification.Modifications, static change => Assert.True(change.IsPermitted, change.ToString()));
        Assert.Equal(PdfSignatureStatus.Valid, Assert.Single(validations, static validation => validation.FieldName == "approval").Status);
    }

    [Fact]
    public void A_signer_that_fails_leaves_the_document_without_the_field_or_its_appearance()
    {
        // Issue #306: the widget, the appearance stream and the Helvetica font it adds are undone with the rest.
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        string before = CosSnapshot.Of(document);
        using var output = new MemoryStream();

        Assert.Throws<CryptographicException>(
            () => document.Sign(output, new FailingSigner(_certificate), Options(new PdfRectangle(100, 600, 400, 680))));

        Assert.Equal(before, CosSnapshot.Of(document));
        Assert.Equal(0, output.Length);
        byte[] signed = Sign(document, Options(new PdfRectangle(100, 600, 400, 680)));
        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
        Assert.InRange(Ink.Count(signed, new PdfRectangle(100, 600, 400, 680)), 200, int.MaxValue);
    }

    [Fact]
    public void An_appearance_that_cannot_be_drawn_is_refused_and_changes_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        string before = CosSnapshot.Of(document);
        var rect = new PdfRectangle(100, 600, 400, 680);

        Assert.Throws<ArgumentOutOfRangeException>(() => Sign(document, Options(rect, page: 1)));
        Assert.Throws<ArgumentException>(() => Sign(document, Options(new PdfRectangle(100, 600, 100, 680))));
        ArgumentException unencodable = Assert.Throws<ArgumentException>(
            () => Sign(document, Options(rect).WithAppearance(new PdfSignatureAppearance(0, rect).WithText("Подпись"))));
        Assert.Contains("embedded", unencodable.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfSignatureAppearance(-1, rect));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfSignatureAppearance(0, rect).WithFontSize(0));

        Assert.Equal(before, CosSnapshot.Of(document));
    }

    private static PdfSignatureOptions Options(PdfRectangle rect, int page = 0) =>
        new PdfSignatureOptions()
            .WithReason("I approve this document")
            .WithLocation("London")
            .WithSigningTime(SigningTime)
            .WithAppearance(new PdfSignatureAppearance(page, rect));

    private byte[] Sign(PdfDocument document, PdfSignatureOptions options)
    {
        using var output = new MemoryStream();
        document.Sign(output, new PdfCertificateSigner(_certificate), options);
        return output.ToArray();
    }

    private static CosDictionary SignatureWidget(PdfDocument document) =>
        ((PdfSignatureField)document.AcroForm!.Fields.Last(static field => field is PdfSignatureField { IsSigned: true })).Dictionary;

    private static PdfRectangle Rect(PdfDocument document, CosDictionary widget)
    {
        var array = (CosArray)document.Resolve(widget[new CosName("Rect")])!;
        double Number(int index) => ((CosNumber)document.Resolve(array[index])!).ToDouble();
        return new PdfRectangle(Number(0), Number(1), Number(2), Number(3));
    }

    /// <summary>The decoded content of the widget's normal appearance.</summary>
    private static string AppearanceContent(PdfDocument document, CosDictionary widget)
    {
        var appearances = (CosDictionary)document.Resolve(widget[new CosName("AP")])!;
        var stream = (CosStream)document.Resolve(appearances[new CosName("N")])!;
        return Encoding.Latin1.GetString(document.DecodeStream(stream).Span);
    }
}

/// <summary>A signer whose device refuses to sign.</summary>
file sealed class FailingSigner(X509Certificate2 certificate) : IPdfSigner
{
    public X509Certificate2 Certificate => certificate;

    public IReadOnlyList<X509Certificate2> CertificateChain => [];

    public byte[] SignDigest(ReadOnlySpan<byte> digest, HashAlgorithmName digestAlgorithm) => throw new CryptographicException("The token is locked.");
}

/// <summary>Counts what poppler draws: the pixels darker than mid-grey inside a rectangle of page 1 at 72 dpi.</summary>
internal static class Ink
{
    public static int Count(byte[] file, PdfRectangle rect) => Pixels(file, rect).Count;

    /// <summary>The smallest rectangle (in default user space) holding every dark pixel inside <paramref name="rect"/>.</summary>
    public static PdfRectangle Bounds(byte[] file, PdfRectangle rect)
    {
        (int X, int Y)[] pixels = [.. Pixels(file, rect)];
        return new PdfRectangle(pixels.Min(p => p.X), 792 - pixels.Max(p => p.Y) - 1, pixels.Max(p => p.X) + 1, 792 - pixels.Min(p => p.Y));
    }

    private static List<(int X, int Y)> Pixels(byte[] file, PdfRectangle rect)
    {
        (int exitCode, byte[] image, string error) = ExternalTool.RunBinary("pdftoppm", file, "-r", "72", "-gray", "-f", "1", "-l", "1");
        Assert.True(exitCode == 0, error);

        // A binary PGM: P5, width, height, maximum, one white-space byte, then a byte per pixel.
        int position = 0;
        string Token()
        {
            while (char.IsWhiteSpace((char)image[position]))
            {
                position++;
            }

            int start = position;
            while (!char.IsWhiteSpace((char)image[position]))
            {
                position++;
            }

            return Encoding.ASCII.GetString(image, start, position - start);
        }

        Assert.Equal("P5", Token());
        int width = int.Parse(Token(), System.Globalization.CultureInfo.InvariantCulture);
        int height = int.Parse(Token(), System.Globalization.CultureInfo.InvariantCulture);
        _ = Token();
        int offset = position + 1;
        var found = new List<(int X, int Y)>();
        for (int y = Math.Max(0, height - (int)Math.Ceiling(rect.Top)); y < Math.Min(height, height - (int)Math.Floor(rect.Bottom)); y++)
        {
            for (int x = Math.Max(0, (int)Math.Floor(rect.Left)); x < Math.Min(width, (int)Math.Ceiling(rect.Right)); x++)
            {
                if (image[offset + (y * width) + x] < 128)
                {
                    found.Add((x, y));
                }
            }
        }

        return found;
    }
}
