using System.Text;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Tests.Writing;

/// <summary>
/// Saving with a PDF/A conformance level (issue #201): the check step names every veraPDF PDF/A-2B rule the document cannot meet,
/// refuses in strict mode with the document left as it was, and in lenient mode saves without claiming the conformance; a document
/// that can conform gets an sRGB output intent, the PDF/A identification and a PDF 1.7 header. Files Broadside writes this way are
/// validated by veraPDF itself in <see cref="PdfA2bVeraPdfTests"/> and in the Standard 14 tests' <c>PdfA2bTests</c>.
/// </summary>
public sealed class PdfAConformanceTests
{
    private static readonly PdfSaveOptions PdfA2b = new PdfSaveOptions().WithPdfAConformance(PdfAConformance.PdfA2b);

    [Fact]
    public void A_new_document_saved_as_pdf_a_2b_is_pdf_1_7_with_an_srgb_output_intent_and_the_pdf_a_identification()
    {
        using PdfDocument document = PdfDocument.Create();
        DrawRgbSquare(document);

        byte[] saved = Save(document, PdfA2b);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.StartsWith("%PDF-1.7\n", Encoding.ASCII.GetString(saved, 0, 9), StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(1, 7), reopened.Version);
        PdfOutputIntent intent = Assert.Single(reopened.OutputIntents);
        Assert.Equal("GTS_PDFA1", intent.Subtype);
        Assert.Equal("sRGB IEC61966-2.1", intent.OutputConditionIdentifier);
        Assert.Equal(PdfIccBasedColorSpace.CreateSrgbProfileData(), reopened.DecodeStream(intent.DestinationProfile!.Stream).ToArray());
        string packet = Encoding.UTF8.GetString(reopened.DecodeStream(reopened.Metadata!.Stream).Span);
        Assert.Contains("<pdfaid:part>2</pdfaid:part>", packet, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", packet, StringComparison.Ordinal);
        Assert.Equal(2, ((CosArray)reopened.Trailer[KnownNames.ID]).Count);
        Assert.False(reopened.IsEncrypted);
    }

    [Fact]
    public void An_existing_pdf_a_output_intent_is_kept_and_a_second_save_adds_none()
    {
        using PdfDocument document = PdfDocument.Create();
        DrawRgbSquare(document);
        byte[] first = Save(document, PdfA2b);

        using PdfDocument reopened = PdfDocument.Open(first);
        reopened.Properties.Title = "Again";
        using PdfDocument again = PdfDocument.Open(Save(reopened, PdfA2b));

        Assert.Single(again.OutputIntents);
        Assert.Equal("Again", again.Properties.Title);
    }

    [Fact]
    public void A_non_embedded_font_throws_in_strict_mode_naming_rule_6_2_11_4_1_1_and_leaves_the_document_as_it_was()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStrict());
        DrawStandard14Text(document);
        string before = CosSnapshot.Of(document);

        DiagnosticException refused = Assert.Throws<DiagnosticException>(() => Save(document, PdfA2b));

        Assert.Equal(DiagnosticCodes.PdfANonConformant, refused.Diagnostic.Code);
        Assert.Contains("6.2.11.4.1-1", refused.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Helvetica", refused.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(before, CosSnapshot.Of(document));
    }

