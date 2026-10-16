using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// Saving as PDF/A-4 or PDF/A-4f (issue #203): the file is PDF 2.0, the identification names part 4 and revision 2020 (and level F
/// for 4f), the trailer keeps no document information dictionary, and the check step reports under the PDF/A-4 profile's rule
/// identifiers (veraPDF rules 6.1.2-1, 6.1.3-4, 6.1.3-5, 6.1.12-1, 6.6.3-1, 6.7.3-2 to 6.7.3-5, 6.9-1 to 6.9-5). veraPDF validates
/// these files in <see cref="PdfA4VeraPdfTests"/>.
/// </summary>
public sealed class PdfA4ConformanceTests
{
    private static readonly CosName Info = new("Info");

    public static TheoryData<string, string> EmbeddedFileViolations => new()
    {
        { "no relationship", "6.9-4" },
        { "no media type", "6.9-1" },
        { "no UF", "6.9-2" },
    };

    [Theory]
    [InlineData(PdfAConformance.PdfA4, null)]
    [InlineData(PdfAConformance.PdfA4f, "F")]
    public void The_identification_names_part_4_and_its_revision_and_the_file_is_pdf_2_0(PdfAConformance conformance, string? level)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = "Archive";
        PdfA2u3bConformanceTests.DrawRgbSquare(document);
        if (conformance == PdfAConformance.PdfA4f)
        {
            PdfA2u3bConformanceTests.AttachXmlSource(document);
        }

        byte[] saved = PdfA2u3bConformanceTests.Save(document, conformance);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.StartsWith("%PDF-2.0\n", Encoding.ASCII.GetString(saved, 0, 9), StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        string packet = Packet(reopened);
        Assert.Contains("<pdfaid:part>4</pdfaid:part>", packet, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:rev>2020</pdfaid:rev>", packet, StringComparison.Ordinal);
        if (level is null)
        {
            Assert.DoesNotContain("pdfaid:conformance", packet, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains($"<pdfaid:conformance>{level}</pdfaid:conformance>", packet, StringComparison.Ordinal);
        }

        Assert.Equal("Archive", reopened.Properties.Title);
        Assert.False(reopened.Trailer.ContainsKey(Info));
        Assert.Equal("GTS_PDFA1", Assert.Single(reopened.OutputIntents).Subtype);
    }

    [Fact]
    public void A_read_pdf_1_x_file_is_raised_to_2_0_and_its_information_dictionary_moves_to_the_packet()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("info-dictionary.pdf"));
        string? title = document.Properties.Title;
        document.Catalog[KnownNames.Version] = new CosName("1.7");

        byte[] saved = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.StartsWith("%PDF-2.0\n", Encoding.ASCII.GetString(saved, 0, 9), StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.False(reopened.Trailer.ContainsKey(Info));
        Assert.False(reopened.Catalog.ContainsKey(KnownNames.Version));
        Assert.NotNull(title);
        Assert.Equal(title, reopened.Properties.Title);
    }

    [Fact]
    public void With_piece_information_in_the_catalog_the_information_dictionary_keeps_only_its_modification_date()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("info-dictionary.pdf"));
        document.Catalog[new CosName("PieceInfo")] = new CosDictionary();

