using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>The engine and options: one open path for the static entry point and engine instances, lenient and strict reading (ADR 0005).</summary>
public class EngineTests
{
    [Theory]
    [MemberData(nameof(Corpus.WellFormedFiles), MemberType = typeof(Corpus))]
    public void The_static_entry_point_and_an_engine_instance_open_documents_identically(string fileName)
    {
        var engine = new PdfEngine(new PdfOptions());
        using PdfDocument fromEngine = engine.Open(Corpus.Bytes(fileName));
        using PdfDocument fromStatic = PdfDocument.Open(Corpus.Bytes(fileName));

        Assert.Equal(DocumentProjection.Of(fromStatic), DocumentProjection.Of(fromEngine));
    }

    [Fact]
    public void Strict_mode_throws_the_first_deviation_and_lenient_mode_records_it()
    {
        byte[] file = TestPdf.OnePage(string.Empty);

        using (PdfDocument lenient = PdfDocument.Open(file, new PdfOptions().UseLenient()))
        {
            Assert.Single(lenient.Pages);
            Assert.Equal("PageMediaBoxMissing", Assert.Single(lenient.Diagnostics).Code);
        }

        using PdfDocument strict = new PdfEngine(new PdfOptions().UseStrict()).Open(file);
        DiagnosticException error = Assert.Throws<DiagnosticException>(() => strict.Pages.Count);
        Assert.Equal("PageMediaBoxMissing", error.Diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Diagnostic.Severity);
        Assert.IsAssignableFrom<FormatException>(error);
    }

    [Fact]
    public void Strict_mode_throws_during_open_for_a_deviation_in_what_open_reads()
    {
        byte[] file = new TestPdf().Build("<< /Pages 2 0 R >>", "<< /Type /Pages /Kids [] /Count 0 >>");

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => PdfDocument.Open(file, new PdfOptions().UseStrict()));

        Assert.Equal("CatalogTypeInvalid", error.Diagnostic.Code);
        Assert.Equal(new CosReference(1, 0), error.Diagnostic.ObjectReference);
    }

    [Fact]
    public void Strict_mode_throws_from_the_member_that_loads_a_deviating_object()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("missing-endobj.pdf"), new PdfOptions().UseStrict());

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => document.Pages.Count);

        Assert.Equal("MissingEndobj", error.Diagnostic.Code);
        Assert.Equal(new CosReference(3, 0), error.Diagnostic.ObjectReference);
    }

    [Theory]
    [MemberData(nameof(Corpus.WellFormedFiles), MemberType = typeof(Corpus))]
    public void Strict_mode_opens_every_well_formed_file(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName), new PdfOptions().UseStrict());

        Assert.NotEmpty(document.Pages);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity > Broadside.Diagnostics.DiagnosticSeverity.Information);
    }

    [Fact]
    public void An_engine_copies_its_options_so_later_changes_do_not_affect_it()
    {
        var options = new PdfOptions();
        var engine = new PdfEngine(options);
        options.UseStrict();

        using PdfDocument document = engine.Open(TestPdf.OnePage(string.Empty));

        Assert.Single(document.Pages);
        Assert.Equal(PdfReadingMode.Strict, options.ReadingMode);
        Assert.Equal("PageMediaBoxMissing", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Options_are_lenient_by_default_and_fluent()
    {
        var options = new PdfOptions();

        Assert.Equal(PdfReadingMode.Lenient, options.ReadingMode);
        Assert.Same(options, options.UseStrict());
        Assert.Equal(PdfReadingMode.Strict, options.ReadingMode);
        Assert.Same(options, options.UseLenient());
        Assert.Equal(PdfReadingMode.Lenient, options.ReadingMode);
    }

    [Fact]
    public void A_disposed_document_cannot_load_more_objects()
    {
        PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        document.Dispose();

        Assert.Throws<ObjectDisposedException>(() => document.Pages.Count);
    }
}
