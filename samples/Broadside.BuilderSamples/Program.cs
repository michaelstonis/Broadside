using Broadside;
using Broadside.BuilderSamples;
using Broadside.Diagnostics;
using Broadside.Layout.Flow;

// Writes every sample of the document builder guide into the folder given (the current folder by default), one PDF each, and exits
// with 1 when building or saving one reports more than information. Each document saves with the conformance it declared.
string folder = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
Directory.CreateDirectory(folder);
ImageSource logo = ImageSource.FromFile(Path.Combine(AppContext.BaseDirectory, "assets", "logo.png"));
int failures = 0;
foreach ((string name, Func<PdfOptions, ImageSource, PdfDocument> build) in Samples.All)
{
    using PdfDocument document = build(new PdfOptions(), logo);
    string path = Path.Combine(folder, name + ".pdf");
    document.Save(path);
    Diagnostic[] problems = [.. document.Diagnostics.Where(diagnostic => diagnostic.Severity != DiagnosticSeverity.Information)];
    Console.WriteLine($"{path}: {document.Pages.Count} page(s){(problems.Length == 0 ? string.Empty : $", {problems.Length} problem(s)")}");
    foreach (Diagnostic problem in problems)
    {
        Console.WriteLine($"  {problem.Severity} {problem.Code}: {problem.Message}");
    }

    failures += problems.Length == 0 ? 0 : 1;
}

return failures == 0 ? 0 : 1;
