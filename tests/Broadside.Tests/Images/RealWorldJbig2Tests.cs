using System.Globalization;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Tests.Filters;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// Every JBIG2 image XObject in the fetched real-world corpora decodes bit for bit to its reference through <see cref="PdfImage.Decode"/>
/// and <see cref="PdfDocument.DecodeStream(CosStream)"/>, or is listed with the reason it is declined (<c>Jbig2CorpusResults.txt</c>,
/// next to this file). Skips the files that are not fetched. ISO 32000-2 §7.4.7; ITU-T T.88 §6, §7, §8, Annexes A to E.
/// </summary>
/// <remarks>
/// The <c>bitmap-*</c> files (pdf.js and PDFium's copies of the SerenityOS JBIG2 suite) all encode one 399 x 400 picture,
/// SerenityOS's <c>Tests/LibGfx/test-inputs/bmp/bitmap.bmp</c>: its packed raster (1 = black, padding 0) has the MD5
/// cf9fa4d02110fdaead640de90c2f57aa and the PDF samples the SHA-256 68729cd5...; jbig2dec 0.20 and poppler 26.10 each fail on some of
/// them (intermediate regions, context reuse, TPGRON, some Huffman tables), so the golden picture is the reference. Other files are
/// checked against jbig2dec 0.20 (<c>jbig2dec -e</c> on the streams qpdf extracts) and poppler 26.10 (<c>pdfimages</c>);
/// <c>make_jbig2_corpus_references.py</c> recomputes both for every listed image.
/// </remarks>
[Trait("Category", "Corpus")]
public class RealWorldJbig2Tests
{
    /// <summary>The results list: file, object, size, SHA-256 of the PDF samples, verdict, JBIG2 codes, reference or reason.</summary>
    public static readonly string ResultsPath = Path.GetFullPath(Path.Combine(Corpus.Directory, "..", "Broadside.Tests", "Images", "Jbig2CorpusResults.txt"));

    private static readonly CosName FilterKey = new("Filter");
    private static readonly CosName Jbig2Decode = new("JBIG2Decode");
    private static readonly CosName SizeKey = new("Size");

    // The pdf.js test manifest's password for its encrypted test file (test_manifest.json, entry issue3371).
    private static readonly Dictionary<string, string> Passwords = new(StringComparer.Ordinal) { ["pdfjs/issue3371.pdf"] = "ELXRTQWS" };

    public static TheoryData<string, int, string, string, string, string> Images
    {
        get
        {
            var data = new TheoryData<string, int, string, string, string, string>();
            foreach (string[] fields in ReadResults())
            {
                data.Add(fields[0], int.Parse(fields[1], CultureInfo.InvariantCulture), fields[2], fields[3], fields[4], fields[5]);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Images))]
    public void Every_JBIG2_image_of_the_corpora_decodes_to_its_reference_or_is_declined_as_listed(string file, int objectNumber, string size, string sha256, string verdict, string codes)
    {
        SkipUnlessFetched(file);
        using PdfDocument document = Open(file);
        CosObject stream = document.Resolve(new CosReference(objectNumber, 0));
        PdfImage? image = document.GetImage(stream);

        using DecodedImage? decoded = image?.Decode();

        if (verdict == "declined")
        {
            Assert.Null(decoded);
            return;
        }

        Assert.NotNull(decoded);
        byte[] bytes = document.DecodeStream((CosStream)stream).ToArray();
        Assert.Equal(size, string.Create(CultureInfo.InvariantCulture, $"{decoded.Width}x{decoded.Height}"));
        Assert.Equal((1, 1, false), (decoded.Components, decoded.BitsPerComponent, decoded.SamplesInverted));
        Assert.Equal(sha256, Jbig2Testing.Sha256(decoded.Samples));
        Assert.Equal(sha256, Jbig2Testing.Sha256(bytes));
        string[] expectedCodes = codes == "-" ? [] : codes.Split(',');
        Assert.Equal(expectedCodes, document.Diagnostics.Select(diagnostic => diagnostic.Code).Where(code => code.StartsWith("Jbig2", StringComparison.Ordinal)).Distinct());
    }

    public static TheoryData<string> CorporaWithJbig2 => new() { "pdfjs", "pdfium-tests", "pdfbox", "qpdf", "pdf20examples", "verapdf-corpus" };

    [Theory]
    [MemberData(nameof(CorporaWithJbig2))]
    public void The_results_list_every_JBIG2_image_of_a_fetched_corpus(string corpus)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpus);
        if (files.Count == 0)
        {
            Assert.Skip($"{corpus} not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        var listed = ReadResults().Where(fields => fields[0].StartsWith(corpus + "/", StringComparison.Ordinal)).Select(fields => (fields[0], int.Parse(fields[1], CultureInfo.InvariantCulture))).ToHashSet();
        var found = new HashSet<(string, int)>();
        byte[] marker = "/JBIG2Decode"u8.ToArray();
        foreach (string file in files)
        {
            // Stream dictionaries cannot live in object streams, so a file without the name's bytes has no JBIG2 image.
            if (File.ReadAllBytes(RealWorldCorpus.PathOf(file)).AsSpan().IndexOf(marker) < 0)
            {
                continue;
            }

            using PdfDocument document = Open(file);
            long count = document.Trailer.TryGetValue(SizeKey, out CosObject? entry) && entry is CosInteger integer ? integer.Value : 0;
            for (int number = 1; number < Math.Min(count, DocumentWalker.MaxObjectNumber + 1L); number++)
            {
                if (document.Resolve(new CosReference(number, 0)) is CosStream stream && IsJbig2(document, stream))
                {
                    found.Add((file, number));
                }
            }
        }

        Assert.Empty(found.Except(listed));
        Assert.Empty(listed.Except(found));
    }

    private static bool IsJbig2(PdfDocument document, CosStream stream) =>
        document.Resolve(stream.Dictionary.TryGetValue(FilterKey, out CosObject? filter) ? filter : null) switch
        {
            CosName name => name.Equals(Jbig2Decode),
            CosArray { Count: > 0 } array => document.Resolve(array[^1]) is CosName last && last.Equals(Jbig2Decode),
            _ => false,
        };

    private static PdfDocument Open(string file) => Passwords.TryGetValue(file, out string? password)
        ? PdfDocument.Open(RealWorldCorpus.PathOf(file), new PdfOptions().WithPassword(password))
        : PdfDocument.Open(RealWorldCorpus.PathOf(file));

    private static void SkipUnlessFetched(string file)
    {
        string corpus = file[..file.IndexOf('/', StringComparison.Ordinal)];
        if (!RealWorldCorpus.Files(corpus).Contains(file))
        {
            Assert.Skip($"{file} not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }
    }

    private static IEnumerable<string[]> ReadResults() =>
        File.ReadLines(ResultsPath).Where(line => line.Length > 0 && line[0] != '#').Select(line => line.Split('\t'));
}
