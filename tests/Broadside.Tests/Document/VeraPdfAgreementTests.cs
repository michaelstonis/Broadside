using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>How a disagreement between strict mode and veraPDF is explained.</summary>
public enum VeraPdfTriage
{
    /// <summary>(a) The rule is PDF/A's (ISO 19005), stricter than ISO 32000-2, which strict mode implements.</summary>
    PdfAOnly,

    /// <summary>(b) ISO 32000-2 is violated and strict mode does not see it yet: a known gap, with the issue that closes it.</summary>
    StrictTooLenient,

    /// <summary>(c) Strict mode rejects what ISO 32000-2 allows.</summary>
    StrictTooStrict,

    /// <summary>(d) veraPDF's verdict is wrong.</summary>
    VeraPdfIssue,
}

/// <summary>
/// Strict mode against veraPDF's well-formedness verdicts on the purely syntactic subset of the veraPDF corpus (issue #47): the
/// PDF/A-1b (veraPDF and Isartor), PDF/A-2b and PDF/A-4 "6.1 File structure" tests for the header, trailer, cross-reference
/// table, strings, streams, names, indirect objects and implementation limits. veraPDF's verdict is the <c>pass</c>/<c>fail</c> in
/// each file name; strict mode's is whether opening and walking the file (<see cref="DocumentWalker"/>) throws. Every
/// disagreement is triaged, by rule, in <see cref="Triage"/>; the snapshot lists each one. ISO 32000-2 §7.2-§7.5.
/// </summary>
[Trait("Category", "Corpus")]
public partial class VeraPdfAgreementTests(ITestOutputHelper output)
{
    /// <summary>The syntactic folders, below <c>6.1 File structure/</c> of each profile.</summary>
    private static readonly string[] SyntacticFolders =
    [
        "File header", "File trailer", "Cross reference", "String objects", "Stream objects", "6.1.7.1 General", "Name objects",
        "Indirect objects", "Implementation limits", "Implementation Limits",
    ];

