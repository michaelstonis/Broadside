using Broadside.Forms;
using Broadside.Security;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Forms;

/// <summary>
/// Every default appearance string of every field in the pdf.js form files parses; one that departs from the clause or names a font
/// no default resources hold records its diagnostic. Skips when the corpus is not fetched. ISO 32000-2 §12.7.4.3.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldDefaultAppearanceTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_default_appearance_of_the_pdfjs_forms_parses_and_a_malformed_one_records_a_diagnostic()
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files("pdfjs");
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus 'pdfjs' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        int forms = 0;
        int parsed = 0;
        int resolved = 0;
        var problems = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            if (RealWorldCorpusTests.Unreadable.ContainsKey(file))
            {
                continue;
            }

            using PdfDocument? document = OpenOrNull(file);
            if (document?.AcroForm is not { } form)
            {
                continue;
            }

            forms++;
            foreach (PdfField field in form.AllFields)
            {
                if (field.DefaultAppearance is null)
                {
                    continue;
                }

                PdfDefaultAppearance appearance = Assert.IsType<PdfDefaultAppearance>(field.ParsedDefaultAppearance);
                parsed++;
                if (!appearance.IsValid)
                {
                    string key = appearance.Problems.ToString();
                    problems[key] = problems.GetValueOrDefault(key) + 1;
                    Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "DefaultAppearanceInvalid");
                }

                if (appearance.Font is null)
                {
                    Assert.True(appearance.FontName is null || document.Diagnostics.Any(diagnostic => diagnostic.Code == "DefaultAppearanceFontMissing"), $"{file} {field.FullyQualifiedName}");
                }
                else
                {
                    resolved++;
                    Assert.NotNull(appearance.FontResources);
                }
            }
        }

        output.WriteLine($"{forms} forms, {parsed} default appearance strings parsed, {resolved} fonts resolved");
        output.WriteLine(string.Join(", ", problems.Select(entry => $"{entry.Key}: {entry.Value}")));
        Assert.True(parsed >= 700, $"Only {parsed} default appearance strings were found; the corpus looks incomplete.");
    }

    private static PdfDocument? OpenOrNull(string file)
    {
        try
        {
            return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)));
        }
        catch (Exception exception) when (exception is PdfPasswordException or PdfCertificateException or NotSupportedException)
        {
            return null;
        }
    }
}
