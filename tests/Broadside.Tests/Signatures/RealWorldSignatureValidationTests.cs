using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Security;
using Broadside.Signatures;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Signatures;

/// <summary>
/// Every signed signature field in the real-world corpora (PDFBox, veraPDF and the others the gate fetches) is validated: each one is
/// valid or reports specific reasons, and the per-signature outcome is a snapshot reviewed by hand. No trust anchor is given, so a
/// complete chain ends <see cref="PdfSignatureProblemKind.ChainUntrusted"/>; the validation time is fixed so certificate expiry does not
/// drift with the calendar. Skips when the corpora are not fetched. ISO 32000-2 §12.8.1, §12.8.3.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldSignatureValidationTests(ITestOutputHelper output)
{
    private static readonly string[] CorpusIds = ["pdfjs", "pdfbox", "qpdf", "pdfium-tests", "pdf20examples", "verapdf-corpus", "gwg-output-suite", "bfo-pdfa-testsuite"];

    private static readonly DateTimeOffset ValidationTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Every_signature_in_the_real_world_corpora_validates_or_reports_its_reasons_as_snapshotted()
    {
        string[] fetched = [.. CorpusIds.Where(id => RealWorldCorpus.Directory(id) is not null)];
        if (fetched.Length != CorpusIds.Length)
        {
            Assert.Skip($"Corpora not fetched ({string.Join(", ", CorpusIds.Except(fetched))}); run tools/CorpusFetcher.");
        }

        var lines = new ConcurrentBag<string>();
        var failures = new ConcurrentBag<string>();
        Parallel.ForEach(
            CorpusIds.SelectMany(RealWorldCorpus.Files).Where(static file => !RealWorldCorpusTests.Unreadable.ContainsKey(file)),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            file =>
            {
                try
                {
                    foreach (string line in Validate(file))
                    {
                        lines.Add(line);
                    }
                }
#pragma warning disable CA1031 // Any exception from validation is the finding.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    failures.Add($"{file}: {exception}");
                }
            });

        Assert.Empty(failures);
        string[] ordered = [.. lines.Order(StringComparer.Ordinal)];
        Assert.True(ordered.Length >= 20, $"Only {ordered.Length} signatures were found; the corpora look incomplete.");
        var snapshot = new StringBuilder();
        snapshot.Append(CultureInfo.InvariantCulture, $"{ordered.Length} signatures: ");
        snapshot.AppendJoin(", ", ordered.Select(static line => line.Split(" | ")[4]).GroupBy(static status => status).OrderBy(static group => group.Key, StringComparer.Ordinal).Select(static group => $"{group.Key} {group.Count()}"));
        snapshot.Append('\n');
        foreach (string line in ordered)
        {
            snapshot.Append(line).Append('\n');
        }

        output.WriteLine(snapshot.ToString());
        await Verify(snapshot.ToString());
    }

    private static PdfDocument? OpenOrNull(string file)
    {
        try
        {
            return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)));
        }
        catch (Exception exception) when (exception is DiagnosticException or PdfPasswordException or PdfCertificateException or PdfEncryptionNotSupportedException)
        {
            return null;
        }
    }

    private static List<string> Validate(string file)
    {
        using PdfDocument? document = OpenOrNull(file);
        var lines = new List<string>();
        if (document is not null)
        {
            foreach (PdfSignatureValidation validation in document.ValidateSignatures(new PdfSignatureValidationOptions().WithValidationTime(ValidationTime)))
            {
                Assert.Equal(validation.Status == PdfSignatureStatus.Valid, validation.Problems.Count == 0);
                string kinds = validation.Problems.Count == 0 ? "-" : string.Join(", ", validation.Problems.Select(static problem => problem.Kind).Distinct().Order());
                lines.Add($"{file} | {validation.FieldName} | {validation.SubFilter ?? "-"} | {validation.DigestAlgorithm?.Name ?? "-"} | {validation.Status} | {kinds}");
            }
        }

        return lines;
    }
}
