using Broadside.Diagnostics;
using Broadside.Security;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// Issue #229 over real-world files: every file of the pdfbox and pdf20examples corpora is converted to each PDF/A level; each
/// conversion that says the file conforms is saved and must pass veraPDF with no failed rule (the converter never claims a
/// conformance the file lacks), each that does not names the rules it still breaks, and the number that conform is pinned.
/// </summary>
/// <remarks>
/// Fonts come from the Standard 14 package only (no system fonts), so the counts are the same on every machine. CI's veraPDF job
/// fetches the two corpora; elsewhere the test skips when veraPDF or the corpora are absent.
/// </remarks>
[Trait("Category", "VeraPdf")]
public sealed class RealWorldPdfAConversionTests(ITestOutputHelper output)
{
    private static readonly string[] CorpusIds = ["pdfbox", "pdf20examples"];

    [Theory]
    [InlineData(PdfAConformance.PdfA2b, "2b", 123)]
    [InlineData(PdfAConformance.PdfA2u, "2u", 119)]
    [InlineData(PdfAConformance.PdfA3b, "3b", 127)]
    [InlineData(PdfAConformance.PdfA4, "4", 123)]
    public void Real_world_files_the_conversion_says_conform_pass_verapdf(PdfAConformance level, string flavour, int floor)
    {
        string[] files = [.. CorpusIds.SelectMany(RealWorldCorpus.Files)];
        if (files.Length == 0)
        {
            if (Environment.GetEnvironmentVariable("BROADSIDE_REQUIRE_VERAPDF") == "1")
            {
                Assert.Fail($"The pdfbox and pdf20examples corpora are not fetched; run tools/CorpusFetcher --only pdfbox,pdf20examples (or set {RealWorldCorpus.EnvironmentVariable}).");
            }

            Assert.Skip($"The pdfbox and pdf20examples corpora are not fetched (set {RealWorldCorpus.EnvironmentVariable}).");
        }

        // BROADSIDE_PDFA_OUT keeps the converted files there, for a look at what veraPDF reports.
        string? kept = Environment.GetEnvironmentVariable("BROADSIDE_PDFA_OUT");
        string directory = kept is { Length: > 0 } ? Path.Combine(kept, flavour) : Path.Combine(Path.GetTempPath(), $"broadside-pdfa-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            PdfOptions options = new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null);
            var saved = new Dictionary<string, string>(StringComparer.Ordinal);
            var blocking = new Dictionary<string, (int Files, string Example)>(StringComparer.Ordinal);
            int converted = 0;
            foreach (string file in files)
            {
                string path = Path.GetFullPath(Path.Combine(directory, $"{saved.Count:D3}.pdf"));
                switch (Convert(file, options, level, path))
                {
                    case null:
                        break;
                    case { IsConformant: true }:
                        converted++;
                        saved[path] = file;
                        break;
                    case { } conversion:
                        converted++;
                        Assert.NotEmpty(conversion.Violations);
                        foreach (PdfARuleViolation violation in conversion.Violations.DistinctBy(violation => violation.Rule))
                        {
                            blocking[violation.Rule] = (blocking.GetValueOrDefault(violation.Rule).Files + 1, $"{file}: {violation.Message} [{(conversion.NotConverted.Count > 0 ? conversion.NotConverted[0] : string.Empty)}]");
                        }

                        break;
                }
            }

            IReadOnlyDictionary<string, IReadOnlyList<string>> results = VeraPdf.FailedRulesByFile([.. saved.Keys], flavour);
            string[] failures = [.. saved
                .Where(pair => !results.TryGetValue(pair.Key, out IReadOnlyList<string>? failed) || failed.Count > 0)
                .Select(pair => $"{pair.Value}: {string.Join("; ", results.GetValueOrDefault(pair.Key) ?? ["no report"])}")];
            output.WriteLine($"{level}: {converted} files converted, {saved.Count} conform, {failures.Length} of them fail veraPDF.");
            foreach ((string rule, (int count, string example)) in blocking.OrderByDescending(pair => pair.Value.Files))
            {
                output.WriteLine($"  blocking {rule}: {count} files, such as {example}");
            }

            foreach (string failure in failures)
            {
                output.WriteLine($"  FAIL {failure}");
            }

            if (kept is { Length: > 0 })
            {
                File.WriteAllLines(Path.Combine(directory, "index.txt"), saved.Select(pair => $"{Path.GetFileName(pair.Key)} {pair.Value}"));
            }

            Assert.Empty(failures);
            Assert.True(saved.Count >= floor, $"{saved.Count} files conform to {level}, fewer than the {floor} pinned.");
        }
        finally
        {
            if (kept is not { Length: > 0 })
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Converts a corpus file and, when it conforms, saves it to <paramref name="path"/>; null when it cannot be opened or converted.</summary>
    private static PdfAConversion? Convert(string file, PdfOptions options, PdfAConformance level, string path)
    {
        using PdfDocument? document = OpenOrNull(file, options);
        if (document is null)
        {
            return null;
        }

        PdfAConversion conversion;
        try
        {
            conversion = document.ConvertToPdfA(level);
        }
        catch (Exception exception) when (exception is DiagnosticException or NotSupportedException)
        {
            return null;
        }

        if (conversion.IsConformant)
        {
            using (FileStream stream = File.Create(path))
            {
                document.Save(stream, conversion.CreateSaveOptions());
            }

            Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == "PdfANotIdentified");
        }

        return conversion;
    }

    private static PdfDocument? OpenOrNull(string file, PdfOptions options)
    {
        try
        {
            return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)), options);
        }
        catch (Exception exception) when (exception is PdfPasswordException or PdfCertificateException or NotSupportedException or DiagnosticException)
        {
            return null;
        }
    }
}
