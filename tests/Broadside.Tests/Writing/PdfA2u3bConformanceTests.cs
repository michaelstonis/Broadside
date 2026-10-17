using System.Text;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Tests.Writing;

/// <summary>
/// Saving as PDF/A-2u, PDF/A-3b or PDF/A-3u (issue #202): level U needs a Unicode value for every glyph shown (veraPDF rules
/// 6.2.11.7.2-1 and -2), part 3 allows any embedded file as an associated file with a relationship (rules 6.8-1, 6.8-3, 6.8-4 instead
/// of 6.8-5), and the identification names the part and level. veraPDF validates these files in <see cref="PdfA2u3bVeraPdfTests"/>.
/// </summary>
public sealed class PdfA2u3bConformanceTests
{
    public static TheoryData<PdfAConformance, string, string> Identifications => new()
    {
        { PdfAConformance.PdfA2u, "2", "U" },
        { PdfAConformance.PdfA3b, "3", "B" },
        { PdfAConformance.PdfA3u, "3", "U" },
    };

    public static TheoryData<string, string> EmbeddedFileViolations => new()
    {
        { "no relationship", "6.8-3" },
        { "not associated", "6.8-4" },
        { "no media type", "6.8-1" },
    };

    [Theory]
    [MemberData(nameof(Identifications))]
    public void The_identification_names_the_part_and_level_and_the_file_is_pdf_1_7(PdfAConformance conformance, string part, string level)
    {
        using PdfDocument document = PdfDocument.Create();
        DrawType3Text(document, "A", toUnicode: "<41> <0041>");

        byte[] saved = Save(document, conformance);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.StartsWith("%PDF-1.7\n", Encoding.ASCII.GetString(saved, 0, 9), StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        string packet = Encoding.UTF8.GetString(reopened.DecodeStream(reopened.Metadata!.Stream).Span);
        Assert.Contains($"<pdfaid:part>{part}</pdfaid:part>", packet, StringComparison.Ordinal);
        Assert.Contains($"<pdfaid:conformance>{level}</pdfaid:conformance>", packet, StringComparison.Ordinal);
        Assert.Equal("GTS_PDFA1", Assert.Single(reopened.OutputIntents).Subtype);
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA2u)]
    [InlineData(PdfAConformance.PdfA3u)]
    public void A_glyph_without_a_unicode_mapping_is_reported_in_a_level_u_save_and_refused_in_strict_mode(PdfAConformance conformance)
    {
        using (PdfDocument lenient = PdfDocument.Create())
        {
            DrawType3Text(lenient, "g1", toUnicode: null);

            byte[] saved = Save(lenient, conformance);

            Diagnostic reported = Assert.Single(lenient.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant);
            Assert.Contains("veraPDF rule 6.2.11.7.2-1,", reported.Message, StringComparison.Ordinal);
            Assert.Contains("T3", reported.Message, StringComparison.Ordinal);
            Assert.Contains(lenient.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANotIdentified);
            using PdfDocument reopened = PdfDocument.Open(saved);
            Assert.Empty(reopened.OutputIntents);
        }

        using PdfDocument strict = PdfDocument.Create(new PdfOptions().UseStrict());
        DrawType3Text(strict, "g1", toUnicode: null);
        string before = CosSnapshot.Of(strict);

        DiagnosticException refused = Assert.Throws<DiagnosticException>(() => Save(strict, conformance));

        Assert.Contains("veraPDF rule 6.2.11.7.2-1,", refused.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(before, CosSnapshot.Of(strict));
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA2b)]
    [InlineData(PdfAConformance.PdfA3b)]
    public void A_glyph_without_a_unicode_mapping_is_allowed_at_level_b(PdfAConformance conformance)
    {
        using PdfDocument document = PdfDocument.Create();
        DrawType3Text(document, "g1", toUnicode: null);

        Save(document, conformance);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is DiagnosticCodes.PdfANonConformant or DiagnosticCodes.PdfANotIdentified);
    }

    [Theory]
    [InlineData("<41> <0000>")]
    [InlineData("<41> <FEFF>")]
    [InlineData("<41> <FFFE>")]
    public void A_unicode_value_of_zero_or_a_byte_order_mark_is_reported_in_a_level_u_save(string mapping)
    {
        using PdfDocument document = PdfDocument.Create();
        DrawType3Text(document, "g1", toUnicode: mapping);

        Save(document, PdfAConformance.PdfA2u);

        Diagnostic reported = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant);
        Assert.Contains("veraPDF rule 6.2.11.7.2-2,", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_glyph_name_from_the_adobe_glyph_list_maps_without_a_tounicode_cmap()
    {
        using PdfDocument document = PdfDocument.Create();
        DrawType3Text(document, "A", toUnicode: null);

        Save(document, PdfAConformance.PdfA2u);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is DiagnosticCodes.PdfANonConformant or DiagnosticCodes.PdfANotIdentified);
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA3b)]
    [InlineData(PdfAConformance.PdfA3u)]
    public void A_pdf_a_3_document_with_an_xml_source_attachment_conforms_and_stays_pdf_1_7(PdfAConformance conformance)
    {
        using PdfDocument document = PdfDocument.Create();
        DrawRgbSquare(document);
        AttachXmlSource(document);

        byte[] saved = Save(document, conformance);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.StartsWith("%PDF-1.7\n", Encoding.ASCII.GetString(saved, 0, 9), StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfFileSpecification file = Assert.Single(reopened.AssociatedFiles);
        Assert.Equal(PdfFileRelationship.Source, file.Relationship);
        Assert.Single(reopened.OutputIntents);
    }

    [Fact]
    public void An_incremental_pdf_a_3_update_adding_an_associated_file_keeps_the_version_at_1_7()
    {
        using PdfDocument document = PdfDocument.Create();
        DrawRgbSquare(document);
        byte[] first = Save(document, PdfAConformance.PdfA3b);
        using PdfDocument reopened = PdfDocument.Open(first);
        AttachXmlSource(reopened);

        byte[] updated = Save(reopened, PdfAConformance.PdfA3b, incremental: true);

        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        using PdfDocument again = PdfDocument.Open(updated);
        Assert.Equal(new PdfVersion(1, 7), again.Version);
        Assert.Single(again.AssociatedFiles);
    }

    [Fact]
    public void An_associated_attachment_is_still_reported_in_a_pdf_a_2_save()
    {
        using PdfDocument document = PdfDocument.Create();
        DrawRgbSquare(document);
        AttachXmlSource(document);

        Save(document, PdfAConformance.PdfA2b);

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant && diagnostic.Message.Contains("veraPDF rule 6.8-5,", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(EmbeddedFileViolations))]
    public void Each_embedded_file_rule_of_pdf_a_3_is_reported_by_its_verapdf_rule_and_refused_in_strict_mode(string violation, string rule)
    {
        foreach (bool strict in new[] { false, true })
        {
            using PdfDocument document = PdfDocument.Create(strict ? new PdfOptions().UseStrict() : new PdfOptions());
            DrawRgbSquare(document);
            BreakEmbeddedFile(document, violation);
            if (strict)
            {
                string before = CosSnapshot.Of(document);
                DiagnosticException refused = Assert.Throws<DiagnosticException>(() => Save(document, PdfAConformance.PdfA3b));
                Assert.Contains($"veraPDF rule {rule}, ISO 19005-3 clause 6.8", refused.Diagnostic.Message, StringComparison.Ordinal);
                Assert.Equal(before, CosSnapshot.Of(document));
            }
            else
            {
                Save(document, PdfAConformance.PdfA3b);
                Diagnostic reported = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant);
                Assert.Contains($"veraPDF rule {rule},", reported.Message, StringComparison.Ordinal);
                Assert.Contains("PDF/A-3b", reported.Message, StringComparison.Ordinal);
                Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANotIdentified);
            }
        }
    }

    /// <summary>Attaches an XML file as the document's source: embedded, listed and associated with the catalog (ISO 19005-3 §6.8).</summary>
    internal static void AttachXmlSource(PdfDocument document)
    {
        PdfFileSpecification file = document.CreateEmbeddedFile(
            "invoice.xml",
            "<?xml version=\"1.0\"?><invoice><total>42.00</total></invoice>"u8,
            new PdfEmbeddedFileOptions()
                .WithMediaType("text/xml")
                .WithRelationship(PdfFileRelationship.Source)
                .WithDescription("The invoice data this document was made from")
                .WithModificationDate(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)));
        document.AddEmbeddedFile("invoice.xml", file);
        document.AddAssociatedFile(document.Catalog, file);
    }

    /// <summary>
    /// Shows one glyph of a Type 3 font (which PDF/A does not require to be embedded) named <paramref name="glyphName"/> by the font's
    /// encoding, with a ToUnicode CMap mapping <paramref name="toUnicode"/> (a <c>bfchar</c> line) when it is not null.
    /// </summary>
    internal static void DrawType3Text(PdfDocument document, string glyphName, string? toUnicode)
    {
        var name = new CosName(glyphName);
        var font = new CosDictionary
        {
            [new CosName("Type")] = new CosName("Font"),
            [new CosName("Subtype")] = new CosName("Type3"),
            [new CosName("FontBBox")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(750), new CosInteger(750)]),
            [new CosName("FontMatrix")] = new CosArray([new CosReal(0.001), new CosInteger(0), new CosInteger(0), new CosReal(0.001), new CosInteger(0), new CosInteger(0)]),
            [new CosName("CharProcs")] = new CosDictionary { [name] = document.AddObject(new CosStream(new CosDictionary(), "750 0 0 0 750 750 d1 0 0 750 750 re f"u8.ToArray())) },
            [new CosName("Encoding")] = new CosDictionary
            {
                [new CosName("Type")] = new CosName("Encoding"),
                [new CosName("Differences")] = new CosArray([new CosInteger(65), name]),
            },
            [new CosName("FirstChar")] = new CosInteger(65),
            [new CosName("LastChar")] = new CosInteger(65),
            [new CosName("Widths")] = new CosArray([new CosInteger(750)]),
            [new CosName("Resources")] = new CosDictionary(),
        };
        if (toUnicode is not null)
        {
            string cmap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def "
                + "/CMapName /Adobe-Identity-UCS def /CMapType 2 def 1 begincodespacerange <00> <FF> endcodespacerange "
                + $"1 beginbfchar {toUnicode} endbfchar endcmap CMapName currentdict /CMap defineresource pop end end";
            font[new CosName("ToUnicode")] = document.AddObject(new CosStream(new CosDictionary(), Encoding.ASCII.GetBytes(cmap)));
        }

        CosDictionary page = document.Pages[0].Dictionary;
        page[KnownNames.Resources] = new CosDictionary { [new CosName("Font")] = new CosDictionary { [new CosName("T3")] = document.AddObject(font) } };
        page[new CosName("Contents")] = document.AddObject(new CosStream(new CosDictionary(), "BT /T3 24 Tf 72 700 Td (AA) Tj [(A) -250 (A)] TJ ET"u8.ToArray()));
    }

    internal static void DrawRgbSquare(PdfDocument document)
    {
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        canvas.SetFillColor(PdfColor.FromRgb(0.2f, 0.4f, 0.8f));
        canvas.Rectangle(72, 600, 100, 100);
        canvas.Fill();
    }

    internal static byte[] Save(PdfDocument document, PdfAConformance conformance, bool incremental = false)
    {
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithPdfAConformance(conformance).WithIncrementalUpdate(incremental));
        return stream.ToArray();
    }

    private static void BreakEmbeddedFile(PdfDocument document, string violation)
    {
        var options = new PdfEmbeddedFileOptions().WithMediaType("text/plain");
        PdfFileSpecification file = document.CreateEmbeddedFile("notes.txt", "notes"u8, violation == "no relationship" ? options : options.WithRelationship(PdfFileRelationship.Supplement));
        document.AddEmbeddedFile("notes.txt", file);
        if (violation != "not associated")
        {
            document.AddAssociatedFile(document.Catalog, file);
        }

        if (violation == "no media type")
        {
            var files = (CosDictionary)document.Resolve(file.Dictionary![new CosName("EF")]);
            ((CosStream)document.Resolve(files[new CosName("F")])).Dictionary.Remove(new CosName("Subtype"));
        }
    }
}
