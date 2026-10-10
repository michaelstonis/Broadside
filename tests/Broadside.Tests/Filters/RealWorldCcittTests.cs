using System.Security.Cryptography;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// Real-world CCITT fax images decode bit for bit as Ghostscript's PostScript <c>CCITTFaxDecode</c> filter (Adobe's filter semantics)
/// decodes them. The hashes were produced offline: each image's raw stream data (<c>qpdf --show-object=N --raw-stream-data</c>)
/// through <c>gs -dSAFER -dNODISPLAY -c "/src (in) (r) file &lt;&lt; DecodeParms &gt;&gt; /CCITTFaxDecode filter ..."</c>
/// (Ghostscript 10.08.0), SHA-256 of the output. Skips when the corpora are not fetched. ISO 32000-2 §7.4.6.
/// </summary>
[Trait("Category", "Corpus")]
public class RealWorldCcittTests
{
    public static TheoryData<string, int, string> GhostscriptDecodes => new()
    {
        // pdf.js ccitt_EndOfBlock_false.pdf: 81 x 26, K -1 / 0 / 1, each with EndOfBlock false and true.
        { "pdfjs/ccitt_EndOfBlock_false.pdf", 6, "e36f750d7917176a3a0904356e8c4a94fdcbad495e88b8cfae0b33e11da00467" },
        { "pdfjs/ccitt_EndOfBlock_false.pdf", 7, "e36f750d7917176a3a0904356e8c4a94fdcbad495e88b8cfae0b33e11da00467" },
        { "pdfjs/ccitt_EndOfBlock_false.pdf", 8, "8e6688c8074ab9f3a5fb680a6d03a9ef7a2cdd5bec258d07e6871b90b9eefe12" },
        { "pdfjs/ccitt_EndOfBlock_false.pdf", 9, "8e6688c8074ab9f3a5fb680a6d03a9ef7a2cdd5bec258d07e6871b90b9eefe12" },
        { "pdfjs/ccitt_EndOfBlock_false.pdf", 10, "b771a298d7a1d98368e88b18d5e0fabc5bbcc691389afad106708ff1f47a1acb" },
        { "pdfjs/ccitt_EndOfBlock_false.pdf", 11, "b771a298d7a1d98368e88b18d5e0fabc5bbcc691389afad106708ff1f47a1acb" },
        { "pdfjs/issue13561_reduced.pdf", 15, "28ff3443e93d515350cd77251e419d79cd938c2697e1b2c79bbed6f44d8b9f18" },
        { "pdfjs/freeculture.pdf", 610, "ff8d2ac18774f67b2caa1b11442506b185f81e480ea89a0fa3fbe8ce0ad5e576" },
        { "pdfjs/freeculture.pdf", 900, "40568b429d38ef8138f5ab1d3f218cf64c359f47fb39654e27439cd9c4c80a9c" },
        { "pdfjs/bug1815476.pdf", 47, "7c72d6b40218e5c2a7285fe386b723e545e3add78abb7693aaadf636313cd62e" },
        { "pdfjs/bug1815476.pdf", 48, "84d7ccd075a96b6203bd60a3ae80fc079c1ee3b3aff2533f0ed0080622d19965" },
        { "pdfjs/bug1815476.pdf", 50, "9997844416c17c7219ab02150661a6cf653f64dbba3da169363bbffce553a720" },
        { "pdfjs/issue13372.pdf", 14, "829736f89e7a7ed2cbf7d70d1425148dfa2644fb8df1825412663ab44c1413da" },
        { "pdfjs/issue4379.pdf", 1, "db397dc4aef9731de4bc0eefbd4478df5049605138e49472d60bd12b6e59b829" },
        { "pdfjs/images_1bit_grayscale.pdf", 9, "e139778a0bbb80db5b38e4efc032fb67b740e9dbccd308f2b47ad0524040e8b9" },
        { "pdfjs/images_1bit_grayscale.pdf", 10, "7ff3e93525f2345953d265ddd98f976980ea27f4246369162d72ee148087fbdd" },
        { "pdfjs/issue1985.pdf", 14, "127e90fc5f15411925d2d31fc22668d44b3af094fea1ec115950fc2aa17cfdd9" },
        { "qpdf/fax-decode-parms.pdf", 15, "9bb9b3b71901925de6ed2386f0feccc015c63b78bea6d2d151134ca84d693da2" },
        { "qpdf/fax-decode-parms.pdf", 16, "f443ef10841eff23d643c86ee55202075208914495e5ab49cca80a87eac7bddd" },
    };

    [Theory]
    [MemberData(nameof(GhostscriptDecodes))]
    public void A_real_world_fax_image_decodes_as_Ghostscript_decodes_it(string file, int objectNumber, string sha256)
    {
        string corpus = file[..file.IndexOf('/', StringComparison.Ordinal)];
        if (!RealWorldCorpus.Files(corpus).Contains(file))
        {
            Assert.Skip($"{file} not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        using PdfDocument document = PdfDocument.Open(RealWorldCorpus.PathOf(file));
        var stream = (CosStream)document.Resolve(new CosReference(objectNumber, 0));

        byte[] decoded = document.DecodeStream(stream).ToArray();

        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(decoded)));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Ccitt", StringComparison.Ordinal));
    }
}
