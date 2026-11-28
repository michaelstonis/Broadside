using System.Globalization;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.Writing;

namespace Broadside.Tests.StructureTree;

/// <summary>
/// PDF/UA validation of a document as read, without a save (issue #652): the checks of a PDF/UA save on the file's own values, each
/// PDF/UA-1 violation cited by its Matterhorn Protocol 1.1 checkpoint, PDF/UA-2 ones by ISO 14289-2 clause, structure repairs reported
/// as violations, and the coverage lists honest about what is not checked. ISO 14289-1 clauses 5 to 7; ISO 14289-2 clauses 5 to 8.
/// </summary>
public sealed class PdfUAValidationTests
{
    private const string Catalog = "/Lang (en-US) /ViewerPreferences << /DisplayDocTitle true >> /Metadata 7 0 R";

    /// <summary>The ISO 14289-1 clauses issue #527 still has to cover.</summary>
    private static readonly string[] Issue527PdfUA1 = ["7.1", "7.2", "7.7", "7.10", "7.11", "7.12", "7.13", "7.14", "7.16", "7.17", "7.18", "7.19", "7.20", "7.21"];

    /// <summary>The ISO 14289-2 clauses issue #527 still has to cover.</summary>
    private static readonly string[] Issue527PdfUA2 = ["8.2", "8.4", "8.5", "8.6", "8.7", "8.8", "8.9", "8.10", "8.12", "8.13", "8.14"];

    [Fact]
    public void A_document_that_meets_every_rule_checked_and_claims_PDF_UA_1_validates_with_no_violations()
    {
        using PdfDocument document = PdfDocument.Open(Tagged());

        PdfUAValidation validation = document.ValidatePdfUA();

        Assert.Equal(PdfUAConformance.PdfUA1, validation.Conformance);
        Assert.True(validation.IsClaimed);
        Assert.Empty(validation.Violations);
        Assert.True(validation.IsConformant);
        Assert.Equal(PdfWellTaggedConformance.None, validation.WellTagged);
    }

    [Fact]
    public void A_document_saved_as_PDF_UA_1_validates_with_no_violations_after_it_is_read_back()
    {
        using PdfDocument document = PdfDocument.Open(Tagged(catalogEntries: "/Lang (en-US)", metadata: null));
        document.Properties.Title = "Annual report";
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is "PdfUANonConformant" or "PdfUANotIdentified");

        using PdfDocument reopened = PdfDocument.Open(stream.ToArray());
        PdfUAValidation validation = reopened.ValidatePdfUA();