    /// <summary>Every disagreement's explanation, keyed by profile and veraPDF rule (clause and test number).</summary>
    public static readonly IReadOnlyDictionary<string, (VeraPdfTriage Triage, string Reason)> Triage = new Dictionary<string, (VeraPdfTriage, string)>(StringComparer.Ordinal)
    {
        // Header (§7.5.2).
        ["Isartor 6-1-2-t01"] = (VeraPdfTriage.PdfAOnly, "Bytes before %PDF-: §7.5.2 NOTE 1 allows them; offsets count from the header."),
        ["PDF_A-2b 6-1-2-t01"] = (VeraPdfTriage.PdfAOnly, "%PDF-2.0 or %PDF-1.9 in PDF/A-2, or bytes before the header: ISO 32000-2 accepts 1.0-2.0 and leading bytes."),
        ["PDF_A-4 6-1-2-t01"] = (VeraPdfTriage.PdfAOnly, "%PDF-1.7 in PDF/A-4 (which requires 2.n), or bytes before the header: both legal in ISO 32000-2."),
        ["Isartor 6-1-2-t02"] = (VeraPdfTriage.StrictTooLenient, "Binary comment line missing or short. §7.5.2: a file with binary data shall have it. Not checked: it needs a whole-file scan for binary bytes; follow-up."),
        ["PDF_A-1b 6-1-2-t02"] = (VeraPdfTriage.StrictTooLenient, "Binary comment line missing or short (§7.5.2); not checked yet, follow-up."),
        ["PDF_A-2b 6-1-2-t02"] = (VeraPdfTriage.StrictTooLenient, "Binary comment line missing, short or not immediately after the header (§7.5.2); not checked yet, follow-up."),
        ["PDF_A-4 6-1-2-t02"] = (VeraPdfTriage.StrictTooLenient, "Binary comment line missing, short or not immediately after the header (§7.5.2); not checked yet, follow-up."),

        // Trailer (§7.5.5, §14.4).
        ["Isartor 6-1-3-t01"] = (VeraPdfTriage.PdfAOnly, "No ID in a PDF 1.4 trailer: Table 15 requires ID only in PDF 2.0 or with Encrypt."),
        ["PDF_A-2b 6-1-3-t01"] = (VeraPdfTriage.PdfAOnly, "PDF 1.7 trailer without ID, or a linearized first-page trailer without ID: ID is optional before 2.0. (An ID shorter than 16 bytes now fails: FileIdentifierInvalid, Table 15.)"),
        ["Isartor 6-1-3-t02"] = (VeraPdfTriage.PdfAOnly, "Encrypt in the trailer: PDF/A forbids encryption; ISO 32000-2 §7.6 defines it."),
        ["PDF_A-1b 6-1-3-t02"] = (VeraPdfTriage.PdfAOnly, "Encrypt in the trailer: PDF/A forbids encryption."),
        ["PDF_A-2b 6-1-3-t02"] = (VeraPdfTriage.PdfAOnly, "Encrypt in the trailer: PDF/A forbids encryption."),
        ["Isartor 6-1-3-t04"] = (VeraPdfTriage.PdfAOnly, "Linearized file whose first-page and last trailer IDs differ: ISO 32000-2 does not require them to match."),
        ["PDF_A-4 6-1-3-t04"] = (VeraPdfTriage.PdfAOnly, "Info in the trailer without PieceInfo: a PDF/A-4 rule (Info is deprecated, not forbidden, in PDF 2.0)."),
        ["PDF_A-4 6-1-3-t05"] = (VeraPdfTriage.PdfAOnly, "Info dictionary with entries other than ModDate: a PDF/A-4 rule."),

        // Cross-reference table (§7.5.4).
        ["PDF_A-1b 6-1-4-t02"] = (VeraPdfTriage.PdfAOnly, "Space after xref, or a blank line before the subsection header: §7.5.4 asks only for a line containing the keyword xref."),
        ["PDF_A-2b 6-1-4-t01"] = (VeraPdfTriage.PdfAOnly, "Space after xref, or a blank line before the subsection header (PDF/A-2 6.1.4)."),
        ["PDF_A-4 6-1-4-t01"] = (VeraPdfTriage.PdfAOnly, "Space after xref, or a blank line before the subsection header (PDF/A-4 6.1.4)."),
        ["PDF_A-1b 6-1-4-t03"] = (VeraPdfTriage.PdfAOnly, "Cross-reference stream: PDF/A-1 forbids them; §7.5.8 defines them."),

        // Strings (§7.3.4).
        ["Isartor 6-1-6-t01"] = (VeraPdfTriage.PdfAOnly, "Hexadecimal string with an odd number of digits: §7.3.4.3 pads the last digit with 0."),
        ["PDF_A-1b 6-1-6-t01"] = (VeraPdfTriage.PdfAOnly, "Hexadecimal string with an odd number of digits: legal (§7.3.4.3)."),
        ["PDF_A-2b 6-1-6-t01"] = (VeraPdfTriage.PdfAOnly, "Hexadecimal string with an odd number of digits: legal (§7.3.4.3)."),
        ["PDF_A-4 6-1-5-t01"] = (VeraPdfTriage.PdfAOnly, "Hexadecimal string with an odd number of digits: legal (§7.3.4.3)."),
        ["PDF_A-2b 6-1-6-t02"] = (VeraPdfTriage.StrictTooLenient, "A non-hexadecimal character in a hexadecimal string inside a content stream (<484!> Tj). Content streams are parsed by the content interpreter (Phase 2), not by the Phase 1 walk."),
        ["PDF_A-4 6-1-5-t02"] = (VeraPdfTriage.StrictTooLenient, "A non-hexadecimal character in a hexadecimal string inside a content stream; parsed from Phase 2 (content interpreter)."),

        // Streams (§7.3.8).
        ["Isartor 6-1-7-t02"] = (VeraPdfTriage.PdfAOnly, "endstream not preceded by an end-of-line marker: §7.3.8.1 says there should be one."),
        ["PDF_A-2b 6-1-7-1-t02"] = (VeraPdfTriage.PdfAOnly, "endstream preceded by an end-of-line marker and a space: §7.3.8.1 says should."),
        ["Isartor 6-1-7-t03"] = (VeraPdfTriage.PdfAOnly, "Length counts the end-of-line marker before endstream. Read by Length the stream ends right before endstream, which §7.3.8.1 allows (the marker is a should); only a PDF/A reader that requires the marker can tell."),
        ["Isartor 6-1-7-t04"] = (VeraPdfTriage.PdfAOnly, "F, FFilter or FDecodeParms in a stream dictionary: legal in Table 5 (external file data is not read: an Information diagnostic)."),
        ["PDF_A-2b 6-1-7-1-t04"] = (VeraPdfTriage.PdfAOnly, "F, FFilter or FDecodeParms in a stream dictionary: legal in Table 5."),
        ["PDF_A-4 6-1-6-1-t02"] = (VeraPdfTriage.PdfAOnly, "F, FFilter or FDecodeParms in a stream dictionary: legal in Table 5."),

        // Names (§7.3.5).
        ["PDF_A-2b 6-1-8-t01"] = (VeraPdfTriage.PdfAOnly, "Font, colourant or structure type name that is not valid UTF-8: §7.3.5 says names should be UTF-8."),
        ["PDF_A-4 6-1-7-t01"] = (VeraPdfTriage.PdfAOnly, "Name that is not valid UTF-8: §7.3.5 says should."),

        // Indirect objects (§7.3.10).
        ["Isartor 6-1-8-t01"] = (VeraPdfTriage.PdfAOnly, "More than one white-space character between object and generation number: §7.3.10 asks only for white space."),
        ["Isartor 6-1-8-t02"] = (VeraPdfTriage.PdfAOnly, "More than one white-space character between generation number and obj: legal (§7.3.10)."),
        ["Isartor 6-1-8-t03"] = (VeraPdfTriage.PdfAOnly, "Object number not preceded by an end-of-line marker: legal (§7.3.10, §7.2.3)."),
        ["Isartor 6-1-8-t04"] = (VeraPdfTriage.PdfAOnly, "endobj not preceded by an end-of-line marker: legal."),
        ["Isartor 6-1-8-t05"] = (VeraPdfTriage.PdfAOnly, "obj not followed by an end-of-line marker: legal."),
        ["Isartor 6-1-8-t06"] = (VeraPdfTriage.PdfAOnly, "endobj not followed by an end-of-line marker: legal."),
        ["PDF_A-1b 6-1-8-t01"] = (VeraPdfTriage.PdfAOnly, "obj/endobj spacing and end-of-line rules of PDF/A-1 6.1.8: ISO 32000-2 requires only white space."),
        ["PDF_A-2b 6-1-9-t01"] = (VeraPdfTriage.PdfAOnly, "obj/endobj spacing rules of PDF/A-2 6.1.9: legal in ISO 32000-2."),
        ["PDF_A-2b 6-1-9-t02"] = (VeraPdfTriage.PdfAOnly, "obj/endobj spacing rules of PDF/A-2 6.1.9: legal in ISO 32000-2."),
        ["PDF_A-2b 6-1-9-t03"] = (VeraPdfTriage.PdfAOnly, "obj/endobj end-of-line rules of PDF/A-2 6.1.9: legal in ISO 32000-2."),
        ["PDF_A-2b 6-1-9-t04"] = (VeraPdfTriage.PdfAOnly, "obj/endobj end-of-line rules of PDF/A-2 6.1.9: legal in ISO 32000-2."),
        ["PDF_A-4 6-1-8-t01"] = (VeraPdfTriage.PdfAOnly, "obj/endobj spacing and end-of-line rules of PDF/A-4 6.1.8: legal in ISO 32000-2."),

        // Implementation limits: ISO 32000-2 Annex C is informative advice, not requirements on a file.
        ["Isartor 6-1-12-t01"] = (VeraPdfTriage.PdfAOnly, "Array over 8191 elements, name over 127 bytes, integer over 2^31-1: PDF/A-1 limits (ISO 32000-2 Annex C is informative)."),
        ["PDF_A-1b 6-1-12-t02"] = (VeraPdfTriage.PdfAOnly, "Real over 32767: a PDF/A-1 limit."),
        ["PDF_A-1b 6-1-12-t03"] = (VeraPdfTriage.PdfAOnly, "String over 65535 bytes: a PDF/A-1 limit."),
        ["PDF_A-1b 6-1-12-t04"] = (VeraPdfTriage.PdfAOnly, "Name over 127 bytes: a PDF/A-1 limit."),
        ["PDF_A-1b 6-1-12-t06"] = (VeraPdfTriage.PdfAOnly, "Dictionary over 4095 entries: a PDF/A-1 limit."),
        ["PDF_A-1b 6-1-12-t08"] = (VeraPdfTriage.PdfAOnly, "q/Q nesting over 28: a PDF/A-1 limit (and a content stream rule)."),
        ["PDF_A-1b 6-1-12-t10"] = (VeraPdfTriage.PdfAOnly, "CID over 65535: a PDF/A-1 limit."),
        ["PDF_A-2b 6-1-13-t03"] = (VeraPdfTriage.PdfAOnly, "String over 32767 bytes: a PDF/A-2 limit."),
        ["PDF_A-2b 6-1-13-t04"] = (VeraPdfTriage.PdfAOnly, "Name over 127 bytes: a PDF/A-2 limit."),
        ["PDF_A-2b 6-1-13-t08"] = (VeraPdfTriage.PdfAOnly, "q/Q nesting over 28: a PDF/A-2 limit."),
        ["PDF_A-2b 6-1-13-t09"] = (VeraPdfTriage.PdfAOnly, "DeviceN over 32 colourants: a PDF/A-2 limit."),
        ["PDF_A-2b 6-1-13-t10"] = (VeraPdfTriage.PdfAOnly, "CID over 65535: a PDF/A-2 limit."),
    };

