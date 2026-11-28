using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Broadside.TestSupport;
using Broadside.Writing;

namespace Broadside.Tests.StructureTree;

/// <summary>
/// <see cref="PdfDocument.ValidatePdfUA"/> against veraPDF on the veraPDF PDF/UA-1 and PDF/UA-2 test files (issue #652). Each file
/// tests one rule and carries veraPDF's verdict in its name (<c>7.3-t01-fail-a.pdf</c>: rule 7.3-1 fails); for every file whose rule
/// Broadside checks, Broadside's verdict on that rule (reported or not) is compared with veraPDF's. The disagreements are a snapshot,
/// reviewed by hand: each is a rule Broadside checks more narrowly than veraPDF (issue #527) or a reading that differs.
/// ISO 14289-1 clauses 5 to 7; ISO 14289-2 clauses 5 to 8.
/// </summary>
[Trait("Category", "Corpus")]
public sealed partial class PdfUAValidationVeraPdfAgreementTests(ITestOutputHelper output)
{
    [Fact]
    public async Task The_rules_Broadside_checks_agree_with_veraPDF_or_the_disagreement_matches_the_snapshot()
    {
        IReadOnlyList<string> files = [.. RealWorldCorpus.Files("verapdf-corpus").Where(static file => file.Contains("/PDF_UA-", StringComparison.Ordinal))];
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus 'verapdf-corpus' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        string summary = Compare(files);
        output.WriteLine(summary);
        Assert.DoesNotContain(" threw ", summary, StringComparison.Ordinal);
        await Verify(summary);
    }

    /// <summary>Validates each file whose rule Broadside checks and lists where its verdict on that rule differs from veraPDF's.</summary>
    private static string Compare(IReadOnlyList<string> files)
    {
        var table = new StringBuilder();
        int compared = 0;
        int agreements = 0;
        int notChecked = 0;
        foreach (string file in files)
        {
            int part = file.Contains("/PDF_UA-2/", StringComparison.Ordinal) ? 2 : 1;
            Match name = FileName().Match(Path.GetFileName(file));
            if (!name.Success)
            {
                continue;
            }

            string rule = string.Create(CultureInfo.InvariantCulture, $"{name.Groups["clause"].Value}-{int.Parse(name.Groups["test"].Value, CultureInfo.InvariantCulture)}");
            if (!PdfUAChecker.RulesReported(part).Contains(rule))
            {
                notChecked++;
                continue;
            }

            compared++;
            string veraPdf = name.Groups["verdict"].Value;
            string ours;
            try
            {
                using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)));
                PdfUAValidation validation = document.ValidatePdfUA(part == 2 ? PdfUAConformance.PdfUA2 : PdfUAConformance.PdfUA1);
                ours = validation.Violations.Any(violation => violation.Rule == rule) ? "fail" : "pass";
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ours = $"threw {exception.GetType().Name}";
            }

            if (ours == veraPdf)
            {
                agreements++;
                continue;
            }

            table.Append(CultureInfo.InvariantCulture, $"PDF/UA-{part} | {rule} | veraPDF {veraPdf}, Broadside {ours} | {file[(file.IndexOf("/PDF_UA-", StringComparison.Ordinal) + 1)..]}\n");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{compared} files compared, {agreements} agree, {compared - agreements} disagree; {notChecked} files test rules Broadside does not check\n") + table;
    }

    [GeneratedRegex("^(?<clause>[0-9]+(\\.[0-9]+)*)-t(?<test>[0-9]+)-(?<verdict>pass|fail)-")]
    private static partial Regex FileName();
}
