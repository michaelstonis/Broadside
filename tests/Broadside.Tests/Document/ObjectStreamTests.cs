using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// Objects stored in object streams resolve like any other object, through the type 2 entries of a cross-reference stream.
/// ISO 32000-2 §7.5.7 (Table 16) and §7.5.8.3 (Table 18).
/// </summary>
public sealed class ObjectStreamTests
{
    private static readonly int[] W121 = [1, 2, 1];

    [Fact]
    public void The_catalog_page_tree_and_page_resolve_from_an_object_stream()
    {
        // object-stream.pdf: objects 1-3 are members 0-2 of object stream 4 (tests/Corpus/README.md).
        using PdfDocument document = PdfDocument.Open(Corpus.Path("object-stream.pdf"));

        CosDictionary page = Assert.IsType<CosDictionary>(document.Resolve(new CosReference(3, 0)));

        Assert.Equal(new CosName("Page"), page[new CosName("Type")]);
        Assert.Same(page, document.Pages[0].Dictionary);
        Assert.Same(page, document.Resolve(new CosReference(3, 0)));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_reference_to_a_member_with_a_generation_other_than_0_resolves_to_null()
    {
        // §7.5.7: "The generation number of an object stream and of any compressed object shall be zero."
        using PdfDocument document = PdfDocument.Open(Corpus.Path("object-stream.pdf"));

        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(3, 1)));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Members_need_no_white_space_between_them()
    {
        // §7.5.7 NOTE 7: each object is bounded by the next one's offset, so "(a)(b)12" holds three objects.
        byte[] file = OneObjectStream([(4, "(a)"), (5, "(b)"), (6, "12"), (7, "<< /K 1 >>")], separator: string.Empty);

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal("a", Assert.IsType<CosString>(document.Resolve(new CosReference(4, 0))).DecodeText());
        Assert.Equal("b", Assert.IsType<CosString>(document.Resolve(new CosReference(5, 0))).DecodeText());
        Assert.Equal(new CosInteger(12), document.Resolve(new CosReference(6, 0)));
        Assert.IsType<CosDictionary>(document.Resolve(new CosReference(7, 0)));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_member_at_another_index_than_its_entry_says_is_found_by_its_object_number()
    {
        // The stream holds 4 then 5; the cross-reference stream says 4 is at index 1.
        byte[] file = OneObjectStream([(4, "(four)"), (5, "(five)")], indexes: [1, 0]);

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal("four", Assert.IsType<CosString>(document.Resolve(new CosReference(4, 0))).DecodeText());
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("ObjectStreamIndexMismatch", diagnostic.Code);
        Assert.Equal(new CosReference(4, 0), diagnostic.ObjectReference);
    }

    [Fact]
    public void Offsets_out_of_order_bound_each_member_by_the_next_offset_in_sorted_order()
    {
        // Header "5 6 4 0": object 5 is stored after object 4 although its pair comes first.
        byte[] file = OneObjectStream([(4, "(four)"), (5, "(five)")], separator: string.Empty, header: "5 6 4 0 ");

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal("four", Assert.IsType<CosString>(document.Resolve(new CosReference(4, 0))).DecodeText());
        Assert.Equal("five", Assert.IsType<CosString>(document.Resolve(new CosReference(5, 0))).DecodeText());
        Assert.Contains("ObjectStreamHeaderInvalid", document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_member_the_object_stream_does_not_hold_resolves_to_null()
    {
        byte[] file = OneObjectStream([(4, "(four)")], extraEntries: [(5, 0)]);

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(5, 0)));
        Assert.Equal("ObjectStreamMemberMissing", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void An_object_stream_whose_N_is_one_of_its_own_members_resolves_its_members_to_null()
    {
        // A container cannot be decoded with a value it holds; the loader must end instead of recursing.
        byte[] file = OneObjectStream([(4, "(four)"), (5, "2")], count: "5 0 R");

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(4, 0)));
        Assert.Equal(["ObjectStreamCycle", "ObjectStreamInvalid"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void An_object_stream_listed_as_compressed_itself_is_not_read()
    {
        // Streams cannot be stored in object streams (§7.5.7), so a container with a type 2 entry is invalid.
        var pdf = new XrefStreamPdf().AddOnePage();
        byte[] file = pdf.Finish(4, "/Size 6 /Root 1 0 R /W [1 2 1]", W121, (0, 0, 255), (1, pdf.Offset(1), 0), (1, pdf.Offset(2), 0), (1, pdf.Offset(3), 0), (2, 4, 0), (2, 4, 0));

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(5, 0)));
        Assert.Equal("ObjectStreamNested", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_type_2_entry_naming_an_object_that_is_not_a_stream_resolves_to_null()
    {
        var pdf = new XrefStreamPdf().AddOnePage();
        byte[] file = pdf.Finish(4, "/Size 5 /Root 1 0 R /W [1 2 1]", W121, (0, 0, 255), (1, pdf.Offset(1), 0), (1, pdf.Offset(2), 0), (1, pdf.Offset(3), 0), (2, 2, 0));

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(4, 0)));
        Assert.Equal("ObjectStreamInvalid", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_flate_encoded_object_stream_is_decoded_through_the_filters()
    {
        byte[] data = FilterEncoders.Zlib("4 0 5 7 (four) (five)"u8.ToArray());
        var pdf = new XrefStreamPdf().AddOnePage();
        pdf.Add(6, $"<< /Type /ObjStm /N 2 /First 8 /Filter /FlateDecode /Length {data.Length} >>\nstream\n{System.Text.Encoding.Latin1.GetString(data)}\nendstream");
        byte[] file = pdf.Finish(
            7,
            "/Size 7 /Root 1 0 R /W [1 2 1]",
            W121,
            (0, 0, 255),
            (1, pdf.Offset(1), 0),
            (1, pdf.Offset(2), 0),
            (1, pdf.Offset(3), 0),
            (2, 6, 0),
            (2, 6, 1),
            (1, pdf.Offset(6), 0));

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal("five", Assert.IsType<CosString>(document.Resolve(new CosReference(5, 0))).DecodeText());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Members_of_one_object_stream_resolve_to_the_same_instances_from_concurrent_threads()
    {
        // Thread-safety contract (CLAUDE.md): concurrent reads of an unmutated document are safe and see one instance per object.
        (int, string)[] members = [.. Enumerable.Range(4, 50).Select(number => (number, $"<< /N {number} >>"))];
        byte[] file = OneObjectStream(members);
        using PdfDocument document = PdfDocument.Open(file);

        CosObject[][] seen = new CosObject[8][];
        Parallel.For(0, seen.Length, thread =>
            seen[thread] = [.. Enumerable.Range(4, 50).Select(number => document.Resolve(new CosReference(number, 0)))]);

        for (int index = 0; index < 50; index++)
        {
            Assert.All(seen, objects => Assert.Same(seen[0][index], objects[index]));
        }

        Assert.Equal(new CosInteger(53), ((CosDictionary)seen[0][49])[new CosName("N")]);
        Assert.Empty(document.Diagnostics);
    }

    /// <summary>
    /// Objects 1-3 as plain objects, the given members in object stream 100, and a cross-reference stream (101) with a type 2 entry
    /// per member (at <paramref name="indexes"/> when given) and the given extra type 2 entries (object number, index).
    /// </summary>
    private static byte[] OneObjectStream(
        (int Number, string Body)[] members,
        string separator = "\n",
        string? header = null,
        int[]? indexes = null,
        (int Number, int Index)[]? extraEntries = null,
        string? count = null)
    {
        var entries = new List<(long, long, long)>();
        var numbers = new List<int>();
        XrefStreamPdf pdf = new XrefStreamPdf().AddOnePage().AddObjectStream(100, members, separator, header, count);
        for (int index = 0; index < members.Length; index++)
        {
            numbers.Add(members[index].Number);
            entries.Add((2, 100, indexes?[index] ?? index));
        }

        foreach ((int number, int index) in extraEntries ?? [])
        {
            numbers.Add(number);
            entries.Add((2, 100, index));
        }

        // One subsection for objects 1-3, one per member, one for the object stream itself.
        var subsections = new System.Text.StringBuilder("1 3");
        var rows = new List<(long, long, long)> { (1, pdf.Offset(1), 0), (1, pdf.Offset(2), 0), (1, pdf.Offset(3), 0) };
        foreach ((int number, (long, long, long) entry) in numbers.Zip(entries))
        {
            subsections.Append(System.Globalization.CultureInfo.InvariantCulture, $" {number} 1");
            rows.Add(entry);
        }

        subsections.Append(" 100 1");
        rows.Add((1, pdf.Offset(100), 0));
        return pdf.Finish(101, $"/Size 102 /Index [{subsections}] /Root 1 0 R /W [1 2 1]", W121, [.. rows]);
    }
}