        byte[] saved = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4);

        using PdfDocument reopened = PdfDocument.Open(saved);
        var info = (CosDictionary)reopened.Resolve(reopened.Trailer[Info]);
        Assert.Equal([new CosName("ModDate")], info.Keys);
    }

    [Fact]
    public void An_incremental_pdf_a_4_update_of_a_pdf_1_x_file_is_reported_and_refused_in_strict_mode()
    {
        byte[] original = File.ReadAllBytes(Corpus.Path("empty-page.pdf"));
        using (PdfDocument lenient = PdfDocument.Open(original))
        {
            byte[] updated = PdfA2u3bConformanceTests.Save(lenient, PdfAConformance.PdfA4, incremental: true);

            Diagnostic reported = Assert.Single(lenient.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant);
            Assert.Contains("veraPDF rule 6.1.2-1, ISO 19005-4 clause 6.1.2", reported.Message, StringComparison.Ordinal);
            Assert.Contains(lenient.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANotIdentified);
            using PdfDocument reopened = PdfDocument.Open(updated);
            Assert.Empty(reopened.OutputIntents);
        }

        using PdfDocument strict = PdfDocument.Open(original, new PdfOptions().UseStrict());
        string before = CosSnapshot.Of(strict);

        DiagnosticException refused = Assert.Throws<DiagnosticException>(() => PdfA2u3bConformanceTests.Save(strict, PdfAConformance.PdfA4, incremental: true));

        Assert.Contains("veraPDF rule 6.1.2-1,", refused.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(before, CosSnapshot.Of(strict));
    }

    [Fact]
    public void An_embedded_file_is_reported_in_a_pdf_a_4_save_and_allowed_in_a_pdf_a_4f_save()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfA2u3bConformanceTests.DrawRgbSquare(document);
        PdfA2u3bConformanceTests.AttachXmlSource(document);

        byte[] asPdfA4 = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4);

        Diagnostic reported = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant);
        Assert.Contains("veraPDF rule 6.9-3,", reported.Message, StringComparison.Ordinal);
        using (PdfDocument reopened = PdfDocument.Open(asPdfA4))
        {
            Assert.Empty(reopened.OutputIntents);
        }

        using PdfDocument again = PdfDocument.Create();
        PdfA2u3bConformanceTests.DrawRgbSquare(again);
        PdfA2u3bConformanceTests.AttachXmlSource(again);
        PdfA2u3bConformanceTests.Save(again, PdfAConformance.PdfA4f);
        Assert.DoesNotContain(again.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
    }

    [Fact]
    public void A_pdf_a_4f_save_without_embedded_files_is_reported()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfA2u3bConformanceTests.DrawRgbSquare(document);

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4f);

        Diagnostic reported = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant);
        Assert.Contains("veraPDF rule 6.9-5,", reported.Message, StringComparison.Ordinal);
        Assert.Contains("PDF/A-4f", reported.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EmbeddedFileViolations))]
    public void Each_embedded_file_rule_of_pdf_a_4f_is_reported_by_its_verapdf_rule_and_refused_in_strict_mode(string violation, string rule)
    {
        using (PdfDocument lenient = PdfDocument.Create())
        {
            BreakEmbeddedFile(lenient, violation);

            PdfA2u3bConformanceTests.Save(lenient, PdfAConformance.PdfA4f);

            Diagnostic reported = Assert.Single(lenient.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant);
            Assert.Contains($"veraPDF rule {rule},", reported.Message, StringComparison.Ordinal);
            Assert.Contains(lenient.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANotIdentified);
        }

        using PdfDocument strict = PdfDocument.Create(new PdfOptions().UseStrict());
        BreakEmbeddedFile(strict, violation);
        string before = CosSnapshot.Of(strict);

        DiagnosticException refused = Assert.Throws<DiagnosticException>(() => PdfA2u3bConformanceTests.Save(strict, PdfAConformance.PdfA4f));

        Assert.Contains($"veraPDF rule {rule},", refused.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(before, CosSnapshot.Of(strict));
    }

    [Theory]
    [InlineData("page open action", PdfAConformance.PdfA2b, "6.5.2-2")]
    [InlineData("page open action", PdfAConformance.PdfA4, "6.6.3-1")]
    [InlineData("requirements", PdfAConformance.PdfA2b, "6.11-1")]
    [InlineData("interpolated image", PdfAConformance.PdfA3b, "6.2.8-3")]
    [InlineData("interpolated image", PdfAConformance.PdfA4f, "6.2.7.1-3")]
    [InlineData("requirements", PdfAConformance.PdfA4, "6.12-1")]
    public void A_rule_is_reported_by_the_identifier_of_the_level_s_profile(string violation, PdfAConformance conformance, string rule)
    {
        using PdfDocument document = PdfDocument.Create();
        Break(document, violation);
        if (conformance == PdfAConformance.PdfA4f)
        {
            PdfA2u3bConformanceTests.AttachXmlSource(document);
        }

        PdfA2u3bConformanceTests.Save(document, conformance);

        Diagnostic reported = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant);
        Assert.Contains($"veraPDF rule {rule}, ISO 19005-{conformance.ToString()[4]} clause {rule[..rule.LastIndexOf('-')]}", reported.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("page actions with an allowed trigger", "6.5.2-2")]
    [InlineData("JavaScript name tree", "6.5.1-1")]
    [InlineData("JavaScript open action", "6.5.1-1")]
    public void What_pdf_a_4_allows_and_pdf_a_2_forbids_is_reported_only_at_part_2(string violation, string pdfA2Rule)
    {
        using (PdfDocument document = PdfDocument.Create())
        {
            Break(document, violation);

            PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4);

            Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is DiagnosticCodes.PdfANonConformant or DiagnosticCodes.PdfANotIdentified);
        }

        using PdfDocument asPdfA2 = PdfDocument.Create();
        Break(asPdfA2, violation);

        PdfA2u3bConformanceTests.Save(asPdfA2, PdfAConformance.PdfA2b);

        Diagnostic reported = Assert.Single(asPdfA2.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant);
        Assert.Contains($"veraPDF rule {pdfA2Rule},", reported.Message, StringComparison.Ordinal);
    }

    internal static string Packet(PdfDocument document) => Encoding.UTF8.GetString(document.DecodeStream(document.Metadata!.Stream).Span);

    private static void Break(PdfDocument document, string violation)
    {
        CosDictionary page = document.Pages[0].Dictionary;
        switch (violation)
        {
            case "page open action":
                page[new CosName("AA")] = new CosDictionary { [new CosName("O")] = NamedAction("NextPage") };
                break;
            case "page actions with an allowed trigger":
                page[new CosName("AA")] = new CosDictionary { [new CosName("E")] = NamedAction("NextPage") };
                break;
            case "JavaScript open action":
                document.Catalog[new CosName("OpenAction")] = new CosDictionary { [new CosName("S")] = new CosName("JavaScript"), [new CosName("JS")] = new CosString("app.alert(1)"u8.ToArray()) };
                break;
            case "interpolated image":
                var image = new CosStream(
                    new CosDictionary
                    {
                        [new CosName("Type")] = new CosName("XObject"),
                        [new CosName("Subtype")] = new CosName("Image"),
                        [new CosName("Width")] = new CosInteger(1),
                        [new CosName("Height")] = new CosInteger(1),
                        [new CosName("ColorSpace")] = new CosName("DeviceRGB"),
                        [new CosName("BitsPerComponent")] = new CosInteger(8),
                        [new CosName("Interpolate")] = CosBoolean.True,
                    },
                    new byte[] { 0x33, 0x66, 0x99 });
                page[KnownNames.Resources] = new CosDictionary { [new CosName("XObject")] = new CosDictionary { [new CosName("Im0")] = document.AddObject(image) } };
                page[KnownNames.Contents] = document.AddObject(new CosStream(new CosDictionary(), "q 100 0 0 100 72 600 cm /Im0 Do Q"u8.ToArray()));
                break;
            case "requirements":
                document.Catalog[new CosName("Requirements")] = new CosArray();
                break;
            case "JavaScript name tree":
                document.Catalog[new CosName("Names")] = new CosDictionary
                {
                    [new CosName("JavaScript")] = new CosDictionary { [new CosName("Names")] = new CosArray() },
                };
                break;
        }

        static CosDictionary NamedAction(string name) => new()
        {
            [new CosName("S")] = new CosName("Named"),
            [new CosName("N")] = new CosName(name),
        };
    }

    private static void BreakEmbeddedFile(PdfDocument document, string violation)
    {
        var options = new PdfEmbeddedFileOptions().WithMediaType("text/plain");
        PdfFileSpecification file = document.CreateEmbeddedFile("notes.txt", "notes"u8, violation == "no relationship" ? options : options.WithRelationship(PdfFileRelationship.Supplement));
        document.AddEmbeddedFile("notes.txt", file);
        document.AddAssociatedFile(document.Catalog, file);
        switch (violation)
        {
            case "no media type":
                var files = (CosDictionary)document.Resolve(file.Dictionary![new CosName("EF")]);
                ((CosStream)document.Resolve(files[new CosName("F")])).Dictionary.Remove(new CosName("Subtype"));
                break;
            case "no UF":
                file.Dictionary!.Remove(new CosName("UF"));
                break;
        }
    }
}