    public static TheoryData<string> Files
    {
        get
        {
            var data = new TheoryData<string>();
            IReadOnlyList<string> files = SubsetFiles();
            if (files.Count == 0)
            {
                data.Add("verapdf-corpus/(not fetched)");
            }

            foreach (string file in files)
            {
                data.Add(file);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Strict_mode_agrees_with_veraPDF_or_the_disagreement_is_triaged(string file)
    {
        RealWorldCorpusTests.SkipWhenNotFetched(file);

        CorpusOutcome outcome = await RealWorldCorpus.OutcomeAsync(file, strict: true);

        Assert.False(outcome.IsFailure, $"{outcome.Kind}: {outcome.Detail}");
        Assert.Contains(outcome.Kind, (CorpusOutcomeKind[])[CorpusOutcomeKind.Clean, CorpusOutcomeKind.Rejected]);
        if (Ours(outcome) != VeraPdf(file))
        {
            Assert.True(Triage.ContainsKey(Rule(file)), $"Strict mode says {Ours(outcome)}, veraPDF {VeraPdf(file)}, and rule '{Rule(file)}' is not triaged ({outcome.Detail}).");
        }
    }

    [Fact]
    public async Task Every_disagreement_matches_the_snapshot_and_every_triaged_rule_still_disagrees()
    {
        IReadOnlyList<string> files = SubsetFiles();
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus 'verapdf-corpus' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        CorpusOutcome[] outcomes = await Task.WhenAll(files.Select(file => RealWorldCorpus.OutcomeAsync(file, strict: true)));
        var table = new StringBuilder();
        var disagreeingRules = new HashSet<string>(StringComparer.Ordinal);
        int agreements = 0;
        for (int index = 0; index < files.Count; index++)
        {
            string file = files[index];
            string ours = Ours(outcomes[index]);
            if (ours == VeraPdf(file))
            {
                agreements++;
                continue;
            }

            string rule = Rule(file);
            disagreeingRules.Add(rule);
            string triage = Triage.TryGetValue(rule, out (VeraPdfTriage Triage, string Reason) entry) ? entry.Triage.ToString() : "UNTRIAGED";
            table.Append(CultureInfo.InvariantCulture, $"{triage} | {rule} | veraPDF {VeraPdf(file)}, strict {ours} | {file}\n");
        }

        string summary = string.Create(CultureInfo.InvariantCulture, $"{files.Count} files, {agreements} agree, {files.Count - agreements} disagree\n") + table;
        output.WriteLine(summary);
        Assert.Empty(Triage.Keys.Except(disagreeingRules));
        await Verify(summary);
    }

    private static IReadOnlyList<string> SubsetFiles() =>
    [
        .. RealWorldCorpus.Files("verapdf-corpus").Where(static file =>
        {
            int structure = file.IndexOf("/6.1 File structure/", StringComparison.Ordinal);
            if (structure < 0 || !(file.Contains("/PDF_A-", StringComparison.Ordinal) || file.Contains("/Isartor test files/", StringComparison.Ordinal)))
            {
                return false;
            }

            string folder = file[(structure + "/6.1 File structure/".Length)..];
            return !folder.Contains("Filters", StringComparison.Ordinal) && SyntacticFolders.Any(name => folder.Contains(name, StringComparison.Ordinal));
        }),
    ];

    private static string Ours(CorpusOutcome outcome) => outcome.Kind == CorpusOutcomeKind.Rejected ? "fail" : "pass";

    private static string VeraPdf(string file) => file.Contains("-pass-", StringComparison.Ordinal) ? "pass" : "fail";

    /// <summary>The profile (first folder) and the rule id from the file name, such as <c>PDF_A-2b 6-1-2-t02</c>.</summary>
    private static string Rule(string file)
    {
        string[] parts = file.Split('/');
        string profile = parts[1] == "Isartor test files" ? "Isartor" : parts[1];
        return $"{profile} {RuleId().Match(parts[^1]).Value}";
    }

    [GeneratedRegex("[0-9]+(-[0-9]+)+-t[0-9]+")]
    private static partial Regex RuleId();
}