    [Fact]
    public void A_non_embedded_font_in_lenient_mode_records_the_rule_and_saves_without_claiming_pdf_a()
    {
        using PdfDocument document = PdfDocument.Create();
        DrawStandard14Text(document);

        byte[] saved = Save(document, PdfA2b);

        Diagnostic rule = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant);
        Assert.Equal(DiagnosticSeverity.Warning, rule.Severity);
        Assert.Contains("6.2.11.4.1-1", rule.Message, StringComparison.Ordinal);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANotIdentified && diagnostic.Severity == DiagnosticSeverity.Information);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Empty(reopened.OutputIntents);
        Assert.DoesNotContain("pdfaid", Encoding.UTF8.GetString(reopened.DecodeStream(reopened.Metadata!.Stream).Span), StringComparison.Ordinal);
        Assert.Equal("Hello", reopened.Pages[0].ExtractText().Text);
    }

    public static TheoryData<string, string> Violations => new()
    {
        { "encryption", "6.1.3-2" },
        { "JavaScript open action", "6.5.1-1" },
        { "named action other than paging", "6.5.1-2" },
        { "catalog additional actions", "6.5.2-1" },
        { "page additional actions", "6.5.2-2" },
        { "annotation without an appearance", "6.3.3-1" },
        { "hidden annotation", "6.3.2-2" },
        { "annotation without flags", "6.3.2-1" },
        { "3D annotation", "6.3.1-1" },
        { "DeviceCMYK fill under the sRGB output intent", "6.2.4.3-3" },
        { "transfer function", "6.2.5-1" },
        { "NeedAppearances", "6.4.1-3" },
        { "embedded file", "6.8-5" },
        { "incremental update of a PDF 2.0 file", "6.1.2-1" },
    };

    [Theory]
    [MemberData(nameof(Violations))]
    public void Each_rule_the_document_cannot_meet_is_reported_by_its_verapdf_rule_and_refused_in_strict_mode(string violation, string rule)
    {
        foreach (bool strict in new[] { false, true })
        {
            using PdfDocument document = PdfDocument.Create(strict ? new PdfOptions().UseStrict() : new PdfOptions());
            DrawRgbSquare(document);
            PdfSaveOptions options = Break(document, violation);
            if (strict)
            {
                string before = CosSnapshot.Of(document);
                DiagnosticException refused = Assert.Throws<DiagnosticException>(() => Save(document, options));
                Assert.Equal(DiagnosticCodes.PdfANonConformant, refused.Diagnostic.Code);
                Assert.Contains($"veraPDF rule {rule},", refused.Diagnostic.Message, StringComparison.Ordinal);
                Assert.Equal(before, CosSnapshot.Of(document));
            }
            else
            {
                byte[] saved = Save(document, options);
                Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant && diagnostic.Message.Contains($"veraPDF rule {rule},", StringComparison.Ordinal));
                Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant && !diagnostic.Message.Contains($"veraPDF rule {rule},", StringComparison.Ordinal));
                Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANotIdentified);
                Assert.NotEmpty(saved);
            }
        }
    }

    [Fact]
    public void A_save_without_a_conformance_level_checks_nothing()
    {
        using PdfDocument document = PdfDocument.Create();
        DrawStandard14Text(document);

        using PdfDocument reopened = PdfDocument.Open(Save(document, new PdfSaveOptions()));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is DiagnosticCodes.PdfANonConformant or DiagnosticCodes.PdfANotIdentified);
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        Assert.Empty(reopened.OutputIntents);
    }

    [Fact]
    public void An_undefined_conformance_level_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfSaveOptions().WithPdfAConformance((PdfAConformance)99));
        Assert.Equal(PdfAConformance.None, new PdfSaveOptions().PdfAConformance);
    }

    private static PdfSaveOptions Break(PdfDocument document, string violation)
    {
        CosDictionary page = document.Pages[0].Dictionary;
        switch (violation)
        {
            case "encryption":
                return new PdfSaveOptions().WithPdfAConformance(PdfAConformance.PdfA2b).WithEncryption(new PdfEncryptionOptions("user", "owner"));
            case "JavaScript open action":
                document.Catalog[new CosName("OpenAction")] = new CosDictionary { [new CosName("S")] = new CosName("JavaScript"), [new CosName("JS")] = new CosString("app.alert(1)"u8.ToArray()) };
                break;
            case "named action other than paging":
                document.Catalog[new CosName("OpenAction")] = new CosDictionary { [new CosName("S")] = new CosName("Named"), [new CosName("N")] = new CosName("Print") };
                break;
            case "catalog additional actions":
                document.Catalog[new CosName("AA")] = new CosDictionary();
                break;
            case "page additional actions":
                page[new CosName("AA")] = new CosDictionary();
                break;
            case "annotation without an appearance":
                AddAnnotation(document, "Square", flags: 4, appearance: false);
                break;
            case "hidden annotation":
                AddAnnotation(document, "Square", flags: 4 | 2, appearance: true);
                break;
            case "annotation without flags":
                AddAnnotation(document, "Square", flags: null, appearance: true);
                break;
            case "3D annotation":
                AddAnnotation(document, "3D", flags: 4, appearance: true);
                break;
            case "DeviceCMYK fill under the sRGB output intent":
                using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
                {
                    canvas.SetFillColor(PdfColor.FromCmyk(0, 1, 1, 0));
                    canvas.Rectangle(10, 10, 20, 20);
                    canvas.Fill();
                }

                break;
            case "transfer function":
                var resources = (CosDictionary)document.Resolve(page[KnownNames.Resources]);
                resources[new CosName("ExtGState")] = new CosDictionary { [new CosName("GS0")] = new CosDictionary { [new CosName("TR")] = new CosName("Identity") } };
                break;
            case "NeedAppearances":
                document.Catalog[new CosName("AcroForm")] = new CosDictionary { [new CosName("Fields")] = new CosArray(), [new CosName("NeedAppearances")] = CosBoolean.True };
                break;
            case "embedded file":
                document.AddEmbeddedFile("notes.txt", "notes"u8);
                break;
            case "incremental update of a PDF 2.0 file":
                return new PdfSaveOptions().WithPdfAConformance(PdfAConformance.PdfA2b).WithIncrementalUpdate(true);
        }

        return PdfA2b;
    }

    private static void AddAnnotation(PdfDocument document, string subtype, int? flags, bool appearance)
    {
        var annotation = new CosDictionary
        {
            [KnownNames.Type] = new CosName("Annot"),
            [new CosName("Subtype")] = new CosName(subtype),
            [new CosName("Rect")] = new CosArray([new CosInteger(100), new CosInteger(100), new CosInteger(200), new CosInteger(150)]),
        };
        if (flags is { } value)
        {
            annotation[new CosName("F")] = new CosInteger(value);
        }

        if (appearance)
        {
            var form = new CosStream(
                new CosDictionary
                {
                    [KnownNames.Type] = new CosName("XObject"),
                    [new CosName("Subtype")] = new CosName("Form"),
                    [new CosName("BBox")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(100), new CosInteger(50)]),
                },
                "0 0 1 rg 0 0 100 50 re f"u8.ToArray());
            annotation[new CosName("AP")] = new CosDictionary { [new CosName("N")] = document.AddObject(form) };
        }

        document.Pages[0].Dictionary[new CosName("Annots")] = new CosArray([document.AddObject(annotation)]);
    }

    private static void DrawRgbSquare(PdfDocument document)
    {
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        canvas.SetFillColor(PdfColor.FromRgb(0.2f, 0.4f, 0.8f));
        canvas.Rectangle(72, 600, 100, 100);
        canvas.Fill();
        canvas.SetStrokeColor(PdfColor.FromGray(0));
        canvas.Rectangle(72, 600, 100, 100);
        canvas.Stroke();
    }

    private static void DrawStandard14Text(PdfDocument document)
    {
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        canvas.BeginText();
        canvas.SetFont(document.GetStandard14Font(Standard14Font.Helvetica), 12);
        canvas.MoveText(72, 720);
        canvas.ShowText("Hello");
        canvas.EndText();
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var stream = new MemoryStream();
        document.Save(stream, options);
        return stream.ToArray();
    }
}
