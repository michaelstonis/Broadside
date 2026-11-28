using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Tests.Writing;

/// <summary>
/// Saving as PDF/A-2a or PDF/A-3a (issue #650): level U and the tagged logical structure of ISO 19005-2 and -3 clause 6.7. The check
/// step reports veraPDF's level A rules (6.7.2.2-1, 6.7.3.3-1, 6.7.3.4-1 to -3, 6.7.4-1, 6.2.11.7.3-1) and Broadside's own level A
/// checks (content tagged, a language, alternate text), which name the ISO 32000-2 clause instead; strict mode refuses, lenient mode
/// saves without the identification. PDF/A-4e's rules are in <see cref="PdfALevelAVeraPdfTests"/>.
/// </summary>
public sealed class PdfALevelAConformanceTests
{
    private const string NonConformant = "PdfANonConformant";
    private const string NotIdentified = "PdfANotIdentified";

    [Theory]
    [InlineData(PdfAConformance.PdfA2a, "2")]
    [InlineData(PdfAConformance.PdfA3a, "3")]
    public void A_tagged_document_saves_as_level_A_and_is_identified_as_conformance_A(PdfAConformance conformance, string part)
    {
        using PdfDocument document = TaggedDocument();

        byte[] saved = PdfA2u3bConformanceTests.Save(document, conformance);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        using PdfDocument reopened = PdfDocument.Open(saved);
        string packet = PdfA4ConformanceTests.Packet(reopened);
        Assert.Contains($"<pdfaid:part>{part}</pdfaid:part>", packet, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:conformance>A</pdfaid:conformance>", packet, StringComparison.Ordinal);
        Assert.True(reopened.MarkInfo.Marked);
    }

    [Fact]
    public void A_save_marks_a_document_with_a_structure_tree_as_tagged()
    {
        using PdfDocument document = TaggedDocument();
        document.MarkInfo.Marked = false;

        byte[] saved = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2a);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.True(reopened.MarkInfo.Marked);
    }

    [Fact]
    public void An_untagged_canvas_document_fails_the_structure_rule_and_is_saved_without_the_identification()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfA2u3bConformanceTests.DrawType3Text(document, "g1", toUnicode: "<41> <0041>");

