using Broadside.Diagnostics;

namespace Broadside.Tests.Document;

/// <summary>
/// <see cref="PdfDocument.ReportDiagnostic"/> (issue #397, C2): what building a document finds lands on <see cref="PdfDocument.Diagnostics"/>,
/// the one list, recorded once and thrown in strict mode like a repair found while reading (ISO 32000-2 §7.5).
/// </summary>
public sealed class ReportDiagnosticTests
{
    [Fact]
    public void A_reported_diagnostic_is_recorded_once_on_a_lenient_document()
    {
        using PdfDocument document = PdfDocument.Create();
        var diagnostic = new Diagnostic(LayoutDiagnostics.KeepTogetherTooTall.Name, DiagnosticSeverity.Warning, "Too tall.");

        document.ReportDiagnostic(diagnostic);
        document.ReportDiagnostic(new Diagnostic(LayoutDiagnostics.KeepTogetherTooTall.Name, DiagnosticSeverity.Warning, "Too tall again."));

        Assert.Same(diagnostic, Assert.Single(document.Diagnostics));
        Assert.Equal(DiagnosticArea.Layout, diagnostic.Kind.Area);
    }

    [Fact]
    public void A_strict_document_records_and_throws_a_warning_but_not_information()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStrict());
        var information = new Diagnostic("Tests.Noted", DiagnosticSeverity.Information, "Noted.");
        var warning = new Diagnostic(LayoutDiagnostics.DrawingStateUnbalanced.Name, DiagnosticSeverity.Warning, "Unbalanced.");

        document.ReportDiagnostic(information);
        DiagnosticException thrown = Assert.Throws<DiagnosticException>(() => document.ReportDiagnostic(warning));

        Assert.Same(warning, thrown.Diagnostic);
        Assert.Equal([information, warning], document.Diagnostics);
    }

    [Fact]
    public void Null_is_refused()
    {
        using PdfDocument document = PdfDocument.Create();

        Assert.Throws<ArgumentNullException>(() => document.ReportDiagnostic(null!));
    }
}
