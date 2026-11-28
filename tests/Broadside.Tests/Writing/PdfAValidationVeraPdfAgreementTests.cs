using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// <see cref="PdfDocument.ValidatePdfA"/> against veraPDF's own verdicts (issue #651): the veraPDF test suite (fetched by
/// <c>tools/CorpusFetcher</c> into <c>corpus/verapdf-corpus</c>) names each file after the rule it tests and veraPDF's verdict, as
/// <c>veraPDF test suite 6-1-2-t01-fail-a.pdf</c> (clause 6.1.2, failed). The test number in the name is the suite's, not always the
/// profile rule's (a <c>6-2-4-3-t04</c> file can fail rule 6.2.4.3-2), so verdicts are compared per clause. Each file is validated
/// at the level its folder holds (<c>PDF_A-2b</c>, <c>PDF_A-4</c>, ...), or the level it claims within that part. A clause counts as
/// checked by Broadside when it reports one of its rules on at least one file of the folder and holds none of the rules left to
/// veraPDF (<see cref="PdfAValidation.RulesNotChecked"/>); then for each file that tests it,
/// Broadside must report a rule of the clause exactly when veraPDF fails the file. A file veraPDF passes is compliant, so any rule
/// Broadside reports on it is a disagreement too. The disagreements are a snapshot, to be driven to zero by issue #533; skips when the
/// corpus is not fetched.
/// </summary>
[Trait("Category", "Corpus")]
public sealed partial class PdfAValidationVeraPdfAgreementTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Every_rule_broadside_checks_agrees_with_verapdf_on_its_test_suite_as_snapshotted()
    {
        if (RealWorldCorpus.Directory("verapdf-corpus") is null)
        {
            Assert.Skip("The veraPDF corpus is not fetched; run tools/CorpusFetcher.");
        }

        var results = new ConcurrentBag<(string Folder, string File, string Clause, bool Fails, string[] Reported, IReadOnlyList<string> NotChecked)>();
        var failures = new ConcurrentBag<string>();
        string[] files = [.. RealWorldCorpus.Files("verapdf-corpus").Where(file => Level(file) is not null && TestedRule().IsMatch(Path.GetFileName(file)))];
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
        {
            Match name = TestedRule().Match(Path.GetFileName(file));
            string rule = name.Groups["clause"].Value.Replace('-', '.');
            try
            {
                using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)));
                PdfAConformance level = Level(file)!.Value;
                PdfAValidation validation = Validate(document, level);
                results.Add((file.Split('/')[1], file, rule, name.Groups["verdict"].Value == "fail", [.. validation.Violations.Select(violation => violation.Rule).Distinct()], validation.RulesNotChecked));
            }
#pragma warning disable CA1031 // Validation never throws for a non-conforming file: any exception is the finding.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                failures.Add($"{file}: {exception.GetType().Name}: {exception.Message}");
            }
        });

        var lines = new List<string>();
        var summary = new StringBuilder();
        foreach (IGrouping<string, (string Folder, string File, string Clause, bool Fails, string[] Reported, IReadOnlyList<string> NotChecked)> folder in results.GroupBy(result => result.Folder).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            IReadOnlyList<string> notChecked = folder.First().NotChecked;
            var checkedRules = folder.SelectMany(result => result.Reported).Select(Clause)
                .Where(clause => !notChecked.Any(rule => Overlaps(clause, rule)))
                .ToHashSet(StringComparer.Ordinal);
            int agreed = 0, compared = 0;
            foreach ((string _, string file, string rule, bool fails, string[] reported, IReadOnlyList<string> _) in folder)
            {
                if (checkedRules.Contains(rule))
                {
                    compared++;
                    if (fails == reported.Any(other => Clause(other) == rule))
                    {
                        agreed++;
                    }
                    else
                    {
                        lines.Add($"{(fails ? "missed" : "reported")} {rule} | {file}");
                    }
                }

                if (!fails)
                {
                    lines.AddRange(reported.Where(other => Clause(other) != rule).Order(StringComparer.Ordinal).Select(other => $"reported on a compliant file {other} | {file}"));
                }
            }

            summary.Append(CultureInfo.InvariantCulture, $"{folder.Key}: {folder.Count()} files, {checkedRules.Count} clauses checked, {agreed} of {compared} verdicts agree\n");
        }

        Assert.Empty(failures);
        Assert.True(results.Count >= 500, $"Only {results.Count} veraPDF test files were validated; the corpus looks incomplete.");
        string snapshot = summary + string.Join('\n', lines.Order(StringComparer.Ordinal)) + "\n";
        output.WriteLine(snapshot);
        await Verify(snapshot);
    }

    /// <summary>The level the file claims within its folder's part, or the folder's.</summary>
    private static PdfAValidation Validate(PdfDocument document, PdfAConformance folder)
    {
        try
        {
            PdfAValidation claimed = document.ValidatePdfA();
            if (Part(claimed.Conformance) == Part(folder))
            {
                return claimed;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
        }

        return document.ValidatePdfA(folder);
    }

    /// <summary>The clause of a veraPDF rule identifier: <c>6.2.4.3</c> for <c>6.2.4.3-2</c>.</summary>
    private static string Clause(string rule) => rule[..rule.LastIndexOf('-')];

    /// <summary>
    /// Whether a clause holds a rule the check leaves to veraPDF (<see cref="PdfAValidation.RulesNotChecked"/>: a rule or a clause),
    /// so a verdict on the clause is not Broadside's to give.
    /// </summary>
    private static bool Overlaps(string clause, string notChecked) =>
        notChecked == clause || notChecked.StartsWith(clause + "-", StringComparison.Ordinal) || notChecked.StartsWith(clause + ".", StringComparison.Ordinal)
        || clause.StartsWith(notChecked + ".", StringComparison.Ordinal);

    private static char Part(PdfAConformance conformance) => conformance.ToString()[4];

    /// <summary>The level of the file's folder (<c>PDF_A-2b</c> is <see cref="PdfAConformance.PdfA2b"/>), when Broadside defines it.</summary>
    private static PdfAConformance? Level(string file)
    {
        string[] parts = file.Split('/');
        return parts.Length > 2 && parts[1].StartsWith("PDF_A-", StringComparison.Ordinal)
            && Enum.TryParse(string.Concat("PdfA", parts[1].AsSpan("PDF_A-".Length)), ignoreCase: true, out PdfAConformance level)
            && level != PdfAConformance.None
            ? level
            : null;
    }

    [GeneratedRegex(@"^veraPDF test suite (?<clause>\d+(?:-\d+)*)-t\d+-(?<verdict>pass|fail)-[a-z]+\.pdf$", RegexOptions.CultureInvariant)]
    private static partial Regex TestedRule();
}
