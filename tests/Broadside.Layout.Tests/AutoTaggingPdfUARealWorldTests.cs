using System.Globalization;
using System.Text;
using Broadside.Structure;
using Broadside.TestSupport;

namespace Broadside.Layout.Tests;

/// <summary>
/// The failures that remain when real-world untagged files are auto-tagged, given a language, a title and alternate text for their
/// figures, and saved as PDF/UA-1 (issue #206): each file's PDF/UA-1 rules the save reports (never claims), categorised in a snapshot.
/// The files are TCPDF's examples (one generator exercising most features) and pdf.js's tracemonkey paper.
/// </summary>
public sealed class AutoTaggingPdfUARealWorldTests
{
    private const string NonConformant = "PdfUANonConformant";
    private const string RulePrefix = "veraPDF rule ";

    /// <summary>What each rule a real-world file still fails is about; the caller, not auto-tagging, has to supply or fix it.</summary>
    private static readonly (string Prefix, string Category)[] Categories =
    [
        ("7.21.", "fonts: not embedded, or a glyph with no Unicode (supply fonts and ToUnicode; PDF/A conversion embeds them)"),
        ("7.18.", "annotations and form fields: no alternate description (Contents, TU)"),
        ("7.3-", "figures: no alternate text"),
        ("7.1-", "content neither tagged nor an artifact (inside form XObjects)"),
        ("7.2-", "language and table structure"),
        ("7.11-", "embedded file specifications without F and UF"),
        ("7.10-", "optional content configurations without a Name"),
        ("6.1-", "features later than PDF 1.7"),
    ];

    [Fact]
    [Trait("Category", "Corpus")]
    public async Task Real_world_files_auto_tagged_for_PDF_UA_1_report_their_remaining_failures_by_category()
    {
        if (RealWorldCorpus.Directory("pdfium-tests") is null || RealWorldCorpus.Directory("pdfjs") is null)
        {
            Assert.Skip($"Real-world corpora not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        string[] files = [.. RealWorldCorpus.Files("pdfium-tests").Where(static file => file.Contains("/tcpdf/", StringComparison.Ordinal)), "pdfjs/tracemonkey.pdf"];
        var lines = new List<string>();
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int tagged = 0;
        int conforming = 0;
        foreach (string file in files)
        {
            using PdfDocument document = PdfDocument.Open(RealWorldCorpus.PathOf(file));
            AutoTaggingResult result = document.AutoTag(new AutoTaggingOptions().WithLanguage("en-US"));
            if (!result.IsTagged)
            {
                lines.Add($"{file}: already tagged, left alone");
                continue;
            }

            tagged++;
            foreach (PdfStructureElement figure in result.Figures)
            {
                figure.AlternateDescription = "A figure of the sample";
            }

            document.Properties.Title = "Sample";
            PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));
            string[] rules = [.. document.Diagnostics.Where(static diagnostic => diagnostic.Code == NonConformant)
                .Select(static diagnostic => diagnostic.Message[(diagnostic.Message.IndexOf(RulePrefix, StringComparison.Ordinal) + RulePrefix.Length)..])
                .Select(static rest => rest[..rest.IndexOf(',', StringComparison.Ordinal)])
                .Distinct().Order(StringComparer.Ordinal)];
            if (rules.Length == 0)
            {
                conforming++;
                lines.Add($"{file}: conforms (PDF/UA-1 identified)");
                continue;
            }

            lines.Add($"{file}: {string.Join(", ", rules)}");
            foreach (string category in rules.Select(Category).Distinct())
            {
                counts[category] = counts.GetValueOrDefault(category) + 1;
            }
        }

        var summary = new StringBuilder();
        summary.AppendLine(CultureInfo.InvariantCulture, $"{files.Length} files, {tagged} auto-tagged, {conforming} conform to PDF/UA-1 by the library's check");
        foreach ((string category, int count) in counts)
        {
            summary.AppendLine(CultureInfo.InvariantCulture, $"{count} files: {category}");
        }

        summary.AppendLine();
        foreach (string line in lines)
        {
            summary.AppendLine(line);
        }

        await VerifyXunit.Verifier.Verify(summary.ToString());
    }

    private static string Category(string rule) =>
        Categories.FirstOrDefault(entry => rule.StartsWith(entry.Prefix, StringComparison.Ordinal)).Category ?? "other";
}