        byte[] saved = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2a);

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant && diagnostic.Message.Contains("veraPDF rule 6.7.3.3-1,", StringComparison.Ordinal));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == NotIdentified);
        Assert.Equal(["6.7.3.3-1", "6.7.2.2-1"], Rules(document, PdfAConformance.PdfA2a));
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Null(reopened.Metadata?.Packet?.PdfAPart);
    }

    [Fact]
    public void Strict_mode_refuses_a_level_A_save_of_an_untagged_document()
    {
        using PdfDocument strict = PdfDocument.Create(new PdfOptions().UseStrict());
        PdfA2u3bConformanceTests.DrawType3Text(strict, "g1", toUnicode: "<41> <0041>");

        DiagnosticException refused = Assert.Throws<DiagnosticException>(() => PdfA2u3bConformanceTests.Save(strict, PdfAConformance.PdfA3a));

        Assert.Equal(NonConformant, refused.Diagnostic.Code);
        Assert.Contains("PDF/A-3a", refused.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Level_U_and_B_do_not_need_a_structure_tree()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfA2u3bConformanceTests.DrawType3Text(document, "g1", toUnicode: "<41> <0041>");

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2u);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
    }

    [Fact]
    public void A_document_with_no_language_fails_the_language_check()
    {
        using PdfDocument document = TaggedDocument(language: null);

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2a);

        Diagnostic reported = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
        Assert.Contains("rule ISO 32000-2 14.9.2-1, a level A check Broadside makes beyond veraPDF's profile", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_language_on_every_structure_element_that_encloses_content_is_enough()
    {
        using PdfDocument document = TaggedDocument(language: null);
        document.StructureTree!.Children[0].Language = "en";

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2a);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
    }

    [Theory]
    [InlineData("en_US", "the catalog's Lang")]
    [InlineData("element", "a structure element's Lang")]
    public void A_malformed_language_fails_rule_6_7_4_1(string where, string what)
    {
        using PdfDocument document = TaggedDocument();
        if (where == "element")
        {
            document.StructureTree!.Children[0].Language = "en US";
        }
        else
        {
            document.Catalog[new CosName("Lang")] = new CosString(Encoding.ASCII.GetBytes(where));
        }

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2a);

        Diagnostic reported = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
        Assert.Contains("veraPDF rule 6.7.4-1,", reported.Message, StringComparison.Ordinal);
        Assert.Contains(what, reported.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Figure")]
    [InlineData("Formula")]
    public void A_figure_or_formula_without_alternate_text_fails_the_description_check(string type)
    {
        using PdfDocument document = TaggedDocument();
        PdfStructureElement figure = AddFigure(document, type);

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2a);

        Diagnostic reported = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
        Assert.Contains("rule ISO 32000-2 14.9.3-1", reported.Message, StringComparison.Ordinal);
        Assert.Contains($"a {type} has no alternate text", reported.Message, StringComparison.Ordinal);
        Assert.Equal(figure.Reference, reported.ObjectReference);
    }

    [Fact]
    public void A_figure_with_alternate_text_passes()
    {
        using PdfDocument document = TaggedDocument();
        AddFigure(document, "Figure").AlternateDescription = "A blue square";

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2a);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
    }

    [Fact]
    public void An_unmapped_structure_type_fails_rule_6_7_3_4_1_and_strict_mode_refuses()
    {
        using PdfDocument document = TaggedDocument(type: "Para");

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2a);

        Diagnostic reported = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
        Assert.Contains("veraPDF rule 6.7.3.4-1,", reported.Message, StringComparison.Ordinal);
        Assert.Contains("Para", reported.Message, StringComparison.Ordinal);

        using PdfDocument strict = TaggedDocument(type: "Para", strict: true);
        DiagnosticException refused = Assert.Throws<DiagnosticException>(() => PdfA2u3bConformanceTests.Save(strict, PdfAConformance.PdfA2a));
        Assert.Contains("6.7.3.4-1", refused.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_role_mapped_structure_type_passes()
    {
        using PdfDocument document = TaggedDocument(type: "Para");
        document.StructureTree!.MapRole(new CosName("Para"), new CosName("P"));

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2a);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
    }

    [Fact]
    public void A_circular_role_mapping_fails_rule_6_7_3_4_2_and_a_remapped_standard_type_rule_6_7_3_4_3()
    {
        using PdfDocument document = TaggedDocument(type: "Para");
        document.StructureTree!.MapRole(new CosName("Para"), new CosName("Body"));
        document.StructureTree.MapRole(new CosName("Body"), new CosName("Para"));
        document.StructureTree.MapRole(new CosName("Sect"), new CosName("Div"));

        // The diagnostics keep one PdfANonConformant per object, so the rules are read from the check step.
        Assert.Equal(["6.7.3.4-3", "6.7.3.4-1", "6.7.3.4-2"], Rules(document, PdfAConformance.PdfA2a));
    }

    [Fact]
    public void Content_neither_tagged_nor_an_artifact_fails_the_tagging_check()
    {
        using PdfDocument document = TaggedDocument();
        AppendContent(document, " 0 0 1 rg 72 400 50 50 re f");

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2a);

        Diagnostic reported = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
        Assert.Contains("rule ISO 32000-2 14.8.2.2-1", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Content_marked_as_an_artifact_passes()
    {
        using PdfDocument document = TaggedDocument();
        AppendContent(document, " /Artifact BMC 0 0 1 rg 72 400 50 50 re f EMC");

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2a);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void A_glyph_mapped_to_the_private_use_area_needs_actual_text(bool actualText, bool reported)
    {
        using PdfDocument document = TaggedDocument(toUnicode: "<41> <E000>");
        if (actualText)
        {
            document.StructureTree!.Children[0].Children.OfType<PdfStructureElement>().Single().ActualText = "AA";
        }

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2a);

        Assert.Equal(reported, document.Diagnostics.Any(diagnostic => diagnostic.Code == NonConformant && diagnostic.Message.Contains("veraPDF rule 6.2.11.7.3-1,", StringComparison.Ordinal)));
    }

    [Fact]
    public void ConvertToPdfA_level_A_auto_tags_an_untagged_document_and_the_result_passes()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = "Converted";
        PdfA2u3bConformanceTests.DrawType3Text(document, "g1", toUnicode: "<41> <0041>");
        SetContent(document, "BT /T3 24 Tf 72 700 Td (AA) Tj ET");
        document.Catalog[new CosName("Lang")] = new CosString("en-US"u8.ToArray());

        PdfAConversion conversion = document.ConvertToPdfA(PdfAConformance.PdfA2a);

        Assert.True(conversion.IsConformant, string.Join("; ", conversion.Violations.Select(violation => $"{violation.Rule}: {violation.Message}")));
        Assert.Contains(conversion.Changes, change => change.Contains("AutoTag", StringComparison.Ordinal));
        Assert.NotNull(document.StructureTree);
        byte[] saved = Save(document, conversion.CreateSaveOptions());
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is NonConformant or NotIdentified);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Contains("<pdfaid:conformance>A</pdfaid:conformance>", PdfA4ConformanceTests.Packet(reopened), StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertToPdfA_level_A_keeps_an_existing_structure_tree()
    {
        using PdfDocument document = TaggedDocument();
        CosDictionary root = document.StructureTree!.Dictionary;

        PdfAConversion conversion = document.ConvertToPdfA(PdfAConformance.PdfA3a);

        Assert.True(conversion.IsConformant);
        Assert.DoesNotContain(conversion.Changes, change => change.Contains("AutoTag", StringComparison.Ordinal));
        Assert.Same(root, document.StructureTree!.Dictionary);
    }

    /// <summary>
    /// A one-page document whose Type 3 text (two glyphs, mapped to Unicode by <paramref name="toUnicode"/>) is a <paramref name="type"/>
    /// element under <c>Document</c>, in the language <paramref name="language"/> when it is not null.
    /// </summary>
    internal static PdfDocument TaggedDocument(string? language = "en-US", string type = "P", string toUnicode = "<41> <0041>", bool strict = false)
    {
        PdfDocument document = PdfDocument.Create(strict ? new PdfOptions().UseStrict() : new PdfOptions());
        document.Properties.Title = "Tagged archive";
        PdfA2u3bConformanceTests.DrawType3Text(document, "g1", toUnicode);
        PdfPage page = document.Pages[0];
        PdfStructureTreeRoot root = document.CreateStructureTree();
        PdfStructureElement element = root.AddElement("Document").AddElement(type);
        int mcid = element.AddMarkedContent(page).Mcid;
        SetContent(document, $"/{type} <</MCID {mcid}>> BDC BT /T3 24 Tf 72 700 Td (AA) Tj [(A) -250 (A)] TJ ET EMC");
        if (language is not null)
        {
            document.Catalog[new CosName("Lang")] = new CosString(Encoding.ASCII.GetBytes(language));
        }

        return document;
    }

    /// <summary>Adds a <paramref name="type"/> element (a figure or formula) painting a square, without alternate text.</summary>
    internal static PdfStructureElement AddFigure(PdfDocument document, string type)
    {
        PdfStructureElement figure = document.StructureTree!.Children[0].AddElement(type);
        int mcid = figure.AddMarkedContent(document.Pages[0]).Mcid;
        AppendContent(document, $" /{type} <</MCID {mcid}>> BDC 0 0 1 rg 72 500 100 100 re f EMC");
        return figure;
    }

    private static void SetContent(PdfDocument document, string content) =>
        document.Pages[0].Dictionary[new CosName("Contents")] = document.AddObject(new CosStream(new CosDictionary(), Encoding.ASCII.GetBytes(content)));

    private static void AppendContent(PdfDocument document, string content)
    {
        var stream = (CosStream)document.Resolve(document.Pages[0].Dictionary[new CosName("Contents")]);
        SetContent(document, Encoding.ASCII.GetString(document.DecodeStream(stream).Span) + content);
    }

    /// <summary>The rules the check step of a save at <paramref name="conformance"/> reports, in order.</summary>
    private static string[] Rules(PdfDocument document, PdfAConformance conformance) =>
        [.. Broadside.Writing.PdfAChecker.Check(document, new PdfSaveOptions().WithPdfAConformance(conformance)).Select(violation => violation.Rule)];

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var stream = new MemoryStream();
        document.Save(stream, options);
        return stream.ToArray();
    }
}
