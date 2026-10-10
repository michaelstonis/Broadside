using System.Buffers;
using System.Security.Cryptography;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Tests.Document;
using Broadside.TestSupport;
using static Broadside.Tests.Filters.Dct.DctVectors;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// The JPEGs of the fetched real-world corpora decode within ±1 of libjpeg-turbo (<c>djpeg -dct int -nosmooth</c>), for every
/// image that <c>make_corpus_references.py</c> (next to this file) wrote a reference for: baseline, extended, progressive or
/// arithmetic-coded, one, three or four components (CMYK and YCCK against TurboJPEG's raw CMYK), decoded by libjpeg-turbo
/// without a warning. Skips when the corpora or the references are absent.
/// ISO 32000-2 §7.4.8.
/// </summary>
[Trait("Category", "Corpus")]
public class CorpusDctReferenceTests(ITestOutputHelper output)
{
    public static TheoryData<string> CorpusIds => new(RealWorldCorpusTests.CorpusIds);

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Every_referenced_JPEG_of_a_real_world_corpus_decodes_within_one_of_libjpeg_turbo(string corpusId)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpusId);
        string references = files.Count == 0 ? string.Empty : RealWorldCorpus.PathOf(Path.Combine("references", "dct"));
        if (files.Count == 0 || !System.IO.Directory.Exists(references))
        {
            Assert.Skip($"Corpus '{corpusId}' or its DCT references are absent; run tools/CorpusFetcher, then make_corpus_references.py.");
        }

        int compared = 0;
        var filter = new DctDecodeFilter();
        foreach (string file in files)
        {
            byte[] bytes = File.ReadAllBytes(RealWorldCorpus.PathOf(file));
            if (RealWorldCorpusTests.Unreadable.ContainsKey(file) || bytes.AsSpan().IndexOf("DCT"u8) < 0)
            {
                continue;
            }

            using PdfDocument? document = OpenOrNull(bytes);
            foreach (byte[] jpeg in document is null ? [] : DctStreams(document))
            {
                string reference = Path.Combine(references, Convert.ToHexStringLower(SHA256.HashData(jpeg)) + ".pnm");
                if (!File.Exists(reference))
                {
                    continue;
                }

                var context = new FilterContext();
                var samples = new ArrayBufferWriter<byte>();
                filter.Decode(jpeg, samples, context);
                DctGolden golden = ParsePnm(File.ReadAllBytes(reference));
                Assert.True(samples.WrittenCount == golden.Samples.Length, $"{file}: {samples.WrittenCount} samples where the reference has {golden.Samples.Length} ({string.Join(", ", Codes(context))}).");
                AssertWithinOne(golden, samples.WrittenSpan);
                compared++;
            }
        }

        output.WriteLine($"{corpusId}: {compared} JPEGs compared with libjpeg-turbo.");
    }

    /// <summary>The data each DCTDecode-last stream of the document gives its DCT filter, after the filters before it.</summary>
    private static IEnumerable<byte[]> DctStreams(PdfDocument document)
    {
        int size = document.Trailer.TryGetValue(new CosName("Size"), out CosObject? value) && document.Resolve(value) is CosInteger { Value: > 0 } count
            ? (int)Math.Min(count.Value, 1_000_000)
            : 0;
        for (int number = 1; number < size; number++)
        {
            if (document.Resolve(new CosReference(number, 0)) is not CosStream stream || !EndsWithDct(document, stream))
            {
                continue;
            }

            var data = new ArrayBufferWriter<byte>();
            document.Streams.Decode(stream, data, 0, stopBeforeImageFilter: true, out StreamDecoder.ChainOutcome outcome);
            if (outcome.Complete && outcome.ImageFilter is DctDecodeFilter)
            {
                yield return data.WrittenSpan.ToArray();
            }
        }
    }

    private static bool EndsWithDct(PdfDocument document, CosStream stream)
    {
        CosObject filter = document.Resolve(stream.Dictionary.TryGetValue(new CosName("Filter"), out CosObject? value) ? value : null);
        CosObject last = filter is CosArray { Count: > 0 } array ? document.Resolve(array[^1]) : filter;
        return last is CosName { Value: "DCTDecode" or "DCT" };
    }

    private static PdfDocument? OpenOrNull(byte[] bytes)
    {
        try
        {
            return PdfDocument.Open(bytes, new PdfOptions().WithPassword("owner"));
        }
        catch (Exception exception) when (exception is PdfPasswordException or PdfCertificateException or NotSupportedException)
        {
            return null;
        }
    }
}
