using System.Globalization;
using System.Text;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// Hybrid-reference files: a classic table whose trailer names a cross-reference stream through <c>XRefStm</c>, consulted after the
/// table and before older sections. ISO 32000-2 §7.5.8.4, Table 19.
/// </summary>
public sealed class HybridFileTests
{
    [Fact]
    public void An_object_only_the_XRefStm_stream_lists_is_found()
    {
        // hybrid-xref.pdf: the main table lists 4-6 free; the update's XRefStm stream puts the Info dictionary (4) in object stream 5.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("hybrid-xref.pdf"));

        CosDictionary info = Assert.IsType<CosDictionary>(document.Resolve(document.Trailer[new CosName("Info")]));

        Assert.Equal("hybrid", Assert.IsType<CosString>(info[new CosName("Title")]).DecodeText());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_in_use_entry_in_the_table_of_a_section_takes_precedence_over_its_XRefStm_stream()
    {
        // §7.5.8.4: the stream is searched for an object the table does not define; an in-use table entry defines it.
        byte[] file = Hybrid(object4InTable: "in use", streamEntries: string.Empty);

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal("table", Assert.IsType<CosString>(document.Resolve(new CosReference(4, 0))).DecodeText());
        Assert.Single(document.Pages);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_free_entry_in_the_table_of_a_section_gives_way_to_its_XRefStm_stream()
    {
        // §7.5.8.4: "A PDF reader shall look in the cross-reference stream first, find the object there, and shall ignore the free
        // entry"; the NOTE puts the free entry only typically in a previous section, so a free entry in the same table yields too.
        byte[] file = Hybrid(object4InTable: "free", streamEntries: string.Empty);

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal("stream", Assert.IsType<CosString>(document.Resolve(new CosReference(4, 0))).DecodeText());
        Assert.Single(document.Pages);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_XRefStm_streams_dictionary_is_not_the_trailer_and_its_Prev_is_not_followed()
    {
        // Table 17: Prev in an XRefStm stream is "not meaningful in hybrid-reference files"; Root comes from the table's trailer.
        byte[] file = Hybrid(object4InTable: null, streamEntries: "/Prev 999999 /Root 3 0 R /Info 4 0 R");

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal("stream", Assert.IsType<CosString>(document.Resolve(new CosReference(4, 0))).DecodeText());
        Assert.Equal(new CosReference(1, 0), document.Trailer[new CosName("Root")]);
        Assert.False(document.Trailer.ContainsKey(new CosName("Info")));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_XRefStm_offset_that_names_no_stream_is_reported_and_the_table_is_still_read()
    {
        byte[] file = Hybrid(object4InTable: null, streamEntries: string.Empty, xrefStm: "13");

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Single(document.Pages);
        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(4, 0)));
        Assert.Equal("TrailerXRefStmInvalid", Assert.Single(document.Diagnostics).Code);
    }

    /// <summary>
    /// One section: objects 1-3 in the classic table, and object 4 listed there as <paramref name="object4InTable"/> says ("in use",
    /// written as "(table)"; "free"; or not listed when <see langword="null"/>); object stream 5 holding 4 as "(stream)"; and
    /// cross-reference stream 6 for 4-6 named by the trailer's XRefStm (or by <paramref name="xrefStm"/>).
    /// </summary>
    private static byte[] Hybrid(string? object4InTable, string streamEntries, string? xrefStm = null)
    {
        var text = new StringBuilder("%PDF-1.5\n");
        var offsets = new Dictionary<int, int>();
        void Add(int number, string body)
        {
            offsets[number] = text.Length;
            text.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n");
        }

        for (int index = 0; index < XrefStreamPdf.OnePage.Length; index++)
        {
            Add(index + 1, XrefStreamPdf.OnePage[index]);
        }

        if (object4InTable == "in use")
        {
            Add(4, "(table)");
        }

        Add(5, "<< /Type /ObjStm /N 1 /First 4 /Length 12 >>\nstream\n4 0 (stream)\nendstream");
        int stream = text.Length;
        byte[] rows = XrefStreamPdf.Rows([1, 2, 1], (2, 5, 0), (1, offsets[5], 0), (1, stream, 0));
        text.Append(CultureInfo.InvariantCulture, $"6 0 obj\n<< /Type /XRef /Size 7 /Index [4 3] /W [1 2 1] {streamEntries} /Length {rows.Length} >>\nstream\n");
        text.Append(Encoding.Latin1.GetString(rows)).Append("\nendstream\nendobj\n");
        int table = text.Length;
        int listed = object4InTable is null ? 3 : 4;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {listed + 1}\n0000000000 65535 f \n");
        for (int number = 1; number <= listed; number++)
        {
            text.Append(offsets.TryGetValue(number, out int offset) ? $"{offset:D10} 00000 n \n" : "0000000000 00001 f \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 7 /Root 1 0 R /XRefStm {xrefStm ?? stream.ToString(CultureInfo.InvariantCulture)} >>\nstartxref\n{table}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }
}
