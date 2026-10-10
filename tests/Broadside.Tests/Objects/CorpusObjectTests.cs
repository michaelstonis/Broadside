using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Objects;

/// <summary>
/// Every indirect object in the minimal corpus, cut out between <c>N G obj</c> and <c>endobj</c> by the test (file structure is
/// issue #37's), parses through <see cref="CosObject.Parse"/> and survives a write-then-parse round trip. ISO 32000-2 §7.3.
/// </summary>
public sealed partial class CorpusObjectTests
{
    [Theory]
    [MemberData(nameof(Corpus.WellFormedFiles), MemberType = typeof(Corpus))]
    public void Every_object_in_a_well_formed_file_parses_and_round_trips(string fileName)
    {
        IReadOnlyList<(int Number, byte[] Body)> objects = IndirectObjects(Corpus.Bytes(fileName));
        Assert.NotEmpty(objects);

        foreach ((int number, byte[] body) in objects)
        {
            CosObject parsed = CosObject.Parse(body);
            var buffer = new ArrayBufferWriter<byte>();
            parsed.WriteTo(buffer);

            Assert.False(parsed.IsDirty, $"{fileName} object {number}");
            Assert.True(CosObject.DeepEquals(parsed, CosObject.Parse(buffer.WrittenSpan)), $"{fileName} object {number} did not round-trip");
        }
    }

    [Fact]
    public void The_page_of_empty_page_pdf_has_a_letter_media_box()
    {
        byte[] body = IndirectObjects(Corpus.Bytes("empty-page.pdf")).Single(static entry => entry.Number == 3).Body;

        CosDictionary page = Assert.IsType<CosDictionary>(CosObject.Parse(body));

        Assert.Equal(new CosName("Page"), page[new CosName("Type")]);
        Assert.Equal(new CosReference(2, 0), page[new CosName("Parent")]);
        CosArray mediaBox = Assert.IsType<CosArray>(page[new CosName("MediaBox")]);
        Assert.Equal([0d, 0d, 612d, 792d], mediaBox.Cast<CosNumber>().Select(static number => number.ToDouble()));
    }

    [Fact]
    public void The_metadata_stream_of_metadata_xmp_pdf_holds_its_declared_length()
    {
        IReadOnlyList<(int Number, byte[] Body)> objects = IndirectObjects(Corpus.Bytes("metadata-xmp.pdf"));

        CosStream metadata = Assert.IsType<CosStream>(CosObject.Parse(objects.Single(static entry => entry.Number == 4).Body));
        CosDictionary info = Assert.IsType<CosDictionary>(CosObject.Parse(objects.Single(static entry => entry.Number == 5).Body));

        Assert.Equal(new CosName("XML"), metadata.Dictionary[new CosName("Subtype")]);
        Assert.Equal(391, metadata.EncodedData.Length);
        Assert.StartsWith("<?xpacket begin=", Encoding.UTF8.GetString(metadata.EncodedData.Span), StringComparison.Ordinal);
        Assert.Equal("Broadside", Assert.IsType<CosString>(info[new CosName("Title")]).DecodeText());
    }

    [Fact]
    public void The_stream_with_a_wrong_length_is_rejected_by_strict_parsing()
    {
        byte[] body = IndirectObjects(Corpus.Bytes("wrong-stream-length.pdf")).Single(static entry => entry.Number == 4).Body;

        FormatException error = Assert.Throws<FormatException>(() => CosObject.Parse(body));

        Assert.StartsWith("StreamLengthInvalid", error.Message, StringComparison.Ordinal);
    }

    private static List<(int Number, byte[] Body)> IndirectObjects(byte[] file)
    {
        string text = Encoding.Latin1.GetString(file);
        return [.. IndirectObjectPattern().Matches(text).Select(match => (
            int.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture),
            file[match.Groups["body"].Index..(match.Groups["body"].Index + match.Groups["body"].Length)]))];
    }

    [GeneratedRegex(@"(?<=^|[\r\n])(?<number>\d+) \d+ obj(?<body>.*?)endobj", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex IndirectObjectPattern();
}
