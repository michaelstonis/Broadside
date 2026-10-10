using System.Security.Cryptography;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// The JPEG 2000 images of the fetched real-world corpora decode as OpenJPEG decodes them (ISO 32000-2 §7.4.9). Two kinds of golden:
/// <list type="bullet">
/// <item><see cref="Decodable"/>: SHA-256 hashes of the §8.9.3 samples built from <c>opj_decompress</c> 2.5.4's component output, exact
/// (for issue12213.pdf, whose PDF colour space is Indexed, the codestream's palette indices, checked by mapping them through the file's
/// pclr box to OpenJPEG's output).</item>
/// <item><c>jpx-corpus-references.txt</c>: every JPXDecode image of every fetched corpus, with the hash of the samples (and alpha
/// plane) this decoder produces, accepted after comparing them with <c>opj_decompress</c> 2.5.4 by <c>jpx-corpus-references.py</c>
/// (peak absolute error at most 4 and mean squared error at most 1 per channel, exact for the 5/3 path; the measured errors are
/// recorded on each line), or an exclusion with its reason.</item>
/// </list>
/// Skips when the corpora are not fetched. With <c>BROADSIDE_JPX_DUMP</c> set to a directory, the sweep writes each image's encoded
/// data and decoded samples there for the script.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldJpxTests(ITestOutputHelper output)
{
    private const string DumpVariable = "BROADSIDE_JPX_DUMP";

    public static TheoryData<string, string, string, string> Decodable => new()
    {
        { "pdfjs/bug_jpx.pdf", "Im0", "128x64x1@8", "9f1dcbc35c350d6027f98be0f5c8b43b42ca52b7604459c0c42be3aa88913d47" },
        { "pdfjs/issue12213.pdf", "Im0", "684x74x1@4", "eb13096ade467b9a8d02b9e649aa1b5882782d82b556cd9590e9f45cda45580b" },
        { "pdfjs/issue19326.pdf", "Im0", "551x337x1@16", "278c960c2bdf924c11a242461c89e55b0c95c46870455d027b29273839961783" },
        { "pdfjs/issue5567.pdf", "Im0", "1500x1125x3@8", "9b1867ce271f845f2554889155bc72eb8887bcf2198a9fc45cdd334b165675b6" },
        { "pdfjs/jp2k-resetprob.pdf", "Im0", "40x27x3@8", "1afd5fc16ce16dab36523f06c290fc5e9a8506f1eab1ba28f3ac67b898b79ade" },
        { "pdfjs/jpx_smaskindata.pdf", "Im5", "2x1x3@8", "fc22de0cf91e158746409661edf4752b6190dd1ae7133be5436786d584b0c76f" },
        { "verapdf-corpus/PDF_A-2b/6.2 Graphics/6.2.8 Images/6.2.8.3 JPEG2000/veraPDF test suite 6-2-8-3-t01-pass-a.pdf", "Im1", "640x480x3@8", "7b1d653ae545066152e5fcab24d405229cd84de0ff230a448879bbf278ebe03c" },
    };

    public static TheoryData<string> CorpusIds => new([.. Document.RealWorldCorpusTests.CorpusIds, "verapdf-corpus"]);

    [Theory]
    [MemberData(nameof(Decodable))]
    public void A_real_world_JPEG_2000_image_decodes_to_the_samples_OpenJPEG_gives(string file, string name, string shape, string sha256)
    {
        using PdfDocument document = Open(file);

        using DecodedImage decoded = document.Pages[0].GetImage(name)!.Decode()!;

        Assert.Equal(shape, $"{decoded.Width}x{decoded.Height}x{decoded.Components}@{decoded.BitsPerComponent}");
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(decoded.Samples)));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Jpx", StringComparison.Ordinal) && diagnostic.Severity > DiagnosticSeverity.Information);
    }

    [Fact]
    public void An_image_without_a_colour_space_and_with_SMaskInData_gets_its_colour_channels_and_a_premultiplied_alpha_plane()
    {
        // pdf.js jpx_smaskindata.pdf Im7: an RGBA JP2 (cdef: three colours and an opacity of type 1), no ColorSpace, SMaskInData 2.
        // opj_decompress 2.5.4 gives R, G, B, A = (128, 128, 255, 128) and (0, 0, 255, 0): blue premultiplied by alpha over the
        // dictionary's Matte [0 0 1]; the PDF value 2 makes the alpha premultiplied.
        using PdfDocument document = Open("pdfjs/jpx_smaskindata.pdf");
        PdfImage image = document.Pages[0].GetImage("Im7")!;

        using DecodedImage decoded = image.Decode()!;

        Assert.Equal((2, 1, 3, 8, ImageColorModel.Rgb), (decoded.Width, decoded.Height, decoded.Components, decoded.BitsPerComponent, decoded.ColorModel));
        Assert.NotNull(decoded.Alpha);
        Assert.True(decoded.AlphaPremultiplied);
        Assert.Equal(PdfImageMaskKind.SoftInData, image.MaskKind);
        Assert.Equal([128, 128, 255, 0, 0, 255], decoded.Samples.ToArray());
        Assert.Equal([128, 0], decoded.Alpha!.Samples.ToArray());
    }

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Every_JPEG_2000_image_of_a_real_world_corpus_decodes_as_its_reference_records(string corpusId)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpusId);
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus '{corpusId}' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        Dictionary<(string File, int Object), Reference> references = ReadReferences();
        string? dump = Environment.GetEnvironmentVariable(DumpVariable);
        var seen = new HashSet<(string, int)>();
        var failures = new List<string>();
        foreach (string file in files)
        {
            if (Document.RealWorldCorpusTests.Unreadable.ContainsKey(file))
            {
                continue;
            }

            Sweep(file, references, dump, seen, failures);
        }

        foreach (((string file, int number), Reference _) in references)
        {
            if (files.Contains(file) && !seen.Contains((file, number)))
            {
                failures.Add($"{file} | {number}: listed in jpx-corpus-references.txt but not found");
            }
        }

        output.WriteLine($"{corpusId}: {seen.Count} JPEG 2000 images.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static void Sweep(string file, Dictionary<(string File, int Object), Reference> references, string? dump, HashSet<(string, int)> seen, List<string> failures)
    {
        using PdfDocument? document = OpenOrNull(file);
        if (document is null)
        {
            return;
        }

        foreach ((CosReference reference, CosStream stream) in JpxStreams(document))
        {
            string key = $"{file} | {reference.ObjectNumber}";
            seen.Add((file, reference.ObjectNumber));
            using DecodedImage? decoded = document.GetImage(reference)!.Decode();
            string actual = Describe(decoded);
            if (dump is not null)
            {
                Dump(dump, file, reference.ObjectNumber, stream, decoded);
            }

            if (!references.TryGetValue((file, reference.ObjectNumber), out Reference? expected))
            {
                failures.Add($"{key}: not in jpx-corpus-references.txt (decoded as {actual})");
            }
            else if (expected.Excluded is not null ? decoded is not null : actual != expected.Outcome)
            {
                failures.Add($"{key}: expected {expected.Excluded ?? expected.Outcome}, decoded as {actual}");
            }
        }
    }

    /// <summary>The image's outcome as the references record it: shape, alpha, and the SHA-256 of samples then alpha samples.</summary>
    private static string Describe(DecodedImage? decoded)
    {
        if (decoded is null)
        {
            return "null";
        }

        string alpha = decoded.Alpha is { } plane ? $"+a{plane.BitsPerComponent}{(decoded.AlphaPremultiplied ? "p" : string.Empty)}" : string.Empty;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(decoded.Samples);
        if (decoded.Alpha is { } opacity)
        {
            hash.AppendData(opacity.Samples);
        }

        return $"{decoded.Width}x{decoded.Height}x{decoded.Components}@{decoded.BitsPerComponent}{alpha} | {Convert.ToHexStringLower(hash.GetHashAndReset())}";
    }

    /// <summary>Every image stream whose last filter is JPXDecode, reached from the trailer.</summary>
    private static List<(CosReference Reference, CosStream Stream)> JpxStreams(PdfDocument document)
    {
        var found = new List<(CosReference, CosStream)>();
        var references = new HashSet<CosReference>();
        var containers = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(CosObject Value, CosReference? Reference)>();
        pending.Push((document.Trailer, null));
        while (pending.TryPop(out (CosObject Value, CosReference? Reference) item))
        {
            switch (item.Value)
            {
                case CosReference reference when references.Add(reference):
                    pending.Push((document.Resolve(reference), reference));
                    break;
                case CosStream stream when containers.Add(stream):
                    if (item.Reference is { } owner && IsJpxImage(document, stream))
                    {
                        found.Add((owner, stream));
                    }

                    pending.Push((stream.Dictionary, null));
                    break;
                case CosDictionary dictionary when containers.Add(dictionary):
                    foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
                    {
                        pending.Push((entry.Value, null));
                    }

                    break;
                case CosArray array when containers.Add(array):
                    foreach (CosObject entry in array)
                    {
                        pending.Push((entry, null));
                    }

                    break;
            }
        }

        found.Sort((a, b) => a.Item1.ObjectNumber.CompareTo(b.Item1.ObjectNumber));
        return found;
    }

    private static bool IsJpxImage(PdfDocument document, CosStream stream)
    {
        CosDictionary dictionary = stream.Dictionary;
        if (document.Resolve(dictionary.TryGetValue(new CosName("Subtype"), out CosObject? subtype) ? subtype : null) is not CosName { Value: "Image" })
        {
            return false;
        }

        CosObject filter = document.Resolve(dictionary.TryGetValue(new CosName("Filter"), out CosObject? value) ? value : null);
        CosObject? last = filter is CosArray { Count: > 0 } array ? document.Resolve(array[^1]) : filter;
        return last is CosName { Value: "JPXDecode" };
    }

    private static void Dump(string directory, string file, int number, CosStream stream, DecodedImage? decoded)
    {
        Directory.CreateDirectory(directory);
        string stem = Path.Combine(directory, $"{file.Replace('/', '_').Replace(' ', '_')}-{number}");
        File.WriteAllBytes(stem + ".jpx", stream.EncodedData.ToArray());
        var info = new StringBuilder($"{file}\n{number}\n");
        if (decoded is not null)
        {
            File.WriteAllBytes(stem + ".samples", decoded.Samples.ToArray());
            info.Append($"{decoded.Width} {decoded.Height} {decoded.Components} {decoded.BitsPerComponent}\n");
            if (decoded.Alpha is { } alpha)
            {
                File.WriteAllBytes(stem + ".alpha", alpha.Samples.ToArray());
                info.Append($"alpha {alpha.BitsPerComponent}\n");
                info.Append(decoded.AlphaPremultiplied ? "premultiplied\n" : string.Empty);
            }

            string colorSpace = stream.Dictionary.TryGetValue(new CosName("ColorSpace"), out CosObject? space) ? space.ToString() : "none";
            info.Append($"colorspace {colorSpace}\n");
        }

        File.WriteAllText(stem + ".txt", info.ToString());
    }

    private static Dictionary<(string File, int Object), Reference> ReadReferences()
    {
        var references = new Dictionary<(string, int), Reference>();
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Images", "jpx-corpus-references.txt")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split(" | ");
            int number = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            references[(parts[0], number)] = parts[2] == "excluded"
                ? new Reference(string.Empty, parts[3])
                : new Reference($"{parts[2]} | {parts[3]}", null);
        }

        return references;
    }

    private static PdfDocument Open(string file)
    {
        if (RealWorldCorpus.Directory(file.Split('/')[0]) is null || !File.Exists(RealWorldCorpus.PathOf(file)))
        {
            Assert.Skip($"{file} is not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)));
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

    private sealed record Reference(string Outcome, string? Excluded);
}