        Assert.True(validation.IsClaimed);
        Assert.Empty(validation.Violations);
    }

    [Fact]
    public void What_a_save_would_write_is_checked_on_the_file_and_validation_changes_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Tagged(catalogEntries: "/Lang (en-US)", metadata: null, marked: false, pageEntries: "/Annots [8 0 R]"));
        int entries = document.Catalog.Count;

        PdfUAValidation validation = document.ValidatePdfUA(PdfUAConformance.PdfUA1);

        Assert.False(validation.IsClaimed);
        Assert.Equal(
            [("5-1", "06-002"), ("6.2-1", null), ("7.1-8", "06-001"), ("7.1-10", "07-001"), ("7.1-9", "06-003"), ("7.18.3-1", "28-008")],
            validation.Violations.Where(violation => violation.Rule is "5-1" or "6.2-1" or "7.1-8" or "7.1-9" or "7.1-10" or "7.18.3-1").Select(violation => (violation.Rule, violation.Checkpoint)));
        Assert.Equal(entries, document.Catalog.Count);
        Assert.Null(document.Metadata);
        Assert.False(document.MarkInfo.Marked);
        Assert.False(document.Pages[0].Dictionary.ContainsKey(new CosName("Tabs")));
    }

    [Fact]
    public void DisplayDocTitle_false_and_Tabs_other_than_S_cite_their_own_checkpoints()
    {
        using PdfDocument document = PdfDocument.Open(Tagged(catalogEntries: "/Lang (en-US) /ViewerPreferences << /DisplayDocTitle false >> /Metadata 7 0 R", pageEntries: "/Annots [8 0 R] /Tabs /R"));

        PdfUAValidation validation = document.ValidatePdfUA();

        Assert.Contains(validation.Violations, violation => violation is { Rule: "7.1-10", Checkpoint: "07-002" });
        Assert.Contains(validation.Violations, violation => violation is { Rule: "7.18.3-1", Checkpoint: "28-009" } && violation.Reference is not null);
    }

    [Fact]
    public void An_untagged_file_fails_once_as_not_tagged_with_no_walk_of_its_content()
    {
        byte[] file = new TestPdf().Build(
            $"<< /Type /Catalog /Pages 2 0 R {Catalog} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /Contents 4 0 R /Annots [5 0 R] /Tabs /S >>",
            Stream("0 0 10 10 re f"),
            "<< /Type /Annot /Subtype /Square /Rect [0 0 10 10] >>",
            "<< >>",
            Metadata(part: "1"));
        using PdfDocument document = PdfDocument.Open(file);

        PdfUAValidation validation = document.ValidatePdfUA();

        PdfUARuleViolation untagged = Assert.Single(validation.Violations);
        Assert.Equal("7.1-11", untagged.Rule);
        Assert.Equal("01-005", untagged.Checkpoint);
        Assert.False(validation.IsConformant);
    }

    [Fact]
    public void A_missing_Lang_maps_to_Matterhorn_checkpoint_11_006()
    {
        using PdfDocument document = PdfDocument.Open(Tagged(catalogEntries: "/ViewerPreferences << /DisplayDocTitle true >> /Metadata 7 0 R"));

        PdfUAValidation validation = document.ValidatePdfUA();

        PdfUARuleViolation language = Assert.Single(validation.Violations);
        Assert.Equal("7.2-33", language.Rule);
        Assert.Equal("11-006", language.Checkpoint);
    }

    [Fact]
    public void A_figure_without_alternate_text_maps_to_13_004_with_its_element()
    {
        using PdfDocument document = PdfDocument.Open(Tagged(kids: "[9 0 R]", extra: "<< /Type /StructElem /S /Figure /P 5 0 R >>"));

        PdfUARuleViolation figure = Assert.Single(document.ValidatePdfUA().Violations);

        Assert.Equal(("7.3-1", "13-004"), (figure.Rule, figure.Checkpoint));
        Assert.Equal(new CosReference(9, 0), figure.Reference);
    }

    [Fact]
    public void PDF_UA_2_rules_cite_ISO_14289_2_clauses_and_a_PDF_UA_1_claim_is_not_a_PDF_UA_2_claim()
    {
        using PdfDocument document = PdfDocument.Open(Tagged(kids: "[9 0 R]", extra: "<< /Type /StructElem /S /Figure /P 5 0 R >>"));

        PdfUAValidation validation = document.ValidatePdfUA(PdfUAConformance.PdfUA2);

        Assert.Equal(PdfUAConformance.PdfUA2, validation.Conformance);
        Assert.False(validation.IsClaimed);
        Assert.Contains(validation.Violations, violation => violation.Rule == "5-2");
        Assert.Contains(validation.Violations, violation => violation.Rule == "8.2.5.28.2-1");
        Assert.Contains(validation.Violations, violation => violation.Rule == "8.2.5.2-2");
        Assert.All(validation.Violations, violation =>
        {
            Assert.Null(violation.Checkpoint);
            Assert.Matches(@"^(5|6\.2|8(\.[0-9]+)+)-[0-9]+$|^Table 5\. ", violation.Rule);
        });
    }

    [Theory]
    [InlineData(null, "2024", "5-3")]
    [InlineData("2", null, "5-4")]
    [InlineData("2", "2018", "5-5")]
    [InlineData("2", "2024a", "5-5")]
    [InlineData("3", "2024", "5-2")]
    public void The_PDF_UA_2_identification_is_checked(string? part, string? revision, string rule)
    {
        using PdfDocument document = PdfDocument.Open(Tagged(metadata: Metadata(part, revision)));

        PdfUAValidation validation = document.ValidatePdfUA(PdfUAConformance.PdfUA2);

        Assert.Equal([rule], validation.Violations.Select(violation => violation.Rule).Where(id => id.StartsWith("5-", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_declared_WTPDF_level_is_checked_with_the_PDF_UA_2_rules_without_pdfuaid()
    {
        string declarations =
            "<pdfd:declarations xmlns:pdfd=\"http://pdfa.org/declarations/\"><rdf:Bag>"
            + $"<rdf:li rdf:parseType=\"Resource\"><pdfd:conformsTo>{PdfDeclaration.WellTaggedReuse}</pdfd:conformsTo></rdf:li>"
            + $"<rdf:li rdf:parseType=\"Resource\"><pdfd:conformsTo>{PdfDeclaration.WellTaggedAccessibility}</pdfd:conformsTo></rdf:li>"
            + "</rdf:Bag></pdfd:declarations>";
        using PdfDocument document = PdfDocument.Open(Tagged(
            kids: "[9 0 R]",
            extra: "<< /Type /StructElem /S /Figure /P 5 0 R /NS 10 0 R >>",
            documentEntries: "/NS 10 0 R",
            rootEntries: "/Namespaces [10 0 R]",
            metadata: Metadata(part: null, extra: declarations),
            more: ["<< /Type /Namespace /NS (http://iso.org/pdf2/ssn) >>"]));

        PdfUAValidation validation = document.ValidatePdfUA(PdfUAConformance.PdfUA1);

        Assert.Equal(PdfWellTaggedConformance.Reuse | PdfWellTaggedConformance.Accessibility, validation.WellTagged);
        Assert.Equal(PdfWellTaggedConformance.Reuse, validation.WellTaggedConformant);
    }

    [Fact]
    public void A_structure_tree_repaired_on_reading_is_reported_under_the_rule_it_breaks()
    {
        using PdfDocument document = PdfDocument.Open(Tagged(
            kids: "[9 0 R]",
            extra: "<< /Type /StructElem /S /Div /P 5 0 R /K [10 0 R] >>",
            more: ["<< /Type /StructElem /S /Div /P 9 0 R /K [9 0 R] >>"]));

        PdfUAValidation validation = document.ValidatePdfUA();

        PdfUARuleViolation repaired = Assert.Single(validation.Violations);
        Assert.Equal(("7.1-11", "01-005"), (repaired.Rule, repaired.Checkpoint));
        Assert.Contains("StructTreeCycle", repaired.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_with_no_claim_needs_a_level_and_None_is_refused()
    {
        using PdfDocument document = PdfDocument.Open(Tagged(metadata: Metadata(part: null)));

        Assert.Throws<InvalidOperationException>(() => document.ValidatePdfUA());
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ValidatePdfUA(PdfUAConformance.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ValidatePdfUA((PdfUAConformance)7));
    }

    [Fact]
    public void CheckpointsNotChecked_matches_the_issue_527_list()
    {
        using PdfDocument document = PdfDocument.Open(Tagged());

        PdfUAValidation part1 = document.ValidatePdfUA(PdfUAConformance.PdfUA1);
        PdfUAValidation part2 = document.ValidatePdfUA(PdfUAConformance.PdfUA2);

        Assert.Equal(MatterhornProtocol.NotChecked, part1.CheckpointsNotChecked);
        Assert.Equal(45, part1.CheckpointsNotChecked.Count);
        Assert.All(part1.CheckpointsNotChecked, index =>
        {
            string clause = MatterhornProtocol.Conditions.Single(condition => condition.Index == index).Clause;
            Assert.Contains(Issue527PdfUA1, issue => clause == issue || clause.StartsWith(issue + ".", StringComparison.Ordinal));
        });
        Assert.Equal(Issue527PdfUA2, part2.CheckpointsNotChecked);
        Assert.Equal(MatterhornProtocol.Human, part1.MachineUncheckable);
        Assert.Contains("13-008", part1.MachineUncheckable);
    }

    [Fact]
    public void The_Matterhorn_table_has_every_failure_condition_and_cites_only_known_ones()
    {
        IReadOnlyList<(string Index, string Clause, MatterhornProtocol.How How)> conditions = MatterhornProtocol.Conditions;

        Assert.Equal(137, conditions.Count);
        Assert.Equal(conditions.Count, conditions.Select(condition => condition.Index).Distinct().Count());
        Assert.Equal(87, conditions.Count(condition => condition.How == MatterhornProtocol.How.Machine));
        Assert.Equal(48, conditions.Count(condition => condition.How == MatterhornProtocol.How.Human));
        Assert.Equal(["23-001", "27-001"], conditions.Where(condition => condition.How == MatterhornProtocol.How.None).Select(condition => condition.Index));
        HashSet<string> machine = [.. conditions.Where(condition => condition.How == MatterhornProtocol.How.Machine).Select(condition => condition.Index)];
        Assert.Subset(machine, MatterhornProtocol.Checked.ToHashSet());
        Assert.Subset(machine, MatterhornProtocol.CheckpointOfRule.Values.OfType<string>().ToHashSet());
        Assert.Contains("11-001", MatterhornProtocol.NotChecked);
    }

    /// <summary>
    /// A one-page tagged file: 1 catalog, 2 pages, 3 page (an artifact square), 4 structure tree root, 5 the Document element with
    /// <paramref name="kids"/>, 6 the content, 7 the XMP metadata, 8 a Square annotation in an Annot element (9 + extras after it).
    /// </summary>
    private static byte[] Tagged(
        string catalogEntries = Catalog,
        string? metadata = "default",
        bool marked = true,
        string pageEntries = "",
        string kids = "[]",
        string? extra = null,
        string documentEntries = "",
        string rootEntries = "",
        string[]? more = null)
    {
        bool annotated = pageEntries.Contains("/Annots", StringComparison.Ordinal);
        List<string> objects =
        [
            $"<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked {(marked ? "true" : "false")} >> /StructTreeRoot 4 0 R {catalogEntries} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /Contents 6 0 R /StructParents 0 {pageEntries} >>",
            $"<< /Type /StructTreeRoot /K [5 0 R] /ParentTree 11 0 R {rootEntries} >>",
            $"<< /Type /StructElem /S /Document /P 4 0 R /K {(annotated ? "[12 0 R]" : kids)} {documentEntries} >>",
            Stream("/Artifact BMC 0 0 10 10 re f EMC"),
            metadata == "default" ? Metadata(part: "1") : metadata ?? "<< >>",
            "<< /Type /Annot /Subtype /Square /Rect [0 0 10 10] /Contents (A box) /StructParent 1 >>",
            extra ?? "<< >>",
        ];
        objects.AddRange(more ?? ["<< >>"]);
        objects.Add(annotated ? "<< /Nums [0 [] 1 12 0 R] >>" : "<< /Nums [] >>");
        objects.Add("<< /Type /StructElem /S /Annot /P 5 0 R /Pg 3 0 R /K [<< /Type /OBJR /Obj 8 0 R >>] >>");
        return new TestPdf().Build([.. objects]);
    }

    private static string Stream(string data) => string.Create(CultureInfo.InvariantCulture, $"<< /Length {data.Length} >>\nstream\n{data}\nendstream");

    private static string Metadata(string? part, string? revision = null, string extra = "")
    {
        string identification = (part is null ? string.Empty : $"<pdfuaid:part>{part}</pdfuaid:part>") + (revision is null ? string.Empty : $"<pdfuaid:rev>{revision}</pdfuaid:rev>");
        string xmp =
            "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
            + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"\" "
            + "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:pdfuaid=\"http://www.aiim.org/pdfua/ns/id/\">"
            + "<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">Annual report</rdf:li></rdf:Alt></dc:title>"
            + identification + extra
            + "</rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>";
        return string.Create(CultureInfo.InvariantCulture, $"<< /Type /Metadata /Subtype /XML /Length {xmp.Length} >>\nstream\n{xmp}\nendstream");
    }
}
