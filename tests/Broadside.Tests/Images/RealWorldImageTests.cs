using Broadside.Images;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// Every image XObject and inline image of the fetched real-world corpora reads through the image model in lenient mode: its
/// properties, masks and alternates read and its samples decode (or decline with a diagnostic) without an exception, and every
/// decoded buffer keeps the §8.9.3 layout's invariants. Skips when the corpora are not fetched. ISO 32000-2 §8.9.
/// </summary>
[Trait("Category", "Corpus")]
public class RealWorldImageTests(ITestOutputHelper output)
{
    public static TheoryData<string> CorpusIds => new(RealWorldCorpusTests.CorpusIds);

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Every_image_of_a_real_world_corpus_reads_and_decodes_leniently_without_an_exception(string corpusId)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpusId);
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus '{corpusId}' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        int images = 0;
        int decoded = 0;
        int inline = 0;
        foreach (string file in files)
        {
            if (RealWorldCorpusTests.Unreadable.ContainsKey(file))
            {
                continue;
            }

            using PdfDocument? document = OpenOrNull(file);
            if (document is null)
            {
                continue;
            }

            var seen = new HashSet<CosStream>(ReferenceEqualityComparer.Instance);
            foreach (PdfPage page in document.Pages)
            {
                if (page.Resources is { } resources
                    && resources.TryGetValue(new CosName("XObject"), out CosObject? value)
                    && document.Resolve(value) is CosDictionary xobjects)
                {
                    foreach (KeyValuePair<CosName, CosObject> entry in xobjects)
                    {
                        if (document.GetImage(entry.Value) is { Stream: { } stream } image
                            && document.Resolve(stream.Dictionary.TryGetValue(new CosName("Subtype"), out CosObject? subtype) ? subtype : null) is CosName { Value: "Image" }
                            && seen.Add(stream))
                        {
                            images++;
                            decoded += Read(image, file) ? 1 : 0;
                        }
                    }
                }

                var collector = new InlineImageCollector();
                page.ProcessContent(collector);
                foreach (PdfImage image in collector.Images)
                {
                    inline++;
                    Read(image, file);
                }
            }
        }

        output.WriteLine($"{corpusId}: {images} image XObjects ({decoded} decoded), {inline} inline images.");
    }

    private static bool Read(PdfImage image, string file)
    {
        _ = (image.Width, image.Height, image.BitsPerComponent, image.IsStencil, image.ColorSpace, image.DecodeArray, image.Interpolate, image.Intent);
        _ = (image.MaskKind, image.ColorKey, image.Matte, image.SoftMaskInData, image.Alternates, image.OptionalContent, image.Metadata, image.StructParent, image.Name);
        bool any = Check(image, file);
        if (image.Mask is { } mask)
        {
            Check(mask, file);
        }

        return any;
    }

    private static bool Check(PdfImage image, string file)
    {
        using DecodedImage? samples = image.Decode();
        if (samples is null)
        {
            return false;
        }

        Assert.True(samples.Samples.Length == samples.Stride * samples.Height, file);
        Assert.True(samples.DecodedRows <= samples.Height, file);
        Assert.True(samples.Stride == ((samples.Width * samples.Components * samples.StorageBits) + 7) / 8, file);
        ImageDecodeMap map = image.CreateDecodeMap(samples);
        Assert.Equal(samples.Components, map.Components);
        return true;
    }

    private static PdfDocument? OpenOrNull(string file)
    {
        try
        {
            return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)), new PdfOptions().WithMaxImagePixels(1 << 24));
        }
        catch (Exception exception) when (exception is PdfPasswordException or PdfCertificateException or NotSupportedException)
        {
            return null;
        }
    }
}
