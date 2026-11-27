using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// A full save that merges duplicate objects (<see cref="PdfSaveOptions.MergeDuplicateObjects"/>, issue #634): identical streams
/// and identity-free dictionaries are written once, every reference to a duplicate written as one to the object kept.
/// ISO 32000-2 §7.3.10 and §7.5.3.
/// </summary>
public sealed class DuplicateMergeTests
{
    private static readonly CosName Resources = new("Resources");
    private static readonly CosName XObject = new("XObject");
    private static readonly CosName Font = new("Font");
    private static readonly CosName FontDescriptor = new("FontDescriptor");
    private static readonly CosName FontFile2 = new("FontFile2");
    private static readonly CosName DecodeParms = new("DecodeParms");
    private static readonly CosName Filter = new("Filter");

    [Fact]
    public void Two_identical_image_streams_become_one()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0]; // a new document has one blank page
        CosReference first = document.AddObject(Image([1, 2, 3, 4]));
        CosReference second = document.AddObject(Image([1, 2, 3, 4]));
        page.Dictionary[Resources] = new CosDictionary { [XObject] = new CosDictionary { [new CosName("Im0")] = first, [new CosName("Im1")] = second } };

        byte[] merged = Save(document, Merging());
        byte[] plain = Save(document, new PdfSaveOptions());

        using PdfDocument reopened = PdfDocument.Open(merged);
        CosDictionary images = XObjects(reopened, reopened.Pages[0]);
        Assert.Equal(images[new CosName("Im0")], images[new CosName("Im1")]);
        Assert.Equal(first, images[new CosName("Im0")]);
        Assert.True(merged.Length < plain.Length, $"Merged {merged.Length} bytes, not merged {plain.Length}.");
        AssertQpdfCheck(merged);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, item => item.Code == SavingDiagnostics.DuplicateObjectsMerged.Name);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Contains("1 duplicate object", diagnostic.Message, StringComparison.Ordinal);

        // The document itself is unchanged: both of its objects are still there, each referred to as before.
        CosDictionary live = XObjects(document, document.Pages[0]);
        Assert.Equal(second, live[new CosName("Im1")]);
        Assert.IsType<CosStream>(document.Resolve(second));
    }

    [Fact]
    public void The_same_jpeg_as_three_indirect_objects_is_written_once_and_the_page_renders_from_it()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("duplicate-images.pdf"));

        byte[] merged = Save(document, Merging());

        using PdfDocument reopened = PdfDocument.Open(merged);
        CosDictionary images = XObjects(reopened, reopened.Pages[0]);
        Assert.Single(images.Values.Distinct());
        Assert.Equal(3, images.Count);
        Assert.Single(ImageStreams(reopened));
        Assert.True(merged.Length < Corpus.Bytes("duplicate-images.pdf").Length);
        AssertQpdfCheck(merged);
    }

    [Fact]
    public void A_font_dictionary_pair_that_becomes_equal_only_after_its_font_programs_merge_collapses_in_a_later_round()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0]; // a new document has one blank page

        // The fonts and descriptors take lower numbers than their font programs, so the first round sees the descriptors refer to
        // two different streams: they merge on the second round, once the programs have, and the fonts on the third.
        var fontA = new CosDictionary();
        var fontB = new CosDictionary();
        CosReference fontARef = document.AddObject(fontA);
        CosReference fontBRef = document.AddObject(fontB);
        var descriptorA = new CosDictionary();
        var descriptorB = new CosDictionary();
        CosReference descriptorARef = document.AddObject(descriptorA);
        CosReference descriptorBRef = document.AddObject(descriptorB);
        CosReference programA = document.AddObject(new CosStream(new CosDictionary { [new CosName("Length1")] = new CosInteger(4) }, new byte[] { 0, 1, 0, 0 }));
        CosReference programB = document.AddObject(new CosStream(new CosDictionary { [new CosName("Length1")] = new CosInteger(4) }, new byte[] { 0, 1, 0, 0 }));
        Fill(fontA, descriptorARef);
        Fill(fontB, descriptorBRef);
        Describe(descriptorA, programA);
        Describe(descriptorB, programB);
        page.Dictionary[Resources] = new CosDictionary { [Font] = new CosDictionary { [new CosName("F1")] = fontARef, [new CosName("F2")] = fontBRef } };

        byte[] merged = Save(document, Merging());

        using PdfDocument reopened = PdfDocument.Open(merged);
        var resources = (CosDictionary)reopened.Resolve(reopened.Pages[0].Dictionary[Resources]);
        var fonts = (CosDictionary)reopened.Resolve(resources[Font]);
        Assert.Equal(fontARef, fonts[new CosName("F1")]);
        Assert.Equal(fontARef, fonts[new CosName("F2")]);
        var font = (CosDictionary)reopened.Resolve(fontARef);
        var descriptor = (CosDictionary)reopened.Resolve(font[FontDescriptor]);
        Assert.Equal(programA, descriptor[FontFile2]);
        Assert.IsType<CosNull>(reopened.Resolve(fontBRef));
        Assert.IsType<CosNull>(reopened.Resolve(descriptorBRef));
        Assert.IsType<CosNull>(reopened.Resolve(programB));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, item => item.Code == SavingDiagnostics.DuplicateObjectsMerged.Name);
        Assert.Contains("3 duplicate object(s)", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("in 3 round(s)", diagnostic.Message, StringComparison.Ordinal);
        AssertQpdfCheck(merged);
    }

    [Fact]
    public void Streams_that_differ_only_in_Length_merge()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0]; // a new document has one blank page
        CosStream left = Image([9, 8, 7, 6]);
        CosStream right = Image([9, 8, 7, 6]);
        left.Dictionary[KnownLength] = new CosInteger(4);
        right.Dictionary[KnownLength] = document.AddObject(new CosInteger(4));
        CosReference first = document.AddObject(left);
        CosReference second = document.AddObject(right);
        page.Dictionary[Resources] = new CosDictionary { [XObject] = new CosDictionary { [new CosName("Im0")] = first, [new CosName("Im1")] = second } };

        using PdfDocument reopened = PdfDocument.Open(Save(document, Merging()));

        CosDictionary images = XObjects(reopened, reopened.Pages[0]);
        Assert.Equal(first, images[new CosName("Im0")]);
        Assert.Equal(first, images[new CosName("Im1")]);
    }

    [Fact]
    public void Streams_that_differ_in_DecodeParms_do_not_merge()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0]; // a new document has one blank page
        byte[] data = Flate([1, 2, 3, 4, 5, 6]);
        CosStream left = Image(data);
        CosStream right = Image(data);
        left.Dictionary[Filter] = new CosName("FlateDecode");
        right.Dictionary[Filter] = new CosName("FlateDecode");
        right.Dictionary[DecodeParms] = new CosDictionary { [new CosName("Predictor")] = new CosInteger(1) };
        CosReference first = document.AddObject(left);
        CosReference second = document.AddObject(right);
        page.Dictionary[Resources] = new CosDictionary { [XObject] = new CosDictionary { [new CosName("Im0")] = first, [new CosName("Im1")] = second } };

        using PdfDocument reopened = PdfDocument.Open(Save(document, Merging()));

        CosDictionary images = XObjects(reopened, reopened.Pages[0]);
        Assert.Equal(first, images[new CosName("Im0")]);
        Assert.Equal(second, images[new CosName("Im1")]);
        Assert.DoesNotContain(document.Diagnostics, item => item.Code == SavingDiagnostics.DuplicateObjectsMerged.Name);
    }

    [Fact]
    public void Identical_pages_are_never_merged()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(document.Pages[0].MediaBox);
        document.Pages.Add(document.Pages[0].MediaBox);

        using PdfDocument reopened = PdfDocument.Open(Save(document, Merging()));

        Assert.Equal(3, reopened.Pages.Count);
        Assert.Equal(3, reopened.Pages.Select(page => page.Reference).Distinct().Count());
    }

    [Fact]
    public void Nothing_merges_without_the_option()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("duplicate-images.pdf"));

        using PdfDocument reopened = PdfDocument.Open(Save(document, new PdfSaveOptions()));

        Assert.Equal(3, XObjects(reopened, reopened.Pages[0]).Values.Distinct().Count());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_incremental_save_refuses_the_option()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("duplicate-images.pdf"));
        using var output = new MemoryStream();

        Assert.Throws<InvalidOperationException>(() => document.Save(output, Merging().WithIncrementalUpdate(true)));
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table, PdfObjectNumbering.Automatic, false)]
    [InlineData(PdfCrossReferenceLayout.StreamWithObjectStreams, PdfObjectNumbering.Automatic, false)]
    [InlineData(PdfCrossReferenceLayout.Table, PdfObjectNumbering.Compact, false)]
    [InlineData(PdfCrossReferenceLayout.Table, PdfObjectNumbering.Automatic, true)]
    [InlineData(PdfCrossReferenceLayout.StreamWithObjectStreams, PdfObjectNumbering.Automatic, true)]
    public void Merged_copies_of_one_document_are_smaller_with_the_option_extract_the_same_text_and_pass_qpdf_check(
        PdfCrossReferenceLayout layout,
        PdfObjectNumbering numbering,
        bool linearize)
    {
        using PdfDocument first = PdfDocument.Open(Corpus.Path("text-truetype-embedded.pdf"));
        using PdfDocument second = PdfDocument.Open(Corpus.Path("text-truetype-embedded.pdf"));
        using PdfDocument third = PdfDocument.Open(Corpus.Path("duplicate-images.pdf"));
        using PdfDocument combined = PdfDocument.Merge(first, second, third);
        PdfSaveOptions Options() => new PdfSaveOptions().WithCrossReferenceLayout(layout).WithObjectNumbering(numbering).WithLinearize(linearize);

        byte[] plain = Save(combined, Options());
        byte[] merged = Save(combined, Options().WithMergeDuplicateObjects(true));

        Assert.True(merged.Length < plain.Length, $"Merged {merged.Length} bytes, not merged {plain.Length}.");
        AssertQpdfCheck(merged);
        using PdfDocument before = PdfDocument.Open(plain);
        using PdfDocument after = PdfDocument.Open(merged);
        Assert.Equal(before.Pages.Count, after.Pages.Count);
        for (int index = 0; index < before.Pages.Count; index++)
        {
            Assert.Equal(before.Pages[index].ExtractText().Text, after.Pages[index].ExtractText().Text);
        }

        Assert.Single(ImageStreams(after));
        Assert.Empty(after.Diagnostics);
        if (linearize)
        {
            Assert.True(after.IsLinearized);
            (int exitCode, string output) = ExternalTool.Run("qpdf", merged, "--check-linearization");
            Assert.True(exitCode == 0, output);
        }
    }

    [Fact]
    public void An_encrypted_document_merges_on_its_decrypted_data()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("duplicate-images.pdf"));
        byte[] encrypted = Save(document, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner")));
        using PdfDocument opened = PdfDocument.Open(encrypted, new PdfOptions().WithPassword("user"));

        byte[] merged = Save(opened, Merging());

        using PdfDocument reopened = PdfDocument.Open(merged, new PdfOptions().WithPassword("user"));
        Assert.True(reopened.IsEncrypted);
        Assert.Single(XObjects(reopened, reopened.Pages[0]).Values.Distinct());
        Assert.Single(ImageStreams(reopened));
    }

    private static readonly CosName KnownLength = new("Length");

    private static PdfSaveOptions Merging() => new PdfSaveOptions().WithMergeDuplicateObjects(true);

    private static CosStream Image(byte[] data) => new(
        new CosDictionary
        {
            [new CosName("Type")] = XObject,
            [new CosName("Subtype")] = new CosName("Image"),
            [new CosName("Width")] = new CosInteger(data.Length),
            [new CosName("Height")] = new CosInteger(1),
            [new CosName("ColorSpace")] = new CosName("DeviceGray"),
            [new CosName("BitsPerComponent")] = new CosInteger(8),
        },
        data);

    private static void Fill(CosDictionary font, CosReference descriptor)
    {
        font[new CosName("Type")] = Font;
        font[new CosName("Subtype")] = new CosName("TrueType");
        font[new CosName("BaseFont")] = new CosName("ABCDEF+Minimal");
        font[FontDescriptor] = descriptor;
    }

    private static void Describe(CosDictionary descriptor, CosReference program)
    {
        descriptor[new CosName("Type")] = FontDescriptor;
        descriptor[new CosName("FontName")] = new CosName("ABCDEF+Minimal");
        descriptor[new CosName("Flags")] = new CosInteger(32);
        descriptor[FontFile2] = program;
    }

    private static byte[] Flate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return output.ToArray();
    }

    private static CosDictionary XObjects(PdfDocument document, PdfPage page)
    {
        var resources = (CosDictionary)document.Resolve(page.Dictionary[Resources]);
        return (CosDictionary)document.Resolve(resources[XObject]);
    }

    /// <summary>The image XObjects the file holds, found by walking every object the trailer reaches.</summary>
    private static List<CosStream> ImageStreams(PdfDocument document)
    {
        var images = new List<CosStream>();
        var seen = new HashSet<CosReference>();
        var pending = new Stack<CosObject>();
        pending.Push(document.Trailer);
        while (pending.TryPop(out CosObject? value))
        {
            switch (value)
            {
                case CosReference reference when seen.Add(reference):
                    pending.Push(document.Resolve(reference));
                    break;
                case CosArray array:
                    foreach (CosObject item in array)
                    {
                        pending.Push(item);
                    }

                    break;
                case CosDictionary dictionary:
                    foreach (CosObject item in dictionary.Values)
                    {
                        pending.Push(item);
                    }

                    break;
                case CosStream stream:
                    if (stream.Dictionary.TryGetValue(new CosName("Subtype"), out CosObject? subtype) && subtype.Equals(new CosName("Image")))
                    {
                        images.Add(stream);
                    }

                    pending.Push(stream.Dictionary);
                    break;
                default:
                    break;
            }
        }

        return images;
    }

    private static void AssertQpdfCheck(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }
}
