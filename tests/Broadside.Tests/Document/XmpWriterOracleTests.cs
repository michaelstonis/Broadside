using System.Text;
using Broadside.Tests.Document.LegacyXmp;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// The XMP writer without <c>System.Xml</c> (issue #545) writes the same bytes as the <c>XmlWriter</c>-based writer it replaced,
/// kept in this project as the oracle: for every test-corpus packet read back and for seeded random property trees whose names,
/// values, languages and namespaces hold the characters escaping depends on. ISO 16684-1 §7; ISO 32000-2 §14.3.2.
/// </summary>
public sealed class XmpWriterOracleTests(ITestOutputHelper output)
{
    private static readonly string[] Namespaces =
    [
        XmpNamespaces.DublinCore, XmpNamespaces.Xmp, XmpNamespaces.Pdf, XmpNamespaces.PdfAIdentification, XmpNamespaces.XmpMeta,
        XmpNamespaces.Rdf, "http://www.w3.org/XML/1998/namespace", string.Empty, "urn:a", "urn:b#", "urn:quote\"amp&lt<gt>tab\tlf\ncr\r",
        "http://ns.adobe.com/xap/1.0/sType/ResourceRef#",
    ];

    private static readonly string[] Names = ["title", "li", "value", "a", "\u00E0b", "a.b-c_1", "Description", "lang"];

    private static readonly string[] Pieces =
    [
        "a", "B", " ", "  ", "\t", "\n", "\r", "\r\n", "&", "<", ">", "\"", "'", "]]>", "&amp;", "\u00E9", "\u0085", "\u2028", "\uFEFF",
        "\uD83D\uDE00", "\uD800", "\uDC00", "\u0001", "\u0000", "\uFFFE", "\uFFFF", "\uE000", "x-default",
    ];

    [Fact]
    public void Every_test_corpus_packet_is_written_as_the_old_writer_wrote_it()
    {
        int packets = 0;
        foreach (string name in Corpus.AllFileNames)
        {
            foreach (byte[] data in XmpOracle.MetadataPackets(File.ReadAllBytes(Path.Combine(Corpus.Directory, name))))
            {
                if (XmpPacket.TryParse(data, out XmpPacket? packet))
                {
                    packets++;
                    AssertSame(packet.Properties, packet.About ?? string.Empty);
                }
            }
        }

        output.WriteLine($"{packets} packets written.");
        Assert.True(packets > 0);
    }

    [Fact]
    public void An_empty_packet_is_written_as_the_old_writer_wrote_it() => AssertSame([], string.Empty);

    /// <summary>
    /// The one deliberate difference: a property named in the namespace of namespace declarations (<c>&lt;xmlns:a&gt;</c>, which XML
    /// reads) made the old writer throw, failing the save; it is left out like the RDF and XML syntax names, and the packet reads back.
    /// </summary>
    [Fact]
    public void A_property_in_the_xmlns_namespace_is_left_out_where_the_old_writer_threw()
    {
        byte[] data = Encoding.UTF8.GetBytes(
            "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description xmlns:dc=\"http://purl.org/dc/elements/1.1/\">"
            + "<xmlns:a>x</xmlns:a><dc:format>application/pdf</dc:format></rdf:Description></rdf:RDF>");
        Assert.True(XmpPacket.TryParse(data, out XmpPacket? packet));
        Assert.Equal(2, packet.Properties.Count);

        _ = Assert.Throws<ArgumentException>(() => LegacyXmpPacketWriter.Write(packet.Properties, string.Empty));
        Assert.True(XmpPacket.TryParse(XmpPacketWriter.Write(packet.Properties, string.Empty), out XmpPacket? written));
        XmpProperty property = Assert.Single(written.Properties);
        Assert.Equal("application/pdf", property.Value);
    }

    [Fact]
    public void Random_property_trees_are_written_as_the_old_writer_wrote_them()
    {
#pragma warning disable CA5394 // Reproducible test input, not security.
        var random = new Random(545);
        for (int i = 0; i < 3_000; i++)
        {
            XmpProperty[] properties = [.. Enumerable.Range(0, random.Next(5)).Select(_ => Property(random, 0))];
            AssertSame(properties, Text(random));
        }
#pragma warning restore CA5394
    }

#pragma warning disable CA5394 // Reproducible test input, not security.
    private static XmpProperty Property(Random random, int depth)
    {
        string namespaceName = Namespaces[random.Next(Namespaces.Length)];
        string name = Names[random.Next(Names.Length)];
        string? language = random.Next(4) == 0 ? Text(random) : null;
        XmpProperty[] Children() => depth > 3 ? [] : [.. Enumerable.Range(0, random.Next(4)).Select(_ => Property(random, depth + 1))];
        XmpProperty[] qualifiers = random.Next(5) == 0 ? Children() : [];
        return random.Next(5) switch
        {
            0 => new XmpProperty(namespaceName, name, XmpPropertyKind.Structure, null, language, [], Children(), qualifiers),
            1 => new XmpProperty(namespaceName, name, XmpPropertyKind.UnorderedArray, null, language, Children(), [], qualifiers),
            2 => new XmpProperty(namespaceName, name, random.Next(2) == 0 ? XmpPropertyKind.OrderedArray : XmpPropertyKind.Alternative, null, language, Children(), [], qualifiers),
            3 => new XmpProperty(namespaceName, name, XmpPropertyKind.Simple, Text(random), language, [], [], qualifiers, isUri: true),
            _ => new XmpProperty(namespaceName, name, XmpPropertyKind.Simple, random.Next(6) == 0 ? null : Text(random), language, [], [], qualifiers),
        };
    }

    private static string Text(Random random)
    {
        var builder = new StringBuilder();
        int count = random.Next(5);
        for (int i = 0; i < count; i++)
        {
            _ = builder.Append(Pieces[random.Next(Pieces.Length)]);
        }

        return builder.ToString();
    }
#pragma warning restore CA5394

    private static void AssertSame(IReadOnlyList<XmpProperty> properties, string about)
    {
        string legacy = Outcome(() => LegacyXmpPacketWriter.Write(properties, about));
        string current = Outcome(() => XmpPacketWriter.Write(properties, about));
        Assert.Equal(legacy, current);
    }

    private static string Outcome(Func<byte[]> write)
    {
        try
        {
            return Encoding.UTF8.GetString(write());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return $"{exception.GetType().Name}";
        }
    }
}
