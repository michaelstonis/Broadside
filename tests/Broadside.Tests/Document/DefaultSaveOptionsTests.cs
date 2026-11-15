using System.Text;
using Broadside.Diagnostics;

namespace Broadside.Tests.Document;

/// <summary>
/// <see cref="PdfDocument.DefaultSaveOptions"/> (issue #397, B8): the saves that take no options use them, so a document built for a
/// PDF/A or PDF/UA conformance saves with it; options passed explicitly win, and a different conformance is recorded (ISO 19005, ISO 14289).
/// </summary>
public sealed class DefaultSaveOptionsTests
{
    [Fact]
    public void Saves_without_options_use_the_default_options()
    {
        using PdfDocument document = PdfDocument.Create();
        document.DefaultSaveOptions = new PdfSaveOptions().WithCrossReferenceLayout(PdfCrossReferenceLayout.Stream);

        using var stream = new MemoryStream();
        document.Save(stream);

        Assert.Contains("/XRef", Encoding.Latin1.GetString(stream.ToArray()), StringComparison.Ordinal);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == SavingDiagnostics.ConformanceOverridden.Name);
    }

    [Fact]
    public async Task Asynchronous_saves_without_options_use_the_default_options_too()
    {
        using PdfDocument document = PdfDocument.Create();
        document.DefaultSaveOptions = new PdfSaveOptions().WithCrossReferenceLayout(PdfCrossReferenceLayout.Stream);

        using var stream = new MemoryStream();
        await document.SaveAsync(stream, TestContext.Current.CancellationToken);

        Assert.Contains("/XRef", Encoding.Latin1.GetString(stream.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_options_with_another_conformance_win_and_are_recorded()
    {
        using PdfDocument document = PdfDocument.Create();
        document.DefaultSaveOptions = new PdfSaveOptions().WithPdfAConformance(PdfAConformance.PdfA2b);

        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions());

        Diagnostic overridden = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == SavingDiagnostics.ConformanceOverridden.Name);
        Assert.Equal(DiagnosticSeverity.Information, overridden.Severity);
        using PdfDocument reopened = PdfDocument.Open(stream.ToArray());
        Assert.Null(reopened.Metadata?.Packet?.PdfAPart);
    }

    [Fact]
    public void Explicit_options_with_the_same_conformance_are_not_recorded()
    {
        using PdfDocument document = PdfDocument.Create();
        document.DefaultSaveOptions = new PdfSaveOptions();

        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithCrossReferenceLayout(PdfCrossReferenceLayout.Stream));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == SavingDiagnostics.ConformanceOverridden.Name);
    }
}
