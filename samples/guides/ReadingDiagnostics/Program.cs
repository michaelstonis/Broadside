using Broadside;
using Broadside.Diagnostics;
using Broadside.Samples;

Sample.Start("Reading: diagnostics and strict mode");

ReadLeniently();
FindADiagnostic();
DiagnosticsGrowAsObjectsLoad();
ReadStrictly();
Console.WriteLine("OK");

static void ReadLeniently()
{
    #region lenient
    using PdfDocument document = PdfDocument.Open("startxref-wrong.pdf");
    foreach (Diagnostic diagnostic in document.Diagnostics)
    {
        Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code} at offset {diagnostic.Offset}: {diagnostic.Message}");
    }
    #endregion
    Diagnostic only = document.Diagnostics.Single();
    Sample.Expect(only.Code == "StartxrefInvalid" && only.Severity == DiagnosticSeverity.Warning && only.Offset == 303, "one repair");
    Sample.Expect(document.Pages.Count == 1, "the repaired document has its page");
}

static void FindADiagnostic()
{
    #region find
    using PdfDocument document = PdfDocument.Open("startxref-wrong.pdf");
    bool repairedStartxref = document.Diagnostics.Any(d => d.Kind.Equals(FileStructureDiagnostics.StartxrefInvalid));
    bool anyErrors = document.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    DiagnosticCode kind = document.Diagnostics[0].Kind;
    Console.WriteLine($"{kind.Name}: area {kind.Area}, {kind.Clause}");
    #endregion
    Sample.Expect(repairedStartxref && !anyErrors, "the repair is found by its code");
    Sample.Expect(kind.Area == DiagnosticArea.FileStructure, "the code's area");
}

static void DiagnosticsGrowAsObjectsLoad()
{
    #region lazy
    using PdfDocument document = PdfDocument.Open("broken-xref-offsets.pdf");
    Console.WriteLine($"After opening: {document.Diagnostics.Count} diagnostic(s)");
    _ = document.Pages[0].ExtractText(); // loads the page tree and the page
    Console.WriteLine($"After reading page 1: {document.Diagnostics.Count} diagnostic(s)");
    #endregion
    Sample.Expect(document.Diagnostics.Count == 3, "one repair per object as it loads");
}

static void ReadStrictly()
{
    #region strict
    PdfOptions strict = new PdfOptions().UseStrict();
    try
    {
        using PdfDocument document = PdfDocument.Open("startxref-wrong.pdf", strict);
    }
    catch (DiagnosticException ex)
    {
        Console.WriteLine($"Rejected: {ex.Diagnostic.Code} at offset {ex.Diagnostic.Offset}");
    }
    #endregion
    Sample.Expect(Throws(() => PdfDocument.Open("startxref-wrong.pdf", strict).Dispose()), "strict mode throws");
}

static bool Throws(Action action)
{
    try
    {
        action();
        return false;
    }
    catch (DiagnosticException)
    {
        return true;
    }
}
